using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using AutoHPMA.Capture.Models;
using AutoHPMA.Capture.Native;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Security.Authorization.AppCapabilityAccess;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace AutoHPMA.Capture;

/// <summary>
/// 基于 Windows Graphics Capture (WGC) 的窗口捕获，性能最佳并支持 DirectX 加速窗口。
/// </summary>
public sealed class WindowsGraphicsCapture : IScreenCapture
{
    private const DirectXPixelFormat PixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;

    private static readonly object BorderlessAccessLock = new();
    private static AppCapabilityAccessStatus? _borderlessAccessStatus;

    private readonly object _lock = new();

    private IntPtr _hWnd;
    private bool _disposed;
    private volatile bool _stopping;

    private Device? _d3dDevice;
    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private Texture2D? _staging;

    private CapturedFrame? _latest;

    public bool IsCapturing { get; private set; }

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    public void Start(IntPtr hWnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (hWnd == IntPtr.Zero) throw new ArgumentException("窗口句柄无效。", nameof(hWnd));
        if (!IsSupported) throw new PlatformNotSupportedException("当前系统不支持 Windows Graphics Capture。");

        lock (_lock)
        {
            if (IsCapturing) return;

            _hWnd = hWnd;
            _item = GraphicsCaptureInterop.CreateForWindow(hWnd);
            (_d3dDevice, _winrtDevice) = Direct3D11Interop.CreateDevice();

            _framePool = Direct3D11CaptureFramePool.Create(_winrtDevice, PixelFormat, 2, _item.Size);
            _framePool.FrameArrived += OnFrameArrived;
            _item.Closed += OnItemClosed;

            _session = _framePool.CreateCaptureSession(_item);
            TryDisableDecorations(_session);
            _session.StartCapture();

            IsCapturing = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsCapturing) return;
            _stopping = true;

            try
            {
                if (_framePool is not null) _framePool.FrameArrived -= OnFrameArrived;
                if (_item is not null) _item.Closed -= OnItemClosed;

                _session?.Dispose();
                _framePool?.Dispose();
                _staging?.Dispose();
                _winrtDevice?.Dispose();
                _d3dDevice?.Dispose();
            }
            finally
            {
                _session = null;
                _framePool = null;
                _staging = null;
                _winrtDevice = null;
                _d3dDevice = null;
                _item = null;
                _hWnd = IntPtr.Zero;
                IsCapturing = false;
                _stopping = false;
            }
        }
    }

    public CapturedFrame? TryGetFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Volatile.Read(ref _latest);
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args) => Stop();

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (_stopping) return;

        using var frame = sender.TryGetNextFrame();
        if (frame is null) return;

        lock (_lock)
        {
            if (_stopping || _d3dDevice is null || _framePool is null) return;

            // 帧尺寸变化时重建帧池与暂存纹理。
            if (_item is not null &&
                (frame.ContentSize.Width != _item.Size.Width || frame.ContentSize.Height != _item.Size.Height))
            {
                _framePool.Recreate(_winrtDevice, PixelFormat, 2, frame.ContentSize);
                _staging?.Dispose();
                _staging = null;
            }

            using var sourceTexture = Direct3D11Interop.ToSharpDxTexture(frame.Surface);
            var width = sourceTexture.Description.Width;
            var height = sourceTexture.Description.Height;
            if (width <= 0 || height <= 0) return;

            _staging ??= CreateStagingTexture(_d3dDevice, width, height);
            var ctx = _d3dDevice.ImmediateContext;
            ctx.CopyResource(sourceTexture, _staging);

            var data = ctx.MapSubresource(_staging, 0, MapMode.Read, MapFlags.None);
            try
            {
                var stride = width * 4;
                var pixels = new byte[stride * height];
                var rowPitch = data.RowPitch;

                if (rowPitch == stride)
                {
                    Marshal.Copy(data.DataPointer, pixels, 0, pixels.Length);
                }
                else
                {
                    for (var y = 0; y < height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.DataPointer, y * rowPitch), pixels, y * stride, stride);
                    }
                }

                Volatile.Write(ref _latest, new CapturedFrame
                {
                    Width = width,
                    Height = height,
                    Stride = stride,
                    PixelsBgra8 = pixels,
                });
            }
            finally
            {
                ctx.UnmapSubresource(_staging, 0);
            }
        }
    }

    private static Texture2D CreateStagingTexture(Device device, int width, int height) =>
        new(device, new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None,
        });

    private static void TryDisableDecorations(GraphicsCaptureSession session)
    {
        // 这些属性在较新的 Windows 版本上才有；先做 API/权限检查，避免低版本或未授权时崩溃。
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
                ApiInformation.IsPropertyPresent(
                    "Windows.Graphics.Capture.GraphicsCaptureSession",
                    "IsCursorCaptureEnabled"))
            {
                session.IsCursorCaptureEnabled = false;
            }
        }
        catch { /* ignore */ }

        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) ||
                !IsBorderlessCaptureApiPresent())
            {
                return;
            }

            if (IsBorderlessAccessAllowed())
            {
                session.IsBorderRequired = false;
            }
        }
        catch { /* ignore */ }
    }

    private static bool IsBorderlessCaptureApiPresent()
    {
        return ApiInformation.IsTypePresent("Windows.Graphics.Capture.GraphicsCaptureAccess")
            && ApiInformation.IsEnumNamedValuePresent(
                "Windows.Graphics.Capture.GraphicsCaptureAccessKind",
                nameof(GraphicsCaptureAccessKind.Borderless))
            && ApiInformation.IsPropertyPresent(
                "Windows.Graphics.Capture.GraphicsCaptureSession",
                nameof(GraphicsCaptureSession.IsBorderRequired));
    }

    private static bool IsBorderlessAccessAllowed()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            return false;
        }

        lock (BorderlessAccessLock)
        {
            if (_borderlessAccessStatus.HasValue)
            {
                return _borderlessAccessStatus.Value == AppCapabilityAccessStatus.Allowed;
            }

            _borderlessAccessStatus = GraphicsCaptureAccess
                .RequestAccessAsync(GraphicsCaptureAccessKind.Borderless)
                .AsTask()
                .GetAwaiter()
                .GetResult();

            return _borderlessAccessStatus.Value == AppCapabilityAccessStatus.Allowed;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
