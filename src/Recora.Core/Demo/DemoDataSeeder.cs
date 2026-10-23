using System.Text.Json;
using Recora.Core.Abstractions;
using Recora.Core.Models;
using Recora.Core.Paths;
using Recora.Core.Settings;

namespace Recora.Core.Demo;

/// <summary>
/// Seeds a couple of finished fixture meetings so the library UI is not empty on first look.
/// Idempotent — skips if any meeting with Id prefix <see cref="FixtureIdPrefix"/> already exists.
/// </summary>
public static class DemoDataSeeder
{
    public const string FixtureIdPrefix = "demo-";

    public static async Task<int> SeedIfEnabledAsync(
        RecoraPaths paths,
        ICatalogRepository catalog,
        AppSettings settings,
        CancellationToken ct = default)
    {
        if (!settings.DemoDataEnabled &&
            !string.Equals(Environment.GetEnvironmentVariable("RECORA_DEMO_DATA"), "1", StringComparison.Ordinal))
            return 0;

        await catalog.InitializeAsync(ct).ConfigureAwait(false);
        var existing = await catalog.ListMeetingsAsync(FixtureIdPrefix.TrimEnd('-'), ct).ConfigureAwait(false);
        if (existing.Any(m => m.MeetingId.StartsWith(FixtureIdPrefix, StringComparison.OrdinalIgnoreCase)))
            return 0;

        paths.EnsureCreated();
        var seeded = 0;
        seeded += await SeedOneAsync(paths, catalog, "demo-q3-sync", "Q3 sync (demo)",
            DateTimeOffset.UtcNow.AddDays(-2), TimeSpan.FromMinutes(42), MeetingStatus.Local, ct).ConfigureAwait(false);
        seeded += await SeedOneAsync(paths, catalog, "demo-standup", "Týdenní standup (demo)",
            DateTimeOffset.UtcNow.AddDays(-1), TimeSpan.FromMinutes(18), MeetingStatus.Synced, ct).ConfigureAwait(false);
        return seeded;
    }

    private static async Task<int> SeedOneAsync(
        RecoraPaths paths,
        ICatalogRepository catalog,
        string meetingId,
        string title,
        DateTimeOffset started,
        TimeSpan duration,
        MeetingStatus status,
        CancellationToken ct)
    {
        var dir = paths.MeetingDirectory(meetingId);
        Directory.CreateDirectory(dir);

        var chunkName = "chunk_000.mp4";
        var chunkPath = Path.Combine(dir, chunkName);
        var payload = System.Text.Encoding.UTF8.GetBytes($"RECORA_DEMO|{meetingId}|fixture");
        await File.WriteAllBytesAsync(chunkPath, payload, ct).ConfigureAwait(false);

        var sidecar = new MeetingSidecar
        {
            MeetingId = meetingId,
            Title = title,
            StartedAt = started,
            EndedAt = started + duration,
            DurationSec = duration.TotalSeconds,
            DetectionConfidence = "high",
            QualityProfile = "SpeechFirst",
            AudioMode = "teamsPlusMic",
            IncludeMicrophone = true,
            MicrophoneDeviceId = "default",
            ChunkDurationSec = 1200,
            Chunks =
            [
                new ChunkSidecarEntry
                {
                    Index = 0,
                    File = chunkName,
                    StartSec = 0,
                    EndSec = duration.TotalSeconds,
                    Sha256 = "demo"
                }
            ],
            SchemaVersion = 1
        };

        var json = JsonSerializer.Serialize(sidecar, SettingsJson.Options);
        await File.WriteAllTextAsync(Path.Combine(dir, "meeting.json"), json, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(dir, "chunks.jsonl"),
            JsonSerializer.Serialize(sidecar.Chunks[0], SettingsJson.Options) + Environment.NewLine, ct).ConfigureAwait(false);

        var meeting = new MeetingRecord
        {
            MeetingId = meetingId,
            Title = title,
            StartedAt = started,
            EndedAt = started + duration,
            DurationSec = duration.TotalSeconds,
            TotalBytes = payload.Length,
            Status = status,
            DetectionConfidence = DetectionConfidence.High,
            QualityProfile = QualityProfile.SpeechFirst,
            AudioMode = AudioCaptureMode.TeamsPlusMic,
            IncludeMicrophone = true,
            MicrophoneDeviceId = "default",
            ChunkDurationSec = 1200,
            LocalPath = dir,
            CloudPath = status == MeetingStatus.Synced ? $"Recordings/{meetingId}" : null
        };

        await catalog.UpsertMeetingAsync(meeting, ct).ConfigureAwait(false);
        await catalog.UpsertChunkAsync(new ChunkRecord
        {
            MeetingId = meetingId,
            Index = 0,
            FileName = chunkName,
            StartSec = 0,
            EndSec = duration.TotalSeconds,
            ByteSize = payload.Length,
            Sha256 = "demo",
            Complete = true
        }, ct).ConfigureAwait(false);

        return 1;
    }
}
