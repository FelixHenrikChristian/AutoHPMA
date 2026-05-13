using System;

namespace AutoHPMA.Capture.Models;

/// <summary>
/// 一帧已经拷贝到托管内存的 BGRA8 图像。
/// </summary>
/// <remarks>
/// 使用 BGRA8（每像素 4 字节，行优先）以便直接喂给 <see cref="Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap"/>
/// 的 PixelBuffer。<see cref="Stride"/> 通常等于 <c>Width * 4</c>，捕获器需要保证这一点（必要时由捕获器内部拷贝压紧）。
/// </remarks>
public sealed class CapturedFrame
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int Stride { get; init; }

    public required byte[] PixelsBgra8 { get; init; }

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
