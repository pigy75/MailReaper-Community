using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using GmailToPst.Providers.Helpers;

namespace GmailToPst.Providers.Microsoft365;

/// <summary>
/// Provider ad alte prestazioni per il download ed estrazione di caselle Microsoft 365 (Office 365 / Exchange Online)
/// tramite Microsoft Graph REST API v1.0 e streaming MIME RFC822 ($value).
/// </summary>
public class Microsoft365EmailProvider : IEmailProvider
{
    private readonly HttpClient _httpClient;
    private string _tenantId = string.Empty;
    private string _clientId = string.Empty;
    private string _clientSecret = string.Empty;
    private string _userEmail = string.Empty;
    private string _accessToken = string.Empty;
    private DateTime _tokenExpiration = DateTime.MinValue;
    private bool _disposed = false;

    public Microsoft365EmailProvider()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MailReaper", "1.1.0"));
    }

    public async Task<bool> ConnectAndAuthenticateAsync(AccountConfig config, CancellationToken cancellationToken = default)
    {
        _tenantId = config.M365TenantId?.Trim() ?? string.Empty;
        _clientId = config.M365ClientId?.Trim() ?? string.Empty;
        _clientSecret = config.M365ClientSecret?.Trim() ?? string.Empty;
        _userEmail = config.EmailAddress?.Trim() ?? config.M365UserEmail?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_userEmail))
            throw new ArgumentException("Indirizzo email della casella Microsoft 365 non specificato.");

        await EnsureValidTokenAsync(cancellationToken);

        // Test di connettività verso la casella dell'utente
        using var testReq = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(_userEmail)}/mailFolders?$top=1");
        testReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        using var testResp = await _httpClient.SendAsync(testReq, cancellationToken);
        if (!testResp.IsSuccessStatusCode)
        {
            var errContent = await testResp.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Impossibile accedere alla casella di '{_userEmail}' via Microsoft Graph ({(int)testResp.StatusCode}): {errContent}");
        }

        return true;
    }

    private async Task EnsureValidTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiration.AddMinutes(-5))
        {
            return;
        }

        _accessToken = await Microsoft365AdminService.GetAccessTokenAsync(_tenantId, _clientId, _clientSecret, cancellationToken);
        _tokenExpiration = DateTime.UtcNow.AddMinutes(55);
    }

    public async Task<List<EmailFolder>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureValidTokenAsync(cancellationToken);

        var folderList = new List<EmailFolder>();
        string? nextLink = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(_userEmail)}/mailFolders?$top=250";

        while (!string.IsNullOrEmpty(nextLink))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var req = new HttpRequestMessage(HttpMethod.Get, nextLink);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) break;

            var content = await resp.Content.ReadAsStringAsync(cancellationToken);
            var folderDto = JsonSerializer.Deserialize<GraphFoldersResponseDto>(content);

            if (folderDto?.Value != null)
            {
                foreach (var f in folderDto.Value)
                {
                    if (string.IsNullOrWhiteSpace(f.Id)) continue;

                    var isSystem = IsSystemFolder(f.DisplayName);
                    var isIgnored = string.Equals(f.DisplayName, "Junk Email", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(f.DisplayName, "Posta indesiderata", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(f.DisplayName, "Deleted Items", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(f.DisplayName, "Posta eliminata", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(f.DisplayName, "Spam", StringComparison.OrdinalIgnoreCase);

                    var emailFolder = new EmailFolder
                    {
                        Id = f.Id,
                        Name = MapFolderDisplayName(f.DisplayName ?? f.Id),
                        FullPath = f.DisplayName ?? f.Id,
                        TotalCount = f.TotalItemCount ?? 0,
                        IsSystemFolder = isSystem,
                        IsSelected = !isIgnored
                    };

                    folderList.Add(emailFolder);

                    // Recupera eventuali sotto-cartelle (childFolders)
                    if (f.ChildFolderCount > 0)
                    {
                        await LoadChildFoldersAsync(f.Id, f.DisplayName ?? f.Id, folderList, cancellationToken);
                    }
                }
            }

            nextLink = folderDto?.ODataNextLink;
        }

        return OrderFolders(folderList);
    }

    private async Task LoadChildFoldersAsync(string parentId, string parentPath, List<EmailFolder> targetList, CancellationToken cancellationToken)
    {
        string? nextLink = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(_userEmail)}/mailFolders/{parentId}/childFolders?$top=250";

        while (!string.IsNullOrEmpty(nextLink))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var req = new HttpRequestMessage(HttpMethod.Get, nextLink);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) break;

            var content = await resp.Content.ReadAsStringAsync(cancellationToken);
            var folderDto = JsonSerializer.Deserialize<GraphFoldersResponseDto>(content);

            if (folderDto?.Value != null)
            {
                foreach (var f in folderDto.Value)
                {
                    if (string.IsNullOrWhiteSpace(f.Id)) continue;

                    string fullPath = $"{parentPath}/{f.DisplayName ?? f.Id}";
                    var child = new EmailFolder
                    {
                        Id = f.Id,
                        Name = MapFolderDisplayName(f.DisplayName ?? f.Id),
                        FullPath = fullPath,
                        TotalCount = f.TotalItemCount ?? 0,
                        IsSystemFolder = false,
                        IsSelected = true
                    };

                    targetList.Add(child);

                    if (f.ChildFolderCount > 0)
                    {
                        await LoadChildFoldersAsync(f.Id, fullPath, targetList, cancellationToken);
                    }
                }
            }

            nextLink = folderDto?.ODataNextLink;
        }
    }

    public async Task<int> CountMessagesAsync(BackupFilter filter, CancellationToken cancellationToken = default)
    {
        var folders = await GetFoldersAsync(cancellationToken);
        int total = 0;

        foreach (var f in folders.Where(x => x.IsSelected))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string query = BuildMessagesQuery(_userEmail, f.Id, filter, top: 1, includeCount: true);

            using var req = new HttpRequestMessage(HttpMethod.Get, query);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            req.Headers.TryAddWithoutValidation("ConsistencyLevel", "eventual");

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                var content = await resp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("@odata.count", out var countProp))
                {
                    total += countProp.GetInt32();
                }
                else
                {
                    total += f.TotalCount;
                }
            }
            else
            {
                total += f.TotalCount;
            }
        }

        return total;
    }

    public async IAsyncEnumerable<(EmailMessage Message, byte[] RawEmlBytes)> FetchMessagesAsync(
        BackupFilter filter,
        IProgress<BackupProgress>? progress = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureValidTokenAsync(cancellationToken);

        var progressState = new BackupProgress
        {
            Phase = BackupPhase.ScanningFolders,
            StatusMessage = "Scansione cartelle Microsoft 365..."
        };
        progress?.Report(progressState);

        var allFolders = await GetFoldersAsync(cancellationToken);
        var selectedFolders = allFolders.Where(f => f.IsSelected).ToList();

        // 1. Raccoglie tutti gli ID dei messaggi da scaricare
        var messageQueue = new List<(string MessageId, string FolderId, string FolderName)>();

        progressState.Phase = BackupPhase.QueryingServer;
        progressState.StatusMessage = "Interrogazione messaggi in corso...";
        progress?.Report(progressState);

        foreach (var folder in selectedFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? nextLink = BuildMessagesQuery(_userEmail, folder.Id, filter, top: 100);

            while (!string.IsNullOrEmpty(nextLink))
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var req = new HttpRequestMessage(HttpMethod.Get, nextLink);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                using var resp = await _httpClient.SendAsync(req, cancellationToken);
                if (!resp.IsSuccessStatusCode) break;

                var content = await resp.Content.ReadAsStringAsync(cancellationToken);
                var msgsDto = JsonSerializer.Deserialize<GraphMessagesResponseDto>(content);

                if (msgsDto?.Value != null)
                {
                    foreach (var m in msgsDto.Value)
                    {
                        if (!string.IsNullOrWhiteSpace(m.Id))
                        {
                            messageQueue.Add((m.Id, folder.Id, folder.Name));
                        }
                    }
                }

                nextLink = msgsDto?.ODataNextLink;
            }
        }

        progressState.TotalMessages = messageQueue.Count;
        progressState.Phase = BackupPhase.DownloadingMessages;
        progressState.StatusMessage = $"Inizio download di {messageQueue.Count} messaggi da Microsoft 365...";
        progress?.Report(progressState);

        // 2. Canale asincrono per download parallelo dei contenuti MIME ($value)
        var channel = Channel.CreateBounded<(EmailMessage Message, byte[] RawEmlBytes)>(new BoundedChannelOptions(150)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true
        });

        int processed = 0;

        var producerTask = Task.Run(async () =>
        {
            try
            {
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 8,
                    CancellationToken = cancellationToken
                };

                await Parallel.ForEachAsync(messageQueue, parallelOptions, async (item, ct) =>
                {
                    if (filter.ExistingMessageIds != null && filter.ExistingMessageIds.Contains(item.MessageId))
                    {
                        var cur = Interlocked.Increment(ref processed);
                        progressState.ProcessedMessages = cur;
                        progressState.StatusMessage = $"Già archiviato (saltato): ({cur}/{messageQueue.Count})";
                        progress?.Report(progressState);
                        return;
                    }

                    byte[]? rawEmlBytes = null;

                    // Retry esponenziale con gestione Rate-Limiting (HTTP 429) di Microsoft Graph
                    for (int attempt = 1; attempt <= 5; attempt++)
                    {
                        try
                        {
                            await EnsureValidTokenAsync(ct);

                            string mimeUrl = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(_userEmail)}/messages/{item.MessageId}/$value";
                            using var getReq = new HttpRequestMessage(HttpMethod.Get, mimeUrl);
                            getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                            using var getResp = await _httpClient.SendAsync(getReq, ct);

                            if ((int)getResp.StatusCode == 429)
                            {
                                int retryAfter = 2;
                                if (getResp.Headers.RetryAfter?.Delta.HasValue == true)
                                {
                                    retryAfter = (int)getResp.Headers.RetryAfter.Delta.Value.TotalSeconds;
                                }
                                await Task.Delay(TimeSpan.FromSeconds(Math.Max(retryAfter, 2)), ct);
                                continue;
                            }

                            if (getResp.IsSuccessStatusCode)
                            {
                                rawEmlBytes = await getResp.Content.ReadAsByteArrayAsync(ct);
                                break;
                            }
                        }
                        catch (Exception) when (attempt < 5)
                        {
                            await Task.Delay(400 * attempt, ct);
                        }
                    }

                    if (rawEmlBytes == null || rawEmlBytes.Length == 0)
                        return;

                    var (parsedMsg, _) = MimeParserHelper.ParseMimeMessage(rawEmlBytes, item.FolderId, item.FolderName, item.MessageId);

                    await channel.Writer.WriteAsync((parsedMsg, rawEmlBytes), ct);

                    var count = Interlocked.Increment(ref processed);
                    progressState.ProcessedMessages = count;
                    progressState.StatusMessage = $"Scaricato: {parsedMsg.Subject ?? "(Nessun oggetto)"} ({count}/{messageQueue.Count})";
                    progress?.Report(progressState);
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
    }

    public static string BuildMessagesQuery(string userEmail, string folderId, BackupFilter filter, int top = 100, bool includeCount = false)
    {
        var queryParams = new List<string>
        {
            $"$top={top}",
            "$select=id,subject,receivedDateTime,hasAttachments",
            "$orderby=receivedDateTime desc"
        };

        if (includeCount)
        {
            queryParams.Add("$count=true");
        }

        var filters = new List<string>();

        if (filter.Year.HasValue)
        {
            var start = new DateTime(filter.Year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(filter.Year.Value + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            filters.Add($"receivedDateTime ge {start:yyyy-MM-ddTHH:mm:ssZ} and receivedDateTime lt {end:yyyy-MM-ddTHH:mm:ssZ}");
        }
        else if (filter.StartDate.HasValue || filter.EndDate.HasValue)
        {
            if (filter.StartDate.HasValue)
            {
                var s = filter.StartDate.Value.ToUniversalTime();
                filters.Add($"receivedDateTime ge {s:yyyy-MM-ddTHH:mm:ssZ}");
            }
            if (filter.EndDate.HasValue)
            {
                var e = filter.EndDate.Value.ToUniversalTime();
                filters.Add($"receivedDateTime le {e:yyyy-MM-ddTHH:mm:ssZ}");
            }
        }

        if (filters.Any())
        {
            queryParams.Add($"$filter={string.Join(" and ", filters)}");
        }

        return $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(userEmail)}/mailFolders/{folderId}/messages?{string.Join("&", queryParams)}";
    }

    private static bool IsSystemFolder(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var sys = new[] { "inbox", "sent items", "drafts", "deleted items", "junk email", "archive", "outbox" };
        return sys.Contains(name.Trim().ToLowerInvariant());
    }

    private static string MapFolderDisplayName(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "inbox" => "Posta in arrivo",
            "sent items" => "Posta inviata",
            "drafts" => "Bozze",
            "deleted items" => "Posta eliminata",
            "junk email" => "Posta indesiderata",
            "archive" => "Archivio",
            "outbox" => "Posta in uscita",
            _ => name
        };
    }

    private static List<EmailFolder> OrderFolders(List<EmailFolder> folders)
    {
        var priorityOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Posta in arrivo", 1 },
            { "Posta inviata", 2 },
            { "Bozze", 3 },
            { "Archivio", 4 },
            { "Posta indesiderata", 90 },
            { "Posta eliminata", 99 }
        };

        return folders.OrderBy(f => priorityOrder.TryGetValue(f.Name, out var p) ? p : 50)
                      .ThenBy(f => f.Name)
                      .ToList();
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _httpClient?.Dispose();
            _disposed = true;
        }
        return ValueTask.CompletedTask;
    }
}

internal class GraphFoldersResponseDto
{
    [JsonPropertyName("@odata.nextLink")]
    public string? ODataNextLink { get; set; }

    [JsonPropertyName("value")]
    public List<GraphFolderDto>? Value { get; set; }
}

internal class GraphFolderDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("totalItemCount")]
    public int? TotalItemCount { get; set; }

    [JsonPropertyName("childFolderCount")]
    public int? ChildFolderCount { get; set; }
}

internal class GraphMessagesResponseDto
{
    [JsonPropertyName("@odata.nextLink")]
    public string? ODataNextLink { get; set; }

    [JsonPropertyName("value")]
    public List<GraphMessageHeaderDto>? Value { get; set; }
}

internal class GraphMessageHeaderDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("receivedDateTime")]
    public DateTime? ReceivedDateTime { get; set; }
}
