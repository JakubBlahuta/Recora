using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Recora.Core.Abstractions;
using Recora.Core.Models;

namespace Recora.Capture;

/// <summary>
/// WASAPI device enumeration + mix helpers.
/// Full COM activation for process loopback is Windows-only; methods return false soft-fail otherwise.
/// </summary>
public sealed class WasapiMicrophoneEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<MicrophoneDevice> ListMicrophones()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return
            [
                new MicrophoneDevice { Id = "default", DisplayName = "System default", IsDefault = true },
                new MicrophoneDevice { Id = "sim-mic-1", DisplayName = "Simulated Microphone", IsDefault = false }
            ];
        }

        try
        {
            return NativeWasapi.EnumerateCaptureDevices();
        }
        catch
        {
            return [new MicrophoneDevice { Id = "default", DisplayName = "System default", IsDefault = true }];
        }
    }
}

internal static class NativeWasapi
{
    /// <summary>
    /// Placeholder for MMDeviceEnumerator COM calls. Returns default until wired with NAudio/CSCore on Windows.
    /// </summary>
    public static IReadOnlyList<MicrophoneDevice> EnumerateCaptureDevices()
    {
        // Spike note: replace with MMDeviceEnumerator.EnumAudioEndpoints(eCapture, DEVICE_STATE_ACTIVE)
        // and map IMMDevice.GetId / property store friendly name.
        return
        [
            new MicrophoneDevice { Id = "default", DisplayName = "System default", IsDefault = true }
        ];
    }
}

internal sealed class AudioMixer : IDisposable
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

        // Spike target: WASAPI process loopback targeting ms-teams.exe (Win11+).
        // Activate AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS when building on Windows.
        _teamsLoopback = true;
        _logger?.LogInformation("Teams WASAPI process loopback armed (bind on Windows runtime)");
        return true;
    }

    public bool TryStartSystemLoopback()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;
        _systemLoopback = true;
        _logger?.LogWarning("Using system loopback fallback");
        return true;
    }

    public bool TryStartMicrophone()
    {
        if (!_includeMic) return false;
        var list = _devices.ListMicrophones();
        var chosen = list.FirstOrDefault(d => d.Id == _micDeviceId) ?? list.FirstOrDefault(d => d.IsDefault);
        if (chosen is null) return false;
        _mic = true;
        _logger?.LogInformation("Microphone capture armed device={Device}", chosen.Id);
        return true;
    }

    public bool TryPullMixedPcm(out PcmAudioEventArgs pcm)
    {
        pcm = new PcmAudioEventArgs();
        if (!_teamsLoopback && !_systemLoopback && !_mic)
            return false;

        // Real PCM pull happens via IAudioCaptureClient on Windows.
        // Emit empty buffer markers so the encoder can keep A/V clock alignment.
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
        _teamsLoopback = false;
        _systemLoopback = false;
        _mic = false;
    }
}

/// <summary>
/// WinRT Graphics Capture host wrapper. On Windows, create GraphicsCaptureItem for HWND
/// via GraphicsCaptureItem.CreateFromInterop / IGraphicsCaptureItemInterop.
/// </summary>
internal sealed class GraphicsCaptureHost : IDisposable
{
    private IntPtr _hwnd;
    private readonly ILogger? _logger;
    public bool WindowLost { get; private set; }

    public GraphicsCaptureHost(IntPtr hwnd, ILogger? logger)
    {
        _hwnd = hwnd;
        _logger = logger;
    }

    public bool TryStart()
    {
        if (_hwnd == IntPtr.Zero) return false;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
        _logger?.LogInformation("Graphics Capture armed for HWND {Hwnd}", _hwnd);
        // Windows build: GraphicsCaptureItem + Direct3D11CaptureFramePool
        return true;
    }

    public bool TryPullFrame(out RawFrameEventArgs frame)
    {
        frame = new RawFrameEventArgs();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
        // Frame pool pull — when connected, map ID3D11Texture2D → NV12/Bgra8
        return false;
    }

    public void TryRebind(IntPtr hwnd)
    {
        _hwnd = hwnd;
        WindowLost = false;
        _ = TryStart();
    }

    public void Dispose() { }
}
