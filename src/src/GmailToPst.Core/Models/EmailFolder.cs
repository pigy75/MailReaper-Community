using System.Collections.ObjectModel;

namespace GmailToPst.Core.Models;

public class EmailFolder
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public int TotalCount { get; set; }
    public int FilteredCount { get; set; }
    public bool IsSelected { get; set; } = true;
    public bool IsSystemFolder { get; set; }
    public ObservableCollection<EmailFolder> SubFolders { get; set; } = new();

    public override string ToString() => Name;
}
