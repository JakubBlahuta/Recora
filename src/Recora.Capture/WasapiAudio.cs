using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Recora.Core.Abstractions;
using Recora.Core.Models;

namespace Recora.Capture;

/// <summary>
/// WASAPI device enumeration + mix helpers.
/// Windows TFM: real MMDevice / NAudio. Other TFMs: simulated devices.
/// </summary>
public sealed class WasapiMicrophoneEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<MicrophoneDevice> ListMicrophones()
    {
#if RECORA_WINDOWS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                return NativeWasapi.EnumerateCaptureDevices();
            }
            catch
            {
                return [new MicrophoneDevice { Id = "default", DisplayName = "System default", IsDefault = true }];
            }
        }
#endif
        return
        [
            new MicrophoneDevice { Id = "default", DisplayName = "System default", IsDefault = true },
            new MicrophoneDevice { Id = "sim-mic-1", DisplayName = "Simulated Microphone", IsDefault = false }
        ];
    }
}

#if !RECORA_WINDOWS
internal static class NativeWasapi
{
    public static IReadOnlyList<MicrophoneDevice> EnumerateCaptureDevices() =>
    [
        new MicrophoneDevice { Id = "default", DisplayName = "System default", IsDefault = true }
    ];
}
#endif

internal sealed partial class AudioMixer : IDisposable
{
    private readonly bool _includeMic;
    private readonly string _micDeviceId;
    private readonly IAudioDeviceEnumerator _devices;
    private readonly ILogger? _logger;
    private bool _teamsLoopback;
    private bool _systemLoopback;
    private bool _mic;

    public AudioMixer(bool includeMic, string microphoneDeviceId, IAudioDeviceEnumerator devices, ILogger? logger)
    {
        _includeMic = includeMic;
        _micDeviceId = microphoneDeviceId;
        _devices = devices;
        _logger = logger;
    }

    public bool TryStartTeamsLoopback()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        var ok = false;
        StartTeamsLoopbackCore(ref ok);
        _teamsLoopback = ok;
        if (!ok)
            _logger?.LogWarning("Teams/system loopback not started");
        return ok;
    }

    public bool TryStartSystemLoopback()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;
        var ok = false;
        StartSystemLoopbackCore(ref ok);
        _systemLoopback = ok;
        return ok;
    }

    public bool TryStartMicrophone()
    {
        if (!_includeMic) return false;
        var list = _devices.ListMicrophones();
        var chosen = list.FirstOrDefault(d => d.Id == _micDeviceId) ?? list.FirstOrDefault(d => d.IsDefault);
        if (chosen is null) return false;

        var ok = false;
        StartMicrophoneCore(ref ok);
        _mic = ok;
        if (ok)
            _logger?.LogInformation("Microphone capture armed device={Device}", chosen.Id);
        return ok;
    }

    public bool TryPullMixedPcm(out PcmAudioEventArgs pcm)
    {
        pcm = new PcmAudioEventArgs();
        if (!_teamsLoopback && !_systemLoopback && !_mic)
            return false;

        var ok = false;
        PullMixedPcmCore(ref ok, ref pcm);
        if (ok) return true;

        // Heartbeat when armed but no samples yet (encoder keeps clock)
        pcm = new PcmAudioEventArgs
        {
            SampleRate = 48000,
            Channels = 2,
            Samples = Array.Empty<float>(),
            Simulated = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        };
        return true;
    }

    public void Dispose()
    {
        DisposeCore();
        _teamsLoopback = false;
        _systemLoopback = false;
        _mic = false;
    }

    partial void StartTeamsLoopbackCore(ref bool ok);
    partial void StartSystemLoopbackCore(ref bool ok);
    partial void StartMicrophoneCore(ref bool ok);
    partial void PullMixedPcmCore(ref bool ok, ref PcmAudioEventArgs pcm);
    partial void DisposeCore();
}

#if !RECORA_WINDOWS
internal sealed partial class AudioMixer
{
    partial void StartTeamsLoopbackCore(ref bool ok)
    {
        ok = false;
        _logger?.LogInformation("Teams loopback stub (non-Windows TFM)");
    }

    partial void StartSystemLoopbackCore(ref bool ok) => ok = false;

    partial void StartMicrophoneCore(ref bool ok) => ok = false;

    partial void PullMixedPcmCore(ref bool ok, ref PcmAudioEventArgs pcm) => ok = false;

    partial void DisposeCore() { }
}
#endif
