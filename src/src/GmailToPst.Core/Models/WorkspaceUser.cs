namespace GmailToPst.Core.Models;

public class WorkspaceUser
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string GivenName { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; } = false;
    public bool IsSuspended { get; set; } = false;
    public bool IsSelected { get; set; } = true;

    public string DisplayText => string.IsNullOrWhiteSpace(FullName) 
        ? Email 
        : $"{FullName} ({Email}){(IsAdmin ? " [Admin]" : "")}";
}
