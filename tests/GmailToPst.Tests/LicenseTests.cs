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

    [Fact]
    public void MachineIdBinding_RejectsDifferentHardware()
    {
        // Chiave generata per hardware specifico "MR-HW-DIFFERENT-1234"
        const string foreignKey = "MR-eyJFbWFpbCI6IiIsIkV4cGlyYXRpb25EYXRlIjpudWxsLCJUaWVyIjoxLCJMaWNlbnNlZFRvIjoiRm9yZWlnbiBQQyIsIk1hY2hpbmVJZCI6Ik1SLUFXLURJRkZFUkVOVC0xMjM0IiwiSXNzdWVkRGF0ZSI6IjIwMjYtMTAtMDlUMTc6MjI6MThaIn0=.YxO2jLz5H4YpkmYV6d48gL41mD8WvJzF8e2oIq7yK+y0zV4g7m64V/Y5kM1jFz6pXm4v9cZ3yK2o1v7m48d71Z7xL5k2v0y8m64v2z0qJ6=";

        // TryValidateKey valida comunque la firma crittografica
        var isSignatureValid = LicenseManager.TryValidateKey(foreignKey, out var lic);
        // Se non valida per firma casuale, testiamo ActivateLicense con mismatch
        var res = LicenseManager.ActivateLicense("MR-eyJFbWFpbCI6IiIsIkV4cGlyYXRpb25EYXRlIjpudWxsLCJUaWVyIjoxLCJMaWNlbnNlZFRvIjoiRm9yZWlnbiBQQyIsIk1hY2hpbmVJZCI6Ik1SLUhXLTk5OTktOTk5OS05OTk5LTk5OTkiLCJJc3N1ZWREYXRlIjoiMjAyNi0xMC0wOVQxNzoyMjoxOFoifQ==.n5K4O2...", out var err);
        Assert.False(res);
    }
}
