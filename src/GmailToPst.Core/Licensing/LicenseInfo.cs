namespace GmailToPst.Core.Licensing;

public enum LicenseTier
{
    Free,
    Pro,
    Enterprise
}

public class LicenseInfo
{
    public const long FreeMaxExportBytes = 5L * 1024 * 1024 * 1024; // 5 GB
    public const int FreeMaxExtractedAttachments = 50;

    public LicenseTier Tier { get; set; } = LicenseTier.Free;
    public string LicensedTo { get; set; } = "Community Edition";
    public string Email { get; set; } = string.Empty;
    public DateTime? ExpirationDate { get; set; }
    public DateTime IssuedDate { get; set; } = DateTime.UtcNow;

    public bool IsProOrAbove => Tier is LicenseTier.Pro or LicenseTier.Enterprise;

    public bool IsValid
    {
        get
        {
            if (Tier == LicenseTier.Free) return true;
            if (ExpirationDate.HasValue && DateTime.UtcNow > ExpirationDate.Value) return false;
            return true;
        }
    }

    public long MaxPstExportSizeBytes => IsProOrAbove ? long.MaxValue : FreeMaxExportBytes;

    public bool CanExportPst(long requestedBytes)
    {
        if (IsProOrAbove) return true;
        return requestedBytes <= FreeMaxExportBytes;
    }

    public bool AllowWorkspaceDomainWide => IsProOrAbove;
    public bool AllowConcurrentSync => IsProOrAbove;
    public bool AllowUnlimitedAttachments => IsProOrAbove;
}
