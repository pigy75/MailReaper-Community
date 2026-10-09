using System.IO;
using System.Runtime.CompilerServices;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using GmailToPst.Providers.Helpers;
using MailKit.Net.Pop3;
using MailKit.Security;

namespace GmailToPst.Providers.Pop3;

public class Pop3Provider : IEmailProvider
{
    private Pop3Client? _client;
    private AccountConfig? _config;

    public async Task<bool> ConnectAndAuthenticateAsync(AccountConfig config, CancellationToken cancellationToken = default)
    {
        _config = config;
        _client = new Pop3Client();

        try
        {
            var host = !string.IsNullOrEmpty(config.Pop3Host) ? config.Pop3Host : config.ImapHost;
            var port = config.Pop3Port > 0 ? config.Pop3Port : 995;
            var secureSocketOption = config.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.Auto;

            await _client.ConnectAsync(host, port, secureSocketOption, cancellationToken);

            var password = config.AppPassword.Replace(" ", "");
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

    public Task<List<EmailFolder>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        int count = _client!.Count;
        var list = new List<EmailFolder>
        {
            new EmailFolder
            {
                Id = "INBOX",
                Name = "Posta in arrivo (POP3)",
                FullPath = "INBOX",
                TotalCount = count,
                IsSystemFolder = true
            }
        };
        return Task.FromResult(list);
    }

    public async Task<int> CountMessagesAsync(BackupFilter filter, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        int totalCount = _client!.Count;
        if (!filter.Year.HasValue && !filter.StartDate.HasValue && !filter.EndDate.HasValue)
        {
            return totalCount;
        }

        int matchCount = 0;
        for (int i = 0; i < totalCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var headers = await _client.GetMessageHeadersAsync(i, cancellationToken);
                var date = TryGetHeaderDate(headers);
                if (!date.HasValue || MatchesFilter(date.Value, filter))
                {
                    matchCount++;
                }
            }
            catch
            {
                matchCount++;
            }
        }
        return matchCount;
    }

    public async IAsyncEnumerable<(EmailMessage Message, byte[] RawEmlBytes)> FetchMessagesAsync(
        BackupFilter filter,
        IProgress<BackupProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        int totalMessages = _client!.Count;
        var progressState = new BackupProgress
        {
            Phase = BackupPhase.DownloadingMessages,
            TotalMessages = totalMessages,
            CurrentFolder = "Posta in arrivo",
            StatusMessage = $"Connessione POP3 stabilita. {totalMessages} messaggi disponibili..."
        };
        progress?.Report(progressState);

        IList<string>? uids = null;
        try
        {
            uids = await _client.GetMessageUidsAsync(cancellationToken);
        }
        catch
        {
            // Fallback se il server non supporta UIDL
        }

        int processed = 0;
        long totalBytes = 0;

        for (int i = 0; i < totalMessages; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var uid = (uids != null && i < uids.Count) ? uids[i] : $"pop3_msg_{i}";
            var externalId = $"INBOX_{uid}";

            if (filter.ExistingMessageIds != null && 
                (filter.ExistingMessageIds.Contains(uid) || filter.ExistingMessageIds.Contains(externalId)))
            {
                processed++;
                progressState.ProcessedMessages = processed;
                progressState.StatusMessage = $"Già archiviato (saltato): ({processed}/{totalMessages})";
                progress?.Report(progressState);
                continue;
            }

            if (filter.Year.HasValue || filter.StartDate.HasValue || filter.EndDate.HasValue)
            {
                try
                {
                    var headers = await _client.GetMessageHeadersAsync(i, cancellationToken);
                    var headerDate = TryGetHeaderDate(headers);
                    if (headerDate.HasValue && !MatchesFilter(headerDate.Value, filter))
                    {
                        processed++;
                        continue;
                    }
                }
                catch
                {
                    // Fallback: scarica l'intero messaggio
                }
            }

            var mime = await _client.GetMessageAsync(i, cancellationToken);
            byte[] rawBytes;
            using (var ms = new MemoryStream())
            {
                await mime.WriteToAsync(ms, cancellationToken);
                rawBytes = ms.ToArray();
            }

            var (message, _) = MimeParserHelper.ParseMimeMessage(rawBytes, "INBOX", "Posta in arrivo", uid);

            if (!MatchesFilter(message.Date.DateTime, filter))
            {
                processed++;
                continue;
            }

            processed++;
            totalBytes += rawBytes.Length;

            progressState.ProcessedMessages = processed;
            progressState.BytesDownloaded = totalBytes;
            progressState.CurrentMessageSubject = message.Subject;
            progressState.StatusMessage = $"Download POP3 ({processed}/{totalMessages}): {message.Subject}";
            progress?.Report(progressState);

            yield return (message, rawBytes);

            if (filter.MaxMessages.HasValue && processed >= filter.MaxMessages.Value)
                break;
        }

        progressState.Phase = BackupPhase.Completed;
        progressState.StatusMessage = $"Completato: {processed} messaggi POP3 scaricati ({progressState.FormattedBytes}).";
        progress?.Report(progressState);
    }

    private static DateTime? TryGetHeaderDate(MimeKit.HeaderList headers)
    {
        var raw = headers[MimeKit.HeaderId.Date];
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (MimeKit.Utils.DateUtils.TryParse(raw, out var dto))
            {
                return dto.DateTime;
            }
            if (DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dto2))
            {
                return dto2.DateTime;
            }
        }
        return null;
    }

    private static bool MatchesFilter(DateTime date, BackupFilter filter)
    {
        if (filter.Year.HasValue && date.Year != filter.Year.Value)
            return false;
        if (filter.StartDate.HasValue && date < filter.StartDate.Value)
            return false;
        if (filter.EndDate.HasValue && date > filter.EndDate.Value)
            return false;
        return true;
    }

    private void EnsureConnected()
    {
        if (_client == null || !_client.IsConnected || !_client.IsAuthenticated)
        {
            throw new InvalidOperationException("Il client POP3 non è connesso o autenticato.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client != null)
        {
            try
            {
                if (_client.IsConnected)
                {
                    await _client.DisconnectAsync(true);
                }
            }
            catch { }
            finally
            {
                _client.Dispose();
                _client = null;
            }
        }
        GC.SuppressFinalize(this);
    }
}
