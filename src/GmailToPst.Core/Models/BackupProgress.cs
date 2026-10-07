namespace GmailToPst.Core.Models;

public enum BackupPhase
{
    Connecting,
    ScanningFolders,
    QueryingServer,
    DownloadingMessages,
    SavingToArchive,
    Exporting,
    Completed,
    Failed,
    Cancelled
}

public class BackupProgress
{
    public BackupPhase Phase { get; set; } = BackupPhase.Connecting;
    public string StatusMessage { get; set; } = string.Empty;
    public string CurrentFolder { get; set; } = string.Empty;
    public int ProcessedMessages { get; set; }
    public int TotalMessages { get; set; }
    public string? CurrentMessageSubject { get; set; }
    public long BytesDownloaded { get; set; }
    public double ProgressPercentage => TotalMessages > 0 ? Math.Min(100.0, (double)ProcessedMessages / TotalMessages * 100.0) : 0.0;
    public string? ErrorMessage { get; set; }

    public string FormattedBytes
    {
        get
        {
            if (BytesDownloaded < 1024) return $"{BytesDownloaded} B";
            if (BytesDownloaded < 1024 * 1024) return $"{BytesDownloaded / 1024.0:F1} KB";
            return $"{BytesDownloaded / (1024.0 * 1024.0):F2} MB";
        }
    }
}
