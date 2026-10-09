using System.IO;
using System.Text.Json;

namespace GmailToPst.Storage.Settings;

public class AppSettings
{
    public string ArchivesPath { get; set; } = string.Empty;
    public string DefaultExportPath { get; set; } = string.Empty;
    public string DefaultAttachmentsPath { get; set; } = string.Empty;

    public static AppSettings CreateDefault()
    {
        // Se esiste il percorso aziendale su Z:\, usalo come priorità assoluta
        if (Directory.Exists(@"Z:\export\Archives"))
        {
            return new AppSettings
            {
                ArchivesPath = @"Z:\export\Archives",
                DefaultExportPath = @"Z:\export",
                DefaultAttachmentsPath = @"Z:\export\Allegati_Gmail"
            };
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        return new AppSettings
        {
            ArchivesPath = Path.Combine(appData, "GmailToPst", "Archives"),
            DefaultExportPath = desktop,
            DefaultAttachmentsPath = Path.Combine(desktop, "Allegati_Gmail")
        };
    }
}

public static class SettingsManager
{
    private static readonly string ExeDir = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string LocalSettingsFile = Path.Combine(ExeDir, "settings.json");
    private static readonly string AppDataSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GmailToPst", "settings.json");

    private static string ActiveSettingsFile => File.Exists(LocalSettingsFile) ? LocalSettingsFile : AppDataSettingsFile;

    private static AppSettings? _cachedSettings;

    public static AppSettings LoadSettings()
    {
        if (_cachedSettings != null) return _cachedSettings;

        try
        {
            var file = ActiveSettingsFile;
            if (File.Exists(file))
            {
                var json = File.ReadAllText(file);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    // Fallback per percorsi vuoti
                    var def = AppSettings.CreateDefault();
                    if (string.IsNullOrWhiteSpace(settings.ArchivesPath)) settings.ArchivesPath = def.ArchivesPath;
                    if (string.IsNullOrWhiteSpace(settings.DefaultExportPath)) settings.DefaultExportPath = def.DefaultExportPath;
                    if (string.IsNullOrWhiteSpace(settings.DefaultAttachmentsPath)) settings.DefaultAttachmentsPath = def.DefaultAttachmentsPath;

                    _cachedSettings = settings;
                    return settings;
                }
            }
            
            // Check if local "Archives" folder exists next to .exe
            var localArchives = Path.Combine(ExeDir, "Archives");
            if (Directory.Exists(localArchives))
            {
                var settings = new AppSettings
                {
                    ArchivesPath = localArchives,
                    DefaultExportPath = ExeDir,
                    DefaultAttachmentsPath = Path.Combine(ExeDir, "Allegati")
                };
                _cachedSettings = settings;
                return settings;
            }
        }
        catch { }

        _cachedSettings = AppSettings.CreateDefault();
        SaveSettings(_cachedSettings);
        return _cachedSettings;
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            var file = ActiveSettingsFile;
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
            _cachedSettings = settings;
        }
        catch { }
    }
}
