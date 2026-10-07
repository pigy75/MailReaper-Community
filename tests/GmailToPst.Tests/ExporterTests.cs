using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using GmailToPst.Exporters.Eml;
using GmailToPst.Exporters.Msg;
using GmailToPst.Providers.Helpers;
using GmailToPst.Storage.Database;
using MimeKit;
using System.IO;
using System.Text;
using Xunit;

namespace GmailToPst.Tests;

public class ExporterTests : IAsyncDisposable
{
    private readonly string _tempArchiveDir;
    private readonly string _tempExportDir;
    private readonly SqliteArchiveStorage _storage;

    public ExporterTests()
    {
        _tempArchiveDir = Path.Combine(Path.GetTempPath(), $"GmailToPst_Arch_{Guid.NewGuid()}");
        _tempExportDir = Path.Combine(Path.GetTempPath(), $"GmailToPst_Exp_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempArchiveDir);
        Directory.CreateDirectory(_tempExportDir);
        _storage = new SqliteArchiveStorage();
    }

    [Fact]
    public async Task TestGaseluceCount()
    {
        var json = @"C:\Users\pierl\Downloads\mystical-method-507313-h8-c194728e1bfc.json";
        if (File.Exists(json))
        {
            var provider = new GmailToPst.Providers.Workspace.GoogleWorkspaceEmailProvider();
            var config = new AccountConfig
            {
                Type = AccountType.GoogleWorkspaceServiceAccount,
                EmailAddress = "gaseluce@rebogas.com",
                ServiceAccountKeyFilePath = json
            };
            await provider.ConnectAndAuthenticateAsync(config);
            var folders = await provider.GetFoldersAsync();
            var countAll = await provider.CountMessagesAsync(new BackupFilter());
            Assert.True(countAll > 0);
            Assert.NotEmpty(folders);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _storage.DisposeAsync();
        try
        {
            if (Directory.Exists(_tempArchiveDir)) Directory.Delete(_tempArchiveDir, true);
            if (Directory.Exists(_tempExportDir)) Directory.Delete(_tempExportDir, true);
        }
        catch { }
    }

    [Fact]
    public async Task MsgFolderExporter_CreatesValidMsgFiles()
    {
        await _storage.InitializeAsync(_tempArchiveDir, "test@domain.com", 2022);

        var builder = new BodyBuilder { HtmlBody = "<b>Report 2022</b>", TextBody = "Report 2022" };
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Sender", "sender@domain.com"));
        mime.To.Add(new MailboxAddress("Receiver", "receiver@domain.com"));
        mime.Subject = "Test Esportazione MSG";
        mime.Date = new DateTimeOffset(2022, 1, 1, 12, 0, 0, TimeSpan.Zero);
        mime.Body = builder.ToMessageBody();

        byte[] rawBytes;
        using (var ms = new MemoryStream())
        {
            mime.WriteTo(ms);
            rawBytes = ms.ToArray();
        }

        var (email, _) = MimeParserHelper.ParseMimeMessage(rawBytes, "INBOX", "Posta in arrivo", "msg-exp-01");
        await _storage.SaveMessageAsync(email, rawBytes);

        var exporter = new MsgFolderExporter();
        Assert.True(exporter.IsAvailableOnCurrentSystem(out _));

        var options = new ExportOptions
        {
            Format = ExportFormat.MsgFolder,
            OutputPath = _tempExportDir
        };

        var finalPath = await exporter.ExportAsync(_storage, options);
        Assert.True(Directory.Exists(finalPath));

        var msgFiles = Directory.GetFiles(_tempExportDir, "*.msg", SearchOption.AllDirectories);
        Assert.Single(msgFiles);
        Assert.True(new FileInfo(msgFiles[0]).Length > 0);
    }

    [Fact]
    public async Task EmlFolderExporter_CreatesValidEmlTree()
    {
        await _storage.InitializeAsync(_tempArchiveDir, "test@domain.com", 2022);

        var builder = new BodyBuilder { TextBody = "Hello EML" };
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Sender", "sender@domain.com"));
        mime.To.Add(new MailboxAddress("Receiver", "receiver@domain.com"));
        mime.Subject = "Test Esportazione EML";
        mime.Date = new DateTimeOffset(2022, 1, 1, 12, 0, 0, TimeSpan.Zero);
        mime.Body = builder.ToMessageBody();

        byte[] rawBytes;
        using (var ms = new MemoryStream())
        {
            mime.WriteTo(ms);
            rawBytes = ms.ToArray();
        }

        var (email, _) = MimeParserHelper.ParseMimeMessage(rawBytes, "INBOX", "Posta in arrivo", "msg-exp-02");
        await _storage.SaveMessageAsync(email, rawBytes);

        var exporter = new EmlFolderExporter();
        var options = new ExportOptions
        {
            Format = ExportFormat.EmlFolder,
            OutputPath = _tempExportDir
        };

        var finalPath = await exporter.ExportAsync(_storage, options);
        Assert.True(Directory.Exists(finalPath));

        var emlFiles = Directory.GetFiles(_tempExportDir, "*.eml", SearchOption.AllDirectories);
        Assert.Single(emlFiles);
    }

    [Fact]
    public async Task OutlookPstExporter_CreatesValidPstFileWithoutOutlook()
    {
        await _storage.InitializeAsync(_tempArchiveDir, "test@domain.com", 2022);

        var builder = new BodyBuilder { HtmlBody = "<b>PST Standalone Message</b>", TextBody = "PST Message" };
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Sender", "sender@domain.com"));
        mime.To.Add(new MailboxAddress("Receiver", "receiver@domain.com"));
        mime.Subject = "Test Esportazione PST Standalone";
        mime.Date = new DateTimeOffset(2022, 1, 1, 12, 0, 0, TimeSpan.Zero);
        mime.Body = builder.ToMessageBody();

        byte[] rawBytes;
        using (var ms = new MemoryStream())
        {
            mime.WriteTo(ms);
            rawBytes = ms.ToArray();
        }

        var (email, _) = MimeParserHelper.ParseMimeMessage(rawBytes, "INBOX", "Posta in arrivo", "msg-exp-pst-01");
        await _storage.SaveMessageAsync(email, rawBytes);

        var exporter = new GmailToPst.Exporters.Outlook.OutlookPstExporter();
        Assert.True(exporter.IsAvailableOnCurrentSystem(out _));

        var targetPst = Path.Combine(_tempExportDir, "TestExport.pst");
        var options = new ExportOptions
        {
            Format = ExportFormat.OutlookPst,
            OutputPath = targetPst,
            OverwriteExisting = true
        };

        var finalPath = await exporter.ExportAsync(_storage, options);
        Assert.True(File.Exists(finalPath));
        Assert.True(new FileInfo(finalPath).Length > 0);
    }
}
