using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Recora.Core.Abstractions;
using Recora.Core.Agent;
using Recora.Core.Localization;
using Recora.Core.Models;
using Recora.Core.Settings;

namespace Recora.App.Views;

public sealed partial class WizardPage : Page
{
    private ServiceProvider _services = null!;
    private SettingsStore _settings = null!;
    private IOneDriveAuthService _auth = null!;

    public WizardPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _services = (ServiceProvider)e.Parameter;
        _settings = _services.GetRequiredService<SettingsStore>();
        _auth = _services.GetRequiredService<IOneDriveAuthService>();
        FolderBox.Text = _settings.Current.Cloud.FolderPath;
        AutoUploadSwitch.IsOn = true;
        SelectLanguage(_settings.Current.UiLanguage);
        ApplyStrings();
    }

    private void SelectLanguage(string code)
    {
        foreach (ComboBoxItem item in LanguageBox.Items)
        {
            if ((string?)item.Tag == code)
            {
                LanguageBox.SelectedItem = item;
                break;
            }
        }
    }

    private UiLanguage CurrentLang() => _settings.Current.GetUiLanguage();

    private void ApplyStrings()
    {
        var lang = CurrentLang();
        TitleText.Text = StringCatalog.Get("Wizard.Title", lang);
        StepText.Text = lang switch
        {
            UiLanguage.Cs => "Nastavte jazyk, souhlas a OneDrive. Lokální knihovna zůstává primární.",
            UiLanguage.Sk => "Nastavte jazyk, súhlas a OneDrive. Lokálna knižnica zostáva primárna.",
            _ => "Set language, consent, and OneDrive. Local library stays primary."
        };
        LanguageLabel.Text = StringCatalog.Get("Wizard.Language", lang);
        ConsentBox.Content = StringCatalog.Get("Wizard.Consent", lang);
        OneDriveLabel.Text = StringCatalog.Get("Wizard.OneDrive", lang);
        SignInButton.Content = lang switch
        {
            UiLanguage.Cs => "Přihlásit OneDrive",
            UiLanguage.Sk => "Prihlásiť OneDrive",
            _ => "Sign in to OneDrive"
        };
        AutoUploadSwitch.Header = StringCatalog.Get("Wizard.AutoUpload", lang);
        FinishButton.Content = StringCatalog.Get("Wizard.Finish", lang);
        AuthStatus.Text = _auth.IsAuthenticated
            ? (lang == UiLanguage.En ? "Signed in (token stored)" : "Přihlášeno (token uložen)")
            : "";
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is not ComboBoxItem item || item.Tag is not string code) return;
        _settings.Update(s => s.UiLanguage = code);
        ApplyStrings();
        if (App.Services.GetService(typeof(MainWindow)) is null && Window.Current is null)
        {
            // Refresh chrome via active window if available
        }
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SignInButton.IsEnabled = false;
            var ok = await _auth.SignInAsync().ConfigureAwait(true);
            _settings.Update(s => s.Cloud.OAuthCompleted = ok);
            ApplyStrings();
        }
        catch (Exception ex)
        {
            AuthStatus.Text = ex.Message;
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConsentBox.IsChecked != true)
        {
            AuthStatus.Text = CurrentLang() switch
            {
                UiLanguage.Cs => "Nejprve potvrďte souhlas.",
                UiLanguage.Sk => "Najprv potvrďte súhlas.",
                _ => "Please accept the consent checkbox first."
            };
            return;
        }

        _settings.Update(s =>
        {
            s.ConsentAccepted = true;
            s.FirstRunCompleted = true;
            s.Cloud.FolderPath = string.IsNullOrWhiteSpace(FolderBox.Text) ? "Recordings" : FolderBox.Text.Trim();
            s.Cloud.AutoUpload = AutoUploadSwitch.IsOn;
        });

        _services.GetRequiredService<RecordingAgent>().NotifyFirstRunCompleted();
        Frame?.Navigate(typeof(LibraryPage), _services);
    }
}
