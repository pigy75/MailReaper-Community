using System.Diagnostics;
using System.IO;
using System.Windows;
using GmailToPst.Core.Interfaces;
using GmailToPst.UI.ViewModels;

namespace GmailToPst.UI.Views;

public partial class ExportDialog : Window
{
    private readonly ExportViewModel _viewModel;

    public ExportDialog(ILocalArchiveStorage storage, string defaultEmail, int? year = null, List<string>? preselectedFolderIds = null)
    {
        InitializeComponent();
        _viewModel = new ExportViewModel(storage, defaultEmail, year, preselectedFolderIds);
        _viewModel.ExportCompleted += (s, path) =>
        {
            var res = MessageBox.Show($"Esportazione completata con successo in:\n{path}\n\nVuoi aprire la cartella di destinazione?", "Esportazione completata", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (res == MessageBoxResult.Yes)
            {
                var folder = File.Exists(path) ? Path.GetDirectoryName(path) : path;
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
                }
            }
            Close();
        };
        DataContext = _viewModel;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedExporter?.Exporter.Format == ExportFormat.OutlookPst)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                FileName = Path.GetFileName(_viewModel.OutputPath),
                Filter = "Archivio di Outlook (*.pst)|*.pst|Tutti i file (*.*)|*.*",
                DefaultExt = ".pst"
            };
            if (sfd.ShowDialog() == true)
            {
                _viewModel.OutputPath = sfd.FileName;
            }
        }
        else
        {
            var ofd = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Seleziona la cartella di destinazione per l'esportazione",
                InitialDirectory = Directory.Exists(_viewModel.OutputPath) ? _viewModel.OutputPath : Path.GetDirectoryName(_viewModel.OutputPath)
            };
            if (ofd.ShowDialog() == true)
            {
                _viewModel.OutputPath = ofd.FolderName;
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
