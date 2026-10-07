using System.Runtime.CompilerServices;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using GmailToPst.Providers.Helpers;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace GmailToPst.Providers.Imap;

public class ImapProvider : IEmailProvider
{
    private ImapClient? _client;
    private AccountConfig? _config;

    public async Task<bool> ConnectAndAuthenticateAsync(AccountConfig config, CancellationToken cancellationToken = default)
    {
        _config = config;
        _client = new ImapClient();

        try
        {
            var secureSocketOption = config.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.Auto;
            await _client.ConnectAsync(config.ImapHost, config.ImapPort, secureSocketOption, cancellationToken);

            var password = config.AppPassword.Replace(" ", ""); // Rimuove eventuali spazi generati da Google App Password
            await _client.AuthenticateAsync(config.EmailAddress, password, cancellationToken);
            return true;
        }
        catch
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync(true, cancellationToken);
            }
            _client.Dispose();
            _client = null;
            throw;
        }
    }

    public async Task<List<EmailFolder>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var personalPath = _client!.PersonalNamespaces.Count > 0 ? _client.PersonalNamespaces[0].Path : "";
        var personal = await _client.GetFolderAsync(personalPath, cancellationToken);
        var folders = new List<EmailFolder>();

        await TraverseFolderAsync(personal, null, folders, cancellationToken);
        return folders;
    }

    private async Task TraverseFolderAsync(IMailFolder mailFolder, string? parentId, List<EmailFolder> resultList, CancellationToken cancellationToken)
    {
        var isNonExistent = mailFolder.Attributes.HasFlag(FolderAttributes.NonExistent);
        var folderId = mailFolder.FullName;
        var folderName = GetFriendlyImapFolderName(mailFolder);

        var isSystem = mailFolder.Attributes.HasFlag(FolderAttributes.Inbox) ||
                       mailFolder.Attributes.HasFlag(FolderAttributes.Sent) ||
                       mailFolder.Attributes.HasFlag(FolderAttributes.Trash) ||
                       mailFolder.Attributes.HasFlag(FolderAttributes.Junk) ||
                       mailFolder.Attributes.HasFlag(FolderAttributes.Drafts);

        if (!isNonExistent)
        {
            int count = 0;
            try
            {
                await mailFolder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                count = mailFolder.Count;
                await mailFolder.CloseAsync(false, cancellationToken);
            }
            catch
            {
                // Alcune cartelle potrebbero non essere apribili
            }

            var folder = new EmailFolder
            {
                Id = folderId,
                Name = folderName,
                FullPath = mailFolder.FullName,
                ParentId = parentId,
                TotalCount = count,
                IsSystemFolder = isSystem
            };

            resultList.Add(folder);
        }

        try
        {
            var subFolders = await mailFolder.GetSubfoldersAsync(false, cancellationToken);
            foreach (var sub in subFolders)
            {
                await TraverseFolderAsync(sub, isNonExistent ? parentId : folderId, resultList, cancellationToken);
            }
        }
        catch
        {
            // Ignora se non ci sono subfolder
        }
    }

    private static string GetFriendlyImapFolderName(IMailFolder mailFolder)
    {
        if (mailFolder.Attributes.HasFlag(FolderAttributes.Inbox)) return "Posta in arrivo";
        if (mailFolder.Attributes.HasFlag(FolderAttributes.Sent)) return "Posta inviata";
        if (mailFolder.Attributes.HasFlag(FolderAttributes.Drafts)) return "Bozze";
        if (mailFolder.Attributes.HasFlag(FolderAttributes.Trash)) return "Cestino";
        if (mailFolder.Attributes.HasFlag(FolderAttributes.Junk)) return "Spam";

        var name = mailFolder.Name;
        return name switch
        {
            "INBOX" => "Posta in arrivo",
            "Sent Mail" or "Sent" or "Posta inviata" => "Posta inviata",
            "Drafts" or "Bozze" => "Bozze",
            "Trash" or "Bin" or "Cestino" => "Cestino",
            "Spam" or "Junk" => "Spam",
            "Starred" or "Speciali" => "Speciali",
            "All Mail" or "Tutti i messaggi" => "Tutti i messaggi",
            "Important" or "Importanti" => "Importanti",
            _ => name
        };
    }

    public async Task<int> CountMessagesAsync(BackupFilter filter, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        var folders = await GetFoldersAsync(cancellationToken);
        int total = 0;

        var targetFolders = filter.SelectedFolderIds.Any()
            ? folders.Where(f => filter.SelectedFolderIds.Contains(f.Id)).ToList()
            : folders;

        var searchQuery = BuildSearchQuery(filter);

        foreach (var f in targetFolders)
        {
            try
            {
                var mailFolder = await _client!.GetFolderAsync(f.FullPath, cancellationToken);
                await mailFolder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                var uids = await mailFolder.SearchAsync(searchQuery, cancellationToken);
                total += uids.Count;
                await mailFolder.CloseAsync(false, cancellationToken);
            }
            catch
            {
                // Ignora cartelle non interrogabili
            }
        }

        return total;
    }

    public async IAsyncEnumerable<(EmailMessage Message, byte[] RawEmlBytes)> FetchMessagesAsync(
        BackupFilter filter,
        IProgress<BackupProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var allFolders = await GetFoldersAsync(cancellationToken);
        var targetFolders = filter.SelectedFolderIds.Any()
            ? allFolders.Where(f => filter.SelectedFolderIds.Contains(f.Id)).ToList()
            : allFolders;

        var searchQuery = BuildSearchQuery(filter);
        var progressState = new BackupProgress
        {
            Phase = BackupPhase.QueryingServer,
            StatusMessage = "Calcolo dei messaggi sui server Google..."
        };
        progress?.Report(progressState);

        // Precalcola totale
        var folderUids = new List<(string FolderId, string FolderPath, string FolderName, IList<UniqueId> Uids)>();
        int totalMessages = 0;

        foreach (var folderModel in targetFolders)
        {
            try
            {
                var mailFolder = await _client!.GetFolderAsync(folderModel.FullPath, cancellationToken);
                await mailFolder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                var uids = await mailFolder.SearchAsync(searchQuery, cancellationToken);
                if (uids.Count > 0)
                {
                    folderUids.Add((folderModel.Id, folderModel.FullPath, mailFolder.Name, uids));
                    totalMessages += uids.Count;
                }
                await mailFolder.CloseAsync(false, cancellationToken);
            }
            catch
            {
                // Continua con le altre cartelle
            }
        }

        progressState.TotalMessages = totalMessages;
        progressState.Phase = BackupPhase.DownloadingMessages;
        progress?.Report(progressState);

        int processed = 0;
        long totalBytes = 0;

        foreach (var (folderId, folderPath, folderName, uids) in folderUids)
        {
            progressState.CurrentFolder = folderName;

            IMailFolder? mailFolder = null;
            try
            {
                mailFolder = await _client!.GetFolderAsync(folderPath, cancellationToken);
                await mailFolder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                foreach (var uid in uids)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var externalId = uid.ToString();
                    var fullExternalId = $"{folderId}_{uid}";

                    if (filter.ExistingMessageIds != null && 
                        (filter.ExistingMessageIds.Contains(externalId) || filter.ExistingMessageIds.Contains(fullExternalId)))
                    {
                        processed++;
                        progressState.ProcessedMessages = processed;
                        progressState.StatusMessage = $"Già archiviato (saltato): {folderName} ({processed}/{totalMessages})";
                        progress?.Report(progressState);
                        continue;
                    }

                    var mime = await mailFolder.GetMessageAsync(uid, cancellationToken);
                    byte[] rawBytes;
                    using (var ms = new MemoryStream())
                    {
                        await mime.WriteToAsync(ms, cancellationToken);
                        rawBytes = ms.ToArray();
                    }

                    var (message, _) = MimeParserHelper.ParseMimeMessage(rawBytes, folderId, folderName, externalId);

                    processed++;
                    totalBytes += rawBytes.Length;

                    progressState.ProcessedMessages = processed;
                    progressState.BytesDownloaded = totalBytes;
                    progressState.CurrentMessageSubject = message.Subject;
                    progressState.StatusMessage = $"Download da '{folderName}' ({processed}/{totalMessages}): {message.Subject}";
                    progress?.Report(progressState);

                    yield return (message, rawBytes);

                    if (filter.MaxMessages.HasValue && processed >= filter.MaxMessages.Value)
                        break;
                }
            }
            finally
            {
                if (mailFolder != null && mailFolder.IsOpen)
                {
                    try { await mailFolder.CloseAsync(false, cancellationToken); } catch { }
                }
            }

            if (filter.MaxMessages.HasValue && processed >= filter.MaxMessages.Value)
                break;
        }

        progressState.Phase = BackupPhase.Completed;
        progressState.StatusMessage = $"Completato: {processed} messaggi scaricati ({progressState.FormattedBytes}).";
        progress?.Report(progressState);
    }

    private SearchQuery BuildSearchQuery(BackupFilter filter)
    {
        var queries = new List<SearchQuery>();

        if (filter.StartDate.HasValue)
        {
            queries.Add(SearchQuery.DeliveredAfter(filter.StartDate.Value));
        }

        if (filter.EndDate.HasValue)
        {
            queries.Add(SearchQuery.DeliveredBefore(filter.EndDate.Value));
        }

        if (!queries.Any())
            return SearchQuery.All;

        SearchQuery current = queries[0];
        for (int i = 1; i < queries.Count; i++)
        {
            current = current.And(queries[i]);
        }

        return current;
    }

    private void EnsureConnected()
    {
        if (_client == null || !_client.IsConnected || !_client.IsAuthenticated)
        {
            throw new InvalidOperationException("Il client IMAP non è connesso o autenticato. Effettua prima la connessione.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client != null)
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync(true);
            }
            _client.Dispose();
            _client = null;
        }
    }
}
