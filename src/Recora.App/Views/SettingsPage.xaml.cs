using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Recora.Core.Abstractions;
using Recora.Core.Localization;
using Recora.Core.Models;
using Recora.Core.Settings;

namespace Recora.App.Views;

public sealed partial class SettingsPage : Page
{
    private ServiceProvider _services = null!;
    private SettingsStore _settings = null!;
    private IAudioDeviceEnumerator _devices = null!;
    private IOneDriveAuthService _auth = null!;
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _services = (ServiceProvider)e.Parameter;
        _settings = _services.GetRequiredService<SettingsStore>();
        _devices = _services.GetRequiredService<IAudioDeviceEnumerator>();
        _auth = _services.GetRequiredService<IOneDriveAuthService>();
        Load();
    }

    private void Load()
    {
        _loading = true;
        var s = _settings.Current;
        ApplyStrings();
        foreach (ComboBoxItem item in LanguageBox.Items)
        {
            if ((string?)item.Tag == s.UiLanguage)
                LanguageBox.SelectedItem = item;
        }

        AutomationSwitch.IsOn = s.AutomationEnabled;
        ChunkMinutes.Value = s.ChunkDurationMinutes;
        MicSwitch.IsOn = s.Capture.IncludeMicrophone;
        DoubleVoiceSwitch.IsOn = s.MitigateDoubleVoice;
        FolderBox.Text = s.Cloud.FolderPath;
        AutoUploadSwitch.IsOn = s.Cloud.AutoUpload;

        MicDeviceBox.Items.Clear();
        foreach (var d in _devices.ListMicrophones())
        {
            var item = new ComboBoxItem { Content = d.DisplayName, Tag = d.Id };
            MicDeviceBox.Items.Add(item);
            if (d.Id == s.Capture.MicrophoneDeviceId || (s.Capture.MicrophoneDeviceId == "default" && d.IsDefault))
                MicDeviceBox.SelectedItem = item;
        }

        StatusText.Text = _auth.IsAuthenticated ? "OneDrive OK" : "OneDrive — not signed in";
        _loading = false;
    }

    private void ApplyStrings()
    {
        var lang = _settings.Current.GetUiLanguage();
        TitleText.Text = StringCatalog.Get("Settings.Title", lang);
        AutomationSwitch.Header = lang switch
        {
            UiLanguage.Cs => "Automatické nahrávání",
            UiLanguage.Sk => "Automatické nahrávanie",
            _ => "Automatic recording"
        };
        ChunkMinutes.Header = lang switch
        {
            UiLanguage.Cs => "Délka chunku (min)",
            UiLanguage.Sk => "Dĺžka chunku (min)",
            _ => "Chunk length (min)"
        };
        MicSwitch.Header = StringCatalog.Get("Settings.Mic", lang);
        MicDeviceBox.Header = StringCatalog.Get("Settings.MicDevice", lang);
        DoubleVoiceSwitch.Header = lang switch
        {
            UiLanguage.Cs => "Zmírnit double-voice (ducking)",
            UiLanguage.Sk => "Zmierniť double-voice (ducking)",
            _ => "Mitigate double-voice (ducking)"
        };
        FolderBox.Header = StringCatalog.Get("Settings.Folder", lang);
        AutoUploadSwitch.Header = StringCatalog.Get("Settings.AutoUpload", lang);
        SignInButton.Content = lang switch
        {
            UiLanguage.Cs => "Přihlásit / obnovit OneDrive",
            UiLanguage.Sk => "Prihlásiť / obnoviť OneDrive",
            _ => "Sign in / refresh OneDrive"
        };
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not ComboBoxItem item || item.Tag is not string code) return;
        _settings.Update(s => s.UiLanguage = code);
        ApplyStrings();
    }

    private void AutomationSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Update(s => s.AutomationEnabled = AutomationSwitch.IsOn);
    }

    private void ChunkMinutes_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue)) return;
        _settings.Update(s => s.ChunkDurationMinutes = (int)Math.Clamp(args.NewValue, 15, 30));
    }

    private void MicSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Update(s =>
        {
            s.Capture.IncludeMicrophone = MicSwitch.IsOn;
            s.Capture.Audio = MicSwitch.IsOn ? "teamsPlusMic" : "teamsOnly";
        });
    }

    private void MicDeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MicDeviceBox.SelectedItem is not ComboBoxItem item || item.Tag is not string id) return;
        _settings.Update(s => s.Capture.MicrophoneDeviceId = id);
    }

    private void DoubleVoiceSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Update(s => s.MitigateDoubleVoice = DoubleVoiceSwitch.IsOn);
    }

    private void FolderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Update(s => s.Cloud.FolderPath = string.IsNullOrWhiteSpace(FolderBox.Text) ? "Recordings" : FolderBox.Text.Trim());
    }

    private void AutoUploadSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Update(s => s.Cloud.AutoUpload = AutoUploadSwitch.IsOn);
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ok = await _auth.SignInAsync().ConfigureAwait(true);
            _settings.Update(s => s.Cloud.OAuthCompleted = ok);
            StatusText.Text = ok ? "OneDrive OK" : "Sign-in failed";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
