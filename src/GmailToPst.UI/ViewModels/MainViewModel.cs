using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using GmailToPst.Storage.Database;
using GmailToPst.Storage.Profiles;
using GmailToPst.Storage.Settings;
using GmailToPst.UI.Views;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace GmailToPst.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private ILocalArchiveStorage? _storage;
    private CancellationTokenSource? _backupCts;

    // Account & Status Info
    [ObservableProperty]
    private string _currentAccountEmail = "Nessun account collegato";

    [ObservableProperty]
    private string _currentYearFilter = "Tutti gli anni";

    // Dynamic Year Filter Selector in Main UI
    public ObservableCollection<string> AvailableArchiveYears { get; } = new();

    [ObservableProperty]
    private string _selectedArchiveYearFilter = "Tutti gli anni";

    [ObservableProperty]
    private int _totalMessagesCount = 0;

    [ObservableProperty]
    private long _totalSizeBytes = 0;

    [ObservableProperty]
    private string _formattedTotalSize = "0 B";

    [ObservableProperty]
    private bool _isPerformingBackup = false;

    [ObservableProperty]
    private string _backupStatusText = "";

    [ObservableProperty]
    private double _backupProgressPercent = 0;

    [ObservableProperty]
    private string _backupBytesText = "";

    // Multi-Account Explorer Tree & Messages
    public ObservableCollection<AccountNode> Accounts { get; } = new();

    public ObservableCollection<EmailFolder> RootFolders { get; } = new();

    [ObservableProperty]
    private EmailFolder? _selectedFolder;

    public ObservableCollection<EmailMessage> CurrentMessages { get; } = new();

    [ObservableProperty]
    private EmailMessage? _selectedMessage;

    [ObservableProperty]
    private EmailMessage? _currentMessageDetails;

    [ObservableProperty]
    private string _searchKeyword = "";

    [ObservableProperty]
    private bool _isLoadingMessages = false;

    [ObservableProperty]
    private bool _showHtmlView = true;

    public MainViewModel()
    {
    }

    public async Task InitializeWithStorageAsync(ILocalArchiveStorage storage, string accountEmail, int? year = null)
    {
        _storage = storage;
        CurrentAccountEmail = accountEmail;

        await RefreshAllAccountsAsync(accountEmail);
    }

    public async Task RefreshAllAccountsAsync(string? activeEmail = null)
    {
        var settings = SettingsManager.LoadSettings();
        var basePath = settings.ArchivesPath;

        if ((string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath)) && Directory.Exists(@"Z:\export\Archives"))
        {
            basePath = @"Z:\export\Archives";
            settings.ArchivesPath = basePath;
            SettingsManager.SaveSettings(settings);
        }

        var discoveredEmails = new List<string>();

        await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(basePath))
                {
                    foreach (var dir in Directory.GetDirectories(basePath))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (File.Exists(Path.Combine(dir, "archive.db")) && !dirName.Contains("_"))
                        {
                            if (!discoveredEmails.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                            {
                                discoveredEmails.Add(dirName);
                            }
                        }
                    }
                }

                // Fallback a Z:\export\Archives se vuoto
                if (!discoveredEmails.Any() && Directory.Exists(@"Z:\export\Archives"))
                {
                    basePath = @"Z:\export\Archives";
                    foreach (var dir in Directory.GetDirectories(basePath))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (File.Exists(Path.Combine(dir, "archive.db")) && !dirName.Contains("_"))
                        {
                            if (!discoveredEmails.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                            {
                                discoveredEmails.Add(dirName);
                            }
                        }
                    }
                }
            }
            catch { }
        });

        Accounts.Clear();
        var yearFilter = GetCurrentYearFilterNumber();

        var targetEmail = activeEmail ?? CurrentAccountEmail;
        var targetToSelect = discoveredEmails.FirstOrDefault(e => string.Equals(e, targetEmail, StringComparison.OrdinalIgnoreCase))
                             ?? discoveredEmails.FirstOrDefault();

        // 1. Popola istantaneamente tutti i nodi
        var nodes = new List<AccountNode>();
        foreach (var email in discoveredEmails)
        {
            var accNode = new AccountNode
            {
                Email = email,
                DisplayName = email,
                ArchiveBasePath = basePath,
                IsExpanded = true
            };
            nodes.Add(accNode);
            Accounts.Add(accNode);
        }

        // 2. Seleziona e inizializza l'account attivo
        if (!string.IsNullOrEmpty(targetToSelect))
        {
            await SwitchToAccountAsync(targetToSelect);
        }

        // 3. Calcola i badge di dimensione e cartelle in background per tutti gli account
        _ = Task.Run(async () =>
        {
            foreach (var accNode in nodes)
            {
                if (string.Equals(accNode.Email, CurrentAccountEmail, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var tempStorage = new SqliteArchiveStorage();
                    await tempStorage.InitializeAsync(basePath, accNode.Email);
                    var count = await tempStorage.GetTotalMessageCountAsync(yearFilter);
                    var size = await tempStorage.GetTotalSizeBytesAsync(yearFilter);
                    var folders = await tempStorage.GetFoldersAsync(yearFilter);
                    await tempStorage.DisposeAsync();

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        accNode.TotalMessagesCount = count;
                        accNode.TotalSizeBytes = size;
                        accNode.FormattedTotalSize = FormatBytes(size);
                        accNode.Folders.Clear();
                        foreach (var f in folders) accNode.Folders.Add(f);
                    });
                }
                catch { }
            }
        });
    }

    public async Task SwitchToAccountAsync(string email, string? selectFolderId = null)
    {
        var settings = SettingsManager.LoadSettings();
        var basePath = settings.ArchivesPath;

        if (string.Equals(CurrentAccountEmail, email, StringComparison.OrdinalIgnoreCase) && _storage != null)
        {
            // Account già attivo: non chiudere la connessione attiva
            await RefreshFoldersAsync();
            if (!string.IsNullOrEmpty(selectFolderId))
            {
                var match = FindFolderById(RootFolders, selectFolderId);
                if (match != null)
                {
                    SelectedFolder = match;
                    await LoadMessagesForFolderAsync(match.Id);
                }
            }
            return;
        }

        if (IsPerformingBackup)
        {
            return;
        }

        var oldStorage = _storage;
        _storage = null;
        if (oldStorage != null)
        {
            _ = Task.Run(async () => await oldStorage.DisposeAsync());
        }

        var newStorage = new SqliteArchiveStorage();
        await newStorage.InitializeAsync(basePath, email);
        _storage = newStorage;
        CurrentAccountEmail = email;

        await RefreshAvailableYearsAsync();

        var yearFilter = GetCurrentYearFilterNumber();
        var (count, size, folders) = await Task.Run(async () =>
        {
            var c = await newStorage.GetTotalMessageCountAsync(yearFilter);
            var s = await newStorage.GetTotalSizeBytesAsync(yearFilter);
            var f = await newStorage.GetFoldersAsync(yearFilter);
            return (c, s, f);
        });

        TotalMessagesCount = count;
        TotalSizeBytes = size;
        FormattedTotalSize = FormatBytes(size);

        RootFolders.Clear();
        foreach (var f in folders)
        {
            RootFolders.Add(f);
        }

        var activeNode = Accounts.FirstOrDefault(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));
        if (activeNode != null)
        {
            activeNode.Folders.Clear();
            foreach (var f in folders)
            {
                activeNode.Folders.Add(f);
            }
            activeNode.TotalMessagesCount = count;
            activeNode.TotalSizeBytes = size;
            activeNode.FormattedTotalSize = FormattedTotalSize;
        }

        if (!string.IsNullOrEmpty(selectFolderId))
        {
            var match = FindFolderById(RootFolders, selectFolderId);
            if (match != null)
            {
                SelectedFolder = match;
                await LoadMessagesForFolderAsync(match.Id);
                return;
            }
        }

        if (SelectedFolder != null && RootFolders.Any(f => f.Id == SelectedFolder.Id))
        {
            await LoadMessagesForFolderAsync(SelectedFolder.Id);
        }
        else if (RootFolders.Any())
        {
            SelectedFolder = RootFolders.First();
        }
        else
        {
            CurrentMessages.Clear();
            SelectedMessage = null;
            CurrentMessageDetails = null;
        }
    }

    private static EmailFolder? FindFolderById(IEnumerable<EmailFolder> list, string id)
    {
        foreach (var f in list)
        {
            if (f.Id == id) return f;
            var sub = FindFolderById(f.SubFolders, id);
            if (sub != null) return sub;
        }
        return null;
    }

    public async Task RefreshAvailableYearsAsync()
    {
        if (_storage == null) return;

        var years = await _storage.GetAvailableYearsAsync();
        var currentSelected = SelectedArchiveYearFilter;

        AvailableArchiveYears.Clear();
        AvailableArchiveYears.Add("Tutti gli anni");
        foreach (var y in years)
        {
            AvailableArchiveYears.Add(y.ToString());
        }

        if (AvailableArchiveYears.Contains(currentSelected))
        {
            SelectedArchiveYearFilter = currentSelected;
        }
        else
        {
            SelectedArchiveYearFilter = "Tutti gli anni";
        }
    }

    [ObservableProperty]
    private bool _hasSelectedSpecificYear = false;

    async partial void OnSelectedArchiveYearFilterChanged(string value)
    {
        CurrentYearFilter = string.IsNullOrEmpty(value) ? "Tutti gli anni" : value;
        HasSelectedSpecificYear = int.TryParse(value, out _);
        await RefreshFoldersAsync();
    }

    public int? GetCurrentYearFilterNumber()
    {
        if (int.TryParse(SelectedArchiveYearFilter, out var y))
            return y;
        return null;
    }

    [RelayCommand]
    public async Task RefreshFoldersAsync()
    {
        if (_storage == null) return;

        var yearFilter = GetCurrentYearFilterNumber();

        RootFolders.Clear();
        var folders = await _storage.GetFoldersAsync(yearFilter);
        foreach (var f in folders)
        {
            RootFolders.Add(f);
        }

        TotalMessagesCount = await _storage.GetTotalMessageCountAsync(yearFilter);
        TotalSizeBytes = await _storage.GetTotalSizeBytesAsync(yearFilter);
        FormattedTotalSize = FormatBytes(TotalSizeBytes);

        // Aggiorna anche l'account corrispondente nella lista Accounts
        var currentAcc = Accounts.FirstOrDefault(a => string.Equals(a.Email, CurrentAccountEmail, StringComparison.OrdinalIgnoreCase));
        if (currentAcc != null)
        {
            currentAcc.Folders.Clear();
            foreach (var f in folders) currentAcc.Folders.Add(f);
            currentAcc.TotalMessagesCount = TotalMessagesCount;
            currentAcc.TotalSizeBytes = TotalSizeBytes;
            currentAcc.FormattedTotalSize = FormattedTotalSize;
        }

        if (SelectedFolder != null && RootFolders.Any(f => f.Id == SelectedFolder.Id))
        {
            await LoadMessagesForFolderAsync(SelectedFolder.Id);
        }
        else if (RootFolders.Any())
        {
            SelectedFolder = RootFolders.First();
        }
        else
        {
            CurrentMessages.Clear();
            SelectedMessage = null;
            CurrentMessageDetails = null;
        }
    }

    async partial void OnSelectedFolderChanged(EmailFolder? value)
    {
        if (value != null)
        {
            await LoadMessagesForFolderAsync(value.Id);
        }
        else
        {
            CurrentMessages.Clear();
            SelectedMessage = null;
            CurrentMessageDetails = null;
        }
    }

    async partial void OnSelectedMessageChanged(EmailMessage? value)
    {
        if (value != null && _storage != null)
        {
            var msgId = value.Id;
            var details = await Task.Run(async () =>
            {
                try
                {
                    return await _storage.GetMessageDetailsAsync(msgId);
                }
                catch
                {
                    return null;
                }
            });
            CurrentMessageDetails = details;
        }
        else
        {
            CurrentMessageDetails = null;
        }
    }

    [RelayCommand]
    private async Task SearchMessagesAsync()
    {
        if (SelectedFolder != null)
        {
            await LoadMessagesForFolderAsync(SelectedFolder.Id);
        }
    }

    public async Task LoadMessagesForFolderAsync(string folderId)
    {
        if (_storage == null) return;

        IsLoadingMessages = true;
        try
        {
            var yearFilter = GetCurrentYearFilterNumber();
            var search = SearchKeyword;
            var storage = _storage;

            var messages = await Task.Run(async () =>
            {
                try
                {
                    return await storage.GetMessagesInFolderAsync(folderId, 0, 300, search, yearFilter);
                }
                catch
                {
                    return new List<EmailMessage>();
                }
            });

            CurrentMessages.Clear();
            foreach (var m in messages)
            {
                CurrentMessages.Add(m);
            }

            if (CurrentMessages.Any())
            {
                SelectedMessage = CurrentMessages.First();
            }
            else
            {
                SelectedMessage = null;
                CurrentMessageDetails = null;
            }
        }
        finally
        {
            IsLoadingMessages = false;
        }
    }

    public async Task RunBackupProcessAsync(
        IEmailProvider provider,
        AccountConfig config,
        BackupFilter filter,
        string accountEmail,
        string archiveBasePath,
        int? year)
    {
        IsPerformingBackup = true;
        BackupStatusText = "Connessione ed autenticazione al server di posta...";
        BackupProgressPercent = 0;
        _backupCts = new CancellationTokenSource();

        try
        {
            await provider.ConnectAndAuthenticateAsync(config, _backupCts.Token);

            var storage = new SqliteArchiveStorage();
            await storage.InitializeAsync(archiveBasePath, accountEmail, year, _backupCts.Token);
            _storage = storage;

            CurrentAccountEmail = accountEmail;

            BackupStatusText = "Recupero struttura cartelle...";
            var folders = await provider.GetFoldersAsync(_backupCts.Token);
            await storage.SaveFolderHierarchyAsync(folders, _backupCts.Token);

            // Carica messaggi già presenti per saltarli e riprendere il download senza riscaricare
            var existingIds = await storage.GetExistingMessageIdsAsync(null, _backupCts.Token);
            filter.ExistingMessageIds = existingIds;

            var progress = new Progress<BackupProgress>(p =>
            {
                BackupStatusText = p.StatusMessage;
                BackupProgressPercent = p.ProgressPercentage;
                BackupBytesText = p.FormattedBytes;
            });

            await foreach (var (msg, rawBytes) in provider.FetchMessagesAsync(filter, progress, _backupCts.Token))
            {
                await storage.SaveMessageAsync(msg, rawBytes, _backupCts.Token);
            }

            MessageBox.Show("Download e archiviazione completati con successo!", "Backup Terminato", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            BackupStatusText = "Operazione interrotta dall'utente. I messaggi scaricati finora sono stati archiviati.";
        }
        catch (Exception ex)
        {
            BackupStatusText = $"Errore: {ex.Message}";
            MessageBox.Show($"Si è verificato un errore durante il backup:\n{ex.Message}", "Errore Backup", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsPerformingBackup = false;
            await provider.DisposeAsync();

            await RefreshAllAccountsAsync(accountEmail);
            if (year.HasValue)
            {
                SelectedArchiveYearFilter = year.Value.ToString();
            }
        }
    }

    [RelayCommand]
    public async Task DeleteSelectedYearAsync()
    {
        if (_storage == null)
        {
            MessageBox.Show("Nessun archivio caricato.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var year = GetCurrentYearFilterNumber();
        if (!year.HasValue)
        {
            MessageBox.Show("Seleziona un anno specifico (es. 2012 o 2022) dal menu a tendina in alto per poterlo eliminare.", "Seleziona un anno", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var count = await _storage.GetTotalMessageCountAsync(year.Value);
        var size = await _storage.GetTotalSizeBytesAsync(year.Value);

        var confirm = MessageBox.Show(
            $"Sei sicuro di voler eliminare DEFINITIVAMENTE tutti i dati dell'anno {year.Value}?\n\n" +
            $"• Messaggi archiviati: {count}\n" +
            $"• Spazio che verrà liberato: {FormatBytes(size)}\n\n" +
            "I file .eml, gli allegati e i record del database relativi a quest'anno verranno cancellati dal disco locale.",
            $"Conferma Eliminazione Anno {year.Value}",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        IsPerformingBackup = true;
        BackupStatusText = $"Eliminazione dei dati dell'anno {year.Value}...";

        try
        {
            var deleted = await _storage.DeleteYearDataAsync(year.Value);
            SelectedArchiveYearFilter = "Tutti gli anni";
            await RefreshAllAccountsAsync(CurrentAccountEmail);

            MessageBox.Show($"Eliminati con successo {deleted} messaggi dell'anno {year.Value}. Lo spazio su disco è stato liberato.", "Anno Eliminato", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante l'eliminazione:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsPerformingBackup = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAccountAsync(AccountNode? accountNode)
    {
        var targetAccount = accountNode ?? Accounts.FirstOrDefault(a => string.Equals(a.Email, CurrentAccountEmail, StringComparison.OrdinalIgnoreCase));
        if (targetAccount == null) return;

        var email = targetAccount.Email;
        var result = MessageBox.Show(
            $"Sei sicuro di voler eliminare definitivamente l'account '{email}' e tutti i suoi dati scaricati (database, messaggi ed allegati)?\n\nQuesta operazione cancellerà tutti i file locali dell'account e non può essere annullata.",
            "Conferma Eliminazione Account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            if (string.Equals(CurrentAccountEmail, email, StringComparison.OrdinalIgnoreCase))
            {
                if (_storage != null)
                {
                    await _storage.DisposeAsync();
                    _storage = null;
                }
                CurrentAccountEmail = "Nessun account collegato";
                RootFolders.Clear();
                CurrentMessages.Clear();
                SelectedMessage = null;
                CurrentMessageDetails = null;
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            var settings = SettingsManager.LoadSettings();
            var accountDir = Path.Combine(settings.ArchivesPath, email);
            if (Directory.Exists(accountDir))
            {
                Directory.Delete(accountDir, true);
            }

            await ProfileManager.DeleteProfileAsync(email, AccountType.ImapAppPassword);
            await ProfileManager.DeleteProfileAsync(email, AccountType.GmailOAuth);
            await ProfileManager.DeleteProfileAsync(email, AccountType.GoogleWorkspaceServiceAccount);

            Accounts.Remove(targetAccount);

            if (Accounts.Any())
            {
                await SwitchToAccountAsync(Accounts.First().Email);
            }
            else
            {
                await RefreshAllAccountsAsync();
            }

            MessageBox.Show($"L'account '{email}' e tutti i suoi file sono stati eliminati correttamente.", "Account Eliminato", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante l'eliminazione dell'account:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            await RefreshAllAccountsAsync();
        }
    }

    [RelayCommand]
    private async Task SyncAccountAsync(AccountNode? accountNode)
    {
        var targetAccount = accountNode ?? Accounts.FirstOrDefault(a => string.Equals(a.Email, CurrentAccountEmail, StringComparison.OrdinalIgnoreCase));
        if (targetAccount == null) return;

        var email = targetAccount.Email;
        var profiles = await ProfileManager.LoadProfilesAsync();

        // 1. Try finding direct profile
        var profile = profiles.FirstOrDefault(p => string.Equals(p.EmailAddress, email, StringComparison.OrdinalIgnoreCase));

        // 2. If not found, check if it belongs to a saved Google Workspace profile
        if (profile == null)
        {
            var wsAdminProfile = profiles.FirstOrDefault(p => p.Type == AccountType.GoogleWorkspaceServiceAccount &&
                (p.DiscoveredUsers.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrEmpty(p.WorkspaceAdminEmail) && email.EndsWith(p.WorkspaceAdminEmail.Substring(p.WorkspaceAdminEmail.IndexOf('@'))))));

            if (wsAdminProfile != null)
            {
                profile = new AccountConfig
                {
                    Type = AccountType.GoogleWorkspaceServiceAccount,
                    EmailAddress = email,
                    WorkspaceAdminEmail = wsAdminProfile.WorkspaceAdminEmail,
                    ServiceAccountKeyFilePath = wsAdminProfile.ServiceAccountKeyFilePath
                };
            }
        }

        if (profile == null)
        {
            MessageBox.Show($"Nessuna credenziale trovata per l'account '{email}'. Apri 'Nuovo Backup...' per configurare la connessione.", "Credenziali non trovate", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IEmailProvider provider;
        if (profile.Type == AccountType.GoogleWorkspaceServiceAccount)
        {
            provider = new GmailToPst.Providers.Workspace.GoogleWorkspaceEmailProvider();
        }
        else if (profile.Type == AccountType.GmailOAuth)
        {
            provider = new GmailToPst.Providers.GmailApi.GmailApiProvider();
        }
        else
        {
            provider = new GmailToPst.Providers.Imap.ImapProvider();
        }

        var settings = SettingsManager.LoadSettings();
        var yearFilter = GetCurrentYearFilterNumber();
        var filter = yearFilter.HasValue ? BackupFilter.ForYear(yearFilter.Value) : new BackupFilter();

        if (targetAccount.IsSyncing)
        {
            MessageBox.Show($"La sincronizzazione per '{email}' è già in esecuzione.", "Sincronizzazione in corso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        targetAccount.IsSyncing = true;
        targetAccount.SyncStatusText = "Avvio sincronizzazione...";
        targetAccount.SyncProgressPercent = 0;

        _ = Task.Run(async () =>
        {
            var syncStorage = new SqliteArchiveStorage();
            try
            {
                await provider.ConnectAndAuthenticateAsync(profile);
                await syncStorage.InitializeAsync(settings.ArchivesPath, email);

                var folders = await provider.GetFoldersAsync();
                await syncStorage.SaveFolderHierarchyAsync(folders);

                var existingIds = await syncStorage.GetExistingMessageIdsAsync(null);
                filter.ExistingMessageIds = existingIds;

                var syncProgress = new Progress<BackupProgress>(p =>
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        targetAccount.SyncStatusText = p.StatusMessage;
                        targetAccount.SyncProgressPercent = p.ProgressPercentage;
                        if (string.Equals(CurrentAccountEmail, email, StringComparison.OrdinalIgnoreCase))
                        {
                            BackupStatusText = p.StatusMessage;
                            BackupProgressPercent = p.ProgressPercentage;
                        }
                    });
                });

                await foreach (var (msg, rawBytes) in provider.FetchMessagesAsync(filter, syncProgress))
                {
                    await syncStorage.SaveMessageAsync(msg, rawBytes);
                }

                var updatedFolders = await syncStorage.GetFoldersAsync(yearFilter);
                var totalCount = await syncStorage.GetTotalMessageCountAsync(yearFilter);
                var totalBytes = await syncStorage.GetTotalSizeBytesAsync(yearFilter);

                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    targetAccount.Folders.Clear();
                    foreach (var f in updatedFolders) targetAccount.Folders.Add(f);
                    targetAccount.TotalMessagesCount = totalCount;
                    targetAccount.TotalSizeBytes = totalBytes;
                    targetAccount.FormattedTotalSize = FormatBytes(totalBytes);

                    if (string.Equals(CurrentAccountEmail, email, StringComparison.OrdinalIgnoreCase))
                    {
                        await RefreshFoldersAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    targetAccount.SyncStatusText = $"Errore: {ex.Message}";
                });
            }
            finally
            {
                await syncStorage.DisposeAsync();
                await provider.DisposeAsync();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    targetAccount.IsSyncing = false;
                });
            }
        });
    }

    [RelayCommand]
    private async Task AnalyzeAccountAsync(AccountNode? accountNode)
    {
        var targetAccount = accountNode ?? Accounts.FirstOrDefault(a => string.Equals(a.Email, CurrentAccountEmail, StringComparison.OrdinalIgnoreCase));
        if (targetAccount == null) return;

        var email = targetAccount.Email;
        var profiles = await ProfileManager.LoadProfilesAsync();
        var profile = profiles.FirstOrDefault(p => string.Equals(p.EmailAddress, email, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
        {
            var wsAdminProfile = profiles.FirstOrDefault(p => p.Type == AccountType.GoogleWorkspaceServiceAccount &&
                (p.DiscoveredUsers.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrEmpty(p.WorkspaceAdminEmail) && email.EndsWith(p.WorkspaceAdminEmail.Substring(p.WorkspaceAdminEmail.IndexOf('@'))))));

            if (wsAdminProfile != null)
            {
                profile = new AccountConfig
                {
                    Type = AccountType.GoogleWorkspaceServiceAccount,
                    EmailAddress = email,
                    WorkspaceAdminEmail = wsAdminProfile.WorkspaceAdminEmail,
                    ServiceAccountKeyFilePath = wsAdminProfile.ServiceAccountKeyFilePath
                };
            }
        }

        if (profile == null)
        {
            MessageBox.Show($"Nessuna credenziale trovata per l'account '{email}'. Apri 'Nuovo Backup...' per configurare la connessione.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IEmailProvider provider;
        if (profile.Type == AccountType.GoogleWorkspaceServiceAccount)
            provider = new GmailToPst.Providers.Workspace.GoogleWorkspaceEmailProvider();
        else if (profile.Type == AccountType.GmailOAuth)
            provider = new GmailToPst.Providers.GmailApi.GmailApiProvider();
        else
            provider = new GmailToPst.Providers.Imap.ImapProvider();

        try
        {
            var summary = await GmailToPst.Providers.Helpers.MailboxAnalyzerService.AnalyzeAsync(provider, profile, _storage);
            var dialog = new MailboxAnalysisDialog(summary);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true && dialog.ActionTriggered)
            {
                var settings = SettingsManager.LoadSettings();
                var filter = dialog.SelectedYear.HasValue ? BackupFilter.ForYear(dialog.SelectedYear.Value) : new BackupFilter();
                await RunBackupProcessAsync(provider, profile, filter, email, settings.ArchivesPath, dialog.SelectedYear);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante l'analisi della casella:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CancelBackup()
    {
        _backupCts?.Cancel();
    }

    [RelayCommand]
    public void ExtractAllAttachments()
    {
        if (_storage == null)
        {
            MessageBox.Show("Nessun archivio caricato. Esegui prima un backup.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var yearFilter = GetCurrentYearFilterNumber();
        var dialog = new AttachmentExtractDialog(_storage, yearFilter, SelectedFolder);
        dialog.Owner = Application.Current.MainWindow;
        dialog.ShowDialog();
    }

    [RelayCommand]
    public void OpenSettings()
    {
        var dialog = new SettingsDialog();
        dialog.Owner = Application.Current.MainWindow;
        dialog.ShowDialog();
    }

    [RelayCommand]
    public void OpenArchivesFolder()
    {
        var settings = SettingsManager.LoadSettings();
        var path = settings.ArchivesPath;
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        else
        {
            MessageBox.Show($"La cartella non esiste ancora:\n{path}", "Cartella non trovata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    public void ShowAbout()
    {
        MessageBox.Show(
            "Gmail & Google Workspace Backup Explorer\nVersione 1.2\n\n" +
            "Archiviazione locale, filtraggio per anno ed esportazione in PST, MSG ed EML.\n" +
            "Include estrattore massivo di allegati ed explorer 3-pane integrato.",
            "Informazioni",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task SaveAttachmentAsync(AttachmentInfo? attachment)
    {
        if (attachment == null || _storage == null) return;

        try
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                FileName = attachment.FileName,
                Filter = "Tutti i file (*.*)|*.*"
            };

            if (sfd.ShowDialog() == true)
            {
                var bytes = await _storage.GetAttachmentBytesAsync(attachment.Id);
                await File.WriteAllBytesAsync(sfd.FileName, bytes);
                MessageBox.Show($"Allegato salvato con successo:\n{sfd.FileName}", "Allegato salvato", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore salvataggio allegato:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task OpenAttachmentAsync(AttachmentInfo? attachment)
    {
        if (attachment == null || _storage == null) return;

        try
        {
            var bytes = await _storage.GetAttachmentBytesAsync(attachment.Id);
            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{attachment.FileName}");
            await File.WriteAllBytesAsync(tempPath, bytes);

            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore apertura allegato:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenRawEmlFile()
    {
        if (CurrentMessageDetails != null && !string.IsNullOrEmpty(CurrentMessageDetails.RawEmlPath) && File.Exists(CurrentMessageDetails.RawEmlPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = CurrentMessageDetails.RawEmlPath,
                UseShellExecute = true
            });
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    public ILocalArchiveStorage? GetCurrentStorage() => _storage;
}
