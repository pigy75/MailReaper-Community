using System.Runtime.CompilerServices;
using System.Text;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using GmailToPst.Providers.Helpers;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace GmailToPst.Providers.GmailApi;

public class GmailApiProvider : IEmailProvider
{
    private GmailService? _service;
    private AccountConfig? _config;
    private readonly string _tokenStoragePath;

    public GmailApiProvider(string? tokenStoragePath = null)
    {
        _tokenStoragePath = tokenStoragePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GmailToPst", "Tokens");
    }

    public async Task<bool> ConnectAndAuthenticateAsync(AccountConfig config, CancellationToken cancellationToken = default)
    {
        _config = config;

        UserCredential credential;
        var clientSecrets = new ClientSecrets
        {
            ClientId = config.ClientId,
            ClientSecret = config.ClientSecret
        };

        var scopes = new[] { GmailService.Scope.GmailReadonly };

        var dataStore = new FileDataStore(_tokenStoragePath, true);
        credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets,
            scopes,
            string.IsNullOrWhiteSpace(config.EmailAddress) ? "user" : config.EmailAddress,
            cancellationToken,
            dataStore);

        _service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "GmailToPst Backup Explorer"
        });

        return true;
    }

    public async Task<List<EmailFolder>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var labelsResponse = await _service!.Users.Labels.List("me").ExecuteAsync(cancellationToken);
        var folders = new List<EmailFolder>();

        if (labelsResponse.Labels == null)
            return folders;

        foreach (var label in labelsResponse.Labels)
        {
            // Ottieni i dettagli del label per avere il conteggio totale
            var labelDetails = await _service.Users.Labels.Get("me", label.Id).ExecuteAsync(cancellationToken);

            var isSystem = label.Type == "system";
            var friendlyName = GetFriendlyLabelName(label.Name);

            folders.Add(new EmailFolder
            {
                Id = label.Id,
                Name = friendlyName,
                FullPath = label.Name,
                TotalCount = labelDetails.MessagesTotal ?? 0,
                IsSystemFolder = isSystem,
                IsSelected = !isSystem || label.Id == "INBOX" || label.Id == "SENT"
            });
        }

        // Costruisci gerarchia logica per le etichette nidificate (es: "Lavoro/Progetti")
        return BuildFolderHierarchy(folders);
    }

    public async Task<int> CountMessagesAsync(BackupFilter filter, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        var query = BuildGmailQuery(filter);

        var request = _service!.Users.Messages.List("me");
        request.Q = query;
        request.IncludeSpamTrash = filter.IncludeSpamAndTrash;
        request.MaxResults = 500;

        int count = 0;
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken);
            if (response.Messages != null)
            {
                count += response.Messages.Count;
            }
            pageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        return count;
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
            StatusMessage = "Interrogazione dei server Gmail con filtro temporale..."
        };
        progress?.Report(progressState);

        var query = BuildGmailQuery(filter);
        var request = _service!.Users.Messages.List("me");
        request.Q = query;
        request.IncludeSpamTrash = filter.IncludeSpamAndTrash;
        request.MaxResults = 250;

        var messageIds = new List<string>();
        string? pageToken = null;

        do
        {
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken);
            if (response.Messages != null)
            {
                messageIds.AddRange(response.Messages.Select(m => m.Id));
            }
            pageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        progressState.TotalMessages = messageIds.Count;
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

                await Parallel.ForEachAsync(messageIds, parallelOptions, async (msgId, ct) =>
                {
                    if (filter.ExistingMessageIds != null && filter.ExistingMessageIds.Contains(msgId))
                    {
                        var cur = Interlocked.Increment(ref processed);
                        progressState.ProcessedMessages = cur;
                        progressState.StatusMessage = $"Già archiviato (saltato): ({cur}/{messageIds.Count})";
                        progress?.Report(progressState);
                        return;
                    }

                    Google.Apis.Gmail.v1.Data.Message? gmailMsg = null;

                    for (int attempt = 1; attempt <= 5; attempt++)
                    {
                        try
                        {
                            var getReq = _service.Users.Messages.Get("me", msgId);
                            getReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Raw;
                            gmailMsg = await getReq.ExecuteAsync(ct);
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

                    if (gmailMsg == null || string.IsNullOrEmpty(gmailMsg.Raw))
                        return;

                    byte[] rawBytes = Base64UrlDecode(gmailMsg.Raw);

                    string folderId = gmailMsg.LabelIds?.FirstOrDefault() ?? "INBOX";
                    string folderName = labelLookup.TryGetValue(folderId, out var name) ? name : folderId;

                    var (message, _) = MimeParserHelper.ParseMimeMessage(rawBytes, folderId, folderName, msgId);
                    message.ThreadId = gmailMsg.ThreadId;

                    var currentProcessed = Interlocked.Increment(ref processed);
                    Interlocked.Add(ref totalBytes, rawBytes.Length);

                    progressState.ProcessedMessages = currentProcessed;
                    progressState.BytesDownloaded = Interlocked.Read(ref totalBytes);
                    progressState.CurrentMessageSubject = message.Subject;
                    progressState.StatusMessage = $"Scaricamento ({currentProcessed}/{messageIds.Count}): {message.Subject}";
                    progress?.Report(progressState);

                    await channel.Writer.WriteAsync((message, rawBytes), ct);
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
        progressState.StatusMessage = $"Completato: {processed} messaggi scaricati ({progressState.FormattedBytes}).";
        progress?.Report(progressState);
    }

    private string BuildGmailQuery(BackupFilter filter)
    {
        var sb = new StringBuilder();

        if (filter.StartDate.HasValue)
        {
            sb.Append($"after:{filter.StartDate.Value:yyyy/MM/dd} ");
        }

        if (filter.EndDate.HasValue)
        {
            // Per Gmail API, "before" è esclusivo, quindi aggiungiamo 1 giorno se coincide con la fine
            var endPlusOne = filter.EndDate.Value.AddDays(1);
            sb.Append($"before:{endPlusOne:yyyy/MM/dd} ");
        }

        if (filter.SelectedFolderIds.Any())
        {
            var labelQueries = filter.SelectedFolderIds.Select(id => $"label:{id}");
            sb.Append($"({string.Join(" OR ", labelQueries)}) ");
        }

        if (!filter.IncludeSpamAndTrash)
        {
            sb.Append("-in:spam -in:trash ");
        }

        return sb.ToString().Trim();
    }

    private static string GetFriendlyLabelName(string labelName)
    {
        return labelName switch
        {
            "INBOX" => "Posta in arrivo",
            "SENT" => "Posta inviata",
            "DRAFT" => "Bozze",
            "SPAM" => "Spam",
            "TRASH" => "Cestino",
            "STARRED" => "Speciali",
            "IMPORTANT" => "Importanti",
            "UNREAD" => "Non letti",
            _ => labelName
        };
    }

    private static List<EmailFolder> BuildFolderHierarchy(List<EmailFolder> flatList)
    {
        // Identifica etichette con / come sottocartelle
        var rootList = new List<EmailFolder>();
        var lookup = flatList.ToDictionary(f => f.FullPath, f => f);

        foreach (var folder in flatList)
        {
            if (folder.FullPath.Contains('/'))
            {
                var parentPath = folder.FullPath.Substring(0, folder.FullPath.LastIndexOf('/'));
                if (lookup.TryGetValue(parentPath, out var parentFolder))
                {
                    folder.ParentId = parentFolder.Id;
                    parentFolder.SubFolders.Add(folder);
                    continue;
                }
            }

            rootList.Add(folder);
        }

        return rootList;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 0: break;
            case 2: output += "=="; break;
            case 3: output += "="; break;
            default: throw new FormatException("Stringa Base64Url non valida.");
        }
        return Convert.FromBase64String(output);
    }

    private void EnsureConnected()
    {
        if (_service == null)
        {
            throw new InvalidOperationException("Il provider Gmail API non è connesso. Esegui prima l'autenticazione.");
        }
    }

    public ValueTask DisposeAsync()
    {
        _service?.Dispose();
        _service = null;
        return ValueTask.CompletedTask;
    }
}
