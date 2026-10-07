using GmailToPst.Storage.Settings;
using System.Windows;

namespace GmailToPst.UI.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var settings = SettingsManager.LoadSettings();
        TxtArchivesPath.Text = settings.ArchivesPath;
        TxtExportPath.Text = settings.DefaultExportPath;
        TxtAttachmentsPath.Text = settings.DefaultAttachmentsPath;
    }

    private void BrowseArchives_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleziona Cartella Archivio Database Locale",
            InitialDirectory = TxtArchivesPath.Text
        };
        if (dialog.ShowDialog() == true)
        {
            TxtArchivesPath.Text = dialog.FolderName;
        }
    }

    private void BrowseExport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleziona Cartella Predefinita Esportazioni PST",
            InitialDirectory = TxtExportPath.Text
        };
        if (dialog.ShowDialog() == true)
        {
            TxtExportPath.Text = dialog.FolderName;
        }
    }

    private void BrowseAttachments_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleziona Cartella Predefinita Allegati e Immagini",
            InitialDirectory = TxtAttachmentsPath.Text
        };
        if (dialog.ShowDialog() == true)
        {
            TxtAttachmentsPath.Text = dialog.FolderName;
        }
    }

    private void ResetDefault_Click(object sender, RoutedEventArgs e)
    {
        var def = AppSettings.CreateDefault();
        TxtArchivesPath.Text = def.ArchivesPath;
        TxtExportPath.Text = def.DefaultExportPath;
        TxtAttachmentsPath.Text = def.DefaultAttachmentsPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = new AppSettings
        {
            ArchivesPath = TxtArchivesPath.Text.Trim(),
            DefaultExportPath = TxtExportPath.Text.Trim(),
            DefaultAttachmentsPath = TxtAttachmentsPath.Text.Trim()
        };

        SettingsManager.SaveSettings(settings);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
