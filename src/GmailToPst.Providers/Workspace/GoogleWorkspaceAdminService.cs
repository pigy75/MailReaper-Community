using Google.Apis.Admin.Directory.directory_v1;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using GmailToPst.Core.Models;
using System.IO;

namespace GmailToPst.Providers.Workspace;

public static class GoogleWorkspaceAdminService
{
    public static async Task<List<WorkspaceUser>> GetDomainUsersAsync(string serviceAccountJsonPath, string adminEmail, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(serviceAccountJsonPath))
        {
            throw new FileNotFoundException("File delle credenziali del Service Account non trovato.", serviceAccountJsonPath);
        }

        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            throw new ArgumentException("L'indirizzo email dell'amministratore è obbligatorio.", nameof(adminEmail));
        }

        var credential = GoogleCredential.FromFile(serviceAccountJsonPath)
            .CreateScoped(new[]
            {
                DirectoryService.Scope.AdminDirectoryUserReadonly,
                "https://mail.google.com/"
            })
            .CreateWithUser(adminEmail);

        var directoryService = new DirectoryService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "GmailToPst-WorkspaceExplorer"
        });

        var usersList = new List<WorkspaceUser>();
        string? pageToken = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var req = directoryService.Users.List();
            req.Customer = "my_customer";
            req.MaxResults = 100;
            req.PageToken = pageToken;
            req.OrderBy = UsersResource.ListRequest.OrderByEnum.Email;

            var resp = await req.ExecuteAsync(cancellationToken);
            if (resp.UsersValue != null)
            {
                foreach (var u in resp.UsersValue)
                {
                    var email = u.PrimaryEmail ?? "";
                    if (string.IsNullOrWhiteSpace(email)) continue;

                    usersList.Add(new WorkspaceUser
                    {
                        Email = email,
                        FullName = u.Name?.FullName ?? "",
                        GivenName = u.Name?.GivenName ?? "",
                        FamilyName = u.Name?.FamilyName ?? "",
                        IsAdmin = u.IsAdmin ?? false,
                        IsSuspended = u.Suspended ?? false,
                        IsSelected = true
                    });
                }
            }

            pageToken = resp.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        return usersList;
    }
}
