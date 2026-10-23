using Recora.Core.Abstractions;
using Recora.Core.Agent;
using Recora.Core.Models;

namespace Recora.Encoding;

/// <summary>
/// Adapts <see cref="ActiveEncoding"/> to the agent bridge, rotating chunks and encoding via
/// <see cref="ChunkMediaEncoder"/> (ffmpeg when available, else minimal playable stub on Windows).
/// </summary>
public sealed class ActiveEncodingBridge : IActiveEncodingBridge
{
    private readonly ActiveEncoding _inner;
    private readonly EncodingSessionRequest _request;
    private readonly ChunkMediaEncoder _encoder;
    private readonly ICatalogRepository? _catalog;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly List<float> _pcm = [];
    private int _sampleRate = 48000;
    private int _channels = 2;
    private double _lastRotateCheck;
    private readonly object _gate = new();

    public ActiveEncodingBridge(
        ActiveEncoding inner,
        EncodingSessionRequest request,
        ChunkMediaEncoder? encoder = null,
        ICatalogRepository? catalog = null)
    {
        _inner = inner;
        _request = request;
        _encoder = encoder ?? new ChunkMediaEncoder();
        _catalog = catalog;
    }

    public string? EncodeWarning => _encoder.LastWarning;

    public void OnPcm(CapturePcmEventArgs pcm)
    {
        if (pcm.Samples.Length == 0) return;
        lock (_gate)
        {
            _sampleRate = pcm.SampleRate > 0 ? pcm.SampleRate : _sampleRate;
            _channels = pcm.Channels > 0 ? pcm.Channels : _channels;
            _pcm.AddRange(pcm.Samples);
            // Cap ~2 minutes of PCM to bound memory between chunk rotates
            var max = _sampleRate * _channels * 120;
            if (_pcm.Count > max)
                _pcm.RemoveRange(0, _pcm.Count - max);
        }
    }

    public void OnFrame(CaptureFrameEventArgs frame)
    {
        // Frame pixel dump → video track is a follow-up; audio-first SpeechFirst is enough for MVP AI.
        _ = frame;
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        var elapsed = _clock.Elapsed.TotalSeconds;
        if (_inner.ShouldRotate(elapsed) && elapsed - _lastRotateCheck > 1)
        {
            _lastRotateCheck = elapsed;
            await WriteChunkAsync(elapsed, ct).ConfigureAwait(false);
        }
    }

    public async Task<MeetingSidecar> FinalizeAsync(bool recovered, CancellationToken ct = default)
    {
        var elapsed = Math.Max(_clock.Elapsed.TotalSeconds, 0.25);
        await WriteChunkAsync(elapsed, ct).ConfigureAwait(false);
        return await _inner.FinalizeAsync(recovered, ct).ConfigureAwait(false);
    }

    private async Task WriteChunkAsync(double endSec, CancellationToken ct)
    {
        float[] snapshot;
        int rate, ch;
        lock (_gate)
        {
            snapshot = _pcm.ToArray();
            _pcm.Clear();
            rate = _sampleRate;
            ch = _channels;
        }

        var encoded = await _encoder.EncodeChunkAsync(
            _request.MeetingId,
            // index is advanced inside CompleteChunkAsync — pass current via side channel
            chunkIndex: 0,
            _inner.Profile,
            snapshot,
            rate,
            ch,
            ct).ConfigureAwait(false);

        var chunk = await _inner.CompleteChunkAsync(encoded.Bytes, endSec, ct).ConfigureAwait(false);
        if (_catalog is not null)
            await _catalog.UpsertChunkAsync(chunk, ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

public static class EncodingHost
{
    public static async Task<IActiveEncodingBridge> StartBridgeAsync(
        EncodingCoordinator coordinator,
        EncodingSessionRequest request,
        ICatalogRepository? catalog = null,
        CancellationToken ct = default)
    {
        var active = await coordinator.StartAsync(request, ct).ConfigureAwait(false);
        var encoder = new ChunkMediaEncoder(preferPlayableStub: OperatingSystem.IsWindows());
        return new ActiveEncodingBridge(active, request, encoder, catalog);
    }
}
