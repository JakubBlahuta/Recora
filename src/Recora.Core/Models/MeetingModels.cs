namespace Recora.Core.Models;

public sealed class MeetingRecord
{
    public required string MeetingId { get; init; }
    public string Title { get; set; } = "Untitled meeting";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public double DurationSec { get; set; }
    public long TotalBytes { get; set; }
    public MeetingStatus Status { get; set; } = MeetingStatus.Local;
    public DetectionConfidence DetectionConfidence { get; set; } = DetectionConfidence.Medium;
    public QualityProfile QualityProfile { get; set; } = QualityProfile.SpeechFirst;
    public AudioCaptureMode AudioMode { get; set; } = AudioCaptureMode.TeamsPlusMic;
    public bool IncludeMicrophone { get; set; } = true;
    public string MicrophoneDeviceId { get; set; } = "default";
    public int ChunkDurationSec { get; set; } = 1200;
    public bool Recovered { get; set; }
    public string LocalPath { get; set; } = "";
    public string? CloudPath { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class ChunkRecord
{
    public required string MeetingId { get; init; }
    public int Index { get; init; }
    public required string FileName { get; init; }
    public double StartSec { get; set; }
    public double EndSec { get; set; }
    public long ByteSize { get; set; }
    public string? Sha256 { get; set; }
    public bool Complete { get; set; }
}

public sealed class GapRecord
{
    public double StartSec { get; init; }
    public double EndSec { get; init; }
    public string Reason { get; init; } = "unknown";
}

public sealed class MeetingSidecar
{
    public string MeetingId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public double DurationSec { get; set; }
    public List<string> Participants { get; set; } = [];
    public string DetectionConfidence { get; set; } = "medium";
    public string TeamsClient { get; set; } = "new";
    public string QualityProfile { get; set; } = "SpeechFirst";
    public string AudioMode { get; set; } = "teamsPlusMic";
    public bool IncludeMicrophone { get; set; } = true;
    public string MicrophoneDeviceId { get; set; } = "default";
    public int ChunkDurationSec { get; set; } = 1200;
    public List<ChunkSidecarEntry> Chunks { get; set; } = [];
    public List<GapRecord> Gaps { get; set; } = [];
    public bool Recovered { get; set; }
    public int SchemaVersion { get; set; } = 1;
}

public sealed class ChunkSidecarEntry
{
    public int Index { get; set; }
    public string File { get; set; } = "";
    public double StartSec { get; set; }
    public double EndSec { get; set; }
    public string? Sha256 { get; set; }
}

public sealed class DetectionResult
{
    public bool IsInCall { get; init; }
    public DetectionConfidence Confidence { get; init; }
    public string? WindowTitle { get; init; }
    public IntPtr WindowHandle { get; init; }
    public int? ProcessId { get; init; }
    public string? ProcessName { get; init; }
    public string? RuleId { get; init; }
    public string TeamsClient { get; init; } = "new";
}

public sealed class MicrophoneDevice
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public bool IsDefault { get; init; }
}
