using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;

namespace GmailToPst.Providers.Helpers;

public static class MailboxAnalyzerService
{
    public static async Task<MailboxAnalysisSummary> AnalyzeAsync(
        IEmailProvider provider, 
        AccountConfig config, 
        ILocalArchiveStorage? localStorage = null, 
        CancellationToken cancellationToken = default)
    {
        var isConnected = await provider.ConnectAndAuthenticateAsync(config, cancellationToken);
        if (!isConnected)
        {
            throw new InvalidOperationException("Impossibile connettersi al server email con le credenziali fornite.");
        }

        var folders = await provider.GetFoldersAsync(cancellationToken);
        int totalFolders = folders.Count;

        var currentYear = DateTime.Now.Year;
        var startYear = currentYear;
        var minYear = 2005;

        var summary = new MailboxAnalysisSummary
        {
            Email = config.EmailAddress,
            TotalFoldersCount = totalFolders
        };

        var allYears = Enumerable.Range(minYear, startYear - minYear + 1).Reverse().ToList();

        // Query message count for each year in parallel
        var results = await Task.WhenAll(allYears.Select(async year =>
        {
            try
            {
                var serverCount = await provider.CountMessagesAsync(BackupFilter.ForYear(year), cancellationToken);
                int localCount = 0;

                if (localStorage != null)
                {
                    localCount = await localStorage.GetTotalMessageCountAsync(year, cancellationToken);
                }

                return new YearStatItem
                {
                    Year = year,
                    ServerCount = serverCount,
                    LocalCount = localCount
                };
            }
            catch
            {
                return new YearStatItem { Year = year, ServerCount = 0, LocalCount = 0 };
            }
        }));

        // Filter out trailing empty years (keep only years with messages, plus current year)
        var populatedYears = results.Where(r => r.ServerCount > 0 || r.Year == currentYear).ToList();
        summary.Years = populatedYears;
        summary.TotalServerMessages = populatedYears.Sum(y => y.ServerCount);
        summary.TotalLocalMessages = populatedYears.Sum(y => y.LocalCount);

        return summary;
    }
}
