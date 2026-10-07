using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using GmailToPst.Storage.Settings;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace GmailToPst.UI.ViewModels;

public partial class AttachmentExtractViewModel : ObservableObject
{
    private readonly ILocalArchiveStorage _storage;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _destinationPath = "";

    [ObservableProperty]
    private AttachmentCategory _selectedCategory = AttachmentCategory.All;

    [ObservableProperty]
    private bool _isCategoryAll = true;

    [ObservableProperty]
    private bool _isCategoryImages = false;

    [ObservableProperty]
    private bool _isCategoryDocuments = false;

    [ObservableProperty]
    private bool _isCategoryArchives = false;

    [ObservableProperty]
    private bool _isCategoryCustom = false;

    [ObservableProperty]
    private string _customExtensions = ".pdf, .jpg, .xlsx";

    [ObservableProperty]
    private bool _filterMinSize = false;

    [ObservableProperty]
    private int _minSizeKb = 10;

    [ObservableProperty]
    private bool _exportOnlySelectedYear = true;

    [ObservableProperty]
    private int? _currentSelectedYear;

    [ObservableProperty]
    private string _yearFilterDisplay = "Tutti gli anni";

    [ObservableProperty]
    private bool _hasYearFilter = false;

    [ObservableProperty]
    private bool _exportAllFolders = true;

    [ObservableProperty]
    private string? _selectedFolderId;

    [ObservableProperty]
    private string _selectedFolderName = "Tutte le cartelle";

    [ObservableProperty]
    private bool _organizeInSubfolders = false;

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private double _progressPercentage = 0;

    public event EventHandler<string>? ExtractionCompleted;

    public AttachmentExtractViewModel(ILocalArchiveStorage storage, int? year = null, EmailFolder? selectedFolder = null)
    {
        _storage = storage;
        _currentSelectedYear = year;
        _hasYearFilter = year.HasValue;
        _yearFilterDisplay = year.HasValue ? $"Anno {year.Value}" : "Tutti gli anni";

        if (selectedFolder != null)
        {
            _selectedFolderId = selectedFolder.Id;
            _selectedFolderName = selectedFolder.Name;
        }

        var settings = SettingsManager.LoadSettings();
        var defaultFolder = settings.DefaultAttachmentsPath;
        if (!Directory.Exists(defaultFolder))
        {
            defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Allegati_Gmail");
        }
        DestinationPath = defaultFolder;
    }

    partial void OnIsCategoryAllChanged(bool value) { if (value) SelectedCategory = AttachmentCategory.All; }
    partial void OnIsCategoryImagesChanged(bool value) { if (value) SelectedCategory = AttachmentCategory.Images; }
    partial void OnIsCategoryDocumentsChanged(bool value) { if (value) SelectedCategory = AttachmentCategory.Documents; }
    partial void OnIsCategoryArchivesChanged(bool value) { if (value) SelectedCategory = AttachmentCategory.Archives; }
    partial void OnIsCategoryCustomChanged(bool value) { if (value) SelectedCategory = AttachmentCategory.Custom; }

    [RelayCommand]
    private void BrowseDestination()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleziona cartella di destinazione per gli allegati",
            InitialDirectory = Directory.Exists(DestinationPath) ? DestinationPath : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog() == true)
        {
            DestinationPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    private async Task StartExtractionAsync()
    {
        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            MessageBox.Show("Seleziona una cartella di destinazione valida.", "Destinazione mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusMessage = "Inizializzazione estrazione allegati...";
        ProgressPercentage = 0;
        _cts = new CancellationTokenSource();

        try
        {
            var yearFilter = (HasYearFilter && ExportOnlySelectedYear) ? CurrentSelectedYear : null;
            var folderFilter = ExportAllFolders ? null : SelectedFolderId;

            var filter = new AttachmentFilterOptions
            {
                Category = SelectedCategory,
                CustomExtensions = CustomExtensions,
                MinSizeKb = FilterMinSize ? MinSizeKb : null,
                Year = yearFilter,
                FolderId = folderFilter,
                OrganizeInSubfolders = OrganizeInSubfolders
            };

            var progress = new Progress<BackupProgress>(p =>
            {
                StatusMessage = p.StatusMessage;
                ProgressPercentage = p.ProgressPercentage;
            });

            var (count, bytes) = await _storage.ExtractAttachmentsAsync(DestinationPath, filter, progress, _cts.Token);
            IsBusy = false;

            var res = MessageBox.Show(
                $"Estrazione completata con successo!\n\n" +
                $"• Allegati estratti: {count}\n" +
                $"• Dimensione totale: {FormatBytes(bytes)}\n" +
                $"• Destinazione: {DestinationPath}\n\n" +
                "Vuoi aprire la cartella?",
                "Estrazione Completata",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (res == MessageBoxResult.Yes)
            {
                if (Directory.Exists(DestinationPath))
                {
                    Process.Start(new ProcessStartInfo { FileName = DestinationPath, UseShellExecute = true });
                }
            }

            ExtractionCompleted?.Invoke(this, DestinationPath);
        }
        catch (OperationCanceledException)
        {
            IsBusy = false;
            StatusMessage = "Estrazione annullata dall'utente.";
        }
        catch (Exception ex)
        {
            IsBusy = false;
            MessageBox.Show($"Errore durante l'estrazione degli allegati:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CancelExtraction()
    {
        _cts?.Cancel();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
