using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GmailToPst.Core.Services;

/// <summary>
/// Integrazione con GitHub REST API v3 per la verifica e notifica di nuove release.
/// Utilizzato da MailReaper Community Edition per connettersi a GitHub e notificare gli utenti.
/// </summary>
public class GitHubUpdateService
{
    private const string RepoOwner = "pigy75";
    private const string RepoName = "MailReaper-Community";
    private const string GitHubApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private static readonly HttpClient HttpClient = new HttpClient();

    static GitHubUpdateService()
    {
        // GitHub REST API richiede obbligatoriamente un User-Agent valido
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MailReaper", "1.0.0"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        HttpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<GitHubReleaseResult> CheckForUpdatesAsync(string currentVersion = "1.0.0")
    {
        try
        {
            var response = await HttpClient.GetAsync(GitHubApiUrl);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new GitHubReleaseResult
                {
                    Success = true,
                    IsUpToDate = true,
                    Message = "Nessuna release pubblica trovata sul repository GitHub. Sei all'ultima versione di sviluppo."
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new GitHubReleaseResult
                {
                    Success = false,
                    Message = $"Risposta API GitHub: {(int)response.StatusCode} {response.ReasonPhrase}"
                };
            }

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubReleaseDto>(json);

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new GitHubReleaseResult
                {
                    Success = true,
                    IsUpToDate = true,
                    Message = "Sei già all'ultima versione disponibile."
                };
            }

            string latestTag = release.TagName.TrimStart('v', 'V');
            string current = currentVersion.TrimStart('v', 'V');

            bool isNewer = false;
            if (Version.TryParse(latestTag, out var latestVer) && Version.TryParse(current, out var currentVer))
            {
                isNewer = latestVer > currentVer;
            }
            else
            {
                isNewer = !string.Equals(latestTag, current, StringComparison.OrdinalIgnoreCase);
            }

            return new GitHubReleaseResult
            {
                Success = true,
                IsUpToDate = !isNewer,
                LatestVersion = release.TagName,
                ReleaseName = release.Name ?? release.TagName,
                ReleaseNotes = release.Body ?? string.Empty,
                HtmlUrl = release.HtmlUrl ?? $"https://github.com/{RepoOwner}/{RepoName}/releases",
                Message = isNewer 
                    ? $"Nuova versione disponibile: {release.TagName}" 
                    : $"La tua versione (v{currentVersion}) è aggiornata."
            };
        }
        catch (Exception ex)
        {
            return new GitHubReleaseResult
            {
                Success = false,
                Message = $"Impossibile verificare gli aggiornamenti via GitHub API: {ex.Message}"
            };
        }
    }
}

public class GitHubReleaseResult
{
    public bool Success { get; set; }
    public bool IsUpToDate { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

internal class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("published_at")]
    public string? PublishedAt { get; set; }
}
