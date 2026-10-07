using GmailToPst.Core.Models;

namespace GmailToPst.Core.Interfaces;

public enum ExportFormat
{
    OutlookPst,
    MsgFolder,
    EmlFolder
}

public enum ExportScopeMode
{
    SinglePstOrSelectedYear = 0,    // Esporta l'anno selezionato o tutto l'archivio (con auto-split di sicurezza se > soglia)
    SeparateByYearWithSplit = 1,    // Esporta per Singolo Anno Separato (es. 2024.pst, 2023.pst, con auto-split se il singolo anno > soglia)
    ContinuousSegmentation = 2     // Auto-segmentazione continua globale dell'intero archivio (es. Part01.pst, Part02.pst da max N GB)
}

public class ExportOptions
{
    public ExportFormat Format { get; set; } = ExportFormat.OutlookPst;
    public ExportScopeMode ScopeMode { get; set; } = ExportScopeMode.SinglePstOrSelectedYear;
    public string OutputPath { get; set; } = string.Empty;
    public List<string>? FolderIdsToExport { get; set; }
    public int? YearFilter { get; set; }
    public List<int>? SpecificYearsToExport { get; set; }
    public bool OverwriteExisting { get; set; } = true;
    public long MaxPstSizeBytes { get; set; } = 40L * 1024 * 1024 * 1024; // Default: 40 GB (Sotto il limite critico dei 50 GB di Outlook)
}

public interface IExporter
{
    ExportFormat Format { get; }
    string Name { get; }
    string Description { get; }
    bool IsAvailableOnCurrentSystem(out string? reasonIfNotAvailable);
    Task<string> ExportAsync(
        ILocalArchiveStorage storage,
        ExportOptions options,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
