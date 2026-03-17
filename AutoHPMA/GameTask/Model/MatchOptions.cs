using OpenCvSharp;

namespace AutoHPMA.GameTask.Model;

/// <summary>
/// 模板匹配选项
/// </summary>
public class MatchOptions
{
    /// <summary>模板遮罩（可选）。作用于模板图像。</summary>
    public Mat? Mask { get; set; }

    /// <summary>是否使用模板的 Alpha 通道生成遮罩</summary>
    public bool UseAlphaMask { get; set; } = false;

    /// <summary>源图区域遮罩（可选）。作用于源图：仅当模板完全落在遮罩非零像素区域内时才参与匹配，支持不规则形状；尺寸与源图不一致时会自动缩放。</summary>
    public Mat? SourceMask { get; set; }

    /// <summary>是否查找多个匹配</summary>
    public bool FindMultiple { get; set; } = false;

    /// <summary>匹配阈值（默认 0.9）</summary>
    public double Threshold { get; set; } = 0.9;

    /// <summary>匹配模式</summary>
    public TemplateMatchModes MatchMode { get; set; } = TemplateMatchModes.CCoeffNormed;
}
