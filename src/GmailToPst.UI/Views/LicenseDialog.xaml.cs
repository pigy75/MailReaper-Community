using System.Windows;
using System.Windows.Media;
using GmailToPst.Core.Licensing;

namespace GmailToPst.UI.Views;

public partial class LicenseDialog : Window
{
    public LicenseDialog()
    {
        InitializeComponent();
        RefreshUi();
    }

    private void RefreshUi()
    {
        var lic = LicenseManager.CurrentLicense;
        if (lic.IsProOrAbove)
        {
            TxtTierName.Text = lic.Tier == LicenseTier.Enterprise ? "MailReaper Enterprise Edition" : "MailReaper PRO Edition";
            TxtTierName.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669")); // Green
            TxtLicensedTo.Text = $"Intestata a: {lic.LicensedTo}" + (string.IsNullOrEmpty(lic.Email) ? "" : $" ({lic.Email})");
            BadgeTier.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1FAE5"));
            TxtBadgeText.Text = "ATTIVA / ILLIMITATA";
            TxtBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));
            BtnDeactivate.Visibility = Visibility.Visible;
        }
        else
        {
            TxtTierName.Text = "Community Edition (Free)";
            TxtTierName.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")); // Blue
            TxtLicensedTo.Text = "Limite esportazione: Max 5 GB per archivio PST. Funzioni Google Workspace limitate.";
            BadgeTier.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"));
            TxtBadgeText.Text = "FREE (Max 5 GB)";
            TxtBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E40AF"));
            BtnDeactivate.Visibility = Visibility.Collapsed;
        }
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        TxtError.Visibility = Visibility.Collapsed;
        var key = TxtLicenseKey.Text.Trim();

        if (string.IsNullOrWhiteSpace(key))
        {
            TxtError.Text = "Inserisci una chiave di licenza valida.";
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        if (LicenseManager.ActivateLicense(key, out var error))
        {
            MessageBox.Show("Licenza MailReaper PRO attivata con successo! Tutte le limitazioni sono state rimosse.", "Attivazione Riuscita", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtLicenseKey.Text = string.Empty;
            RefreshUi();
        }
        else
        {
            TxtError.Text = error;
            TxtError.Visibility = Visibility.Visible;
        }
    }

    private void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("Sei sicuro di voler rimuovere la licenza da questa postazione?", "Conferma Disattivazione", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            LicenseManager.DeactivateLicense();
            RefreshUi();
            MessageBox.Show("Licenza rimossa. Il programma è tornato in modalità Free.", "Disattivata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
