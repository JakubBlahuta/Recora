namespace Recora.Core.Models;

public enum DetectionConfidence
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public enum TrayAgentState
{
    Boot,
    FirstRun,
    Idle,
    Detecting,
    Recording,
    Uncertain,
    Stopping,
    Finalizing
}

public enum MeetingStatus
{
    Recording,
    Finalizing,
    Local,
    UploadPending,
    Uploading,
    Synced,
    Failed,
    Recovered
}

public enum UploadJobStatus
{
    Pending,
    Uploading,
    Done,
    Failed
}

public enum QualityProfile
{
    SpeechFirst,
    Balanced,
    ArchiveHQ
}

public enum AudioCaptureMode
{
    TeamsOnly,
    TeamsPlusMic
}

public enum UiLanguage
{
    Cs,
    Sk,
    En
}
