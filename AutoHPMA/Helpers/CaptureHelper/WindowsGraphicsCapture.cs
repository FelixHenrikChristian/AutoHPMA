using System.Diagnostics;
using SharpDX.Direct3D11;
using Vanara.PInvoke;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using OpenCvSharp;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace AutoHPMA.Helpers.CaptureHelper;

/// <summary>
/// 基于 Windows Graphics Capture API 的屏幕捕获实现
/// </summary>
/// <remarks>
/// 优点：支持硬件加速窗口（DirectX游戏），性能好
/// 缺点：需要 Windows 10 1803 或更高版本
/// </remarks>
public sealed class WindowsGraphicsCapture : IScreenCapture
{
    private nint _hWnd;
    private bool _isDisposed;

    private Direct3D11CaptureFramePool? _captureFramePool;
    private GraphicsCaptureItem? _captureItem;
    private GraphicsCaptureSession? _captureSession;
    private IDirect3DDevice? _d3dDevice;

    /// <inheritdoc/>
    public bool IsCapturing { get; private set; }

    private ResourceRegion? _region;

    // 线程安全标志
    private volatile bool _isStopping;

    // HDR相关
    private bool _isHdrEnabled;
    private DirectXPixelFormat _pixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;

    // 最新帧的存储
    private Mat? _latestFrame;
    private readonly ReaderWriterLockSlim _frameAccessLock = new();

    // 用于获取帧数据的临时纹理和暂存资源
    private Texture2D? _stagingTexture;

    private long _lastFrameTime;

    /// <inheritdoc/>
    public void Start(nint hWnd)
    {
        ThrowIfDisposed();
        
        if (hWnd == nint.Zero)
            throw new ArgumentException("Invalid window handle", nameof(hWnd));

        _hWnd = hWnd;
        _region = GetGameScreenRegion(hWnd);

        _captureItem = WindowsGraphicsCaptureInterop.CreateItemForWindow(_hWnd);
        if (_captureItem == null)
            throw new InvalidOperationException("Failed to create capture item.");

        // 创建D3D设备
        _d3dDevice = Direct3D11Helper.CreateDevice();

        // 检测HDR状态
        _isHdrEnabled = false;
        _pixelFormat = _isHdrEnabled ? DirectXPixelFormat.R16G16B16A16Float : DirectXPixelFormat.B8G8R8A8UIntNormalized;

        // 创建帧池
        _captureFramePool = Direct3D11CaptureFramePool.Create(
            _d3dDevice,
            _pixelFormat,
            2,
            _captureItem.Size);

        _captureItem.Closed += CaptureItemOnClosed;
        _captureFramePool.FrameArrived += OnFrameArrived;

        _captureSession = _captureFramePool.CreateCaptureSession(_captureItem);
        
        if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
        {
            _captureSession.IsCursorCaptureEnabled = false;
        }

        if (ApiInformation.IsWriteablePropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
        {
            _captureSession.IsBorderRequired = false;
        }

        _captureSession.StartCapture();
        IsCapturing = true;
    }

    /// <summary>
    /// 从 DwmGetWindowAttribute 的矩形 截取出 GetClientRect的矩形（游戏区域）
    /// </summary>
    private ResourceRegion? GetGameScreenRegion(nint hWnd)
    {
        var exStyle = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
        if ((exStyle & (int)User32.WindowStylesEx.WS_EX_TOPMOST) != 0)
        {
            return null;
        }

        ResourceRegion region = new();
        DwmApi.DwmGetWindowAttribute<RECT>(hWnd, DwmApi.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, out var windowRect);
        User32.GetClientRect(_hWnd, out var clientRect);
        
        // 使用 ClientToScreen 获取客户区相对于屏幕的位置，计算准确的偏移
        var clientTopLeft = new POINT(0, 0);
        User32.ClientToScreen(_hWnd, ref clientTopLeft);
        
        // 计算客户区相对于窗口扩展边框的偏移
        int leftOffset = clientTopLeft.X - windowRect.Left;
        int topOffset = clientTopLeft.Y - windowRect.Top;

        region.Left = leftOffset;
        region.Top = topOffset;
        region.Right = leftOffset + clientRect.Width;
        region.Bottom = topOffset + clientRect.Height;
        region.Front = 0;
        region.Back = 1;

        return region;
    }

    private Texture2D CreateStagingTexture(Direct3D11CaptureFrame frame, Device device)
    {
        var textureDesc = new Texture2DDescription
        {
            CpuAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            Format = _isHdrEnabled ? Format.R16G16B16A16_Float : Format.B8G8R8A8_UNorm,
            Width = _region == null ? frame.ContentSize.Width : _region.Value.Right - _region.Value.Left,
            Height = _region == null ? frame.ContentSize.Height : _region.Value.Bottom - _region.Value.Top,
            OptionFlags = ResourceOptionFlags.None,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = { Count = 1, Quality = 0 },
            Usage = ResourceUsage.Staging
        };

        return new Texture2D(device, textureDesc);
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        // 线程安全检查：如果正在停止，跳过帧处理
        if (_isStopping)
            return;

        using var frame = sender.TryGetNextFrame();
        if (frame == null)
            return;

        // 限制最高处理帧率为66fps
        var now = Vanara.PInvoke.Kernel32.GetTickCount();
        if (now - _lastFrameTime < 15)
            return;
        _lastFrameTime = now;

        if (_captureItem == null)
            return;

        var frameSize = _captureItem.Size;

        // 检查帧大小是否变化
        if (frameSize.Width != frame.ContentSize.Width ||
            frameSize.Height != frame.ContentSize.Height)
        {
            frameSize = frame.ContentSize;
            _captureFramePool?.Recreate(
                _d3dDevice!,
                _pixelFormat,
                2,
                frameSize
            );
            _stagingTexture?.Dispose();
            _stagingTexture = null;
        }

        // 从捕获的帧创建一个可以被访问的纹理
        using var surfaceTexture = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
        var d3dDevice = surfaceTexture.Device;

        _stagingTexture ??= CreateStagingTexture(frame, d3dDevice);
        var stagingTexture = _stagingTexture;

        // 将捕获的纹理复制到暂存纹理
        if (_region != null)
        {
            d3dDevice.ImmediateContext.CopySubresourceRegion(surfaceTexture, 0, _region, stagingTexture, 0);
        }
        else
        {
            d3dDevice.ImmediateContext.CopyResource(surfaceTexture, stagingTexture);
        }

        // 映射纹理以便CPU读取
        var dataBox = d3dDevice.ImmediateContext.MapSubresource(
            stagingTexture,
            0,
            MapMode.Read,
            MapFlags.None);

        try
        {
            // 创建一个新的Mat
            var newFrame = Mat.FromPixelData(stagingTexture.Description.Height, stagingTexture.Description.Width,
                _isHdrEnabled ? MatType.MakeType(7, 4) : MatType.CV_8UC4, dataBox.DataPointer);

            // 如果是HDR，进行HDR到SDR的转换
            if (_isHdrEnabled)
            {
                newFrame = ConvertHdrToSdr(newFrame);
            }

            // 使用写锁更新最新帧
            _frameAccessLock.EnterWriteLock();
            try
            {
                // 释放之前的帧
                _latestFrame?.Dispose();
                // 克隆新帧以保持对其的引用（因为dataBox.DataPointer将被释放）
                _latestFrame = newFrame.Clone();
            }
            finally
            {
                newFrame.Dispose();
                _frameAccessLock.ExitWriteLock();
            }
        }
        finally
        {
            // 取消映射纹理（注意：必须是 stagingTexture，而不是 surfaceTexture）
            d3dDevice.ImmediateContext.UnmapSubresource(stagingTexture, 0);
        }
    }

    private static Mat ConvertHdrToSdr(Mat hdrMat)
    {
        Mat sdkMat = new(hdrMat.Rows, hdrMat.Cols, MatType.CV_8UC4);
        hdrMat.ConvertTo(sdkMat, MatType.CV_8UC4, 255.0);
        Cv2.CvtColor(sdkMat, sdkMat, ColorConversionCodes.RGBA2BGRA);
        return sdkMat;
    }

    /// <inheritdoc/>
    public Mat? Capture()
    {
        ThrowIfDisposed();
        
        if (!IsCapturing)
            return null;

        // 使用读锁获取最新帧
        _frameAccessLock.EnterReadLock();
        try
        {
            // 如果没有可用帧则返回null
            if (_latestFrame == null)
                return null;

            // 返回最新帧的副本
            return _latestFrame.Clone();
        }
        finally
        {
            _frameAccessLock.ExitReadLock();
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (!IsCapturing)
            return;

        // 设置停止标志，防止 OnFrameArrived 竞态条件
        _isStopping = true;

        _captureSession?.Dispose();
        _captureFramePool?.Dispose();
        _captureSession = null;
        _captureFramePool = null;
        _captureItem = null;
        _stagingTexture?.Dispose();
        _stagingTexture = null;
        _d3dDevice?.Dispose();
        _d3dDevice = null;

        _hWnd = nint.Zero;
        IsCapturing = false;
        _isStopping = false;

        // 释放最新帧
        _frameAccessLock.EnterWriteLock();
        try
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }
        finally
        {
            _frameAccessLock.ExitWriteLock();
        }
    }

    private void CaptureItemOnClosed(GraphicsCaptureItem sender, object args)
    {
        Stop();
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
        _frameAccessLock.Dispose();
        _isDisposed = true;
    }
}