using System;
using AutoHPMA.Capture.Models;

namespace AutoHPMA.Capture;

/// <summary>
/// 屏幕/窗口实时捕获接口。所有实现需保证：
/// <list type="bullet">
///   <item>在 <see cref="Start"/> 前后线程安全；</item>
///   <item><see cref="TryGetFrame"/> 返回的位图为 BGRA8、stride = Width*4；</item>
///   <item><see cref="Dispose"/> 后再调用任何成员均应抛出 <see cref="ObjectDisposedException"/>。</item>
/// </list>
/// </summary>
public interface IScreenCapture : IDisposable
{
    bool IsCapturing { get; }

    /// <summary>开始捕获指定窗口。</summary>
    void Start(IntPtr hWnd);

    /// <summary>停止捕获并释放底层资源（仍可再次 <see cref="Start"/>）。</summary>
    void Stop();

    /// <summary>
    /// 尝试获取最新一帧。若当前没有帧或窗口最小化等原因无法采集，返回 <c>null</c>。
    /// </summary>
    CapturedFrame? TryGetFrame();
}
