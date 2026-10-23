using Recora.Core.Abstractions;
using Recora.Core.Models;

namespace Recora.Capture;

/// <summary>
/// Deterministic capture session for Linux/CI demos and agent E2E tests.
/// Emits PCM + frame heartbeats on a fixed clock; no WinRT/WASAPI required.
/// </summary>
public sealed class FakeCaptureSession : ICaptureSession
{
    private readonly TimeProvider _time;
    private readonly int _sampleRate;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _frameIndex;

    public FakeCaptureSession(TimeProvider? time = null, int sampleRate = 48000)
    {
        _time = time ?? TimeProvider.System;
        _sampleRate = sampleRate;
    }

    public string MeetingId { get; private set; } = "";
    public bool IsRunning { get; private set; }
    public int FramesEmitted { get; private set; }
    public int AudioPacketsEmitted { get; private set; }

    public event EventHandler<CaptureGapEventArgs>? GapDetected;
    public event EventHandler<CapturePcmEventArgs>? PcmAvailable;
    public event EventHandler<CaptureFrameEventArgs>? FrameAvailable;
    public event EventHandler<RawFrameEventArgs>? FrameCaptured;
    public event EventHandler<PcmAudioEventArgs>? AudioCaptured;

    public bool SimulateWindowLostAfterPackets { get; set; }
    public int WindowLostAfterPackets { get; set; } = int.MaxValue;

    public Task StartAsync(CaptureStartRequest request, CancellationToken cancellationToken = default)
    {
        if (IsRunning) throw new InvalidOperationException("Already running.");
        MeetingId = request.MeetingId;
        Directory.CreateDirectory(request.OutputDirectory);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsRunning = true;
        _loop = Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var started = _time.GetTimestamp();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var elapsed = _time.GetElapsedTime(started).TotalSeconds;
                FramesEmitted++;
                _frameIndex++;
                FrameCaptured?.Invoke(this, new RawFrameEventArgs
                {
                    TimestampSec = elapsed,
                    Width = 1280,
                    Height = 720,
                    Simulated = true
                });
                FrameAvailable?.Invoke(this, new CaptureFrameEventArgs
                {
                    TimestampSec = elapsed,
                    Width = 1280,
                    Height = 720,
                    Simulated = true
                });

                // 20 ms of silence stereo float samples (deterministic length)
                var samples = new float[_sampleRate / 50 * 2];
                AudioPacketsEmitted++;
                AudioCaptured?.Invoke(this, new PcmAudioEventArgs
                {
                    TimestampSec = elapsed,
                    SampleRate = _sampleRate,
                    Channels = 2,
                    Samples = samples,
                    Simulated = true
                });
                PcmAvailable?.Invoke(this, new CapturePcmEventArgs
                {
                    TimestampSec = elapsed,
                    SampleRate = _sampleRate,
                    Channels = 2,
                    Samples = samples,
                    Simulated = true
                });

                if (SimulateWindowLostAfterPackets && AudioPacketsEmitted >= WindowLostAfterPackets)
                {
                    GapDetected?.Invoke(this, new CaptureGapEventArgs { Reason = "window_lost" });
                    SimulateWindowLostAfterPackets = false;
                }

                await Task.Delay(20, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        finally
        {
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

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

/// <summary>Fixed set of mic devices for settings UI / tests.</summary>
public sealed class FakeAudioDeviceEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<MicrophoneDevice> ListMicrophones() =>
    [
        new MicrophoneDevice { Id = "default", DisplayName = "System default", IsDefault = true },
        new MicrophoneDevice { Id = "mic-usb-1", DisplayName = "USB Headset Mic", IsDefault = false },
        new MicrophoneDevice { Id = "mic-array", DisplayName = "Laptop Array Mic", IsDefault = false }
    ];
}
