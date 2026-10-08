using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GmailToPst.Core.Interfaces;
using GmailToPst.Core.Models;
using GmailToPst.Providers.GmailApi;
using GmailToPst.Providers.Imap;
using GmailToPst.Storage.Profiles;
using GmailToPst.Storage.Settings;
using System.Collections.ObjectModel;
using System.IO;

namespace GmailToPst.UI.ViewModels;

public partial class ConnectViewModel : ObservableObject
{
    [ObservableProperty]
    private int _selectedAuthTabIndex = 0; // Default to Tab 0: IMAP (Password per le App)

    // Saved Profiles
    public ObservableCollection<AccountConfig> SavedProfiles { get; } = new();

    [ObservableProperty]
    private AccountConfig? _selectedProfile;

    [ObservableProperty]
    private bool _saveProfile = true;

    // IMAP Properties
    [ObservableProperty]
    private string _imapEmail = "";

    [ObservableProperty]
    private string _imapAppPassword = "";

    [ObservableProperty]
    private string _imapHost = "imap.gmail.com";

    [ObservableProperty]
    private int _imapPort = 993;

    // OAuth Properties
    [ObservableProperty]
    private string _oauthEmail = "";

    [ObservableProperty]
    private string _oauthClientId = "";

    [ObservableProperty]
    private string _oauthClientSecret = "";

    // Year / Filter Properties
    [ObservableProperty]
    private bool _filterByYear = true;

    partial void OnFilterByYearChanged(bool value)
    {
        if (value && DownloadAll)
        {
            DownloadAll = false;
        }
    }

    [ObservableProperty]
    private int _selectedYear = DateTime.Now.Year;

    [ObservableProperty]
    private bool _filterByCustomRange = false;

    [ObservableProperty]
    private DateTime _startDate = new(DateTime.Now.Year, 1, 1);

    [ObservableProperty]
    private DateTime _endDate = new(DateTime.Now.Year, 12, 31);

    [ObservableProperty]
    private bool _downloadAll = false;

    partial void OnDownloadAllChanged(bool value)
    {
        if (value && FilterByYear)
        {
            FilterByYear = false;
        }
    }

    // Archive Output
    [ObservableProperty]
    private string _archiveBasePath = SettingsManager.LoadSettings().ArchivesPath;

    [RelayCommand]
    private void BrowseArchiveFolder()
    {
        var ofd = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Seleziona cartella in cui salvare i download e il database",
            InitialDirectory = Directory.Exists(ArchiveBasePath) ? ArchiveBasePath : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };
        if (ofd.ShowDialog() == true)
        {
            ArchiveBasePath = ofd.FolderName;
        }
    }

    public ObservableCollection<int> AvailableYears { get; } = new();

    public event EventHandler<(IEmailProvider Provider, AccountConfig Config, BackupFilter Filter, string Email, string BasePath, int? Year)>? StartBackupRequested;

    public ConnectViewModel()
    {
        var currentYear = DateTime.Now.Year;
        for (int y = currentYear; y >= 2000; y--)
        {
            AvailableYears.Add(y);
        }
        SelectedYear = currentYear;

        _ = LoadProfilesAsync();
    }

    public async Task LoadProfilesAsync()
    {
        SavedProfiles.Clear();
        var profiles = await ProfileManager.LoadProfilesAsync();
        foreach (var p in profiles)
        {
            SavedProfiles.Add(p);
        }

        // Check if there is an existing Workspace profile to pre-fill Tab 2
        var wsProfile = profiles.FirstOrDefault(p => p.Type == AccountType.GoogleWorkspaceServiceAccount);
        if (wsProfile != null)
        {
            WorkspaceAdminEmail = !string.IsNullOrEmpty(wsProfile.WorkspaceAdminEmail) ? wsProfile.WorkspaceAdminEmail : wsProfile.EmailAddress;
            WorkspaceKeyFilePath = wsProfile.ServiceAccountKeyFilePath;
            
            if (wsProfile.DiscoveredUsers != null && wsProfile.DiscoveredUsers.Any())
            {
                WorkspaceUsers.Clear();
                foreach (var u in wsProfile.DiscoveredUsers)
                {
                    WorkspaceUsers.Add(u);
                }
                SelectedWorkspaceUser = WorkspaceUsers.FirstOrDefault();
                HasDiscoveredUsers = true;
                WorkspaceStatusText = $"✅ {WorkspaceUsers.Count} account salvati nel dominio {WorkspaceAdminEmail.Split('@').LastOrDefault()}";
            }
        }

        // Check if there is an existing Microsoft 365 profile to pre-fill Tab 3
        var m365Profile = profiles.FirstOrDefault(p => p.Type == AccountType.Microsoft365TenantAdmin || p.Type == AccountType.Microsoft365SingleAccount);
        if (m365Profile != null)
        {
            M365TenantId = m365Profile.M365TenantId ?? "";
            M365ClientId = m365Profile.M365ClientId ?? "";
            M365ClientSecret = m365Profile.M365ClientSecret ?? "";
            M365UserEmail = m365Profile.M365UserEmail ?? m365Profile.EmailAddress;
            M365IsAdminMode = m365Profile.Type == AccountType.Microsoft365TenantAdmin;
            M365IsSingleAccountMode = !M365IsAdminMode;
        }

        if (SavedProfiles.Any())
        {
            SelectedProfile = SavedProfiles.First();
        }
    }

    // Google Workspace Service Account Properties
    [ObservableProperty]
    private string _workspaceAdminEmail = "";

    [ObservableProperty]
    private string _workspaceKeyFilePath = "";

    [ObservableProperty]
    private bool _isDiscoveringUsers = false;

    [ObservableProperty]
    private bool _isNotDiscoveringUsers = true;

    partial void OnIsDiscoveringUsersChanged(bool value)
    {
        IsNotDiscoveringUsers = !value;
    }

    [ObservableProperty]
    private string _discoverButtonText = "🔍 Recupera Elenco Utenti del Dominio";

    [ObservableProperty]
    private bool _hasDiscoveredUsers = false;

    [ObservableProperty]
    private string _workspaceStatusText = "";

    public ObservableCollection<WorkspaceUser> WorkspaceUsers { get; } = new();

    [ObservableProperty]
    private WorkspaceUser? _selectedWorkspaceUser;

    [RelayCommand]
    private void BrowseWorkspaceKeyFile()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Seleziona il file JSON della chiave del Service Account",
            Filter = "File Chiave JSON (*.json)|*.json|Tutti i file (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (ofd.ShowDialog() == true)
        {
            WorkspaceKeyFilePath = ofd.FileName;
        }
    }

    [RelayCommand]
    private async Task DiscoverWorkspaceUsersAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceKeyFilePath) || !File.Exists(WorkspaceKeyFilePath))
        {
            System.Windows.MessageBox.Show("Seleziona un file JSON valido della chiave del Service Account scaricato da Google Cloud.", "File Chiave Mancante", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(WorkspaceAdminEmail))
        {
            System.Windows.MessageBox.Show("Inserisci l'indirizzo email dell'Amministratore di Google Workspace.", "Email Admin Mancante", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        System.Windows.MessageBox.Show("La scoperta automatica dell'intero dominio Google Workspace è una funzionalità avanzata di MailReaper PRO.\n\nNella versione Community puoi archiviare qualsiasi account tramite Gmail OAuth2 o IMAP Standard.\n\nPer maggiori dettagli visita https://mailreaper.peer2peer.cloud", "MailReaper PRO Feature", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        IsDiscoveringUsers = false;
        DiscoverButtonText = "🔍 Rileva Utenti Dominio";
        WorkspaceStatusText = "Funzionalità riservata a MailReaper PRO (https://mailreaper.peer2peer.cloud)";
        await Task.CompletedTask;
    }

    // Microsoft 365 Properties
    [ObservableProperty]
    private string _m365TenantId = "";

    [ObservableProperty]
    private string _m365ClientId = "";

    [ObservableProperty]
    private string _m365ClientSecret = "";

    [ObservableProperty]
    private string _m365UserEmail = "";

    [ObservableProperty]
    private bool _m365IsAdminMode = true;

    [ObservableProperty]
    private bool _m365IsSingleAccountMode = false;

    partial void OnM365IsAdminModeChanged(bool value)
    {
        if (value && M365IsSingleAccountMode)
        {
            M365IsSingleAccountMode = false;
        }
    }

    partial void OnM365IsSingleAccountModeChanged(bool value)
    {
        if (value && M365IsAdminMode)
        {
            M365IsAdminMode = false;
        }
    }

    [ObservableProperty]
    private bool _m365IsDiscoveringUsers = false;

    [ObservableProperty]
    private bool _m365IsNotDiscoveringUsers = true;

    partial void OnM365IsDiscoveringUsersChanged(bool value)
    {
        M365IsNotDiscoveringUsers = !value;
    }

    [ObservableProperty]
    private string _m365DiscoverButtonText = "🔍 Recupera Elenco Caselle del Dominio 365";

    [ObservableProperty]
    private bool _m365HasDiscoveredUsers = false;

    [ObservableProperty]
    private string _m365StatusText = "";

    public ObservableCollection<WorkspaceUser> M365Users { get; } = new();

    [ObservableProperty]
    private WorkspaceUser? _selectedM365User;

    [RelayCommand]
    private async Task DiscoverM365UsersAsync()
    {
        System.Windows.MessageBox.Show("La scoperta e archiviazione automatica di tutte le caselle dell'intero tenant Microsoft 365 è una funzionalità avanzata di MailReaper PRO.\n\nNella versione Community puoi archiviare qualsiasi singolo account Microsoft 365 inserendo l'indirizzo email dell'utente.\n\nPer maggiori dettagli visita https://mailreaper.peer2peer.cloud", "MailReaper PRO Feature", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        M365StatusText = "Funzionalità riservata a MailReaper PRO (https://mailreaper.peer2peer.cloud)";
        await Task.CompletedTask;
    }

    partial void OnSelectedProfileChanged(AccountConfig? value)
    {
        if (value == null) return;

        if (value.Type == AccountType.ImapAppPassword)
        {
            SelectedAuthTabIndex = 0;
            ImapEmail = value.EmailAddress;
            ImapAppPassword = value.AppPassword;
            ImapHost = string.IsNullOrEmpty(value.ImapHost) ? "imap.gmail.com" : value.ImapHost;
            ImapPort = value.ImapPort > 0 ? value.ImapPort : 993;
        }
        else if (value.Type == AccountType.GmailOAuth)
        {
            SelectedAuthTabIndex = 1;
            OauthEmail = value.EmailAddress;
            OauthClientId = value.ClientId;
            OauthClientSecret = value.ClientSecret;
        }
        else if (value.Type == AccountType.GoogleWorkspaceServiceAccount)
        {
            SelectedAuthTabIndex = 2;
            WorkspaceKeyFilePath = value.ServiceAccountKeyFilePath;
            WorkspaceAdminEmail = !string.IsNullOrEmpty(value.WorkspaceAdminEmail) ? value.WorkspaceAdminEmail : value.EmailAddress;
            
            if (value.DiscoveredUsers != null && value.DiscoveredUsers.Any())
            {
                WorkspaceUsers.Clear();
                foreach (var u in value.DiscoveredUsers)
                {
                    WorkspaceUsers.Add(u);
                }
                SelectedWorkspaceUser = WorkspaceUsers.FirstOrDefault();
                HasDiscoveredUsers = true;
                WorkspaceStatusText = $"✅ {WorkspaceUsers.Count} account salvati nel dominio";
            }
        }
        else if (value.Type == AccountType.Microsoft365TenantAdmin || value.Type == AccountType.Microsoft365SingleAccount)
        {
            SelectedAuthTabIndex = 3;
            M365TenantId = value.M365TenantId ?? "";
            M365ClientId = value.M365ClientId ?? "";
            M365ClientSecret = value.M365ClientSecret ?? "";
            M365UserEmail = value.M365UserEmail ?? value.EmailAddress;
            M365IsAdminMode = value.Type == AccountType.Microsoft365TenantAdmin;
            M365IsSingleAccountMode = !M365IsAdminMode;
        }
    }

    [ObservableProperty]
    private bool _isAnalyzing = false;

    [ObservableProperty]
    private string _analyzeButtonText = "📊 Analizza Volumi per Anno...";

    [RelayCommand]
    private async Task AnalyzeMailboxAsync()
    {
        AccountConfig config;
        IEmailProvider provider;

        if (SelectedAuthTabIndex == 0) // IMAP
        {
            if (string.IsNullOrWhiteSpace(ImapEmail) || string.IsNullOrWhiteSpace(ImapAppPassword))
            {
                System.Windows.MessageBox.Show("Inserisci l'indirizzo email e la Password per le App per analizzare la casella.", "Campi mancanti", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            config = new AccountConfig
            {
                Type = AccountType.ImapAppPassword,
                EmailAddress = ImapEmail.Trim(),
                AppPassword = ImapAppPassword.Trim(),
                ImapHost = ImapHost.Trim(),
                ImapPort = ImapPort
            };
            provider = new ImapProvider();
        }
        else if (SelectedAuthTabIndex == 1) // OAuth
        {
            if (string.IsNullOrWhiteSpace(OauthClientId) || string.IsNullOrWhiteSpace(OauthClientSecret))
            {
                System.Windows.MessageBox.Show("Inserisci Client ID e Client Secret di Google Cloud per l'analisi.", "Campi mancanti", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            config = new AccountConfig
            {
                Type = AccountType.GmailOAuth,
                EmailAddress = OauthEmail.Trim(),
                ClientId = OauthClientId.Trim(),
                ClientSecret = OauthClientSecret.Trim()
            };
            provider = new GmailApiProvider();
        }
        else if (SelectedAuthTabIndex == 2) // Google Workspace
        {
            if (string.IsNullOrWhiteSpace(WorkspaceKeyFilePath) || !File.Exists(WorkspaceKeyFilePath))
            {
                System.Windows.MessageBox.Show("Seleziona il file JSON della chiave del Service Account.", "File Chiave Mancante", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var targetUser = SelectedWorkspaceUser?.Email ?? WorkspaceAdminEmail.Trim();
            if (string.IsNullOrWhiteSpace(targetUser))
            {
                System.Windows.MessageBox.Show("Inserisci o seleziona un utente del dominio da analizzare.", "Utente Mancante", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            config = new AccountConfig
            {
                Type = AccountType.GoogleWorkspaceServiceAccount,
                EmailAddress = targetUser,
                ServiceAccountKeyFilePath = WorkspaceKeyFilePath
            };
            provider = new GmailToPst.Providers.Workspace.GoogleWorkspaceEmailProvider();
        }
        else // Tab 3: Microsoft 365
        {
            if (string.IsNullOrWhiteSpace(M365TenantId) || string.IsNullOrWhiteSpace(M365ClientId) || string.IsNullOrWhiteSpace(M365ClientSecret))
            {
                System.Windows.MessageBox.Show("Inserisci Tenant ID, Client ID e Client Secret di Microsoft Entra ID / Microsoft 365 per l'analisi.", "Credenziali Mancanti", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var targetUser = M365IsAdminMode ? (SelectedM365User?.Email ?? M365UserEmail.Trim()) : M365UserEmail.Trim();
            if (string.IsNullOrWhiteSpace(targetUser))
            {
                System.Windows.MessageBox.Show("Inserisci o seleziona la casella email Microsoft 365 da analizzare.", "Casella Mancante", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            config = new AccountConfig
            {
                Type = M365IsAdminMode ? AccountType.Microsoft365TenantAdmin : AccountType.Microsoft365SingleAccount,
                EmailAddress = targetUser,
                M365TenantId = M365TenantId.Trim(),
                M365ClientId = M365ClientId.Trim(),
                M365ClientSecret = M365ClientSecret.Trim(),
                M365UserEmail = targetUser,
                M365IsAdminMode = M365IsAdminMode
            };
            provider = new GmailToPst.Providers.Microsoft365.Microsoft365EmailProvider();
        }

        IsAnalyzing = true;
        AnalyzeButtonText = "⏳ Analisi in corso...";

        try
        {
            var summary = await GmailToPst.Providers.Helpers.MailboxAnalyzerService.AnalyzeAsync(provider, config);
            var dialog = new GmailToPst.UI.Views.MailboxAnalysisDialog(summary);
            if (System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive) is System.Windows.Window activeWin)
            {
                dialog.Owner = activeWin;
            }

            if (dialog.ShowDialog() == true && dialog.ActionTriggered)
            {
                if (dialog.SelectedYear.HasValue)
                {
                    FilterByYear = true;
                    SelectedYear = dialog.SelectedYear.Value;
                    DownloadAll = false;
                }
                else
                {
                    FilterByYear = false;
                    DownloadAll = true;
                }

                await StartBackupAsync();
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Errore durante l'analisi della casella:\n{ex.Message}", "Errore Analisi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsAnalyzing = false;
            AnalyzeButtonText = "📊 Analizza Volumi per Anno...";
        }
    }

    [RelayCommand]
    private async Task StartBackupAsync()
    {
        AccountConfig config;
        IEmailProvider provider;
        string email;

        if (SelectedAuthTabIndex == 0) // Tab 0: IMAP (Password per le App)
        {
            if (string.IsNullOrWhiteSpace(ImapEmail) || string.IsNullOrWhiteSpace(ImapAppPassword))
            {
                System.Windows.MessageBox.Show("Inserisci l'indirizzo email e la Password per le App di Google (16 caratteri).", "Campi mancanti", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            config = new AccountConfig
            {
                Type = AccountType.ImapAppPassword,
                EmailAddress = ImapEmail.Trim(),
                AppPassword = ImapAppPassword.Trim(),
                ImapHost = ImapHost.Trim(),
                ImapPort = ImapPort
            };
            provider = new ImapProvider();
            email = ImapEmail.Trim();
        }
        else if (SelectedAuthTabIndex == 1) // Tab 1: OAuth 2.0
        {
            if (string.IsNullOrWhiteSpace(OauthClientId) || string.IsNullOrWhiteSpace(OauthClientSecret))
            {
                System.Windows.MessageBox.Show("Inserisci Client ID e Client Secret di Google Cloud per l'accesso OAuth.", "Campi mancanti", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            config = new AccountConfig
            {
                Type = AccountType.GmailOAuth,
                EmailAddress = OauthEmail.Trim(),
                ClientId = OauthClientId.Trim(),
                ClientSecret = OauthClientSecret.Trim()
            };
            provider = new GmailApiProvider();
            email = string.IsNullOrWhiteSpace(OauthEmail) ? "GoogleAccount" : OauthEmail.Trim();
        }
        else if (SelectedAuthTabIndex == 2) // Tab 2: Google Workspace Service Account
        {
            if (string.IsNullOrWhiteSpace(WorkspaceKeyFilePath) || !File.Exists(WorkspaceKeyFilePath))
            {
                System.Windows.MessageBox.Show("Seleziona il file JSON della chiave del Service Account.", "File Chiave Mancante", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var targetUser = SelectedWorkspaceUser?.Email ?? WorkspaceAdminEmail.Trim();
            if (string.IsNullOrWhiteSpace(targetUser))
            {
                System.Windows.MessageBox.Show("Inserisci o seleziona un utente del dominio di cui scaricare la posta.", "Utente Mancante", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            config = new AccountConfig
            {
                Type = AccountType.GoogleWorkspaceServiceAccount,
                EmailAddress = targetUser,
                ServiceAccountKeyFilePath = WorkspaceKeyFilePath
            };
            provider = new GmailToPst.Providers.Workspace.GoogleWorkspaceEmailProvider();
            email = targetUser;
        }
        else // Tab 3: Microsoft 365 (Singolo & Dominio Admin)
        {
            if (string.IsNullOrWhiteSpace(M365TenantId) || string.IsNullOrWhiteSpace(M365ClientId) || string.IsNullOrWhiteSpace(M365ClientSecret))
            {
                System.Windows.MessageBox.Show("Inserisci Tenant ID, Client ID e Client Secret di Microsoft Entra ID / Microsoft 365.", "Credenziali Mancanti", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var targetUser = M365IsAdminMode ? (SelectedM365User?.Email ?? M365UserEmail.Trim()) : M365UserEmail.Trim();
            if (string.IsNullOrWhiteSpace(targetUser))
            {
                System.Windows.MessageBox.Show("Inserisci o seleziona la casella email Microsoft 365 di cui effettuare il backup.", "Casella Mancante", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            config = new AccountConfig
            {
                Type = M365IsAdminMode ? AccountType.Microsoft365TenantAdmin : AccountType.Microsoft365SingleAccount,
                EmailAddress = targetUser,
                M365TenantId = M365TenantId.Trim(),
                M365ClientId = M365ClientId.Trim(),
                M365ClientSecret = M365ClientSecret.Trim(),
                M365UserEmail = targetUser,
                M365IsAdminMode = M365IsAdminMode
            };
            provider = new GmailToPst.Providers.Microsoft365.Microsoft365EmailProvider();
            email = targetUser;
        }

        if (SaveProfile)
        {
            try
            {
                if (SelectedAuthTabIndex == 2)
                {
                    config.WorkspaceAdminEmail = WorkspaceAdminEmail.Trim();
                    await ProfileManager.SaveProfileAsync(config);

                    var adminProfile = new AccountConfig
                    {
                        Type = AccountType.GoogleWorkspaceServiceAccount,
                        EmailAddress = WorkspaceAdminEmail.Trim(),
                        WorkspaceAdminEmail = WorkspaceAdminEmail.Trim(),
                        ServiceAccountKeyFilePath = WorkspaceKeyFilePath,
                        DiscoveredUsers = WorkspaceUsers.ToList()
                    };
                    await ProfileManager.SaveProfileAsync(adminProfile);
                }
                else if (SelectedAuthTabIndex == 3)
                {
                    await ProfileManager.SaveProfileAsync(config);

                    if (M365IsAdminMode && M365Users.Any())
                    {
                        var adminProfile = new AccountConfig
                        {
                            Type = AccountType.Microsoft365TenantAdmin,
                            EmailAddress = $"admin@{M365TenantId.Substring(0, Math.Min(8, M365TenantId.Length))}.tenant",
                            M365TenantId = M365TenantId.Trim(),
                            M365ClientId = M365ClientId.Trim(),
                            M365ClientSecret = M365ClientSecret.Trim(),
                            M365IsAdminMode = true,
                            DiscoveredUsers = M365Users.ToList()
                        };
                        await ProfileManager.SaveProfileAsync(adminProfile);
                    }
                }
                else
                {
                    await ProfileManager.SaveProfileAsync(config);
                }
            }
            catch { }
        }

        BackupFilter filter;
        int? yearNumber = null;

        if (FilterByYear)
        {
            filter = BackupFilter.ForYear(SelectedYear);
            yearNumber = SelectedYear;
        }
        else if (FilterByCustomRange)
        {
            filter = BackupFilter.ForRange(StartDate, EndDate);
        }
        else
        {
            filter = new BackupFilter();
        }

        StartBackupRequested?.Invoke(this, (provider, config, filter, email, ArchiveBasePath, yearNumber));
    }
}
