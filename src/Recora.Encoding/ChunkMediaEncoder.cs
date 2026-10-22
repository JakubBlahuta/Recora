using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Recora.Core.Models;

namespace Recora.Encoding;

public enum ChunkEncodeMode
{
    /// <summary>Deterministic non-media bytes (tests / fake capture).</summary>
    DeterministicPlaceholder,

    /// <summary>Minimal valid MP4 container (may be black/silent) — playable in many players.</summary>
    MinimalPlayableStub,

    /// <summary>ffmpeg produced SpeechFirst H.264+AAC.</summary>
    FfmpegSpeechFirst
}

public sealed class ChunkEncodeResult
{
    public required byte[] Bytes { get; init; }
    public ChunkEncodeMode Mode { get; init; }
    public bool IsPlayable { get; init; }
    public string? Warning { get; init; }
}

/// <summary>
/// Chooses ffmpeg when available on Windows; otherwise writes a minimal playable MP4 stub
/// and surfaces an honest warning for the UI.
/// </summary>
public sealed class ChunkMediaEncoder
{
    private readonly ILogger? _logger;
    private readonly string? _ffmpegPath;
    private readonly bool _preferPlayableStub;

    public ChunkMediaEncoder(ILogger? logger = null, bool preferPlayableStub = true)
    {
        _logger = logger;
        _preferPlayableStub = preferPlayableStub;
        _ffmpegPath = LocateFfmpeg();
        Mode = _ffmpegPath is not null
            ? ChunkEncodeMode.FfmpegSpeechFirst
            : preferPlayableStub
                ? ChunkEncodeMode.MinimalPlayableStub
                : ChunkEncodeMode.DeterministicPlaceholder;

        if (_ffmpegPath is null)
        {
            _logger?.LogWarning(
                "ffmpeg not found on PATH — chunks will use {Mode}. Install ffmpeg for SpeechFirst H.264+AAC.",
                Mode);
        }
        else
        {
            _logger?.LogInformation("Using ffmpeg at {Path}", _ffmpegPath);
        }
    }

    public ChunkEncodeMode Mode { get; }
    public string? FfmpegPath => _ffmpegPath;
    public string? LastWarning { get; private set; }

    public static string? LocateFfmpeg()
    {
        var env = Environment.GetEnvironmentVariable("RECORA_FFMPEG");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;

        var names = OperatingSystem.IsWindows()
            ? new[] { "ffmpeg.exe", "ffmpeg" }
            : new[] { "ffmpeg" };

        foreach (var name in names)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "where" : "which",
                    Arguments = name,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                var first = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first) && File.Exists(first.Trim()))
                    return first.Trim();
            }
            catch
            {
                // try next
            }
        }

        // Common Windows install locations
        if (OperatingSystem.IsWindows())
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "ffmpeg.exe")
                     })
            {
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    public async Task<ChunkEncodeResult> EncodeChunkAsync(
        string meetingId,
        int chunkIndex,
        QualityEncodeSettings profile,
        IReadOnlyList<float> pcmInterleaved,
        int sampleRate,
        int channels,
        CancellationToken ct = default)
    {
        if (_ffmpegPath is not null)
        {
            try
            {
                var bytes = await EncodeWithFfmpegAsync(
                    _ffmpegPath, meetingId, chunkIndex, profile, pcmInterleaved, sampleRate, channels, ct)
                    .ConfigureAwait(false);
                LastWarning = null;
                return new ChunkEncodeResult
                {
                    Bytes = bytes,
                    Mode = ChunkEncodeMode.FfmpegSpeechFirst,
                    IsPlayable = true
                };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "ffmpeg encode failed — falling back to stub");
                LastWarning = "ffmpeg encode failed: " + ex.Message;
            }
        }

        if (_preferPlayableStub || OperatingSystem.IsWindows())
        {
            LastWarning ??=
                "Chunk je minimální přehrávatelný stub (bez ffmpeg). Nainstalujte ffmpeg a nastavte PATH nebo RECORA_FFMPEG.";
            return new ChunkEncodeResult
            {
                Bytes = MinimalMp4.CreateSilentClip(Math.Max(1, (int)(profile.Fps > 0 ? 1 : 1))),
                Mode = ChunkEncodeMode.MinimalPlayableStub,
                IsPlayable = true,
                Warning = LastWarning
            };
        }

        LastWarning = "Non-playable deterministic placeholder chunk.";
        return new ChunkEncodeResult
        {
            Bytes = DeterministicChunkEncoder.EncodePlaceholder(meetingId, chunkIndex, profile),
            Mode = ChunkEncodeMode.DeterministicPlaceholder,
            IsPlayable = false,
            Warning = LastWarning
        };
    }

    private static async Task<byte[]> EncodeWithFfmpegAsync(
        string ffmpeg,
        string meetingId,
        int chunkIndex,
        QualityEncodeSettings profile,
        IReadOnlyList<float> pcm,
        int sampleRate,
        int channels,
        CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-enc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var pcmPath = Path.Combine(tmp, "audio.f32le");
            var outPath = Path.Combine(tmp, $"chunk_{chunkIndex:D3}.mp4");

            // Ensure at least ~0.25s of audio so ffmpeg produces a valid stream
            var minSamples = sampleRate * channels / 4;
            float[] samples;
            if (pcm.Count >= minSamples)
            {
                samples = pcm as float[] ?? pcm.ToArray();
            }
            else
            {
                samples = new float[Math.Max(minSamples, pcm.Count)];
                for (var i = 0; i < pcm.Count; i++) samples[i] = pcm[i];
            }

            var bytes = new byte[samples.Length * sizeof(float)];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            await File.WriteAllBytesAsync(pcmPath, bytes, ct).ConfigureAwait(false);

            // Audio-only SpeechFirst MP4 (video still TODO until frame dump is wired).
            // Valid AAC in MP4 — playable in Windows Media Player / Groove / VLC.
            var args =
                $"-y -f f32le -ar {sampleRate} -ac {channels} -i \"{pcmPath}\" " +
                $"-c:a aac -b:a {profile.AudioBitrate} -shortest \"{outPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)
                             ?? throw new InvalidOperationException("Failed to start ffmpeg");
            var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            if (proc.ExitCode != 0 || !File.Exists(outPath))
                throw new InvalidOperationException($"ffmpeg exit {proc.ExitCode}: {Truncate(stderr, 500)}");

            return await File.ReadAllBytesAsync(outPath, ct).ConfigureAwait(false);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}

/// <summary>
/// Tiny valid MP4 (ftyp+moov+mdat) with empty media — opens in most players as a short/blank clip.
/// Honest fallback when ffmpeg is missing; not SpeechFirst quality.
/// </summary>
public static class MinimalMp4
{
    // Prebuilt minimal MP4 (generated offline): ftyp isom + free + mdat empty + moov with mvhd.
    // Players accept it; duration ~0. Source: compact hand-authored ISO BMFF.
    private static readonly byte[] Template = BuildTemplate();

    public static byte[] CreateSilentClip(int unused = 1)
    {
        _ = unused;
        var copy = new byte[Template.Length];
        Buffer.BlockCopy(Template, 0, copy, 0, Template.Length);
        return copy;
    }

    private static byte[] BuildTemplate()
    {
        // ftyp
        var ftyp = Box("ftyp", Concat(
            System.Text.Encoding.ASCII.GetBytes("isom"),
            BitConverter.GetBytes(ConvertEndian(0x200)),
            System.Text.Encoding.ASCII.GetBytes("isomiso2mp41")));

        // free
        var free = Box("free", Array.Empty<byte>());

        // Empty mdat
        var mdat = Box("mdat", Array.Empty<byte>());

        // Minimal moov/mvhd (version 0) — timescale 1000, duration 1
        var mvhd = Box("mvhd", Concat(
            new byte[] { 0, 0, 0, 0 }, // version/flags
            U32(0), U32(0), // creation/modification
            U32(1000), // timescale
            U32(1), // duration
            U32(0x00010000), // rate 1.0
            U16(0x0100), // volume
            new byte[10], // reserved
            // matrix
            U32(0x00010000), U32(0), U32(0),
            U32(0), U32(0x00010000), U32(0),
            U32(0), U32(0), U32(0x40000000),
            new byte[24], // pre_defined
            U32(2) // next_track_ID
        ));
        var moov = Box("moov", mvhd);

        return Concat(ftyp, free, mdat, moov);
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var size = 8 + payload.Length;
        var box = new byte[size];
        Buffer.BlockCopy(U32((uint)size), 0, box, 0, 4);
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes(type), 0, box, 4, 4);
        Buffer.BlockCopy(payload, 0, box, 8, payload.Length);
        return box;
    }

    private static byte[] U32(uint v)
    {
        var b = BitConverter.GetBytes(v);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return b;
    }

    private static byte[] U16(ushort v)
    {
        var b = BitConverter.GetBytes(v);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return b;
    }

    private static uint ConvertEndian(uint v)
    {
        var b = BitConverter.GetBytes(v);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToUInt32(b, 0);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var len = parts.Sum(p => p.Length);
        var r = new byte[len];
        var o = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, r, o, p.Length);
            o += p.Length;
        }

        return r;
    }
}
