using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GmailToPst.Core.Licensing;

public static class LicenseManager
{
    private const string PublicKeyXml = "<RSAKeyValue><Modulus>xzjMWrAdwAA+HtET5gtYeLbu1Aedv6NvbG655fSe6VFkbuo3/sgRtWaGsSCysrA8n9vHb89CqDTXQDqNfBNvX097OMNVe0BSGzO8YJvuQ2Kyr22USHatd/iyTlaxE+8wvi98K2RrmG2k+mRt6QI7NLCK3/36RyrWPRD/dB+91c8MIyXXAJDCz4P3RwgCP1EU+hlJnDydbJy/SfTrD82MhrhrcRlwtw3FgyQx8Yrzp+GF6Evn1DQaLyE6sXLRfnvkXja4pMvICur8zNj/y9jY9vfdtiVXRi8JL8l3qK6wMGZxHeZwBLxjM7Ec30EfSNyS7h7c8LyjQmK3I2QfexFfkQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    private static readonly string LicenseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GmailToPst");

    private static readonly string LicenseFilePath = Path.Combine(LicenseDirectory, "license.key");

    private static LicenseInfo _currentLicense = new();

    public static LicenseInfo CurrentLicense => _currentLicense;

    public static event Action? LicenseChanged;

    static LicenseManager()
    {
        LoadLicense();
    }

    public static void LoadLicense()
    {
        try
        {
            if (File.Exists(LicenseFilePath))
            {
                var key = File.ReadAllText(LicenseFilePath).Trim();
                if (TryValidateKey(key, out var lic) && lic != null)
                {
                    _currentLicense = lic;
                    return;
                }
            }
        }
        catch { }

        _currentLicense = new LicenseInfo(); // Free tier
    }

    public static bool ActivateLicense(string licenseKey, out string errorMessage)
    {
        errorMessage = string.Empty;
        licenseKey = licenseKey.Trim();

        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            errorMessage = "Il codice di licenza non può essere vuoto.";
            return false;
        }

        if (!TryValidateKey(licenseKey, out var lic) || lic == null)
        {
            errorMessage = "Codice di licenza non valido o firma digitale non verificata.";
            return false;
        }

        if (!lic.IsValid)
        {
            errorMessage = $"La licenza è scaduta il {lic.ExpirationDate:dd/MM/yyyy}.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(LicenseDirectory);
            File.WriteAllText(LicenseFilePath, licenseKey, Encoding.UTF8);
            _currentLicense = lic;
            LicenseChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Errore durante il salvataggio della licenza: {ex.Message}";
            return false;
        }
    }

    public static void DeactivateLicense()
    {
        try
        {
            if (File.Exists(LicenseFilePath))
            {
                File.Delete(LicenseFilePath);
            }
        }
        catch { }

        _currentLicense = new LicenseInfo();
        LicenseChanged?.Invoke();
    }

    public static bool TryValidateKey(string licenseKey, out LicenseInfo? license)
    {
        license = null;
        try
        {
            if (!licenseKey.StartsWith("MR-", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parts = licenseKey[3..].Split('.');
            if (parts.Length != 2) return false;

            var payloadBytes = Convert.FromBase64String(parts[0]);
            var signatureBytes = Convert.FromBase64String(parts[1]);

            using var rsa = RSA.Create();
            rsa.FromXmlString(PublicKeyXml);

            var isValid = rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (!isValid) return false;

            var json = Encoding.UTF8.GetString(payloadBytes);
            license = JsonSerializer.Deserialize<LicenseInfo>(json);
            return license != null;
        }
        catch
        {
            license = null;
            return false;
        }
    }
}
