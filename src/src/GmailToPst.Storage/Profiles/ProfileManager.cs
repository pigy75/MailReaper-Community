using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GmailToPst.Core.Models;

namespace GmailToPst.Storage.Profiles;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class ProfileManager
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GmailToPst");

    private static readonly string ProfilesFilePath = Path.Combine(AppDataFolder, "profiles.dat");

    public static async Task<List<AccountConfig>> LoadProfilesAsync()
    {
        try
        {
            if (!File.Exists(ProfilesFilePath))
            {
                return new List<AccountConfig>();
            }

            var encryptedBytes = await File.ReadAllBytesAsync(ProfilesFilePath);
            if (encryptedBytes.Length == 0)
                return new List<AccountConfig>();

            // Decifra usando Windows Data Protection API (DPAPI)
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decryptedBytes);
            return JsonSerializer.Deserialize<List<AccountConfig>>(json) ?? new List<AccountConfig>();
        }
        catch
        {
            return new List<AccountConfig>();
        }
    }

    public static async Task SaveProfileAsync(AccountConfig profile)
    {
        var profiles = await LoadProfilesAsync();
        var existingIndex = profiles.FindIndex(p => string.Equals(p.EmailAddress, profile.EmailAddress, StringComparison.OrdinalIgnoreCase) && p.Type == profile.Type);

        if (existingIndex >= 0)
        {
            profiles[existingIndex] = profile;
        }
        else
        {
            profiles.Add(profile);
        }

        await SaveAllProfilesAsync(profiles);
    }

    public static async Task DeleteProfileAsync(string emailAddress, AccountType type)
    {
        var profiles = await LoadProfilesAsync();
        profiles.RemoveAll(p => string.Equals(p.EmailAddress, emailAddress, StringComparison.OrdinalIgnoreCase) && p.Type == type);
        await SaveAllProfilesAsync(profiles);
    }

    private static async Task SaveAllProfilesAsync(List<AccountConfig> profiles)
    {
        Directory.CreateDirectory(AppDataFolder);
        var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
        var plainBytes = Encoding.UTF8.GetBytes(json);

        // Cifra con DPAPI dell'utente Windows corrente
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(ProfilesFilePath, encryptedBytes);
    }
}
