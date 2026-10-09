using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using GmailToPst.UI.ViewModels;
using System.Windows;

namespace GmailToPst.UI.Views;

public partial class ConnectionDialog : Window
{
    public (IEmailProvider Provider, AccountConfig Config, BackupFilter Filter, string Email, string BasePath, int? Year)? Result { get; private set; }

    public ConnectionDialog()
    {
        InitializeComponent();
        var vm = new ConnectViewModel();
        vm.StartBackupRequested += (s, e) =>
        {
            Result = e;
            DialogResult = true;
            Close();
        };
        DataContext = vm;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
