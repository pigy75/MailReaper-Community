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

    // IMAP / POP3 Properties
    [ObservableProperty]
    private bool _isImapProtocol = true;

    partial void OnIsImapProtocolChanged(bool value)
    {
        if (value)
        {
            _isPop3Protocol = false;
            OnPropertyChanged(nameof(IsPop3Protocol));
            if (ImapPort == 995) ImapPort = 993;
            if (ImapHost.StartsWith("pop.") || ImapHost.StartsWith("pop3."))
            {
                ImapHost = ImapHost.Replace("pop3.", "imap.").Replace("pop.", "imap.");
            }
        }
    }

    [ObservableProperty]
    private bool _isPop3Protocol = false;

    partial void OnIsPop3ProtocolChanged(bool value)
    {
        if (value)
        {
            _isImapProtocol = false;
            OnPropertyChanged(nameof(IsImapProtocol));
            if (ImapPort == 993) ImapPort = 995;
            if (ImapHost.StartsWith("imap.") || ImapHost.StartsWith("imaps."))
            {
                ImapHost = ImapHost.Replace("imaps.", "pop.").Replace("imap.", "pop.");
            }
        }
    }

    [ObservableProperty]
    private string _imapEmail = "";

    partial void OnImapEmailChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var atIndex = value.IndexOf('@');
        if (atIndex < 0 || atIndex >= value.Length - 1) return;
        var domain = value.Substring(atIndex + 1).ToLowerInvariant().Trim();

        if (domain == "gmail.com" || domain == "googlemail.com")
        {
            ImapHost = IsPop3Protocol ? "pop.gmail.com" : "imap.gmail.com";
            ImapPort = IsPop3Protocol ? 995 : 993;
        }
        else if (domain == "aruba.it" || domain.EndsWith(".aruba.it"))
        {
            ImapHost = IsPop3Protocol ? "pop3.aruba.it" : "imaps.aruba.it";
            ImapPort = IsPop3Protocol ? 995 : 993;
        }
        else if (domain == "libero.it")
        {
            ImapHost = IsPop3Protocol ? "popmail.libero.it" : "imapmail.libero.it";
            ImapPort = IsPop3Protocol ? 995 : 993;
        }
        else if (domain == "virgilio.it")
        {
            ImapHost = "in.virgilio.it";
            ImapPort = IsPop3Protocol ? 995 : 993;
        }
        else if (domain == "tim.it" || domain == "tin.it" || domain == "alice.it")
        {
            ImapHost = IsPop3Protocol ? "box.tin.it" : "imap.tim.it";
            ImapPort = IsPop3Protocol ? 995 : 993;
        }
        else if (domain == "outlook.com" || domain == "hotmail.com" || domain == "live.com")
        {
            ImapHost = "outlook.office365.com";
            ImapPort = IsPop3Protocol ? 995 : 993;
        }
        else if (domain == "yahoo.com" || domain == "yahoo.it")
        {
            ImapHost = IsPop3Protocol ? "pop.mail.yahoo.com" : "imap.mail.yahoo.com";
            ImapPort = IsPop3Protocol ? 995 : 993;
        }
    }

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

            if (m365Profile.DiscoveredUsers != null && m365Profile.DiscoveredUsers.Any())
            {
                M365Users.Clear();
                foreach (var u in m365Profile.DiscoveredUsers)
                {
                    M365Users.Add(u);
                }
                SelectedM365User = M365Users.FirstOrDefault();
                M365HasDiscoveredUsers = true;
                M365StatusText = $"✅ {M365Users.Count} caselle salvate per il tenant Microsoft 365";
            }
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

        IsDiscoveringUsers = true;
        DiscoverButtonText = "⏳ Recupero utenti in corso...";
        WorkspaceStatusText = "Connessione alla Directory API di Google Workspace in corso...";
        WorkspaceUsers.Clear();
        HasDiscoveredUsers = false;

        try
        {
            var users = await GmailToPst.Providers.Workspace.GoogleWorkspaceAdminService.GetDomainUsersAsync(WorkspaceKeyFilePath, WorkspaceAdminEmail.Trim());
            foreach (var u in users)
            {
                WorkspaceUsers.Add(u);
            }

            if (WorkspaceUsers.Any())
            {
                SelectedWorkspaceUser = WorkspaceUsers.First();
                HasDiscoveredUsers = true;
                WorkspaceStatusText = $"✅ Trovati con successo {WorkspaceUsers.Count} account nel dominio!";

                // Automatically save Admin profile with discovered users list so it stays persistent!
                var adminConfig = new AccountConfig
                {
                    Type = AccountType.GoogleWorkspaceServiceAccount,
                    EmailAddress = WorkspaceAdminEmail.Trim(),
                    WorkspaceAdminEmail = WorkspaceAdminEmail.Trim(),
                    ServiceAccountKeyFilePath = WorkspaceKeyFilePath,
                    DiscoveredUsers = WorkspaceUsers.ToList()
                };
                await ProfileManager.SaveProfileAsync(adminConfig);
                await LoadProfilesAsync();
            }
            else
            {
                WorkspaceStatusText = "⚠️ Nessun utente trovato nel dominio specificato.";
            }
        }
        catch (Exception ex)
        {
            WorkspaceStatusText = $"❌ Errore: {ex.Message}";
            System.Windows.MessageBox.Show($"Impossibile recuperare gli utenti del dominio:\n{ex.Message}\n\nAssicurati di aver abilitato 'Admin SDK API' su Google Cloud e autorizzato il Client ID su admin.google.com nella Delega di Dominio.", "Errore Directory Workspace", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsDiscoveringUsers = false;
            DiscoverButtonText = "🔄 Aggiorna Elenco Utenti del Dominio";
        }
    }

    // Microsoft 365 Properties (Single & Tenant Admin)
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
        if (string.IsNullOrWhiteSpace(M365TenantId))
        {
            System.Windows.MessageBox.Show("Inserisci il Tenant ID (Directory ID) di Microsoft Entra ID / Microsoft 365.", "Tenant ID Mancante", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(M365ClientId))
        {
            System.Windows.MessageBox.Show("Inserisci il Client ID (Application ID) dell'applicazione registrata su Azure / Entra ID.", "Client ID Mancante", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(M365ClientSecret))
        {
            System.Windows.MessageBox.Show("Inserisci il Client Secret generato per l'applicazione su Azure / Entra ID.", "Client Secret Mancante", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        M365IsDiscoveringUsers = true;
        M365DiscoverButtonText = "⏳ Connessione a Microsoft Graph...";
        M365StatusText = "Interrogazione utenti del tenant Microsoft 365 in corso...";
        M365Users.Clear();
        M365HasDiscoveredUsers = false;

        try
        {
            var users = await GmailToPst.Providers.Microsoft365.Microsoft365AdminService.GetTenantUsersAsync(M365TenantId.Trim(), M365ClientId.Trim(), M365ClientSecret.Trim());
            foreach (var u in users)
            {
                M365Users.Add(u);
            }

            if (M365Users.Any())
            {
                SelectedM365User = M365Users.First();
                M365HasDiscoveredUsers = true;
                M365StatusText = $"✅ Trovate con successo {M365Users.Count} caselle nel tenant Microsoft 365!";

                var adminConfig = new AccountConfig
                {
                    Type = AccountType.Microsoft365TenantAdmin,
                    EmailAddress = SelectedM365User.Email,
                    M365TenantId = M365TenantId.Trim(),
                    M365ClientId = M365ClientId.Trim(),
                    M365ClientSecret = M365ClientSecret.Trim(),
                    M365IsAdminMode = true,
                    DiscoveredUsers = M365Users.ToList()
                };
                await ProfileManager.SaveProfileAsync(adminConfig);
                await LoadProfilesAsync();
            }
            else
            {
                M365StatusText = "⚠️ Nessuna casella postale trovata nel tenant specificato.";
            }
        }
        catch (Exception ex)
        {
            M365StatusText = $"❌ Errore: {ex.Message}";
            System.Windows.MessageBox.Show($"Impossibile connettersi al tenant Microsoft 365:\n\n{ex.Message}\n\nVerifica che l'applicazione su Azure Portal abbia i permessi 'Mail.Read' e 'User.Read.All' (Application) e che sia stato concesso il consenso amministratore.", "Errore Connessione Microsoft 365", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            M365IsDiscoveringUsers = false;
            M365DiscoverButtonText = "🔄 Aggiorna Elenco Caselle Dominio 365";
        }
    }

    partial void OnSelectedProfileChanged(AccountConfig? value)
    {
        if (value == null) return;

        if (value.Type == AccountType.ImapAppPassword)
        {
            SelectedAuthTabIndex = 0;
            IsImapProtocol = true;
            IsPop3Protocol = false;
            ImapEmail = value.EmailAddress;
            ImapAppPassword = value.AppPassword;
            ImapHost = string.IsNullOrEmpty(value.ImapHost) ? "imap.gmail.com" : value.ImapHost;
            ImapPort = value.ImapPort > 0 ? value.ImapPort : 993;
        }
        else if (value.Type == AccountType.Pop3)
        {
            SelectedAuthTabIndex = 0;
            IsPop3Protocol = true;
            IsImapProtocol = false;
            ImapEmail = value.EmailAddress;
            ImapAppPassword = value.AppPassword;
            ImapHost = !string.IsNullOrEmpty(value.Pop3Host) ? value.Pop3Host : (string.IsNullOrEmpty(value.ImapHost) ? "pop.gmail.com" : value.ImapHost);
            ImapPort = value.Pop3Port > 0 ? value.Pop3Port : 995;
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

            if (value.DiscoveredUsers != null && value.DiscoveredUsers.Any())
            {
                M365Users.Clear();
                foreach (var u in value.DiscoveredUsers)
                {
                    M365Users.Add(u);
                }
                SelectedM365User = M365Users.FirstOrDefault();
                M365HasDiscoveredUsers = true;
                M365StatusText = $"✅ {M365Users.Count} caselle salvate nel tenant";
            }
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

        if (SelectedAuthTabIndex == 0) // IMAP / POP3
        {
            if (string.IsNullOrWhiteSpace(ImapEmail) || string.IsNullOrWhiteSpace(ImapAppPassword))
            {
                System.Windows.MessageBox.Show("Inserisci l'indirizzo email e la password (o Password per le App) per analizzare la casella.", "Campi mancanti", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (IsPop3Protocol)
            {
                config = new AccountConfig
                {
                    Type = AccountType.Pop3,
                    EmailAddress = ImapEmail.Trim(),
                    AppPassword = ImapAppPassword.Trim(),
                    Pop3Host = ImapHost.Trim(),
                    Pop3Port = ImapPort,
                    ImapHost = ImapHost.Trim(),
                    ImapPort = ImapPort
                };
                provider = new GmailToPst.Providers.Pop3.Pop3Provider();
            }
            else
            {
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

        if (SelectedAuthTabIndex == 0) // Tab 0: IMAP / POP3 Standard
        {
            if (string.IsNullOrWhiteSpace(ImapEmail) || string.IsNullOrWhiteSpace(ImapAppPassword))
            {
                System.Windows.MessageBox.Show("Inserisci l'indirizzo email e la password (o Password per le App).", "Campi mancanti", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (IsPop3Protocol)
            {
                config = new AccountConfig
                {
                    Type = AccountType.Pop3,
                    EmailAddress = ImapEmail.Trim(),
                    AppPassword = ImapAppPassword.Trim(),
                    Pop3Host = ImapHost.Trim(),
                    Pop3Port = ImapPort,
                    ImapHost = ImapHost.Trim(),
                    ImapPort = ImapPort
                };
                provider = new GmailToPst.Providers.Pop3.Pop3Provider();
            }
            else
            {
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
