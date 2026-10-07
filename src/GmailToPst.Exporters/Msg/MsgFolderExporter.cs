using System.IO;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using MimeKit;
using MsgKit;
using MsgKit.Enums;

namespace GmailToPst.Exporters.Msg;

public class MsgFolderExporter : IExporter
{
    public ExportFormat Format => ExportFormat.MsgFolder;
    public string Name => "Cartelle di file .MSG (Formato nativo Outlook)";
    public string Description => "Esporta tutti i messaggi in singoli file .MSG suddivisi per cartella. Compatibile con Outlook senza bisogno di avere Outlook installato.";

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
            StatusMessage = "Preparazione esportazione file .MSG..."
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
                using var emlStream = new MemoryStream(rawEml);
                var mime = await MimeMessage.LoadAsync(emlStream, cancellationToken);

                var sender = new Sender(
                    string.IsNullOrWhiteSpace(mime.From.Mailboxes.FirstOrDefault()?.Address) ? "unknown@mail.com" : mime.From.Mailboxes.First().Address,
                    mime.From.Mailboxes.FirstOrDefault()?.Name ?? "");

                var msgEmail = new MsgKit.Email(sender, mime.Subject ?? "(Nessun oggetto)")
                {
                    SentOn = mime.Date.LocalDateTime,
                    BodyHtml = mime.HtmlBody,
                    BodyText = mime.TextBody,
                    Importance = MsgKit.Enums.MessageImportance.IMPORTANCE_NORMAL
                };

                // Destinatari
                foreach (var to in mime.To.Mailboxes)
                {
                    msgEmail.Recipients.AddTo(to.Address, to.Name);
                }
                foreach (var cc in mime.Cc.Mailboxes)
                {
                    msgEmail.Recipients.AddCc(cc.Address, cc.Name);
                }

                // Allegati
                foreach (var att in mime.Attachments)
                {
                    if (att is MimePart part)
                    {
                        using var ms = new MemoryStream();
                        part.Content?.DecodeTo(ms);
                        ms.Position = 0;
                        var fileName = string.IsNullOrWhiteSpace(part.FileName) ? "allegato.bin" : part.FileName;
                        msgEmail.Attachments.Add(ms, fileName);
                    }
                }

                var safeSubject = string.Concat((msgSummary.Subject ?? "Email").Split(Path.GetInvalidFileNameChars()));
                if (safeSubject.Length > 60) safeSubject = safeSubject.Substring(0, 60);

                var fileNamePrefix = $"{msgSummary.Date.LocalDateTime:yyyyMMdd_HHmmss}_{safeSubject}_{msgSummary.Id.Substring(0, Math.Min(6, msgSummary.Id.Length))}";
                var msgFilePath = Path.Combine(folderOutputDir, $"{fileNamePrefix}.msg");

                msgEmail.Save(msgFilePath);

                processed++;
                progressState.ProcessedMessages = processed;
                progressState.StatusMessage = $"Esportazione MSG ({processed}/{totalCount}): {msgSummary.Subject}";
                progress?.Report(progressState);
            }
        }

        progressState.Phase = BackupPhase.Completed;
        progressState.StatusMessage = $"Esportazione MSG completata con successo ({processed} file).";
        progress?.Report(progressState);

        return outputDir;
    }
}
