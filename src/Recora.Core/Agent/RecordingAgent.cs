using Microsoft.Extensions.Logging;
using Recora.Core.Abstractions;
using Recora.Core.Models;
using Recora.Core.Paths;
using Recora.Core.Settings;

namespace Recora.Core.Agent;

/// <summary>
/// Tray host state machine: Idle → Detecting → Recording → Finalizing → UploadQueued.
/// </summary>
public sealed class RecordingAgent : IAsyncDisposable
{
    private readonly RecoraPaths _paths;
    private readonly SettingsStore _settings;
    private readonly ITeamsDetector _detector;
    private readonly ICatalogRepository _catalog;
    private readonly Func<ICaptureSession> _captureFactory;
    private readonly Func<EncodingSessionRequest, Task<IActiveEncodingBridge>> _encodingFactory;
    private readonly ILogger<RecordingAgent>? _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private ICaptureSession? _capture;
    private IActiveEncodingBridge? _encoding;
    private MeetingRecord? _active;

    public RecordingAgent(
        RecoraPaths paths,
        SettingsStore settings,
        ITeamsDetector detector,
        ICatalogRepository catalog,
        Func<ICaptureSession> captureFactory,
        Func<EncodingSessionRequest, Task<IActiveEncodingBridge>> encodingFactory,
        ILogger<RecordingAgent>? logger = null)
    {
        _paths = paths;
        _settings = settings;
        _detector = detector;
        _catalog = catalog;
        _captureFactory = captureFactory;
        _encodingFactory = encodingFactory;
        _logger = logger;
    }

    public TrayAgentState State { get; private set; } = TrayAgentState.Boot;
    public DetectionResult? LastDetection { get; private set; }
    public MeetingRecord? ActiveMeeting => _active;
    public event EventHandler? StateChanged;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _paths.EnsureCreated();
        await _catalog.InitializeAsync(ct).ConfigureAwait(false);
        await RecoverOrphansAsync(ct).ConfigureAwait(false);

        if (!_settings.Current.FirstRunCompleted)
        {
            State = TrayAgentState.FirstRun;
            RaiseState();
            return;
        }

        State = TrayAgentState.Idle;
        RaiseState();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
    }

    public void NotifyFirstRunCompleted()
    {
        if (State != TrayAgentState.FirstRun) return;
        State = TrayAgentState.Idle;
        RaiseState();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task ForceRecordAsync(string? title = null, CancellationToken ct = default)
    {
        if (State == TrayAgentState.Recording) return;
        var detection = LastDetection ?? new DetectionResult
        {
            IsInCall = true,
            Confidence = DetectionConfidence.High,
            WindowTitle = title ?? "Forced recording",
            RuleId = "force"
        };
        await BeginRecordingAsync(detection, title ?? detection.WindowTitle ?? "Forced recording", ct)
            .ConfigureAwait(false);
    }

    public async Task ForceStopAsync(CancellationToken ct = default)
    {
        if (State != TrayAgentState.Recording) return;
        await StopRecordingAsync(recovered: false, ct).ConfigureAwait(false);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var settings = _settings.Current;
                if (!settings.AutomationEnabled)
                {
                    if (State == TrayAgentState.Detecting)
                    {
                        State = TrayAgentState.Idle;
                        RaiseState();
                    }

                    await Task.Delay(1000, ct).ConfigureAwait(false);
                    continue;
                }

                if (State is TrayAgentState.Idle or TrayAgentState.Detecting or TrayAgentState.Uncertain)
                {
                    State = TrayAgentState.Detecting;
                    var detection = await _detector.DetectAsync(ct).ConfigureAwait(false);
                    LastDetection = detection;

                    if (detection.IsInCall && detection.Confidence >= settings.Detection.GetMinConfidence())
                    {
                        if (detection.Confidence == DetectionConfidence.Medium)
                        {
                            State = TrayAgentState.Uncertain;
                            RaiseState();
                        }

                        await BeginRecordingAsync(
                                detection,
                                detection.WindowTitle ?? "Teams meeting",
                                ct)
                            .ConfigureAwait(false);
                    }
                    else if (State == TrayAgentState.Uncertain && !detection.IsInCall)
                    {
                        State = TrayAgentState.Idle;
                        RaiseState();
                    }
                }
                else if (State == TrayAgentState.Recording)
                {
                    var detection = await _detector.DetectAsync(ct).ConfigureAwait(false);
                    LastDetection = detection;
                    if (!detection.IsInCall && detection.RuleId != "force")
                    {
                        await StopRecordingAsync(recovered: false, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await _encoding!.TickAsync(ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Agent loop error");
            }

            await Task.Delay(1500, ct).ConfigureAwait(false);
        }
    }

    private async Task BeginRecordingAsync(DetectionResult detection, string title, CancellationToken ct)
    {
        if (!EncodingSpaceGuard.HasEnough(_paths.Recordings))
        {
            _logger?.LogWarning("Insufficient disk space — skip recording");
            return;
        }

        var settings = _settings.Current;
        var meetingId = MeetingIdFactory.Create();
        var dir = _paths.MeetingDirectory(meetingId);
        Directory.CreateDirectory(dir);

        _active = new MeetingRecord
        {
            MeetingId = meetingId,
            Title = CleanTitle(title),
            StartedAt = DateTimeOffset.UtcNow,
            Status = MeetingStatus.Recording,
            DetectionConfidence = detection.Confidence,
            QualityProfile = settings.GetQualityProfile(),
            AudioMode = settings.Capture.IncludeMicrophone
                ? AudioCaptureMode.TeamsPlusMic
                : AudioCaptureMode.TeamsOnly,
            IncludeMicrophone = settings.Capture.IncludeMicrophone,
            MicrophoneDeviceId = settings.Capture.MicrophoneDeviceId,
            ChunkDurationSec = Math.Clamp(settings.ChunkDurationMinutes, 15, 30) * 60,
            LocalPath = dir
        };

        await _catalog.UpsertMeetingAsync(_active, ct).ConfigureAwait(false);

        var encodeRequest = new EncodingSessionRequest
        {
            MeetingId = meetingId,
            Title = _active.Title,
            OutputDirectory = dir,
            QualityProfile = _active.QualityProfile,
            ChunkDurationSec = _active.ChunkDurationSec,
            IncludeMicrophone = _active.IncludeMicrophone,
            MicrophoneDeviceId = _active.MicrophoneDeviceId,
            AudioMode = _active.AudioMode,
            DetectionConfidence = detection.Confidence
        };

        _encoding = await _encodingFactory(encodeRequest).ConfigureAwait(false);
        _capture = _captureFactory();
        await _capture.StartAsync(new CaptureStartRequest
        {
            MeetingId = meetingId,
            WindowHandle = detection.WindowHandle,
            IncludeMicrophone = _active.IncludeMicrophone,
            MicrophoneDeviceId = _active.MicrophoneDeviceId,
            OutputDirectory = dir
        }, ct).ConfigureAwait(false);

        State = TrayAgentState.Recording;
        RaiseState();
        _logger?.LogInformation("Recording started {MeetingId}", meetingId);
    }

    private async Task StopRecordingAsync(bool recovered, CancellationToken ct)
    {
        State = TrayAgentState.Stopping;
        RaiseState();

        if (_capture is not null)
        {
            await _capture.StopAsync(ct).ConfigureAwait(false);
            await _capture.DisposeAsync().ConfigureAwait(false);
            _capture = null;
        }

        State = TrayAgentState.Finalizing;
        RaiseState();

        MeetingSidecar? sidecar = null;
        if (_encoding is not null)
        {
            sidecar = await _encoding.FinalizeAsync(recovered, ct).ConfigureAwait(false);
            await _encoding.DisposeAsync().ConfigureAwait(false);
            _encoding = null;
        }

        if (_active is not null)
        {
            _active.EndedAt = DateTimeOffset.UtcNow;
            _active.DurationSec = sidecar?.DurationSec ?? (_active.EndedAt.Value - _active.StartedAt).TotalSeconds;
            _active.Recovered = recovered;
            _active.Status = recovered ? MeetingStatus.Recovered : MeetingStatus.Local;
            if (sidecar is not null)
            {
                _active.TotalBytes = sidecar.Chunks.Sum(c =>
                {
                    var path = Path.Combine(_active.LocalPath, c.File);
                    return File.Exists(path) ? new FileInfo(path).Length : 0;
                });
            }

            await _catalog.UpsertMeetingAsync(_active, ct).ConfigureAwait(false);

            var settings = _settings.Current;
            if (settings.Cloud.AutoUpload && settings.Cloud.OAuthCompleted)
            {
                await _catalog.EnqueueUploadAsync(_active.MeetingId, ct).ConfigureAwait(false);
                _active.Status = MeetingStatus.UploadPending;
                await _catalog.UpsertMeetingAsync(_active, ct).ConfigureAwait(false);
            }
        }

        _active = null;
        State = TrayAgentState.Idle;
        RaiseState();
    }

    private async Task RecoverOrphansAsync(CancellationToken ct)
    {
        var orphans = await _catalog.ListOrphanRecordingSessionsAsync(ct).ConfigureAwait(false);
        foreach (var orphan in orphans)
        {
            _logger?.LogWarning("Recovering orphan session {Id}", orphan.MeetingId);
            orphan.Status = MeetingStatus.Recovered;
            orphan.Recovered = true;
            orphan.EndedAt ??= DateTimeOffset.UtcNow;
            await _catalog.UpsertMeetingAsync(orphan, ct).ConfigureAwait(false);
        }
    }

    private static string CleanTitle(string title)
    {
        title = title.Trim();
        if (title.Length > 120) title = title[..120];
        return string.IsNullOrWhiteSpace(title) ? "Teams meeting" : title;
    }

    private void RaiseState() => StateChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            if (_loop is not null)
            {
                try { await _loop.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }

        if (_capture is not null) await _capture.DisposeAsync().ConfigureAwait(false);
        if (_encoding is not null) await _encoding.DisposeAsync().ConfigureAwait(false);
    }
}

public interface IActiveEncodingBridge : IAsyncDisposable
{
    Task TickAsync(CancellationToken ct = default);
    Task<MeetingSidecar> FinalizeAsync(bool recovered, CancellationToken ct = default);
}

public static class EncodingSpaceGuard
{
    public static bool HasEnough(string path, long requiredBytes = 1_000_000_000L)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return true;
            var drive = new DriveInfo(root);
            return !drive.IsReady || drive.AvailableFreeSpace >= requiredBytes;
        }
        catch
        {
            return true;
        }
    }
}
