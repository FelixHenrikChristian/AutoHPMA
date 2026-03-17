using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace AutoHPMA.GameTask.Model;

/// <summary>
/// 模板匹配结果
/// </summary>
public class MatchResult
{
    /// <summary>是否匹配成功</summary>
    public bool Success { get; set; }

    /// <summary>匹配位置（单个匹配时的左上角坐标，未缩放）</summary>
    public Point Location { get; set; }

    /// <summary>匹配区域列表（已缩放，用于显示）</summary>
    public List<Rect> Rects { get; set; } = new();

    /// <summary>匹配区域列表（未缩放，用于多重匹配点击）</summary>
    public List<Rect> RectsUnscaled { get; set; } = new();

    /// <summary>模板尺寸（未缩放）</summary>
    public Size TemplateSize { get; set; }

    /// <summary>静态失败结果</summary>
    public static MatchResult Failed => new() { Success = false };
}
