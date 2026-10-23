#if RECORA_WINDOWS
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Recora.Capture;

internal sealed partial class GraphicsCaptureHost
{
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private IDirect3DDevice? _device;
    private readonly object _frameGate = new();
    private RawFrameEventArgs? _latest;

    partial void TryStartCore(ref bool ok)
    {
        try
        {
            DisposeCore();

            _device = Direct3D11Helper.CreateDevice();
            if (_device is null)
            {
                _logger?.LogWarning("D3D11/WinRT device unavailable for Graphics Capture");
                ok = false;
                return;
            }

            _item = CaptureItemForWindow(_hwnd);
            if (_item is null)
            {
                _logger?.LogWarning("GraphicsCaptureItem.CreateForWindow failed for {Hwnd}", _hwnd);
                ok = false;
                return;
            }

            _item.Closed += (_, _) => MarkWindowLost();

            var size = _item.Size;
            if (size.Width < 2 || size.Height < 2)
            {
                _logger?.LogWarning("Capture item size invalid {W}x{H}", size.Width, size.Height);
                ok = false;
                return;
            }

            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                size);

            _framePool.FrameArrived += OnFrameArrived;
            _session = _framePool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = true;
            _session.StartCapture();

            _logger?.LogInformation("Graphics Capture started HWND {Hwnd} {W}x{H}", _hwnd, size.Width, size.Height);
            ok = true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Graphics Capture start failed");
            DisposeCore();
            ok = false;
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame is null) return;
        var size = frame.ContentSize;
        lock (_frameGate)
        {
            _latest = new RawFrameEventArgs
            {
                TimestampSec = 0,
                Width = size.Width,
                Height = size.Height,
                Simulated = false
            };
        }
    }

    partial void TryPullFrameCore(ref bool ok, ref RawFrameEventArgs frame)
    {
        lock (_frameGate)
        {
            if (_latest is null)
            {
                ok = false;
                return;
            }

            frame = _latest;
            _latest = null;
            ok = true;
        }
    }

    partial void TryRebindCore(IntPtr hwnd)
    {
        DisposeCore();
        _hwnd = hwnd;
    }

    partial void DisposeCore()
    {
        try
        {
            _session?.Dispose();
            if (_framePool is not null)
                _framePool.FrameArrived -= OnFrameArrived;
            _framePool?.Dispose();
        }
        catch
        {
            // ignore teardown races
        }

        _session = null;
        _framePool = null;
        _item = null;
        _device = null;
    }

    /// <summary>
    /// Create GraphicsCaptureItem for an HWND via IGraphicsCaptureItemInterop.
    /// Uses CsWinRT <see cref="ActivationFactory"/> + COM QI (not WindowsRuntimeMarshal).
    /// </summary>
    private static GraphicsCaptureItem? CaptureItemForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;

        try
        {
            var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
            var unk = factory.ThisPtr;
            var iidInterop = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"); // IGraphicsCaptureItemInterop
            var qi = Marshal.QueryInterface(unk, ref iidInterop, out var interopPtr);
            if (qi < 0 || interopPtr == IntPtr.Zero)
                return null;

            try
            {
                var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(interopPtr);
                // IID of Windows.Graphics.Capture.IGraphicsCaptureItem
                var iidItem = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30754");
                var hr = interop.CreateForWindow(hwnd, ref iidItem, out var abi);
                if (hr < 0 || abi == IntPtr.Zero)
                    return null;

                return MarshalInspectable<GraphicsCaptureItem>.FromAbi(abi);
            }
            finally
            {
                Marshal.Release(interopPtr);
            }
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>COM interop for HWND → GraphicsCaptureItem (Win10 1903+).</summary>
[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    [PreserveSig]
    int CreateForWindow(
        IntPtr window,
        ref Guid iid,
        out IntPtr result);
}

internal static class Direct3D11Helper
{
    private const int D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint D3D11_SDK_VERSION = 7;

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out IntPtr ppDevice,
        out int pFeatureLevel,
        out IntPtr ppImmediateContext);

    [DllImport("Windows.Graphics.dll", ExactSpelling = true, EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static IDirect3DDevice? CreateDevice()
    {
        var hr = D3D11CreateDevice(
            IntPtr.Zero,
            D3D_DRIVER_TYPE_HARDWARE,
            IntPtr.Zero,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            IntPtr.Zero,
            0,
            D3D11_SDK_VERSION,
            out var device,
            out _,
            out var context);

        if (hr < 0 || device == IntPtr.Zero)
            return null;

        try
        {
            var iidDxgi = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice
            var qi = Marshal.QueryInterface(device, ref iidDxgi, out var dxgi);
            if (qi < 0 || dxgi == IntPtr.Zero)
                return null;

            try
            {
                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var winrtDevice);
                if (hr < 0 || winrtDevice == IntPtr.Zero)
                    return null;
                return MarshalInspectable<IDirect3DDevice>.FromAbi(winrtDevice);
            }
            finally
            {
                Marshal.Release(dxgi);
            }
        }
        finally
        {
            if (context != IntPtr.Zero) Marshal.Release(context);
            Marshal.Release(device);
        }
    }
}
#endif
