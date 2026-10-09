namespace GmailToPst.Core.Models;

public class BackupFilter
{
    public int? Year { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<string> SelectedFolderIds { get; set; } = new();
    public bool IncludeSpamAndTrash { get; set; } = false;
    public int? MaxMessages { get; set; }
    public HashSet<string>? ExistingMessageIds { get; set; }

    public static BackupFilter ForYear(int year, List<string>? folderIds = null)
    {
        return new BackupFilter
        {
            Year = year,
            StartDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            SelectedFolderIds = folderIds ?? new List<string>()
        };
    }

    public static BackupFilter ForRange(DateTime start, DateTime end, List<string>? folderIds = null)
    {
        return new BackupFilter
        {
            StartDate = start,
            EndDate = end,
            SelectedFolderIds = folderIds ?? new List<string>()
        };
    }
}
