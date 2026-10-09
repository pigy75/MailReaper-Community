using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace GmailToPst.Core.Licensing;

public static class MachineFingerprint
{
    private static string? _cachedMachineId;

    public static string GetMachineId()
    {
        if (_cachedMachineId != null)
        {
            return _cachedMachineId;
        }

        var rawInput = new StringBuilder();

        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var rk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var cryptoKey = rk.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var guid = cryptoKey?.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    rawInput.Append(guid);
                }
            }
        }
        catch
        {
            // Fallback se registry non accessibile
        }

        // Aggiungi parametri di sistema per ulteriore stabilità
        rawInput.Append('|');
        rawInput.Append(Environment.MachineName);
        rawInput.Append('|');
        rawInput.Append(Environment.ProcessorCount);
        rawInput.Append('|');
        rawInput.Append(Environment.OSVersion.VersionString);

        var bytes = Encoding.UTF8.GetBytes(rawInput.ToString());
        var hash = SHA256.HashData(bytes);

        // Formatta in 16 caratteri alfanumerici a gruppi di 4: MR-HW-XXXX-XXXX-XXXX-XXXX
        var hex = Convert.ToHexString(hash).ToUpperInvariant();
        _cachedMachineId = $"MR-HW-{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
        return _cachedMachineId;
    }
}
