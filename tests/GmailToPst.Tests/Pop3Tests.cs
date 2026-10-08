using GmailToPst.Core.Models;
using GmailToPst.Providers.Pop3;
using Xunit;

namespace GmailToPst.Tests;

public class Pop3Tests
{
    [Fact]
    public void AccountConfig_Pop3Defaults_AreCorrect()
    {
        var config = new AccountConfig
        {
            Type = AccountType.Pop3,
            EmailAddress = "test@example.com",
            Pop3Host = "pop.example.com",
            Pop3Port = 995,
            UseSsl = true
        };

        Assert.Equal(AccountType.Pop3, config.Type);
        Assert.Equal("pop.example.com", config.Pop3Host);
        Assert.Equal(995, config.Pop3Port);
        Assert.True(config.UseSsl);
    }

    [Fact]
    public async Task Pop3Provider_ThrowsBeforeConnection()
    {
        var provider = new Pop3Provider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetFoldersAsync());
        await provider.DisposeAsync();
    }
}
