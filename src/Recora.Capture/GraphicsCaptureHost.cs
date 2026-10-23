using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Recora.Capture;

/// <summary>
/// WinRT Graphics Capture host. Windows TFM implements real frame pool; other TFMs are no-ops.
/// </summary>
internal sealed partial class GraphicsCaptureHost : IDisposable
{
    private IntPtr _hwnd;
    private readonly ILogger? _logger;
    public bool WindowLost { get; private set; }

    public GraphicsCaptureHost(IntPtr hwnd, ILogger? logger)
    {
        _hwnd = hwnd;
        _logger = logger;
    }

    public bool TryStart()
    {
        if (_hwnd == IntPtr.Zero) return false;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
        var ok = false;
        TryStartCore(ref ok);
        return ok;
    }

    public bool TryPullFrame(out RawFrameEventArgs frame)
    {
        frame = new RawFrameEventArgs();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
        var ok = false;
        TryPullFrameCore(ref ok, ref frame);
        return ok;
    }

    public void TryRebind(IntPtr hwnd)
    {
        _hwnd = hwnd;
        WindowLost = false;
        TryRebindCore(hwnd);
        _ = TryStart();
    }

    public void Dispose() => DisposeCore();

    partial void TryStartCore(ref bool ok);
    partial void TryPullFrameCore(ref bool ok, ref RawFrameEventArgs frame);
    partial void TryRebindCore(IntPtr hwnd);
    partial void DisposeCore();

    private void MarkWindowLost() => WindowLost = true;
}

#if !RECORA_WINDOWS
internal sealed partial class GraphicsCaptureHost
{
    partial void TryStartCore(ref bool ok)
    {
        _logger?.LogInformation("Graphics Capture stub HWND {Hwnd}", _hwnd);
        ok = true; // "armed" for pipeline tests; pulls return false
    }

    partial void TryPullFrameCore(ref bool ok, ref RawFrameEventArgs frame) => ok = false;

    partial void TryRebindCore(IntPtr hwnd) { }

    partial void DisposeCore() { }
}
#endif
