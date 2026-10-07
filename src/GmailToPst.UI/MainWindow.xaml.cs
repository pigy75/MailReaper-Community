using GmailToPst.Core.Models;
using GmailToPst.UI.ViewModels;
using GmailToPst.UI.Views;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace GmailToPst.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += MainWindow_Loaded;
        DataContext = _viewModel;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshAllAccountsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore caricamento archivi:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentMessageDetails))
        {
            UpdateEmailBrowserContent();
        }
    }

    private void UpdateEmailBrowserContent()
    {
        if (_viewModel.CurrentMessageDetails == null)
        {
            try { EmailBrowser.NavigateToString("<html><body></body></html>"); } catch { }
            return;
        }

        var html = _viewModel.CurrentMessageDetails.BodyHtml;
        if (string.IsNullOrWhiteSpace(html))
        {
            var text = _viewModel.CurrentMessageDetails.BodyText ?? "(Nessun contenuto nel corpo dell'email)";
            var escapedText = System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br/>");
            html = $"<html><body style='font-family: Segoe UI, sans-serif; font-size: 13px; line-height: 1.5; color: #1F2937; padding: 15px;'>{escapedText}</body></html>";
        }
        else
        {
            // Aggiungi stile base per garantire rendering leggibile
            html = $"<html><head><meta http-equiv='X-UA-Compatible' content='IE=Edge'/><style>body {{ font-family: Segoe UI, sans-serif; font-size: 13px; color: #1F2937; margin: 15px; word-wrap: break-word; }} img {{ max-width: 100%; height: auto; }}</style></head><body>{html}</body></html>";
        }

        try
        {
            EmailBrowser.NavigateToString(html);
        }
        catch
        {
            // Fallback
        }
    }

    private async void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is AccountNode acc)
        {
            if (!string.Equals(_viewModel.CurrentAccountEmail, acc.Email, StringComparison.OrdinalIgnoreCase))
            {
                if (!_viewModel.IsPerformingBackup)
                {
                    await _viewModel.SwitchToAccountAsync(acc.Email);
                }
            }
        }
        else if (e.NewValue is EmailFolder folder)
        {
            var parentAcc = _viewModel.Accounts.FirstOrDefault(a => FolderBelongsToAccount(a.Folders, folder.Id));
            if (parentAcc != null && !string.Equals(_viewModel.CurrentAccountEmail, parentAcc.Email, StringComparison.OrdinalIgnoreCase))
            {
                if (!_viewModel.IsPerformingBackup)
                {
                    await _viewModel.SwitchToAccountAsync(parentAcc.Email, folder.Id);
                }
            }
            else
            {
                _viewModel.SelectedFolder = folder;
            }
        }
    }

    private static bool FolderBelongsToAccount(IEnumerable<EmailFolder> list, string folderId)
    {
        foreach (var f in list)
        {
            if (f.Id == folderId) return true;
            if (FolderBelongsToAccount(f.SubFolders, folderId)) return true;
        }
        return false;
    }

    private async void NewBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConnectionDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result.HasValue)
        {
            var r = dialog.Result.Value;
            await _viewModel.RunBackupProcessAsync(r.Provider, r.Config, r.Filter, r.Email, r.BasePath, r.Year);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var storage = _viewModel.GetCurrentStorage();
        if (storage == null)
        {
            MessageBox.Show("Nessun archivio caricato. Esegui prima un backup.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var currentYear = _viewModel.GetCurrentYearFilterNumber();
        var dialog = new ExportDialog(storage, _viewModel.CurrentAccountEmail, currentYear) { Owner = this };
        dialog.ShowDialog();
    }

    private void ExportSelectedFolder_Click(object sender, RoutedEventArgs e)
    {
        var storage = _viewModel.GetCurrentStorage();
        if (storage == null || _viewModel.SelectedFolder == null)
        {
            MessageBox.Show("Seleziona una cartella da esportare.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var currentYear = _viewModel.GetCurrentYearFilterNumber();
        var dialog = new ExportDialog(storage, _viewModel.CurrentAccountEmail, currentYear, new List<string> { _viewModel.SelectedFolder.Id })
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private async void SyncAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AccountNode acc)
        {
            await _viewModel.SyncAccountCommand.ExecuteAsync(acc);
        }
        else if (_viewModel.Accounts.FirstOrDefault(a => string.Equals(a.Email, _viewModel.CurrentAccountEmail, StringComparison.OrdinalIgnoreCase)) is AccountNode currentAcc)
        {
            await _viewModel.SyncAccountCommand.ExecuteAsync(currentAcc);
        }
    }

    private async void AnalyzeAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AccountNode acc)
        {
            await _viewModel.AnalyzeAccountCommand.ExecuteAsync(acc);
        }
        else if (_viewModel.Accounts.FirstOrDefault(a => string.Equals(a.Email, _viewModel.CurrentAccountEmail, StringComparison.OrdinalIgnoreCase)) is AccountNode currentAcc)
        {
            await _viewModel.AnalyzeAccountCommand.ExecuteAsync(currentAcc);
        }
    }

    private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AccountNode acc)
        {
            await _viewModel.DeleteAccountCommand.ExecuteAsync(acc);
        }
        else if (_viewModel.Accounts.FirstOrDefault(a => string.Equals(a.Email, _viewModel.CurrentAccountEmail, StringComparison.OrdinalIgnoreCase)) is AccountNode currentAcc)
        {
            await _viewModel.DeleteAccountCommand.ExecuteAsync(currentAcc);
        }
    }

    private void License_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.LicenseDialog { Owner = this };
        dlg.ShowDialog();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        var updateService = new GmailToPst.Core.Services.GitHubUpdateService();
        var result = await updateService.CheckForUpdatesAsync("1.0.0");

        if (!result.Success)
        {
            MessageBox.Show(result.Message, "Verifica Aggiornamenti GitHub", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (result.IsUpToDate)
        {
            MessageBox.Show(result.Message, "MailReaper Aggiornato", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            var answer = MessageBox.Show(
                $"{result.Message}\n\nNote di rilascio:\n{result.ReleaseNotes}\n\nVuoi aprire la pagina di download su GitHub?",
                "Aggiornamento Disponibile (GitHub Releases)",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(result.HtmlUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = result.HtmlUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}