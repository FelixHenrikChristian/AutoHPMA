using OpenCvSharp;

namespace AutoHPMA.GameTask.Model;

/// <summary>
/// 状态检测规则
/// </summary>
/// <typeparam name="TState">状态枚举类型</typeparam>
public record StateRule<TState>(
    Mat[] Templates,
    TState State,
    string DisplayName,
    double Threshold = 0.9
);
