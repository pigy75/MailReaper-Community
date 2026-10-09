using System.IO;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;

namespace GmailToPst.Exporters.Eml;

public class EmlFolderExporter : IExporter
{
    public ExportFormat Format => ExportFormat.EmlFolder;
    public string Name => "Archivio standard .EML (Struttura Cartelle)";
    public string Description => "Esporta tutti i messaggi in formato standard .EML organizzati in sottocartelle corrispondenti a Gmail.";

    public bool IsAvailableOnCurrentSystem(out string? reasonIfNotAvailable)
    {
        reasonIfNotAvailable = null;
        return true;
    }

    public async Task<string> ExportAsync(
        ILocalArchiveStorage storage,
        ExportOptions options,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var outputDir = options.OutputPath;
        Directory.CreateDirectory(outputDir);

        var folders = await storage.GetFoldersAsync(options.YearFilter, cancellationToken);
        var targetFolders = options.FolderIdsToExport?.Any() == true
            ? folders.Where(f => options.FolderIdsToExport.Contains(f.Id)).ToList()
            : folders;

        var progressState = new BackupProgress
        {
            Phase = BackupPhase.Exporting,
            StatusMessage = "Preparazione esportazione file .EML..."
        };
        progress?.Report(progressState);

        int totalCount = 0;
        foreach (var folder in targetFolders)
        {
            totalCount += await storage.GetMessageCountInFolderAsync(folder.Id, null, options.YearFilter, cancellationToken);
        }
        progressState.TotalMessages = totalCount;

        int processed = 0;

        foreach (var folder in targetFolders)
        {
            var sanitizedFolderName = string.Concat(folder.Name.Split(Path.GetInvalidFileNameChars()));
            var folderOutputDir = Path.Combine(outputDir, sanitizedFolderName);
            Directory.CreateDirectory(folderOutputDir);

            var messages = await storage.GetMessagesInFolderAsync(folder.Id, 0, 50000, null, options.YearFilter, cancellationToken);

            foreach (var msgSummary in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rawEml = await storage.GetRawEmlBytesAsync(msgSummary.Id, cancellationToken);

                var safeSubject = string.Concat((msgSummary.Subject ?? "Email").Split(Path.GetInvalidFileNameChars()));
                if (safeSubject.Length > 60) safeSubject = safeSubject.Substring(0, 60);

                var fileNamePrefix = $"{msgSummary.Date.LocalDateTime:yyyyMMdd_HHmmss}_{safeSubject}_{msgSummary.Id.Substring(0, Math.Min(6, msgSummary.Id.Length))}";
                var emlFilePath = Path.Combine(folderOutputDir, $"{fileNamePrefix}.eml");

                await File.WriteAllBytesAsync(emlFilePath, rawEml, cancellationToken);

                processed++;
                progressState.ProcessedMessages = processed;
                progressState.StatusMessage = $"Esportazione EML ({processed}/{totalCount}): {msgSummary.Subject}";
                progress?.Report(progressState);
            }
        }

        progressState.Phase = BackupPhase.Completed;
        progressState.StatusMessage = $"Esportazione EML completata con successo ({processed} file).";
        progress?.Report(progressState);

        return outputDir;
    }
}
