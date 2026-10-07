using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GmailToPst.Core.Models;
using System.Collections.ObjectModel;

namespace GmailToPst.UI.ViewModels;

public partial class MailboxAnalysisViewModel : ObservableObject
{
    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private int _totalServerMessages;

    [ObservableProperty]
    private int _totalLocalMessages;

    [ObservableProperty]
    private int _totalFoldersCount;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _statusText = "Analisi in corso...";

    public ObservableCollection<YearStatItem> Years { get; } = new();

    public int? SelectedYearToDownload { get; private set; }
    public bool DownloadAllRequested { get; private set; }

    public event EventHandler<int?>? YearSelected;

    public void LoadSummary(MailboxAnalysisSummary summary)
    {
        Email = summary.Email;
        TotalServerMessages = summary.TotalServerMessages;
        TotalLocalMessages = summary.TotalLocalMessages;
        TotalFoldersCount = summary.TotalFoldersCount;

        Years.Clear();
        foreach (var y in summary.Years)
        {
            Years.Add(y);
        }

        IsLoading = false;
        StatusText = $"Analisi completata: trovati {TotalServerMessages:N0} messaggi in {TotalFoldersCount} cartelle/etichette.";
    }

    [RelayCommand]
    private void DownloadYear(int year)
    {
        SelectedYearToDownload = year;
        DownloadAllRequested = false;
        YearSelected?.Invoke(this, year);
    }

    [RelayCommand]
    private void DownloadAll()
    {
        SelectedYearToDownload = null;
        DownloadAllRequested = true;
        YearSelected?.Invoke(this, null);
    }
}
