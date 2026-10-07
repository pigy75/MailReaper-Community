using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using GmailToPst.Providers.Helpers;
using System.IO;
using System.Runtime.CompilerServices;

namespace GmailToPst.Providers.Workspace;

public class GoogleWorkspaceEmailProvider : IEmailProvider
{
    private GmailService? _gmailService;
    private string _userEmail = string.Empty;
    private bool _disposed = false;

    public Task<bool> ConnectAndAuthenticateAsync(AccountConfig config, CancellationToken cancellationToken = default)
    {
        _userEmail = config.EmailAddress;

        var credential = GoogleCredential.FromFile(config.ServiceAccountKeyFilePath)
            .CreateScoped(new[] { GmailService.Scope.MailGoogleCom })
            .CreateWithUser(_userEmail);

        _gmailService = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "GmailToPst-WorkspaceBackup"
        });

        return Task.FromResult(true);
    }

    public async Task<List<EmailFolder>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var labelsResponse = await _gmailService!.Users.Labels.List(_userEmail).ExecuteAsync(cancellationToken);
        var folderList = new List<EmailFolder>();

        if (labelsResponse.Labels == null)
            return folderList;

        var systemLabels = new HashSet<string> { "INBOX", "SENT", "DRAFT", "TRASH", "SPAM", "STARRED", "UNREAD", "IMPORTANT", "CHAT" };

        foreach (var label in labelsResponse.Labels)
        {
            var isSystem = systemLabels.Contains(label.Id) || label.Type == "system";
            var displayName = MapSystemLabelName(label.Name ?? label.Id);

            var folder = new EmailFolder
            {
                Id = label.Id,
                Name = displayName,
                FullPath = label.Name ?? label.Id,
                TotalCount = label.MessagesTotal ?? 0,
                IsSystemFolder = isSystem,
                IsSelected = !string.Equals(label.Id, "SPAM", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(label.Id, "TRASH", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(label.Id, "CHAT", StringComparison.OrdinalIgnoreCase)
            };

            folderList.Add(folder);
        }

        return OrderFolders(folderList);
    }

    public async Task<int> CountMessagesAsync(BackupFilter filter, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var query = BuildQueryString(filter);
        var request = _gmailService!.Users.Messages.List(_userEmail);
        request.Q = query;
        request.IncludeSpamTrash = filter.IncludeSpamAndTrash;
        request.MaxResults = 500;

        int totalCount = 0;
        string? pageToken = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Messages != null)
            {
                totalCount += response.Messages.Count;
            }

            pageToken = response.NextPageToken;

            if (filter.MaxMessages.HasValue && totalCount >= filter.MaxMessages.Value)
            {
                return filter.MaxMessages.Value;
            }

        } while (!string.IsNullOrEmpty(pageToken));

        return totalCount;
    }

    public async IAsyncEnumerable<(EmailMessage Message, byte[] RawEmlBytes)> FetchMessagesAsync(
        BackupFilter filter,
        IProgress<BackupProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var labels = await GetFoldersAsync(cancellationToken);
        var labelLookup = labels.ToDictionary(l => l.Id, l => l.Name);

        var progressState = new BackupProgress
        {
            Phase = BackupPhase.QueryingServer,
            StatusMessage = "Interrogazione elenco messaggi su Google Workspace..."
        };
        progress?.Report(progressState);

        var query = BuildQueryString(filter);

        var request = _gmailService!.Users.Messages.List(_userEmail);
        request.Q = query;
        request.IncludeSpamTrash = filter.IncludeSpamAndTrash;
        request.MaxResults = 500;

        var messageHeaders = new List<Message>();
        string? pageToken = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken);

            if (response.Messages != null)
            {
                messageHeaders.AddRange(response.Messages);
            }

            pageToken = response.NextPageToken;

            if (filter.MaxMessages.HasValue && messageHeaders.Count >= filter.MaxMessages.Value)
            {
                messageHeaders = messageHeaders.Take(filter.MaxMessages.Value).ToList();
                break;
            }

            progressState.TotalMessages = messageHeaders.Count;
            progressState.StatusMessage = $"Trovati {messageHeaders.Count} messaggi nell'archivio...";
            progress?.Report(progressState);

        } while (!string.IsNullOrEmpty(pageToken));

        progressState.TotalMessages = messageHeaders.Count;
        progressState.Phase = BackupPhase.DownloadingMessages;
        progress?.Report(progressState);

        var channel = System.Threading.Channels.Channel.CreateBounded<(EmailMessage Message, byte[] RawEmlBytes)>(new System.Threading.Channels.BoundedChannelOptions(150)
        {
            FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true
        });

        int processed = 0;
        long totalBytes = 0;

        var producerTask = Task.Run(async () =>
        {
            try
            {
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 8,
                    CancellationToken = cancellationToken
                };

                await Parallel.ForEachAsync(messageHeaders, parallelOptions, async (msgHeader, ct) =>
                {
                    if (filter.ExistingMessageIds != null && filter.ExistingMessageIds.Contains(msgHeader.Id))
                    {
                        var cur = Interlocked.Increment(ref processed);
                        progressState.ProcessedMessages = cur;
                        progressState.StatusMessage = $"Già archiviato (saltato): ({cur}/{messageHeaders.Count})";
                        progress?.Report(progressState);
                        return;
                    }

                    Message? fullMessage = null;

                    // Retry automatico con exponential backoff per gestire errori transitori di rete o di Google (es. HTTP 500, 503, 429)
                    for (int attempt = 1; attempt <= 5; attempt++)
                    {
                        try
                        {
                            var getReq = _gmailService.Users.Messages.Get(_userEmail, msgHeader.Id);
                            getReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Raw;
                            fullMessage = await getReq.ExecuteAsync(ct);
                            break;
                        }
                        catch (Google.GoogleApiException gEx) when ((int)gEx.HttpStatusCode >= 500 || (int)gEx.HttpStatusCode == 429)
                        {
                            if (attempt == 5) break;
                            await Task.Delay(500 * attempt * attempt, ct);
                        }
                        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException)
                        {
                            if (attempt == 5) break;
                            await Task.Delay(500 * attempt, ct);
                        }
                        catch
                        {
                            if (attempt == 5) break;
                            await Task.Delay(500 * attempt, ct);
                        }
                    }

                    if (fullMessage == null || string.IsNullOrEmpty(fullMessage.Raw))
                        return;

                    var rawBytes = Base64UrlDecode(fullMessage.Raw);

                    string folderId = "INBOX";
                    string folderName = "Posta in arrivo";

                    if (fullMessage.LabelIds != null && fullMessage.LabelIds.Count > 0)
                    {
                        // Give priority to user custom folders first, then SENT, then INBOX
                        var customLabel = fullMessage.LabelIds.FirstOrDefault(id =>
                            !id.StartsWith("CATEGORY_", StringComparison.OrdinalIgnoreCase) &&
                            id != "UNREAD" && id != "STARRED" && id != "IMPORTANT" && id != "INBOX" && id != "CHAT");

                        if (customLabel != null && labelLookup.TryGetValue(customLabel, out var customName))
                        {
                            folderId = customLabel;
                            folderName = customName;
                        }
                        else if (fullMessage.LabelIds.Contains("SENT"))
                        {
                            folderId = "SENT";
                            folderName = "Posta inviata";
                        }
                        else if (fullMessage.LabelIds.Contains("INBOX"))
                        {
                            folderId = "INBOX";
                            folderName = "Posta in arrivo";
                        }
                        else
                        {
                            var firstId = fullMessage.LabelIds.FirstOrDefault(id => labelLookup.ContainsKey(id));
                            if (firstId != null)
                            {
                                folderId = firstId;
                                folderName = labelLookup[firstId];
                            }
                        }
                    }

                    var (emailMessage, _) = MimeParserHelper.ParseMimeMessage(rawBytes, folderId, folderName, fullMessage.Id);
                    emailMessage.ThreadId = fullMessage.ThreadId;

                    var currentProcessed = Interlocked.Increment(ref processed);
                    Interlocked.Add(ref totalBytes, rawBytes.Length);

                    progressState.ProcessedMessages = currentProcessed;
                    progressState.BytesDownloaded = Interlocked.Read(ref totalBytes);
                    progressState.CurrentMessageSubject = emailMessage.Subject;
                    progressState.StatusMessage = $"Download Workspace ({currentProcessed}/{messageHeaders.Count}): {emailMessage.Subject}";
                    progress?.Report(progressState);

                    await channel.Writer.WriteAsync((emailMessage, rawBytes), ct);
                });

                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, cancellationToken);

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }

        await producerTask;

        progressState.Phase = BackupPhase.Completed;
        progressState.StatusMessage = $"Download completato: {processed} messaggi salvati.";
        progress?.Report(progressState);
    }

    private static string BuildQueryString(BackupFilter filter)
    {
        var parts = new List<string>();

        if (filter.StartDate.HasValue)
            parts.Add($"after:{filter.StartDate.Value:yyyy/MM/dd}");

        if (filter.EndDate.HasValue)
            parts.Add($"before:{filter.EndDate.Value.AddDays(1):yyyy/MM/dd}");

        return string.Join(" ", parts);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Convert.FromBase64String(output);
    }

    private static string MapSystemLabelName(string labelId) => labelId.ToUpperInvariant() switch
    {
        "INBOX" => "Posta in arrivo",
        "SENT" => "Posta inviata",
        "DRAFT" => "Bozze",
        "TRASH" => "Cestino",
        "SPAM" => "Spam",
        "STARRED" => "Speciali",
        "IMPORTANT" => "Importanti",
        _ => labelId
    };

    private static List<EmailFolder> OrderFolders(List<EmailFolder> folders)
    {
        var priority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "INBOX", 1 },
            { "SENT", 2 },
            { "DRAFT", 3 },
            { "STARRED", 4 },
            { "IMPORTANT", 5 },
            { "TRASH", 98 },
            { "SPAM", 99 }
        };

        return folders
            .OrderBy(f => priority.GetValueOrDefault(f.Id, 50))
            .ThenBy(f => f.Name)
            .ToList();
    }

    private void EnsureConnected()
    {
        if (_gmailService == null)
            throw new InvalidOperationException("Provider Google Workspace non autenticato.");
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _gmailService?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
