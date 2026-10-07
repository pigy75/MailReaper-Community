namespace GmailToPst.Core.Models;

public enum AccountType
{
    GmailOAuth = 0,
    ImapAppPassword = 1,
    GoogleWorkspaceServiceAccount = 2
}

public class AccountConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EmailAddress { get; set; } = string.Empty;
    public AccountType Type { get; set; } = AccountType.GmailOAuth;

    // OAuth 2.0
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    // IMAP
    public string ImapHost { get; set; } = "imap.gmail.com";
    public int ImapPort { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string AppPassword { get; set; } = string.Empty;

    // Service Account (Google Workspace)
    public string ServiceAccountKeyFilePath { get; set; } = string.Empty;
    public string WorkspaceAdminEmail { get; set; } = string.Empty;
    public List<WorkspaceUser> DiscoveredUsers { get; set; } = new();
}
