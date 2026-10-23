using System.Text.Json;
using Recora.Core.Abstractions;
using Recora.Core.Models;
using Recora.Core.Settings;

namespace Recora.Core.Agent;

/// <summary>
/// Crash recovery: orphan catalog rows + on-disk chunk discovery → finalized sidecar + DB.
/// </summary>
public static class SessionRecovery
{
    public static async Task<IReadOnlyList<MeetingRecord>> RecoverAllAsync(
        ICatalogRepository catalog,
        Func<string, CancellationToken, Task<MeetingSidecar?>> recoverDirectoryAsync,
        CancellationToken ct = default)
    {
        var recovered = new List<MeetingRecord>();
        var orphans = await catalog.ListOrphanRecordingSessionsAsync(ct).ConfigureAwait(false);

        foreach (var orphan in orphans)
        {
            MeetingSidecar? sidecar = null;
            if (!string.IsNullOrWhiteSpace(orphan.LocalPath) && Directory.Exists(orphan.LocalPath))
            {
                sidecar = await recoverDirectoryAsync(orphan.LocalPath, ct).ConfigureAwait(false);
                if (sidecar is not null)
                    await IndexSidecarAsync(catalog, orphan, sidecar, ct).ConfigureAwait(false);
            }

            orphan.Status = MeetingStatus.Recovered;
            orphan.Recovered = true;
            orphan.EndedAt ??= sidecar?.EndedAt ?? DateTimeOffset.UtcNow;
            orphan.DurationSec = sidecar?.DurationSec ?? orphan.DurationSec;
            if (sidecar is not null)
            {
                orphan.TotalBytes = SumChunkBytes(orphan.LocalPath, sidecar);
                orphan.Title = string.IsNullOrWhiteSpace(sidecar.Title) ? orphan.Title : sidecar.Title;
            }

            await catalog.UpsertMeetingAsync(orphan, ct).ConfigureAwait(false);
            recovered.Add(orphan);
        }

        return recovered;
    }

    public static async Task IndexSidecarAsync(
        ICatalogRepository catalog,
        MeetingRecord meeting,
        MeetingSidecar sidecar,
        CancellationToken ct = default)
    {
        foreach (var c in sidecar.Chunks)
        {
            var path = Path.Combine(meeting.LocalPath, c.File);
            long size = File.Exists(path) ? new FileInfo(path).Length : 0;
            await catalog.UpsertChunkAsync(new ChunkRecord
            {
                MeetingId = meeting.MeetingId,
                Index = c.Index,
                FileName = c.File,
                StartSec = c.StartSec,
                EndSec = c.EndSec,
                ByteSize = size,
                Sha256 = c.Sha256,
                Complete = true
            }, ct).ConfigureAwait(false);
        }
    }

    public static long SumChunkBytes(string directory, MeetingSidecar sidecar)
    {
        long total = 0;
        foreach (var c in sidecar.Chunks)
        {
            var path = Path.Combine(directory, c.File);
            if (File.Exists(path)) total += new FileInfo(path).Length;
        }

        return total;
    }

    public static async Task WriteSessionJournalAsync(string meetingDirectory, MeetingRecord meeting, CancellationToken ct = default)
    {
        Directory.CreateDirectory(meetingDirectory);
        var path = Path.Combine(meetingDirectory, "session.journal.json");
        var json = JsonSerializer.Serialize(new
        {
            meeting.MeetingId,
            meeting.Title,
            meeting.StartedAt,
            State = meeting.Status.ToString(),
            WrittenAt = DateTimeOffset.UtcNow
        }, SettingsJson.Options);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }
}
