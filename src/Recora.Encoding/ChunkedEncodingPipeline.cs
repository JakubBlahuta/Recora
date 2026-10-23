using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Recora.Core.Abstractions;
using Recora.Core.Models;
using Recora.Core.Settings;

namespace Recora.Encoding;

public sealed class ChunkedEncodingPipeline : IEncodingPipeline
{
    private readonly ILogger<ChunkedEncodingPipeline>? _logger;

    public ChunkedEncodingPipeline(ILogger<ChunkedEncodingPipeline>? logger = null)
    {
        _logger = logger;
    }

    public Task<EncodingSession> BeginSessionAsync(EncodingSessionRequest request, CancellationToken ct = default)
    {
        Directory.CreateDirectory(request.OutputDirectory);
        var session = new EncodingSession(request, PersistSidecarAsync);
        _logger?.LogInformation(
            "Encoding session started meeting={Id} profile={Profile} chunkMin={Min}",
            request.MeetingId, request.QualityProfile, request.ChunkDurationSec / 60);
        return Task.FromResult(session);
    }

    private async Task PersistSidecarAsync(MeetingSidecar sidecar, CancellationToken ct)
    {
        // Path is reconstructed by caller conventionally; EncodingSession persists via this callback
        // but needs the directory — store alongside meeting via sidecar.MeetingId lookup is caller-owned.
        // We only serialize when EncodingCoordinator calls WriteSidecars.
        await Task.CompletedTask.ConfigureAwait(false);
        _ = sidecar;
        _ = ct;
    }
}

/// <summary>
/// Coordinates chunk rotation, SpeechFirst encode profile, sidecar JSON, and crash finalize.
/// </summary>
public sealed class EncodingCoordinator
{
    private readonly ChunkedEncodingPipeline _pipeline;
    private readonly ILogger<EncodingCoordinator>? _logger;

    public EncodingCoordinator(ChunkedEncodingPipeline? pipeline = null, ILogger<EncodingCoordinator>? logger = null)
    {
        _pipeline = pipeline ?? new ChunkedEncodingPipeline();
        _logger = logger;
    }

    public QualityEncodeSettings ResolveProfile(QualityProfile profile) => profile switch
    {
        QualityProfile.Balanced => new QualityEncodeSettings(1920, 1080, 3_500_000, 15, 128_000),
        QualityProfile.ArchiveHQ => new QualityEncodeSettings(1920, 1080, 8_000_000, 30, 192_000),
        _ => new QualityEncodeSettings(1280, 720, 1_500_000, 8, 96_000) // SpeechFirst
    };

    public async Task<ActiveEncoding> StartAsync(EncodingSessionRequest request, CancellationToken ct = default)
    {
        Directory.CreateDirectory(request.OutputDirectory);
        var session = await _pipeline.BeginSessionAsync(request, ct).ConfigureAwait(false);
        var active = new ActiveEncoding(session, request, ResolveProfile(request.QualityProfile), _logger);
        await active.WriteMeetingStubAsync(ct).ConfigureAwait(false);
        return active;
    }

    public static async Task<MeetingSidecar?> RecoverOrphanAsync(string meetingDirectory, CancellationToken ct = default)
    {
        if (!Directory.Exists(meetingDirectory)) return null;

        var chunks = Directory.GetFiles(meetingDirectory, "chunk_*.mp4")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var meetingJson = Path.Combine(meetingDirectory, "meeting.json");
        MeetingSidecar sidecar;
        if (File.Exists(meetingJson))
        {
            var json = await File.ReadAllTextAsync(meetingJson, ct).ConfigureAwait(false);
            sidecar = JsonSerializer.Deserialize<MeetingSidecar>(json, SettingsJson.Options)
                      ?? new MeetingSidecar { MeetingId = Path.GetFileName(meetingDirectory) };
        }
        else
        {
            sidecar = new MeetingSidecar
            {
                MeetingId = Path.GetFileName(meetingDirectory),
                Title = "Recovered meeting",
                StartedAt = Directory.GetCreationTimeUtc(meetingDirectory)
            };
        }

        sidecar.Chunks = [];
        for (var i = 0; i < chunks.Count; i++)
        {
            var file = chunks[i];
            var name = Path.GetFileName(file);
            var info = new FileInfo(file);
            string? sha = null;
            try { sha = await Sha256FileAsync(file, ct).ConfigureAwait(false); }
            catch { /* ignore */ }

            sidecar.Chunks.Add(new ChunkSidecarEntry
            {
                Index = i,
                File = name,
                StartSec = i * sidecar.ChunkDurationSec,
                EndSec = (i + 1) * sidecar.ChunkDurationSec,
                Sha256 = sha
            });
        }

        sidecar.Recovered = true;
        sidecar.EndedAt ??= DateTimeOffset.UtcNow;
        sidecar.DurationSec = sidecar.Chunks.LastOrDefault()?.EndSec ?? 0;
        await WriteSidecarsAsync(meetingDirectory, sidecar, ct).ConfigureAwait(false);
        return sidecar;
    }

    public static async Task WriteSidecarsAsync(string directory, MeetingSidecar sidecar, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var meetingPath = Path.Combine(directory, "meeting.json");
        var json = JsonSerializer.Serialize(sidecar, SettingsJson.Options);
        await File.WriteAllTextAsync(meetingPath, json, ct).ConfigureAwait(false);

        var jsonl = Path.Combine(directory, "chunks.jsonl");
        await using var writer = new StreamWriter(jsonl, append: false);
        foreach (var chunk in sidecar.Chunks)
        {
            var line = JsonSerializer.Serialize(chunk, SettingsJson.Options);
            await writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
        }
    }

    public static async Task<string> Sha256FileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static long GetAvailableFreeBytes(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root)) return long.MaxValue;
        var drive = new DriveInfo(root);
        return drive.IsReady ? drive.AvailableFreeSpace : long.MaxValue;
    }

    public static bool HasEnoughFreeSpace(string path, long requiredBytes = 1_000_000_000L) =>
        GetAvailableFreeBytes(path) >= requiredBytes;
}

public readonly record struct QualityEncodeSettings(
    int Width,
    int Height,
    int VideoBitrate,
    int Fps,
    int AudioBitrate);

public sealed class ActiveEncoding : IAsyncDisposable
{
    private readonly EncodingSession _session;
    private readonly EncodingSessionRequest _request;
    private readonly QualityEncodeSettings _profile;
    private readonly ILogger? _logger;
    private int _chunkIndex;
    private double _chunkStartSec;
    private readonly List<GapRecord> _gaps = [];

    public ActiveEncoding(
        EncodingSession session,
        EncodingSessionRequest request,
        QualityEncodeSettings profile,
        ILogger? logger)
    {
        _session = session;
        _request = request;
        _profile = profile;
        _logger = logger;
    }

    public string MeetingId => _session.MeetingId;
    public QualityEncodeSettings Profile => _profile;
    public EncodingSession Session => _session;

    public async Task WriteMeetingStubAsync(CancellationToken ct = default)
    {
        var stub = new MeetingSidecar
        {
            MeetingId = _request.MeetingId,
            Title = _request.Title,
            StartedAt = DateTimeOffset.UtcNow,
            QualityProfile = _request.QualityProfile.ToString(),
            AudioMode = _request.AudioMode == AudioCaptureMode.TeamsPlusMic ? "teamsPlusMic" : "teamsOnly",
            IncludeMicrophone = _request.IncludeMicrophone,
            MicrophoneDeviceId = _request.MicrophoneDeviceId,
            ChunkDurationSec = _request.ChunkDurationSec,
            DetectionConfidence = _request.DetectionConfidence.ToString().ToLowerInvariant(),
            SchemaVersion = 1
        };
        await EncodingCoordinator.WriteSidecarsAsync(_request.OutputDirectory, stub, ct).ConfigureAwait(false);
    }

    public string NextChunkPath() =>
        Path.Combine(_request.OutputDirectory, $"chunk_{_chunkIndex:D3}.mp4");

    public bool ShouldRotate(double elapsedSec) =>
        elapsedSec - _chunkStartSec >= _request.ChunkDurationSec;

    /// <summary>
    /// Finalize current chunk file (caller supplies encoded bytes or empty placeholder).
    /// </summary>
    public async Task<ChunkRecord> CompleteChunkAsync(byte[]? encodedMp4, double endSec, CancellationToken ct = default)
    {
        var fileName = $"chunk_{_chunkIndex:D3}.mp4";
        var path = Path.Combine(_request.OutputDirectory, fileName);
        encodedMp4 ??= CreatePlaceholderMp4Hint();
        await File.WriteAllBytesAsync(path, encodedMp4, ct).ConfigureAwait(false);
        var sha = await EncodingCoordinator.Sha256FileAsync(path, ct).ConfigureAwait(false);

        var chunk = new ChunkRecord
        {
            MeetingId = MeetingId,
            Index = _chunkIndex,
            FileName = fileName,
            StartSec = _chunkStartSec,
            EndSec = endSec,
            ByteSize = encodedMp4.Length,
            Sha256 = sha,
            Complete = true
        };

        await _session.NotifyChunkWrittenAsync(chunk, ct).ConfigureAwait(false);
        _logger?.LogInformation("Chunk written {File} {Bytes}B", fileName, encodedMp4.Length);

        _chunkIndex++;
        _chunkStartSec = endSec;
        return chunk;
    }

    public Task MarkGapAsync(string reason, double startSec, double endSec, CancellationToken ct = default)
    {
        var gap = new GapRecord { StartSec = startSec, EndSec = endSec, Reason = reason };
        _gaps.Add(gap);
        return _session.MarkGapAsync(gap, ct);
    }

    public async Task<MeetingSidecar> FinalizeAsync(bool recovered = false, CancellationToken ct = default)
    {
        var sidecar = await _session.FinalizeAsync(recovered, ct).ConfigureAwait(false);
        foreach (var g in _gaps)
        {
            if (!sidecar.Gaps.Contains(g))
                sidecar.Gaps.Add(g);
        }

        await EncodingCoordinator.WriteSidecarsAsync(_request.OutputDirectory, sidecar, ct).ConfigureAwait(false);
        return sidecar;
    }

    /// <summary>
    /// Minimal non-media placeholder so pipelines and library listing work before MF/FFmpeg encode is wired.
    /// On Windows, replace with Media Foundation / FFmpeg SpeechFirst encode.
    /// </summary>
    private static byte[] CreatePlaceholderMp4Hint()
    {
        var hint = "Recora chunk placeholder — replace with MF/FFmpeg H.264+AAC encode on Windows."u8.ToArray();
        return hint;
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
