using System;
using System.Runtime.InteropServices;
using AutoHPMA.Capture.Models;
using AutoHPMA.Capture.Native;

namespace AutoHPMA.Capture;

/// <summary>
/// 基于 GDI 的窗口捕获基类，抽出共享的 GetDIBits 流程，子类只需提供"如何把像素绘制到 hdcMemory"。
/// </summary>
public abstract class GdiWindowCaptureBase : IScreenCapture
{
    private IntPtr _hWnd;
    private bool _disposed;

    public bool IsCapturing { get; private set; }

    public void Start(IntPtr hWnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (hWnd == IntPtr.Zero)
        {
            throw new ArgumentException("窗口句柄无效。", nameof(hWnd));
        }

        _hWnd = hWnd;
        IsCapturing = true;
    }

    public void Stop()
    {
        _hWnd = IntPtr.Zero;
        IsCapturing = false;
    }

    public CapturedFrame? TryGetFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsCapturing || _hWnd == IntPtr.Zero || NativeMethods.IsIconic(_hWnd))
        {
            return null;
        }

        if (!TryGetCaptureSize(_hWnd, out var width, out var height) || width <= 0 || height <= 0)
        {
            return null;
        }

        var hdcSrc = AcquireSourceDC(_hWnd);
        if (hdcSrc == IntPtr.Zero) return null;

        IntPtr hdcMem = IntPtr.Zero, hBitmap = IntPtr.Zero, hOld = IntPtr.Zero;
        try
        {
            hdcMem = NativeMethods.CreateCompatibleDC(hdcSrc);
            if (hdcMem == IntPtr.Zero) return null;

            hBitmap = NativeMethods.CreateCompatibleBitmap(hdcSrc, width, height);
            if (hBitmap == IntPtr.Zero) return null;

            hOld = NativeMethods.SelectObject(hdcMem, hBitmap);

            if (!RenderToMemoryDC(_hWnd, hdcSrc, hdcMem, width, height))
            {
                return null;
            }

            return ReadBgra8(hdcMem, hBitmap, width, height);
        }
        finally
        {
            if (hOld != IntPtr.Zero) NativeMethods.SelectObject(hdcMem, hOld);
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
            if (hdcMem != IntPtr.Zero) NativeMethods.DeleteDC(hdcMem);
            ReleaseSourceDC(_hWnd, hdcSrc);
        }
    }

    /// <summary>子类决定截图区域大小（窗口区域 vs 客户区）。</summary>
    protected abstract bool TryGetCaptureSize(IntPtr hWnd, out int width, out int height);

    /// <summary>子类决定使用 GetWindowDC 还是 GetDC。</summary>
    protected abstract IntPtr AcquireSourceDC(IntPtr hWnd);

    /// <summary>对应的释放函数。</summary>
    protected abstract void ReleaseSourceDC(IntPtr hWnd, IntPtr hdc);

    /// <summary>把窗口内容渲染到 <paramref name="hdcMem"/>，由子类实现 BitBlt / PrintWindow。</summary>
    protected abstract bool RenderToMemoryDC(IntPtr hWnd, IntPtr hdcSrc, IntPtr hdcMem, int width, int height);

    private static CapturedFrame? ReadBgra8(IntPtr hdcMem, IntPtr hBitmap, int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];

        var bmi = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // 负数 => top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BI_RGB,
            },
        };

        var scanLines = NativeMethods.GetDIBits(hdcMem, hBitmap, 0, (uint)height, pixels, ref bmi, NativeMethods.DIB_RGB_COLORS);
        if (scanLines == 0)
        {
            return null;
        }

        // GDI 输出 BGRA，但 Alpha 通道未必有意义；统一置为 0xFF，保证 WriteableBitmap 完全不透明显示。
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 0xFF;
        }

        return new CapturedFrame
        {
            Width = width,
            Height = height,
            Stride = stride,
            PixelsBgra8 = pixels,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
