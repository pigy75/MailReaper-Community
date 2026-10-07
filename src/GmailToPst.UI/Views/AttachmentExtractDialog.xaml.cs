using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using GmailToPst.UI.ViewModels;
using System.Windows;

namespace GmailToPst.UI.Views;

public partial class AttachmentExtractDialog : Window
{
    public AttachmentExtractDialog(ILocalArchiveStorage storage, int? year = null, EmailFolder? selectedFolder = null)
    {
        InitializeComponent();
        var vm = new AttachmentExtractViewModel(storage, year, selectedFolder);
        DataContext = vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
