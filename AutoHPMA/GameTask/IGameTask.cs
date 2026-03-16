using Microsoft.Extensions.Logging;
using System;

namespace AutoHPMA.GameTask;

/// <summary>
/// 游戏任务接口，定义了所有游戏任务的基本行为
/// </summary>
public interface IGameTask : IDisposable
{
    /// <summary>
    /// 启动任务
    /// </summary>
    void Start();

    /// <summary>
    /// 停止任务
    /// </summary>
    void Stop();

    /// <summary>
    /// 任务完成事件
    /// </summary>
    event EventHandler? TaskCompleted;

    /// <summary>
    /// 设置任务参数
    /// </summary>
    /// <param name="parameters">任务参数字典</param>
    /// <returns>是否设置成功</returns>
    bool SetParameters(Dictionary<string, object> parameters);
} 