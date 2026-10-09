using GmailToPst.Core.Models;
using GmailToPst.UI.ViewModels;
using System.Windows;

namespace GmailToPst.UI.Views;

public partial class MailboxAnalysisDialog : Window
{
    public int? SelectedYear { get; private set; }
    public bool DownloadAll { get; private set; }
    public bool ActionTriggered { get; private set; } = false;

    public MailboxAnalysisDialog(MailboxAnalysisSummary summary)
    {
        InitializeComponent();
        var vm = new MailboxAnalysisViewModel();
        vm.LoadSummary(summary);

        vm.YearSelected += (s, year) =>
        {
            SelectedYear = year;
            DownloadAll = vm.DownloadAllRequested;
            ActionTriggered = true;
            DialogResult = true;
            Close();
        };

        DataContext = vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
