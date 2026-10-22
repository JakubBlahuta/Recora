using Recora.Core.Abstractions;
using Recora.Core.Agent;
using Recora.Core.Models;

namespace Recora.Encoding;

/// <summary>
/// Adapts <see cref="ActiveEncoding"/> to the agent bridge, rotating chunks on a monotonic clock.
/// </summary>
public sealed class ActiveEncodingBridge : IActiveEncodingBridge
{
    private readonly ActiveEncoding _inner;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastRotateCheck;

    public ActiveEncodingBridge(ActiveEncoding inner)
    {
        _inner = inner;
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        var elapsed = _clock.Elapsed.TotalSeconds;
        if (_inner.ShouldRotate(elapsed) && elapsed - _lastRotateCheck > 1)
        {
            _lastRotateCheck = elapsed;
            // On Windows, flush MF/FFmpeg encoder here. Placeholder bytes keep catalog/sidecar valid.
            await _inner.CompleteChunkAsync(null, elapsed, ct).ConfigureAwait(false);
        }
    }

    public Task<MeetingSidecar> FinalizeAsync(bool recovered, CancellationToken ct = default) =>
        FinalizeCoreAsync(recovered, ct);

    private async Task<MeetingSidecar> FinalizeCoreAsync(bool recovered, CancellationToken ct)
    {
        var elapsed = _clock.Elapsed.TotalSeconds;
        // Ensure at least one chunk exists for short recordings.
        await _inner.CompleteChunkAsync(null, Math.Max(elapsed, 0.1), ct).ConfigureAwait(false);
        return await _inner.FinalizeAsync(recovered, ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

public static class EncodingHost
{
    public static async Task<IActiveEncodingBridge> StartBridgeAsync(
        EncodingCoordinator coordinator,
        EncodingSessionRequest request,
        CancellationToken ct = default)
    {
        var active = await coordinator.StartAsync(request, ct).ConfigureAwait(false);
        return new ActiveEncodingBridge(active);
    }
}
