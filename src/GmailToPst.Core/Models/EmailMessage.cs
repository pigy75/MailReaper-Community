namespace GmailToPst.Core.Models;

public class EmailMessage
{
    public string Id { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string? ThreadId { get; set; }
    public string FolderId { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string Subject { get; set; } = "(Nessun oggetto)";
    public string From { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    public DateTimeOffset Date { get; set; }
    public bool HasAttachments { get; set; }
    public bool IsRead { get; set; }
    public long SizeBytes { get; set; }
    public string? BodyText { get; set; }
    public string? BodyHtml { get; set; }
    public string? RawEmlPath { get; set; }
    public List<AttachmentInfo> Attachments { get; set; } = new();

    public string DisplaySender => string.IsNullOrWhiteSpace(FromDisplayName) ? From : $"{FromDisplayName} <{From}>";
    public string FormattedDate => Date.LocalDateTime.ToString("dd/MM/yyyy HH:mm");
    
    public string FormattedSize
    {
        get
        {
            if (SizeBytes < 1024) return $"{SizeBytes} B";
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:F1} KB";
            return $"{SizeBytes / (1024.0 * 1024.0):F2} MB";
        }
    }
}
