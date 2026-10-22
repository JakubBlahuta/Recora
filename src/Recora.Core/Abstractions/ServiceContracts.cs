using Recora.Core.Models;

namespace Recora.Core.Abstractions;

public interface ITeamsDetector
{
    DetectionResult Detect();
    Task<DetectionResult> DetectAsync(CancellationToken cancellationToken = default);
}

public interface ICaptureSession : IAsyncDisposable
{
    string MeetingId { get; }
    bool IsRunning { get; }
    event EventHandler<CaptureGapEventArgs>? GapDetected;
    Task StartAsync(CaptureStartRequest request, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class CaptureStartRequest
{
    public required string MeetingId { get; init; }
    public IntPtr WindowHandle { get; init; }
    public bool IncludeMicrophone { get; init; } = true;
    public string MicrophoneDeviceId { get; init; } = "default";
    public string OutputDirectory { get; init; } = "";
}

public sealed class CaptureGapEventArgs : EventArgs
{
    public required string Reason { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}

public interface IAudioDeviceEnumerator
{
    IReadOnlyList<MicrophoneDevice> ListMicrophones();
}

public interface IEncodingPipeline
{
    Task<EncodingSession> BeginSessionAsync(EncodingSessionRequest request, CancellationToken ct = default);
}

public sealed class EncodingSessionRequest
{
    public required string MeetingId { get; init; }
    public required string Title { get; init; }
    public required string OutputDirectory { get; init; }
    public QualityProfile QualityProfile { get; init; } = QualityProfile.SpeechFirst;
    public int ChunkDurationSec { get; init; } = 1200;
    public bool IncludeMicrophone { get; init; } = true;
    public string MicrophoneDeviceId { get; init; } = "default";
    public AudioCaptureMode AudioMode { get; init; } = AudioCaptureMode.TeamsPlusMic;
    public DetectionConfidence DetectionConfidence { get; init; } = DetectionConfidence.Medium;
}

public interface IEncodingSession : IAsyncDisposable
{
    string MeetingId { get; }
    Task NotifyChunkWrittenAsync(ChunkRecord chunk, CancellationToken ct = default);
    Task MarkGapAsync(GapRecord gap, CancellationToken ct = default);
    Task<MeetingSidecar> FinalizeAsync(bool recovered = false, CancellationToken ct = default);
}

public sealed class EncodingSession : IEncodingSession
{
    private readonly List<ChunkRecord> _chunks = [];
    private readonly List<GapRecord> _gaps = [];
    private readonly EncodingSessionRequest _request;
    private readonly Func<MeetingSidecar, CancellationToken, Task> _persist;

    public EncodingSession(EncodingSessionRequest request, Func<MeetingSidecar, CancellationToken, Task> persist)
    {
        _request = request;
        _persist = persist;
        MeetingId = request.MeetingId;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public string MeetingId { get; }
    public DateTimeOffset StartedAt { get; }

    public Task NotifyChunkWrittenAsync(ChunkRecord chunk, CancellationToken ct = default)
    {
        _chunks.Add(chunk);
        return Task.CompletedTask;
    }

    public Task MarkGapAsync(GapRecord gap, CancellationToken ct = default)
    {
        _gaps.Add(gap);
        return Task.CompletedTask;
    }

    public async Task<MeetingSidecar> FinalizeAsync(bool recovered = false, CancellationToken ct = default)
    {
        var ended = DateTimeOffset.UtcNow;
        var sidecar = new MeetingSidecar
        {
            MeetingId = MeetingId,
            Title = _request.Title,
            StartedAt = StartedAt,
            EndedAt = ended,
            DurationSec = Math.Max(0, (ended - StartedAt).TotalSeconds),
            DetectionConfidence = _request.DetectionConfidence.ToString().ToLowerInvariant(),
            QualityProfile = _request.QualityProfile.ToString(),
            AudioMode = _request.AudioMode == AudioCaptureMode.TeamsPlusMic ? "teamsPlusMic" : "teamsOnly",
            IncludeMicrophone = _request.IncludeMicrophone,
            MicrophoneDeviceId = _request.MicrophoneDeviceId,
            ChunkDurationSec = _request.ChunkDurationSec,
            Chunks = _chunks.Select(c => new ChunkSidecarEntry
            {
                Index = c.Index,
                File = c.FileName,
                StartSec = c.StartSec,
                EndSec = c.EndSec,
                Sha256 = c.Sha256
            }).ToList(),
            Gaps = _gaps.ToList(),
            Recovered = recovered,
            SchemaVersion = 1
        };

        await _persist(sidecar, ct).ConfigureAwait(false);
        return sidecar;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public interface ICatalogRepository
{
    Task InitializeAsync(CancellationToken ct = default);
    Task UpsertMeetingAsync(MeetingRecord meeting, CancellationToken ct = default);
    Task<MeetingRecord?> GetMeetingAsync(string meetingId, CancellationToken ct = default);
    Task<IReadOnlyList<MeetingRecord>> ListMeetingsAsync(string? search = null, CancellationToken ct = default);
    Task DeleteMeetingAsync(string meetingId, CancellationToken ct = default);
    Task UpsertChunkAsync(ChunkRecord chunk, CancellationToken ct = default);
    Task<IReadOnlyList<ChunkRecord>> ListChunksAsync(string meetingId, CancellationToken ct = default);
    Task<IReadOnlyList<MeetingRecord>> ListOrphanRecordingSessionsAsync(CancellationToken ct = default);
    Task EnqueueUploadAsync(string meetingId, CancellationToken ct = default);
    Task<IReadOnlyList<UploadJob>> ListPendingUploadsAsync(CancellationToken ct = default);
    Task UpdateUploadStatusAsync(string meetingId, UploadJobStatus status, string? error = null, CancellationToken ct = default);
}

public sealed class UploadJob
{
    public required string MeetingId { get; init; }
    public UploadJobStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public interface IOneDriveAuthService
{
    bool IsAuthenticated { get; }
    Task<bool> SignInAsync(CancellationToken ct = default);
    Task SignOutAsync(CancellationToken ct = default);
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
}

public interface IOneDriveUploadService
{
    Task UploadMeetingAsync(MeetingRecord meeting, string folderPath, CancellationToken ct = default);
}

public interface ISecretStore
{
    Task SaveAsync(string key, string value, CancellationToken ct = default);
    Task<string?> LoadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
