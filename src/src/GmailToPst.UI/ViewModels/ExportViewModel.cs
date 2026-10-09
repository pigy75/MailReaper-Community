using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using GmailToPst.Exporters.Eml;
using GmailToPst.Exporters.Msg;
using GmailToPst.Exporters.Outlook;
using GmailToPst.Storage.Settings;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace GmailToPst.UI.ViewModels;

public class ExporterItem
{
    public IExporter Exporter { get; set; }
    public string Name => Exporter.Name;
    public string Description => Exporter.Description;
    public bool IsAvailable { get; set; }
    public string? UnavailableReason { get; set; }

    public ExporterItem(IExporter exporter)
    {
        Exporter = exporter;
        IsAvailable = exporter.IsAvailableOnCurrentSystem(out var reason);
        UnavailableReason = reason;
    }
}

public partial class ExportViewModel : ObservableObject
{
    private readonly ILocalArchiveStorage _storage;
    private readonly List<string>? _preselectedFolderIds;
    private readonly string _defaultEmail;

    public ObservableCollection<ExporterItem> Exporters { get; } = new();

    [ObservableProperty]
    private ExporterItem? _selectedExporter;

    [ObservableProperty]
    private string _outputPath = "";

    [ObservableProperty]
    private bool _exportAllFolders = true;

    // Modalità di Segmentazione PST
    [ObservableProperty]
    private bool _scopeSingleOrSelectedYear = true;

    [ObservableProperty]
    private bool _scopeSeparateByYear = false;

    [ObservableProperty]
    private bool _scopeContinuousSegmentation = false;

    partial void OnScopeSingleOrSelectedYearChanged(bool value)
    {
        if (value)
        {
            _scopeSeparateByYear = false;
            _scopeContinuousSegmentation = false;
            OnPropertyChanged(nameof(ScopeSeparateByYear));
            OnPropertyChanged(nameof(ScopeContinuousSegmentation));
            UpdateDefaultOutputPath();
        }
    }

    partial void OnScopeSeparateByYearChanged(bool value)
    {
        if (value)
        {
            _scopeSingleOrSelectedYear = false;
            _scopeContinuousSegmentation = false;
            OnPropertyChanged(nameof(ScopeSingleOrSelectedYear));
            OnPropertyChanged(nameof(ScopeContinuousSegmentation));
            UpdateDefaultOutputPath();
        }
    }

    partial void OnScopeContinuousSegmentationChanged(bool value)
    {
        if (value)
        {
            _scopeSingleOrSelectedYear = false;
            _scopeSeparateByYear = false;
            OnPropertyChanged(nameof(ScopeSingleOrSelectedYear));
            OnPropertyChanged(nameof(ScopeSeparateByYear));
            UpdateDefaultOutputPath();
        }
    }

    [ObservableProperty]
    private bool _isPstSelected = true;

    public ObservableCollection<int> AvailableYears { get; } = new();

    [ObservableProperty]
    private int _selectedYear = DateTime.Now.Year;

    [ObservableProperty]
    private bool _hasSelectedSpecificYear = false;

    public ObservableCollection<string> MaxPstSizeOptions { get; } = new()
    {
        "40 GB (Raccomandato per Outlook)",
        "45 GB (Limite massimo sicurezza)",
        "20 GB (Segmenti medi)",
        "10 GB (Archiviazione leggera)"
    };

    [ObservableProperty]
    private string _selectedMaxPstSizeOption = "40 GB (Raccomandato per Outlook)";

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private double _progressPercentage = 0;

    public event EventHandler<string>? ExportCompleted;

    public ExportViewModel(ILocalArchiveStorage storage, string defaultEmail, int? year = null, List<string>? preselectedFolderIds = null)
    {
        _storage = storage;
        _defaultEmail = defaultEmail;
        _preselectedFolderIds = preselectedFolderIds;
        _hasSelectedSpecificYear = year.HasValue;
        if (year.HasValue) _selectedYear = year.Value;

        var outlookExporter = new OutlookPstExporter();
        var msgExporter = new MsgFolderExporter();
        var emlExporter = new EmlFolderExporter();

        Exporters.Add(new ExporterItem(outlookExporter));
        Exporters.Add(new ExporterItem(msgExporter));
        Exporters.Add(new ExporterItem(emlExporter));

        SelectedExporter = Exporters.First();

        _ = LoadAvailableYearsAsync();
        UpdateDefaultOutputPath();
    }

    private async Task LoadAvailableYearsAsync()
    {
        try
        {
            var years = await _storage.GetAvailableYearsAsync();
            AvailableYears.Clear();
            foreach (var y in years)
            {
                AvailableYears.Add(y);
            }

            if (AvailableYears.Any() && !HasSelectedSpecificYear)
            {
                SelectedYear = AvailableYears.First();
            }
        }
        catch { }
    }

    private void UpdateDefaultOutputPath()
    {
        var settings = SettingsManager.LoadSettings();
        var baseFolder = settings.DefaultExportPath;
        if (!Directory.Exists(baseFolder)) baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        var sanitizedEmail = string.Concat(_defaultEmail.Split(Path.GetInvalidFileNameChars()));

        if (SelectedExporter?.Exporter.Format == ExportFormat.OutlookPst)
        {
            if (ScopeSeparateByYear)
            {
                OutputPath = Path.Combine(baseFolder, $"Backup_{sanitizedEmail}_AnniSeparati");
            }
            else if (ScopeContinuousSegmentation)
            {
                OutputPath = Path.Combine(baseFolder, $"Backup_{sanitizedEmail}_Segmentato.pst");
            }
            else
            {
                var yearSuffix = HasSelectedSpecificYear ? $"_{SelectedYear}" : "_Completo";
                OutputPath = Path.Combine(baseFolder, $"Backup_{sanitizedEmail}{yearSuffix}.pst");
            }
        }
        else
        {
            var yearSuffix = HasSelectedSpecificYear ? $"_{SelectedYear}" : "_Completo";
            OutputPath = Path.Combine(baseFolder, $"Backup_{sanitizedEmail}{yearSuffix}");
        }
    }

    partial void OnSelectedExporterChanged(ExporterItem? value)
    {
        if (value == null) return;
        IsPstSelected = value.Exporter.Format == ExportFormat.OutlookPst;
        UpdateDefaultOutputPath();
    }

    private long GetMaxSizeBytesFromOption()
    {
        if (SelectedMaxPstSizeOption.StartsWith("45")) return 45L * 1024 * 1024 * 1024;
        if (SelectedMaxPstSizeOption.StartsWith("20")) return 20L * 1024 * 1024 * 1024;
        if (SelectedMaxPstSizeOption.StartsWith("10")) return 10L * 1024 * 1024 * 1024;
        return 40L * 1024 * 1024 * 1024; // Default: 40 GB
    }

    [RelayCommand]
    private async Task StartExportAsync()
    {
        if (SelectedExporter == null)
        {
            MessageBox.Show("Seleziona un formato di esportazione.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!SelectedExporter.IsAvailable)
        {
            MessageBox.Show(SelectedExporter.UnavailableReason ?? "Questo formato non è supportato sul tuo sistema.", "Non disponibile", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            MessageBox.Show("Specifica il percorso di destinazione per il file o la cartella di esportazione.", "Percorso mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusMessage = "Verifica autorizzazioni ed esportazione archivi...";
        ProgressPercentage = 0;

        try
        {
            ExportScopeMode scopeMode = ExportScopeMode.SinglePstOrSelectedYear;
            int? yearFilter = null;

            if (ScopeSeparateByYear)
            {
                scopeMode = ExportScopeMode.SeparateByYearWithSplit;
            }
            else if (ScopeContinuousSegmentation)
            {
                scopeMode = ExportScopeMode.ContinuousSegmentation;
            }
            else
            {
                scopeMode = ExportScopeMode.SinglePstOrSelectedYear;
                if (HasSelectedSpecificYear)
                {
                    yearFilter = SelectedYear;
                }
            }

            // Controllo limite 5 GB per la versione FREE sui file PST
            if (SelectedExporter.Exporter.Format == ExportFormat.OutlookPst)
            {
                var estimatedBytes = await _storage.GetTotalSizeBytesAsync(yearFilter);
                if (!GmailToPst.Core.Licensing.LicenseManager.CurrentLicense.CanExportPst(estimatedBytes))
                {
                    IsBusy = false;
                    var gbStr = (estimatedBytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.0") + " GB";
                    var msg = $"⚠️ Limite Versione FREE (Max 5 GB per archivio PST):\n\n" +
                              $"La selezione attuale contiene circa {gbStr} di dati.\n" +
                              $"La versione Community Free consente di esportare archivi PST fino a 5 GB.\n\n" +
                              $"Vuoi attivare MailReaper PRO per esportare senza limiti (100–200+ GB)?";
                    var res = MessageBox.Show(msg, "MailReaper - Limite Versione Free (5 GB)", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (res == MessageBoxResult.Yes)
                    {
                        var licDlg = new Views.LicenseDialog { Owner = Application.Current.MainWindow };
                        licDlg.ShowDialog();
                    }
                    return;
                }
            }

            var options = new ExportOptions
            {
                Format = SelectedExporter.Exporter.Format,
                ScopeMode = scopeMode,
                OutputPath = OutputPath,
                YearFilter = yearFilter,
                MaxPstSizeBytes = GetMaxSizeBytesFromOption(),
                FolderIdsToExport = ExportAllFolders ? null : _preselectedFolderIds
            };

            var progress = new Progress<BackupProgress>(p =>
            {
                StatusMessage = p.StatusMessage;
                ProgressPercentage = p.ProgressPercentage;
            });

            var finalPath = await SelectedExporter.Exporter.ExportAsync(_storage, options, progress);
            IsBusy = false;
            ExportCompleted?.Invoke(this, finalPath);
        }
        catch (Exception ex)
        {
            IsBusy = false;
            MessageBox.Show($"Errore durante l'esportazione:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
