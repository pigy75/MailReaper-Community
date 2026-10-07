using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GmailToPst.Core.Models;

public class AccountNode : INotifyPropertyChanged
{
    private string _email = "";
    private string _displayName = "";
    private string _archiveBasePath = "";
    private int _totalMessagesCount = 0;
    private long _totalSizeBytes = 0;
    private string _formattedTotalSize = "0 B";
    private bool _isExpanded = true;
    private bool _isSyncing = false;
    private string _syncStatusText = "";
    private double _syncProgressPercent = 0;

    public string Email
    {
        get => _email;
        set { _email = value; OnPropertyChanged(); }
    }

    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    public string ArchiveBasePath
    {
        get => _archiveBasePath;
        set { _archiveBasePath = value; OnPropertyChanged(); }
    }

    public int TotalMessagesCount
    {
        get => _totalMessagesCount;
        set { _totalMessagesCount = value; OnPropertyChanged(); }
    }

    public long TotalSizeBytes
    {
        get => _totalSizeBytes;
        set { _totalSizeBytes = value; OnPropertyChanged(); }
    }

    public string FormattedTotalSize
    {
        get => _formattedTotalSize;
        set { _formattedTotalSize = value; OnPropertyChanged(); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        set { _isSyncing = value; OnPropertyChanged(); }
    }

    public string SyncStatusText
    {
        get => _syncStatusText;
        set { _syncStatusText = value; OnPropertyChanged(); }
    }

    public double SyncProgressPercent
    {
        get => _syncProgressPercent;
        set { _syncProgressPercent = value; OnPropertyChanged(); }
    }

    public ObservableCollection<EmailFolder> Folders { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
