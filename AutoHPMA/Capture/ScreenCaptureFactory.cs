using System;
using AutoHPMA.Capture.Models;

namespace AutoHPMA.Capture;

/// <summary>
/// 根据 <see cref="CaptureMethod"/> 创建对应的 <see cref="IScreenCapture"/> 实例。
/// </summary>
public static class ScreenCaptureFactory
{
    public static IScreenCapture Create(CaptureMethod method) => method switch
    {
        CaptureMethod.WindowsGraphicsCapture => new WindowsGraphicsCapture(),
        CaptureMethod.BitBlt => new BitBltCapture(),
        CaptureMethod.PrintWindow => new PrintWindowCapture(),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "未知的截屏方式。"),
    };
}
