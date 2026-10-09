namespace GmailToPst.Core.Models;

public enum AccountType
{
    GmailOAuth = 0,
    ImapAppPassword = 1,
    GoogleWorkspaceServiceAccount = 2,
    Microsoft365SingleAccount = 3,
    Microsoft365TenantAdmin = 4,
    Pop3 = 5
}

public class AccountConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EmailAddress { get; set; } = string.Empty;
    public AccountType Type { get; set; } = AccountType.GmailOAuth;

    // OAuth 2.0
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    // IMAP / POP3
    public string ImapHost { get; set; } = "imap.gmail.com";
    public int ImapPort { get; set; } = 993;
    public string Pop3Host { get; set; } = "pop.gmail.com";
    public int Pop3Port { get; set; } = 995;
    public bool UseSsl { get; set; } = true;
    public string AppPassword { get; set; } = string.Empty;

    // Service Account (Google Workspace)
    public string ServiceAccountKeyFilePath { get; set; } = string.Empty;
    public string WorkspaceAdminEmail { get; set; } = string.Empty;
    public List<WorkspaceUser> DiscoveredUsers { get; set; } = new();

    // Microsoft 365 (Single Account & Tenant Admin)
    public string M365TenantId { get; set; } = string.Empty;
    public string M365ClientId { get; set; } = string.Empty;
    public string M365ClientSecret { get; set; } = string.Empty;
    public string M365UserEmail { get; set; } = string.Empty;
    public bool M365IsAdminMode { get; set; } = true;
}
