using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Recora.Core.Abstractions;
using Recora.Core.Models;

namespace Recora.Capture;

/// <summary>
/// Capture orchestration: WinRT Graphics Capture (window) + WASAPI Teams loopback + mic.
/// On non-Windows or when APIs are unavailable, runs a simulation writer for pipeline testing.
/// Real WinRT/WASAPI bindings activate on Windows 10 22H2+ / Windows 11.
/// </summary>
public sealed class CaptureEngine
{
    private readonly IAudioDeviceEnumerator _devices;
    private readonly ILogger<CaptureEngine>? _logger;

    public CaptureEngine(IAudioDeviceEnumerator? devices = null, ILogger<CaptureEngine>? logger = null)
    {
        _devices = devices ?? new WasapiMicrophoneEnumerator();
        _logger = logger;
    }

    public IAudioDeviceEnumerator Devices => _devices;

    public ICaptureSession CreateSession() => new CaptureSession(_devices, _logger);
}

public sealed class CaptureSession : ICaptureSession
{
    private readonly IAudioDeviceEnumerator _devices;
    private readonly ILogger? _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private CaptureStartRequest? _request;

    public CaptureSession(IAudioDeviceEnumerator devices, ILogger? logger = null)
    {
        _devices = devices;
        _logger = logger;
    }

    public string MeetingId { get; private set; } = "";
    public bool IsRunning { get; private set; }
    public event EventHandler<CaptureGapEventArgs>? GapDetected;
    public event EventHandler<RawFrameEventArgs>? FrameCaptured;
    public event EventHandler<PcmAudioEventArgs>? AudioCaptured;

    public Task StartAsync(CaptureStartRequest request, CancellationToken cancellationToken = default)
    {
        if (IsRunning) throw new InvalidOperationException("Capture already running.");
        _request = request;
        MeetingId = request.MeetingId;
        Directory.CreateDirectory(request.OutputDirectory);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsRunning = true;

        var useWindowsApis = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                             && request.WindowHandle != IntPtr.Zero;

        _logger?.LogInformation(
            "Starting capture meeting={MeetingId} hwnd={Hwnd} mic={Mic} device={Device} windowsApi={Api}",
            MeetingId, request.WindowHandle, request.IncludeMicrophone, request.MicrophoneDeviceId, useWindowsApis);

        _loop = Task.Run(() => CaptureLoopAsync(useWindowsApis, _cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task CaptureLoopAsync(bool useWindowsApis, CancellationToken ct)
    {
        var clock = StopwatchClock.StartNew();
        var audio = new AudioMixer(
            includeMic: _request!.IncludeMicrophone,
            microphoneDeviceId: _request.MicrophoneDeviceId,
            devices: _devices,
            logger: _logger);

        GraphicsCaptureHost? graphics = null;
        try
        {
            if (useWindowsApis)
            {
                graphics = new GraphicsCaptureHost(_request.WindowHandle, _logger);
                if (!graphics.TryStart())
                {
                    GapDetected?.Invoke(this, new CaptureGapEventArgs { Reason = "graphics_capture_unavailable" });
                    useWindowsApis = false;
                }

                if (!audio.TryStartTeamsLoopback())
                {
                    GapDetected?.Invoke(this, new CaptureGapEventArgs { Reason = "teams_loopback_fallback_system" });
                    audio.TryStartSystemLoopback();
                }

                if (_request.IncludeMicrophone && !audio.TryStartMicrophone())
                {
                    GapDetected?.Invoke(this, new CaptureGapEventArgs { Reason = "microphone_unavailable" });
                }
            }

            // Pump until stopped. Real frames/PCM are raised when WinRT/WASAPI hooks succeed;
            // otherwise emit heartbeat samples so encoding can still rotate chunks in dry-run.
            while (!ct.IsCancellationRequested)
            {
                if (useWindowsApis && graphics is not null)
                {
                    if (graphics.TryPullFrame(out var frame))
                    {
                        FrameCaptured?.Invoke(this, frame);
                    }
                    else if (graphics.WindowLost)
                    {
                        GapDetected?.Invoke(this, new CaptureGapEventArgs { Reason = "window_lost" });
                        // Caller should re-bind; keep session alive.
                        graphics.TryRebind(_request.WindowHandle);
                    }
                }

                if (audio.TryPullMixedPcm(out var pcm))
                {
                    pcm.TimestampSec = clock.Elapsed.TotalSeconds;
                    AudioCaptured?.Invoke(this, pcm);
                }
                else if (!useWindowsApis)
                {
                    // Dry-run heartbeat for non-Windows / no HWND
                    AudioCaptured?.Invoke(this, new PcmAudioEventArgs
                    {
                        TimestampSec = clock.Elapsed.TotalSeconds,
                        SampleRate = 48000,
                        Channels = 2,
                        Samples = Array.Empty<float>(),
                        Simulated = true
                    });
                }

                await Task.Delay(20, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Capture loop failed");
            GapDetected?.Invoke(this, new CaptureGapEventArgs { Reason = "capture_error" });
        }
        finally
        {
            graphics?.Dispose();
            audio.Dispose();
            IsRunning = false;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        IsRunning = false;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }
}

public sealed class RawFrameEventArgs : EventArgs
{
    public double TimestampSec { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool Simulated { get; init; }
}

public sealed class PcmAudioEventArgs : EventArgs
{
    public double TimestampSec { get; set; }
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public float[] Samples { get; init; } = [];
    public bool Simulated { get; init; }
}

internal sealed class StopwatchClock
{
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    public TimeSpan Elapsed => _sw.Elapsed;
    public static StopwatchClock StartNew() => new();
}
