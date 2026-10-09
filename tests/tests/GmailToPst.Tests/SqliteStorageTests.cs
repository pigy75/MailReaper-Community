using GmailToPst.Core.Models;
using GmailToPst.Providers.Helpers;
using GmailToPst.Storage.Database;
using MimeKit;
using System.IO;
using System.Text;
using Xunit;

namespace GmailToPst.Tests;

public class SqliteStorageTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly SqliteArchiveStorage _storage;

    public SqliteStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"GmailToPst_Test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _storage = new SqliteArchiveStorage();
    }

    public async ValueTask DisposeAsync()
    {
        await _storage.DisposeAsync();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task InitializeAndStoreMessages_WorksAsExpected()
    {
        await _storage.InitializeAsync(_tempDir, "testuser@gmail.com", 2022);

        // 1. Salva cartelle
        var folders = new List<EmailFolder>
        {
            new() { Id = "INBOX", Name = "Posta in arrivo", FullPath = "INBOX", TotalCount = 10, IsSystemFolder = true },
            new() { Id = "SENT", Name = "Inviati", FullPath = "SENT", TotalCount = 5, IsSystemFolder = true }
        };
        await _storage.SaveFolderHierarchyAsync(folders);

        // 2. Crea email e salva
        var builder = new BodyBuilder { TextBody = "Corpo email salvata 2022." };
        builder.Attachments.Add("contratto.pdf", Encoding.UTF8.GetBytes("Contratto PDF"), new ContentType("application", "pdf"));

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Admin", "admin@workspace.com"));
        mime.To.Add(new MailboxAddress("User", "testuser@gmail.com"));
        mime.Subject = "Fattura 2022 n. 104";
        mime.Date = new DateTimeOffset(2022, 3, 10, 14, 0, 0, TimeSpan.Zero);
        mime.Body = builder.ToMessageBody();

        byte[] rawBytes;
        using (var ms = new MemoryStream())
        {
            mime.WriteTo(ms);
            rawBytes = ms.ToArray();
        }

        var (email, _) = MimeParserHelper.ParseMimeMessage(rawBytes, "INBOX", "Posta in arrivo", "msg-2022-001");
        await _storage.SaveMessageAsync(email, rawBytes);

        // 3. Verifica query
        var retrievedFolders = await _storage.GetFoldersAsync();
        Assert.NotEmpty(retrievedFolders);

        var retrievedMessages = await _storage.GetMessagesInFolderAsync("INBOX");
        Assert.Single(retrievedMessages);
        Assert.Equal("Fattura 2022 n. 104", retrievedMessages[0].Subject);

        // 4. Verifica ricerca
        var searchResults = await _storage.GetMessagesInFolderAsync("INBOX", searchKeyword: "Fattura");
        Assert.Single(searchResults);

        var searchMiss = await _storage.GetMessagesInFolderAsync("INBOX", searchKeyword: "NonEsistente");
        Assert.Empty(searchMiss);

        // 5. Verifica recupero dettagli e blob EML
        var details = await _storage.GetMessageDetailsAsync("msg-2022-001");
        Assert.NotNull(details);
        Assert.Single(details!.Attachments);
        Assert.Equal("contratto.pdf", details.Attachments[0].FileName);

        var emlBytes = await _storage.GetRawEmlBytesAsync("msg-2022-001");
        Assert.NotEmpty(emlBytes);

        var attBytes = await _storage.GetAttachmentBytesAsync(details.Attachments[0].Id);
        Assert.Equal("Contratto PDF", Encoding.UTF8.GetString(attBytes));
    }

    [Fact]
    public async Task DeleteYearDataAsync_RemovesMessagesAndFreesSpace()
    {
        await _storage.InitializeAsync(_tempDir, "user_del@gmail.com");

        // Salva un messaggio 2022 e uno 2023
        var mime2022 = new MimeMessage();
        mime2022.From.Add(new MailboxAddress("Sender", "a@b.com"));
        mime2022.To.Add(new MailboxAddress("Receiver", "user_del@gmail.com"));
        mime2022.Subject = "Msg 2022";
        mime2022.Date = new DateTimeOffset(2022, 5, 1, 12, 0, 0, TimeSpan.Zero);
        mime2022.Body = new TextPart("plain") { Text = "Body 2022" };
        var b2022 = Encoding.UTF8.GetBytes(mime2022.ToString());
        var (m2022, _) = MimeParserHelper.ParseMimeMessage(b2022, "INBOX", "Posta in arrivo", "msg-del-2022");
        await _storage.SaveMessageAsync(m2022, b2022);

        var mime2023 = new MimeMessage();
        mime2023.From.Add(new MailboxAddress("Sender", "a@b.com"));
        mime2023.To.Add(new MailboxAddress("Receiver", "user_del@gmail.com"));
        mime2023.Subject = "Msg 2023";
        mime2023.Date = new DateTimeOffset(2023, 6, 1, 12, 0, 0, TimeSpan.Zero);
        mime2023.Body = new TextPart("plain") { Text = "Body 2023" };
        var b2023 = Encoding.UTF8.GetBytes(mime2023.ToString());
        var (m2023, _) = MimeParserHelper.ParseMimeMessage(b2023, "INBOX", "Posta in arrivo", "msg-del-2023");
        await _storage.SaveMessageAsync(m2023, b2023);

        // Verifica entrambi presenti
        var totalBefore = await _storage.GetTotalMessageCountAsync();
        Assert.Equal(2, totalBefore);

        var ids = await _storage.GetExistingMessageIdsAsync();
        Assert.Contains("msg-del-2022", ids);
        Assert.Contains("msg-del-2023", ids);

        // Elimina 2022
        var deletedCount = await _storage.DeleteYearDataAsync(2022);
        Assert.Equal(1, deletedCount);

        // Verifica che 2022 sia sparito e 2023 sia rimasto
        var totalAfter = await _storage.GetTotalMessageCountAsync();
        Assert.Equal(1, totalAfter);

        var count2022 = await _storage.GetTotalMessageCountAsync(2022);
        Assert.Equal(0, count2022);

        var count2023 = await _storage.GetTotalMessageCountAsync(2023);
        Assert.Equal(1, count2023);

        var years = await _storage.GetAvailableYearsAsync();
        Assert.DoesNotContain(2022, years);
        Assert.Contains(2023, years);
    }

    [Fact]
    public async Task ExtractAttachmentsAsync_WithFilterOptions_WorksAsExpected()
    {
        await _storage.InitializeAsync(_tempDir, "user_att@gmail.com");

        var builder = new BodyBuilder { TextBody = "Test con allegati vari" };
        builder.Attachments.Add("foto.png", Encoding.UTF8.GetBytes("PNG data"), new ContentType("image", "png"));
        builder.Attachments.Add("documento.pdf", Encoding.UTF8.GetBytes("PDF data test"), new ContentType("application", "pdf"));

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Sender", "s@test.com"));
        mime.To.Add(new MailboxAddress("Receiver", "user_att@gmail.com"));
        mime.Subject = "Oggetto con allegati";
        mime.Date = new DateTimeOffset(2022, 7, 15, 10, 0, 0, TimeSpan.Zero);
        mime.Body = builder.ToMessageBody();

        byte[] rawBytes;
        using (var ms = new MemoryStream())
        {
            mime.WriteTo(ms);
            rawBytes = ms.ToArray();
        }

        var (email, _) = MimeParserHelper.ParseMimeMessage(rawBytes, "INBOX", "Posta in arrivo", "msg-att-001");
        await _storage.SaveMessageAsync(email, rawBytes);

        var extractDir = Path.Combine(_tempDir, "extracted");

        // 1. Estrai solo immagini
        var imgFilter = new AttachmentFilterOptions { Category = AttachmentCategory.Images };
        var (imgCount, _) = await _storage.ExtractAttachmentsAsync(Path.Combine(extractDir, "images"), imgFilter);
        Assert.Equal(1, imgCount);

        // 2. Estrai solo documenti
        var docFilter = new AttachmentFilterOptions { Category = AttachmentCategory.Documents };
        var (docCount, _) = await _storage.ExtractAttachmentsAsync(Path.Combine(extractDir, "docs"), docFilter);
        Assert.Equal(1, docCount);

        // 3. Estrai tutto
        var allFilter = new AttachmentFilterOptions { Category = AttachmentCategory.All };
        var (allCount, _) = await _storage.ExtractAttachmentsAsync(Path.Combine(extractDir, "all"), allFilter);
        Assert.Equal(2, allCount);
    }

    [Fact]
    public async Task LiveTest_OpenZDriveArchive()
    {
        if (Directory.Exists(@"Z:\export\Archives\amministrazione@rebogas.com"))
        {
            var st = new SqliteArchiveStorage();
            await st.InitializeAsync(@"Z:\export\Archives", "amministrazione@rebogas.com");
            var f = await st.GetFoldersAsync();
            var count = await st.GetTotalMessageCountAsync();
            Assert.True(count > 0);
            await st.DisposeAsync();
        }
    }

    [Fact]
    public async Task LiveTest_MainViewModel_RefreshAllAccounts()
    {
        var settings = GmailToPst.Storage.Settings.SettingsManager.LoadSettings();
        var basePath = settings.ArchivesPath;
        Assert.True(Directory.Exists(basePath), $"Directory does not exist: {basePath}");

        var discoveredEmails = new List<string>();
        foreach (var dir in Directory.GetDirectories(basePath))
        {
            var dirName = Path.GetFileName(dir);
            if (File.Exists(Path.Combine(dir, "archive.db")) && !dirName.Contains("_"))
            {
                discoveredEmails.Add(dirName);
            }
        }
        Assert.NotEmpty(discoveredEmails);
    }
}
