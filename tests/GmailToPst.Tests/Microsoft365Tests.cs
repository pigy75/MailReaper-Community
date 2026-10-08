using GmailToPst.Core.Models;
using GmailToPst.Providers.Microsoft365;
using Xunit;

namespace GmailToPst.Tests;

public class Microsoft365Tests
{
    [Fact]
    public void AccountConfig_StoresM365TenantAndCredentials()
    {
        var config = new AccountConfig
        {
            Type = AccountType.Microsoft365TenantAdmin,
            EmailAddress = "admin@contoso.onmicrosoft.com",
            M365TenantId = "00000000-1111-2222-3333-444444444444",
            M365ClientId = "55555555-6666-7777-8888-999999999999",
            M365ClientSecret = "SuperSecretKey123!",
            M365UserEmail = "user@contoso.onmicrosoft.com",
            M365IsAdminMode = true,
            DiscoveredUsers = new List<WorkspaceUser>
            {
                new() { Email = "user@contoso.onmicrosoft.com", FullName = "Test User" },
                new() { Email = "admin@contoso.onmicrosoft.com", FullName = "Admin User" }
            }
        };

        Assert.Equal(AccountType.Microsoft365TenantAdmin, config.Type);
        Assert.Equal("00000000-1111-2222-3333-444444444444", config.M365TenantId);
        Assert.Equal("55555555-6666-7777-8888-999999999999", config.M365ClientId);
        Assert.Equal("SuperSecretKey123!", config.M365ClientSecret);
        Assert.Equal("user@contoso.onmicrosoft.com", config.M365UserEmail);
        Assert.True(config.M365IsAdminMode);
        Assert.Equal(2, config.DiscoveredUsers.Count);
    }

    [Fact]
    public void Microsoft365EmailProvider_BuildsCorrectQueryUrlWithFilter()
    {
        var filter = BackupFilter.ForYear(2023);

        string query = Microsoft365EmailProvider.BuildMessagesQuery("user@contoso.com", "inbox_folder_id", filter, top: 50);

        Assert.Contains("mailFolders/inbox_folder_id/messages", query);
        Assert.Contains("$top=50", query);
        Assert.Contains("$filter=", query);
        Assert.Contains("receivedDateTime ge 2023-01-01T00:00:00Z", query);
        Assert.Contains("receivedDateTime lt 2024-01-01T00:00:00Z", query);
    }
}
