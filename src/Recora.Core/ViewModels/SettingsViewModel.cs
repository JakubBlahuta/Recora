using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Recora.Core.Abstractions;
using Recora.Core.Models;
using Recora.Core.Settings;

namespace Recora.Core.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _settings;
    private readonly IAudioDeviceEnumerator _devices;
    private readonly IOneDriveAuthService _auth;

    public SettingsViewModel(SettingsStore settings, IAudioDeviceEnumerator devices, IOneDriveAuthService auth)
    {
        _settings = settings;
        _devices = devices;
        _auth = auth;
        ReloadFromStore();
    }

    public ObservableCollection<MicrophoneDevice> Microphones { get; } = [];

    public string UiLanguage
    {
        get => _settings.Current.UiLanguage;
        set
        {
            _settings.Update(s => s.UiLanguage = value);
            OnPropertyChanged();
        }
    }

    public bool AutomationEnabled
    {
        get => _settings.Current.AutomationEnabled;
        set
        {
            _settings.Update(s => s.AutomationEnabled = value);
            OnPropertyChanged();
        }
    }

    public int ChunkDurationMinutes
    {
        get => _settings.Current.ChunkDurationMinutes;
        set
        {
            _settings.Update(s => s.ChunkDurationMinutes = Math.Clamp(value, 15, 30));
            OnPropertyChanged();
        }
    }

    public bool IncludeMicrophone
    {
        get => _settings.Current.Capture.IncludeMicrophone;
        set
        {
            _settings.Update(s =>
            {
                s.Capture.IncludeMicrophone = value;
                s.Capture.Audio = value ? "teamsPlusMic" : "teamsOnly";
            });
            OnPropertyChanged();
        }
    }

    public string MicrophoneDeviceId
    {
        get => _settings.Current.Capture.MicrophoneDeviceId;
        set
        {
            _settings.Update(s => s.Capture.MicrophoneDeviceId = value);
            OnPropertyChanged();
        }
    }

    public bool MitigateDoubleVoice
    {
        get => _settings.Current.MitigateDoubleVoice;
        set
        {
            _settings.Update(s => s.MitigateDoubleVoice = value);
            OnPropertyChanged();
        }
    }

    public string FolderPath
    {
        get => _settings.Current.Cloud.FolderPath;
        set
        {
            _settings.Update(s => s.Cloud.FolderPath = string.IsNullOrWhiteSpace(value) ? "Recordings" : value.Trim());
            OnPropertyChanged();
        }
    }

    public bool AutoUpload
    {
        get => _settings.Current.Cloud.AutoUpload;
        set
        {
            _settings.Update(s => s.Cloud.AutoUpload = value);
            OnPropertyChanged();
        }
    }

    public bool UseMsal
    {
        get => _settings.Current.Cloud.UseMsal;
        set
        {
            _settings.Update(s => s.Cloud.UseMsal = value);
            OnPropertyChanged();
        }
    }

    public bool DemoDataEnabled
    {
        get => _settings.Current.DemoDataEnabled;
        set
        {
            _settings.Update(s => s.DemoDataEnabled = value);
            OnPropertyChanged();
        }
    }

    public string AuthStatus { get; private set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ReloadFromStore()
    {
        Microphones.Clear();
        foreach (var d in _devices.ListMicrophones())
            Microphones.Add(d);
        AuthStatus = _auth.IsAuthenticated ? "OneDrive OK" : "OneDrive — not signed in";
        OnPropertyChanged(nameof(AuthStatus));
        OnPropertyChanged(nameof(UiLanguage));
        OnPropertyChanged(nameof(AutomationEnabled));
        OnPropertyChanged(nameof(ChunkDurationMinutes));
        OnPropertyChanged(nameof(IncludeMicrophone));
        OnPropertyChanged(nameof(MicrophoneDeviceId));
        OnPropertyChanged(nameof(MitigateDoubleVoice));
        OnPropertyChanged(nameof(FolderPath));
        OnPropertyChanged(nameof(AutoUpload));
        OnPropertyChanged(nameof(UseMsal));
        OnPropertyChanged(nameof(DemoDataEnabled));
    }

    public async Task SignInAsync(CancellationToken ct = default)
    {
        var ok = await _auth.SignInAsync(ct).ConfigureAwait(true);
        _settings.Update(s => s.Cloud.OAuthCompleted = ok);
        AuthStatus = ok ? "OneDrive OK" : "Sign-in failed";
        OnPropertyChanged(nameof(AuthStatus));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
