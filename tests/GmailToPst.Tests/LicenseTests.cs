using GmailToPst.Core.Licensing;
using Xunit;

namespace GmailToPst.Tests;

public class LicenseTests
{
    [Fact]
    public void FreeLicense_HasCorrectDefaultsAndLimits()
    {
        var freeLic = new LicenseInfo();
        Assert.Equal(LicenseTier.Free, freeLic.Tier);
        Assert.False(freeLic.IsProOrAbove);
        Assert.Equal(5L * 1024 * 1024 * 1024, freeLic.MaxPstExportSizeBytes);
        Assert.True(freeLic.CanExportPst(4L * 1024 * 1024 * 1024)); // 4 GB OK
        Assert.False(freeLic.CanExportPst(6L * 1024 * 1024 * 1024)); // 6 GB Blocked
        Assert.False(freeLic.AllowWorkspaceDomainWide);
        Assert.False(freeLic.AllowConcurrentSync);
    }

    [Fact]
    public void ValidateGeneratedKey_Succeeds()
    {
        const string key = "MR-eyJFbWFpbCI6IiIsIkxpY2Vuc2VkVG8iOiJBemllbmRhIFRlc3QiLCJFeHBpcmF0aW9uRGF0ZSI6bnVsbCwiVGllciI6MSwiSXNzdWVkRGF0ZSI6IjIwMjYtMTAtMDdUMTg6NTc6MDZaIn0=.pqj3tIYoUn1aMqVf3/cAqDoP3TOC0gyYvjxO4AA39otZrFE7iVd4FiKtO+6R8nMw8utwUq0hixXN5+7Uj1FiCWcPlxjEubnxMkYEoJnfAwZcagD6tlUfVp3BBLlyaJ1pUdu2qxcTDy8wL7pA4kEmoOIZMnGxp5J+a8wZNhrMrebTr48IP0GUjkTSiy8TFC24I5+kTkWhnYpCxSa68yTtvNUfF1Ct8mky42Gnu0ue3KruFmVhENRzb76tDqQyAI/enbtnPEPNUw4+cY4xWAYm12kILOcilrv5oC6AFoSMoFi91bb/MUyo320qYRNK95kj3v/hrgWfmQcRC5/nrvUUXw==";

        var isValid = LicenseManager.TryValidateKey(key, out var lic);
        Assert.True(isValid);
        Assert.NotNull(lic);
        Assert.Equal("Azienda Test", lic.LicensedTo);
        Assert.Equal(LicenseTier.Pro, lic.Tier);
        Assert.True(lic.IsProOrAbove);
        Assert.True(lic.CanExportPst(200L * 1024 * 1024 * 1024)); // 200 GB OK for PRO!
        Assert.True(lic.AllowWorkspaceDomainWide);
    }

    [Fact]
    public void TamperedKey_FailsValidation()
    {
        const string tamperedKey = "MR-eyJFbWFpbCI6IiIsIkxpY2Vuc2VkVG8iOiJTRkxBU0lGSUNBVE8iLCJFeHBpcmF0aW9uRGF0ZSI6bnVsbCwiVGllciI6MSwiSXNzdWVkRGF0ZSI6IjIwMjYtMTAtMDdUMTg6NTc6MDZaIn0=.pqj3tIYoUn1aMqVf3/cAqDoP3TOC0gyYvjxO4AA39otZrFE7iVd4FiKtO+6R8nMw8utwUq0hixXN5+7Uj1FiCWcPlxjEubnxMkYEoJnfAwZcagD6tlUfVp3BBLlyaJ1pUdu2qxcTDy8wL7pA4kEmoOIZMnGxp5J+a8wZNhrMrebTr48IP0GUjkTSiy8TFC24I5+kTkWhnYpCxSa68yTtvNUfF1Ct8mky42Gnu0ue3KruFmVhENRzb76tDqQyAI/enbtnPEPNUw4+cY4xWAYm12kILOcilrv5oC6AFoSMoFi91bb/MUyo320qYRNK95kj3v/hrgWfmQcRC5/nrvUUXw==";

        var isValid = LicenseManager.TryValidateKey(tamperedKey, out var lic);
        Assert.False(isValid);
        Assert.Null(lic);
    }
}
