using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Recora.Core.Abstractions;
using Recora.Core.Agent;
using Recora.Core.Models;

namespace Recora.Encoding;

/// <summary>
/// Writes deterministic pseudo-MP4 chunk payloads (stable SHA for a given meeting/index)
/// so library/catalog demos work without Media Foundation.
/// </summary>
public static class DeterministicChunkEncoder
{
    public static byte[] EncodePlaceholder(string meetingId, int chunkIndex, QualityEncodeSettings profile)
    {
        // Not a real MP4 — a stable binary envelope the pipeline can hash and size.
        var header = System.Text.Encoding.UTF8.GetBytes($"RECORA_CHUNK_v1|{meetingId}|{chunkIndex}|{profile.Width}x{profile.Height}|");
        var seed = SHA256.HashData(header);
        var payload = new byte[4096];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = seed[i % seed.Length];

        var result = new byte[header.Length + 4 + payload.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(header.Length), payload.Length);
        Buffer.BlockCopy(payload, 0, result, header.Length + 4, payload.Length);
        return result;
    }
}

/// <summary>
/// Fast-clock encoding bridge for tests: rotates chunks on virtual elapsed time.
/// </summary>
public sealed class DeterministicEncodingBridge : IActiveEncodingBridge
{
    private readonly ActiveEncoding _inner;
    private readonly EncodingSessionRequest _request;
    private readonly QualityEncodeSettings _profile;
    private readonly ICatalogRepository? _catalog;
    private double _elapsed;
    private double _chunkStart;
    private bool _finalized;

    public DeterministicEncodingBridge(
        ActiveEncoding inner,
        EncodingSessionRequest request,
        QualityEncodeSettings profile,
        ICatalogRepository? catalog = null)
    {
        _inner = inner;
        _request = request;
        _profile = profile;
        _catalog = catalog;
    }

    public double ElapsedSec => _elapsed;
    public int ChunksWritten { get; private set; }

    /// <summary>Advance virtual time (seconds) and rotate chunks as needed.</summary>
    public async Task AdvanceAsync(double deltaSec, CancellationToken ct = default)
    {
        if (_finalized) throw new InvalidOperationException("Already finalized.");
        _elapsed += deltaSec;
        while (_elapsed - _chunkStart >= _request.ChunkDurationSec)
        {
            var end = _chunkStart + _request.ChunkDurationSec;
            await WriteChunkAsync(end, ct).ConfigureAwait(false);
            _chunkStart = end;
        }
    }

    public Task TickAsync(CancellationToken ct = default) =>
        AdvanceAsync(1.0, ct);

    public async Task<MeetingSidecar> FinalizeAsync(bool recovered, CancellationToken ct = default)
    {
        if (_finalized) throw new InvalidOperationException("Already finalized.");
        if (_elapsed <= _chunkStart)
            _elapsed = _chunkStart + 0.5;

        await WriteChunkAsync(_elapsed, ct).ConfigureAwait(false);
        var sidecar = await _inner.FinalizeAsync(recovered, ct).ConfigureAwait(false);
        _finalized = true;
        return sidecar;
    }

    private async Task WriteChunkAsync(double endSec, CancellationToken ct)
    {
        var bytes = DeterministicChunkEncoder.EncodePlaceholder(_request.MeetingId, ChunksWritten, _profile);
        var chunk = await _inner.CompleteChunkAsync(bytes, endSec, ct).ConfigureAwait(false);
        ChunksWritten++;
        if (_catalog is not null)
            await _catalog.UpsertChunkAsync(chunk, ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

public static class DeterministicEncodingHost
{
    public static async Task<DeterministicEncodingBridge> StartAsync(
        EncodingCoordinator coordinator,
        EncodingSessionRequest request,
        ICatalogRepository? catalog = null,
        CancellationToken ct = default)
    {
        var active = await coordinator.StartAsync(request, ct).ConfigureAwait(false);
        var profile = coordinator.ResolveProfile(request.QualityProfile);
        return new DeterministicEncodingBridge(active, request, profile, catalog);
    }
}
