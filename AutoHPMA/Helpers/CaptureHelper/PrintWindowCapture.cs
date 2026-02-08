using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace AutoHPMA.Helpers.CaptureHelper;

/// <summary>
/// 基于 PrintWindow API 的屏幕捕获实现
/// </summary>
/// <remarks>
/// 优点：可捕获非前台窗口，兼容性好
/// 缺点：性能较低（需要窗口重绘），部分硬件加速应用可能返回空白
/// </remarks>
public sealed class PrintWindowCapture : IScreenCapture
{
    private const uint PW_CLIENTONLY = 0x00000001;
    private const uint PW_RENDERFULLCONTENT = 0x00000002; // Win 8.1+

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern nint GetWindowDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint hWnd, nint hdcBlt, uint nFlags);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    private nint _hWnd;
    private bool _isDisposed;

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

        // 检查窗口是否最小化
        if (IsIconic(_hWnd))
            return null;

        try
        {
            // 获取客户区大小
            if (!GetClientRect(_hWnd, out var clientRect))
                return null;

            var width = clientRect.Width;
            var height = clientRect.Height;

            if (width <= 0 || height <= 0)
                return null;

            // 使用 GetDC 而不是 GetWindowDC，因为我们使用 PW_CLIENTONLY 只捕获客户区
            var hdcWindow = GetDC(_hWnd);
            if (hdcWindow == nint.Zero)
                return null;

            try
            {
                var hdcMemory = CreateCompatibleDC(hdcWindow);
                if (hdcMemory == nint.Zero)
                    return null;

                try
                {
                    var hBitmap = CreateCompatibleBitmap(hdcWindow, width, height);
                    if (hBitmap == nint.Zero)
                        return null;

                    try
                    {
                        var hOld = SelectObject(hdcMemory, hBitmap);

                        // 使用 PW_CLIENTONLY 只捕获客户区
                        // 使用 PW_RENDERFULLCONTENT 尝试捕获硬件加速内容 (Win 8.1+)
                        var flags = PW_CLIENTONLY | PW_RENDERFULLCONTENT;
                        
                        if (!PrintWindow(_hWnd, hdcMemory, flags))
                        {
                            // 降级：不使用 PW_RENDERFULLCONTENT
                            PrintWindow(_hWnd, hdcMemory, PW_CLIENTONLY);
                        }

                        SelectObject(hdcMemory, hOld);

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
                    DeleteDC(hdcMemory);
                }
            }
            finally
            {
                ReleaseDC(_hWnd, hdcWindow);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"PrintWindow capture failed: {e}");
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
