using System.IO;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Pst;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;

namespace GmailToPst.Exporters.Outlook;

public class OutlookPstExporter : IExporter
{
    public ExportFormat Format => ExportFormat.OutlookPst;
    public string Name => "Archivio Microsoft Outlook (.PST)";
    public string Description => "Genera file .PST nativi Unicode con segmentazione intelligente anti-corruzione (limite 50 GB di Outlook) e fedeltà cartelle.";

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
        var targetBaseDir = Path.GetDirectoryName(options.OutputPath);
        if (string.IsNullOrEmpty(targetBaseDir))
        {
            targetBaseDir = options.OutputPath.EndsWith(".pst", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(options.OutputPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : options.OutputPath;
        }

        Directory.CreateDirectory(targetBaseDir);

        var baseFileName = Path.GetFileNameWithoutExtension(options.OutputPath);
        if (string.IsNullOrWhiteSpace(baseFileName)) baseFileName = "Archivio_Posta";

        var generatedFiles = new List<string>();

        var progressState = new BackupProgress
        {
            Phase = BackupPhase.Exporting,
            StatusMessage = "Preparazione esportazione PST..."
        };
        progress?.Report(progressState);

        if (options.ScopeMode == ExportScopeMode.SeparateByYearWithSplit)
        {
            // MODALITÀ 2: Esporta per Anno separato (es. 2025.pst, 2024.pst...) con auto-split se il singolo anno supera la soglia
            var availableYears = await storage.GetAvailableYearsAsync(cancellationToken);
            if (options.SpecificYearsToExport?.Any() == true)
            {
                availableYears = availableYears.Where(y => options.SpecificYearsToExport.Contains(y)).ToList();
            }
            else if (options.YearFilter.HasValue)
            {
                availableYears = new List<int> { options.YearFilter.Value };
            }

            if (!availableYears.Any())
            {
                availableYears = new List<int> { DateTime.Now.Year };
            }

            int currentYearIdx = 0;
            foreach (var year in availableYears)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentYearIdx++;

                var yearPrefix = $"{baseFileName}_{year}";
                progressState.StatusMessage = $"Esportazione Anno {year} ({currentYearIdx}/{availableYears.Count})...";
                progress?.Report(progressState);

                var yearFiles = await ExportYearOrChunkAsync(storage, options, targetBaseDir, yearPrefix, year, progress, cancellationToken);
                generatedFiles.AddRange(yearFiles);
            }
        }
        else
        {
            // MODALITÀ 1 o 3: Singolo anno/archivio unificato o Segmentazione Continua Globale (Part01, Part02...)
            var yearFiles = await ExportYearOrChunkAsync(storage, options, targetBaseDir, baseFileName, options.YearFilter, progress, cancellationToken);
            generatedFiles.AddRange(yearFiles);
        }

        progressState.Phase = BackupPhase.Completed;
        progressState.StatusMessage = $"Esportazione completata: generati {generatedFiles.Count} file PST.";
        progress?.Report(progressState);

        return generatedFiles.FirstOrDefault() ?? options.OutputPath;
    }

    private async Task<List<string>> ExportYearOrChunkAsync(
        ILocalArchiveStorage storage,
        ExportOptions options,
        string targetDir,
        string filePrefix,
        int? yearFilter,
        IProgress<BackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var folders = await storage.GetFoldersAsync(yearFilter, cancellationToken);
        var targetFolders = options.FolderIdsToExport?.Any() == true
            ? folders.Where(f => options.FolderIdsToExport.Contains(f.Id)).ToList()
            : folders;

        int totalMessages = 0;
        foreach (var folder in targetFolders)
        {
            totalMessages += await storage.GetMessageCountInFolderAsync(folder.Id, null, yearFilter, cancellationToken);
        }

        if (totalMessages == 0)
        {
            return new List<string>();
        }

        var resultFiles = new List<string>();
        int partNumber = 1;
        long currentPstBytes = 0;
        int processedMessages = 0;

        PersonalStorage? currentPst = null;
        string? currentPstPath = null;
        var folderLookup = new Dictionary<string, FolderInfo>();

        void OpenNewPstPart(bool isMultiPart)
        {
            if (currentPst != null)
            {
                currentPst.Dispose();
                currentPst = null;
            }

            folderLookup.Clear();
            currentPstBytes = 0;

            var partSuffix = isMultiPart ? $"_Part{partNumber:D2}.pst" : ".pst";
            currentPstPath = Path.Combine(targetDir, $"{filePrefix}{partSuffix}");

            if (File.Exists(currentPstPath) && options.OverwriteExisting)
            {
                try { File.Delete(currentPstPath); } catch { }
            }

            currentPst = PersonalStorage.Create(currentPstPath, FileFormatVersion.Unicode);
            resultFiles.Add(currentPstPath);
            partNumber++;
        }

        // Determina se serve partire già con suffisso _Part se la dimensione stimata supera la soglia
        var estimatedTotalSizeBytes = await storage.GetTotalSizeBytesAsync(yearFilter, cancellationToken);
        bool needsSegmentation = estimatedTotalSizeBytes > options.MaxPstSizeBytes;

        OpenNewPstPart(needsSegmentation || options.ScopeMode == ExportScopeMode.ContinuousSegmentation);

        try
        {
            foreach (var folder in targetFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var messages = await storage.GetMessagesInFolderAsync(folder.Id, 0, 50000, null, yearFilter, cancellationToken);
                if (!messages.Any()) continue;

                foreach (var msgSummary in messages)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Se il file PST corrente supera la soglia massima di sicurezza (es. 40 GB), chiudi e apri la parte successiva!
                    if (currentPstBytes > 0 && currentPstBytes + msgSummary.SizeBytes > options.MaxPstSizeBytes)
                    {
                        OpenNewPstPart(true);
                    }

                    // Assicura la presenza della cartella nel PST corrente
                    var pstFolder = GetOrCreatePstFolder(currentPst!, folderLookup, folder);

                    try
                    {
                        var rawEml = await storage.GetRawEmlBytesAsync(msgSummary.Id, cancellationToken);
                        if (rawEml != null && rawEml.Length > 0)
                        {
                            using var emlStream = new MemoryStream(rawEml);
                            using var mapiMsg = MapiMessage.Load(emlStream);
                            pstFolder.AddMessage(mapiMsg);
                            currentPstBytes += rawEml.Length;
                        }
                    }
                    catch
                    {
                        // Tolleranza guasti su singolo messaggio corrotto
                    }

                    processedMessages++;
                    if (progress != null)
                    {
                        var pState = new BackupProgress
                        {
                            Phase = BackupPhase.Exporting,
                            TotalMessages = totalMessages,
                            ProcessedMessages = processedMessages,
                            StatusMessage = $"Scrittura PST [Parte {partNumber - 1}] ({processedMessages}/{totalMessages}): {msgSummary.Subject}"
                        };
                        progress.Report(pState);
                    }
                }
            }
        }
        finally
        {
            if (currentPst != null)
            {
                currentPst.Dispose();
                currentPst = null;
            }
        }

        return resultFiles;
    }

    private static FolderInfo GetOrCreatePstFolder(PersonalStorage pst, Dictionary<string, FolderInfo> lookup, EmailFolder folder)
    {
        if (lookup.TryGetValue(folder.Id, out var existing))
            return existing;

        var safeName = GetSafeFolderName(folder.Name, folder.FullPath);
        FolderInfo created;

        if (!string.IsNullOrEmpty(folder.ParentId) && lookup.TryGetValue(folder.ParentId, out var parentFolder))
        {
            created = GetOrCreateSubFolder(parentFolder, safeName);
        }
        else
        {
            created = GetOrCreateSubFolder(pst.RootFolder, safeName);
        }

        lookup[folder.Id] = created;
        return created;
    }

    private static string GetSafeFolderName(string? name, string? fullPath)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();

        if (!string.IsNullOrWhiteSpace(fullPath))
            return fullPath.Trim();

        return "Cartella";
    }

    private static FolderInfo GetOrCreateSubFolder(FolderInfo parent, string name)
    {
        try
        {
            var existing = parent.GetSubFolder(name);
            if (existing != null)
                return existing;
        }
        catch { }

        try
        {
            return parent.AddSubFolder(name);
        }
        catch
        {
            try
            {
                var fallback = parent.GetSubFolder(name);
                if (fallback != null) return fallback;
            }
            catch { }

            return parent.AddSubFolder($"{name}_{Guid.NewGuid().ToString().Substring(0, 4)}");
        }
    }
}
