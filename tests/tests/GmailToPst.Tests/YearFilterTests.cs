using GmailToPst.Core.Models;
using Xunit;

namespace GmailToPst.Tests;

public class YearFilterTests
{
    [Fact]
    public void ForYear_SetsUtcBoundariesProperly()
    {
        var filter = BackupFilter.ForYear(2022);

        Assert.Equal(2022, filter.Year);
        Assert.NotNull(filter.StartDate);
        Assert.NotNull(filter.EndDate);

        Assert.Equal(new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc), filter.StartDate.Value);
        Assert.Equal(new DateTime(2022, 12, 31, 23, 59, 59, DateTimeKind.Utc), filter.EndDate.Value);
    }

    [Fact]
    public void ForRange_SetsCustomDatesProperly()
    {
        var start = new DateTime(2023, 6, 1);
        var end = new DateTime(2023, 12, 31);
        var filter = BackupFilter.ForRange(start, end, new List<string> { "INBOX" });

        Assert.Equal(start, filter.StartDate);
        Assert.Equal(end, filter.EndDate);
        Assert.Single(filter.SelectedFolderIds);
        Assert.Equal("INBOX", filter.SelectedFolderIds[0]);
    }
}
