namespace GmailToPst.Core.Models;

public enum AttachmentCategory
{
    All,
    Images,
    Documents,
    Archives,
    Custom
}

public class AttachmentFilterOptions
{
    public AttachmentCategory Category { get; set; } = AttachmentCategory.All;
    public string CustomExtensions { get; set; } = "";
    public int? MinSizeKb { get; set; }
    public int? Year { get; set; }
    public string? FolderId { get; set; }
    public bool OrganizeInSubfolders { get; set; } = false;

    public bool MatchesFilter(string fileName, long sizeBytes)
    {
        // 1. Verifica dimensione minima
        if (MinSizeKb.HasValue && MinSizeKb.Value > 0)
        {
            if (sizeBytes < MinSizeKb.Value * 1024L)
                return false;
        }

        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();

        // 2. Verifica estensione / categoria
        return Category switch
        {
            AttachmentCategory.All => true,
            AttachmentCategory.Images => ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" or ".heic" or ".tiff" or ".tif" or ".ico",
            AttachmentCategory.Documents => ext is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".rtf" or ".csv" or ".odt" or ".ods" or ".odp",
            AttachmentCategory.Archives => ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz",
            AttachmentCategory.Custom => MatchesCustomExtensions(ext),
            _ => true
        };
    }

    private bool MatchesCustomExtensions(string ext)
    {
        if (string.IsNullOrWhiteSpace(CustomExtensions)) return true;

        var tokens = CustomExtensions.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in tokens)
        {
            var cleaned = t.Trim().ToLowerInvariant();
            if (!cleaned.StartsWith(".")) cleaned = "." + cleaned;
            if (string.Equals(ext, cleaned, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
