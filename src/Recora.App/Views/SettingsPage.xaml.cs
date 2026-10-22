using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Recora.Core.Abstractions;
using Recora.Core.Localization;
using Recora.Core.Models;
using Recora.Core.Settings;
using Recora.Core.ViewModels;

namespace Recora.App.Views;

public sealed partial class SettingsPage : Page
{
    private SettingsViewModel _vm = null!;
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var services = (ServiceProvider)e.Parameter;
        _vm = new SettingsViewModel(
            services.GetRequiredService<SettingsStore>(),
            services.GetRequiredService<IAudioDeviceEnumerator>(),
            services.GetRequiredService<IOneDriveAuthService>());
        Load();
    }

    private void Load()
    {
        _loading = true;
        ApplyStrings();
        foreach (ComboBoxItem item in LanguageBox.Items)
        {
            if ((string?)item.Tag == _vm.UiLanguage)
                LanguageBox.SelectedItem = item;
        }

        AutomationSwitch.IsOn = _vm.AutomationEnabled;
        ChunkMinutes.Value = _vm.ChunkDurationMinutes;
        MicSwitch.IsOn = _vm.IncludeMicrophone;
        DoubleVoiceSwitch.IsOn = _vm.MitigateDoubleVoice;
        FolderBox.Text = _vm.FolderPath;
        AutoUploadSwitch.IsOn = _vm.AutoUpload;

        MicDeviceBox.Items.Clear();
        foreach (var d in _vm.Microphones)
        {
            var item = new ComboBoxItem { Content = d.DisplayName, Tag = d.Id };
            MicDeviceBox.Items.Add(item);
            if (d.Id == _vm.MicrophoneDeviceId || (_vm.MicrophoneDeviceId == "default" && d.IsDefault))
                MicDeviceBox.SelectedItem = item;
        }

        StatusText.Text = _vm.AuthStatus;
        _loading = false;
    }

    private void ApplyStrings()
    {
        var lang = Enum.TryParse<UiLanguage>(
            _vm.UiLanguage is "sk" ? "Sk" : _vm.UiLanguage is "en" ? "En" : "Cs",
            out var parsed)
            ? parsed
            : UiLanguage.Cs;
        // Prefer store language
        lang = _vm.UiLanguage.ToLowerInvariant() switch
        {
            "sk" => UiLanguage.Sk,
            "en" => UiLanguage.En,
            _ => UiLanguage.Cs
        };

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
        _vm.UiLanguage = code;
        ApplyStrings();
    }

    private void AutomationSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.AutomationEnabled = AutomationSwitch.IsOn;
    }

    private void ChunkMinutes_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue)) return;
        _vm.ChunkDurationMinutes = (int)args.NewValue;
    }

    private void MicSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.IncludeMicrophone = MicSwitch.IsOn;
    }

    private void MicDeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MicDeviceBox.SelectedItem is not ComboBoxItem item || item.Tag is not string id) return;
        _vm.MicrophoneDeviceId = id;
    }

    private void DoubleVoiceSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.MitigateDoubleVoice = DoubleVoiceSwitch.IsOn;
    }

    private void FolderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.FolderPath = FolderBox.Text;
    }

    private void AutoUploadSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.AutoUpload = AutoUploadSwitch.IsOn;
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.SignInAsync().ConfigureAwait(true);
            StatusText.Text = _vm.AuthStatus;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
