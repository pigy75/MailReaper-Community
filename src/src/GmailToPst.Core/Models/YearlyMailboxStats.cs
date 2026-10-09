namespace GmailToPst.Core.Models;

public class YearStatItem
{
    public int Year { get; set; }
    public int ServerCount { get; set; }
    public int LocalCount { get; set; }
    public int PendingCount => Math.Max(0, ServerCount - LocalCount);
    public double CompletionPercentage => ServerCount > 0 ? Math.Min(100.0, (double)LocalCount / ServerCount * 100.0) : 0.0;
    public bool IsFullyDownloaded => ServerCount > 0 && LocalCount >= ServerCount;
    public string StatusBadge => ServerCount == 0 
        ? "Vuoto" 
        : IsFullyDownloaded 
            ? "✅ Scaricato" 
            : LocalCount > 0 
                ? $"Parziale ({LocalCount}/{ServerCount})" 
                : "Da scaricare";
}

public class MailboxAnalysisSummary
{
    public string Email { get; set; } = string.Empty;
    public int TotalServerMessages { get; set; }
    public int TotalLocalMessages { get; set; }
    public int TotalFoldersCount { get; set; }
    public List<YearStatItem> Years { get; set; } = new();
}
