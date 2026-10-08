using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GmailToPst.Core.Models;

namespace GmailToPst.Providers.Microsoft365;

/// <summary>
/// Servizio per la gestione amministrativa e discovery dell'intero dominio / tenant Microsoft 365 (Office 365).
/// Utilizza le Microsoft Graph API v1.0 con permessi applicativi (Client Credentials).
/// </summary>
public static class Microsoft365AdminService
{
    private static readonly HttpClient HttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static Microsoft365AdminService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MailReaper", "1.1.0"));
    }

    /// <summary>
    /// Ottiene un Bearer Access Token per Microsoft Graph via Client Credentials Grant.
    /// </summary>
    public static async Task<string> GetAccessTokenAsync(string tenantId, string clientId, string clientSecret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Il Tenant ID (Directory ID) di Microsoft 365 è obbligatorio.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Il Client ID (Application ID) di Microsoft Entra ID è obbligatorio.", nameof(clientId));
        if (string.IsNullOrWhiteSpace(clientSecret))
            throw new ArgumentException("Il Client Secret di Microsoft Entra ID è obbligatorio.", nameof(clientSecret));

        string tokenUrl = $"https://login.microsoftonline.com/{tenantId.Trim()}/oauth2/v2.0/token";

        var body = new Dictionary<string, string>
        {
            ["client_id"] = clientId.Trim(),
            ["client_secret"] = clientSecret.Trim(),
            ["scope"] = "https://graph.microsoft.com/.default",
            ["grant_type"] = "client_credentials"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(body)
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string errorDetails = responseContent;
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("error_description", out var desc))
                {
                    errorDetails = desc.GetString() ?? responseContent;
                }
            }
            catch { }

            throw new InvalidOperationException($"Autenticazione Microsoft 365 fallita ({(int)response.StatusCode}): {errorDetails}");
        }

        using var jsonDoc = JsonDocument.Parse(responseContent);
        if (jsonDoc.RootElement.TryGetProperty("access_token", out var tokenProp))
        {
            var token = tokenProp.GetString();
            if (!string.IsNullOrEmpty(token)) return token;
        }

        throw new InvalidOperationException("Nessun access token restituito da Microsoft Entra ID.");
    }

    /// <summary>
    /// Interroga Microsoft Graph per rilevare tutti gli utenti e le caselle di posta del tenant Microsoft 365.
    /// </summary>
    public static async Task<List<WorkspaceUser>> GetTenantUsersAsync(string tenantId, string clientId, string clientSecret, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(tenantId, clientId, clientSecret, cancellationToken);
        var usersList = new List<WorkspaceUser>();

        string? nextLink = "https://graph.microsoft.com/v1.0/users?$select=id,displayName,userPrincipalName,mail,givenName,surname,accountEnabled&$top=999";

        while (!string.IsNullOrEmpty(nextLink))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var req = new HttpRequestMessage(HttpMethod.Get, nextLink);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await HttpClient.SendAsync(req, cancellationToken);
            var content = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Errore durante la lettura degli utenti da Microsoft Graph ({(int)resp.StatusCode}): {content}");
            }

            var graphResult = JsonSerializer.Deserialize<GraphUsersResponseDto>(content);
            if (graphResult?.Value != null)
            {
                foreach (var u in graphResult.Value)
                {
                    var email = !string.IsNullOrWhiteSpace(u.Mail) ? u.Mail : u.UserPrincipalName;
                    if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                        continue;

                    usersList.Add(new WorkspaceUser
                    {
                        Email = email.Trim(),
                        FullName = u.DisplayName ?? email,
                        GivenName = u.GivenName ?? "",
                        FamilyName = u.Surname ?? "",
                        IsAdmin = false,
                        IsSuspended = !(u.AccountEnabled ?? true),
                        IsSelected = true
                    });
                }
            }

            nextLink = graphResult?.ODataNextLink;
        }

        return usersList.OrderBy(u => u.Email, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

internal class GraphUsersResponseDto
{
    [JsonPropertyName("@odata.nextLink")]
    public string? ODataNextLink { get; set; }

    [JsonPropertyName("value")]
    public List<GraphUserDto>? Value { get; set; }
}

internal class GraphUserDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("userPrincipalName")]
    public string? UserPrincipalName { get; set; }

    [JsonPropertyName("mail")]
    public string? Mail { get; set; }

    [JsonPropertyName("givenName")]
    public string? GivenName { get; set; }

    [JsonPropertyName("surname")]
    public string? Surname { get; set; }

    [JsonPropertyName("accountEnabled")]
    public bool? AccountEnabled { get; set; }
}
