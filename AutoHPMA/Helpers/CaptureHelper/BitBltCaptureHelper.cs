using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using static Windows.Win32.PInvoke;

namespace AutoHPMA.Helpers.CaptureHelper;

/// <summary>
/// 基于 BitBlt GDI API 的屏幕捕获实现
/// </summary>
/// <remarks>
/// 优点：兼容性好，CPU占用低
/// 缺点：无法捕获硬件加速窗口（如DirectX游戏）
/// </remarks>
public sealed class BitBltCapture : IScreenCapture
{
    private nint _hWnd;
    private bool _isDisposed;

    // P/Invoke for checking window minimized state
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    /// <inheritdoc/>
    public bool IsCapturing { get; private set; }

    /// <inheritdoc/>
    public void Start(nint hWnd)
    {
        ThrowIfDisposed();
        
        if (hWnd == nint.Zero)
            throw new ArgumentException("Invalid window handle", nameof(hWnd));
            
        _hWnd = hWnd;
        IsCapturing = true;
    }

    /// <inheritdoc/>
    public void Stop()
    {
        _hWnd = nint.Zero;
        IsCapturing = false;
    }

    /// <inheritdoc/>
    public Mat? Capture()
    {
        ThrowIfDisposed();
        
        if (!IsCapturing || _hWnd == nint.Zero)
            return null;

        HWND hwnd = new(_hWnd);

        // 检查窗口是否最小化
        if (IsIconic(_hWnd))
            return null;
        
        try
        {
            GetWindowRect(hwnd, out var windowRect);
            var width = windowRect.Width;
            var height = windowRect.Height;

            if (width <= 0 || height <= 0)
                return null;

            var hdcSrc = GetWindowDC(hwnd);
            if (hdcSrc.IsNull)
                return null;

            try
            {
                var hdcDest = CreateCompatibleDC(hdcSrc);
                if (hdcDest.IsNull)
                    return null;

                try
                {
                    var hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
                    if (hBitmap.IsNull)
                        return null;

                    try
                    {
                        var hOld = SelectObject(hdcDest, hBitmap);
                        BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, ROP_CODE.SRCCOPY);
                        SelectObject(hdcDest, hOld);

                        using var bitmap = Image.FromHbitmap(hBitmap);
                        return bitmap.ToMat();
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
                finally
                {
                    DeleteDC(hdcDest);
                }
            }
            finally
            {
                ReleaseDC(hwnd, hdcSrc);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"BitBlt capture failed: {e}");
            return null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_isDisposed)
            return;
            
        Stop();
        _isDisposed = true;
    }
}
