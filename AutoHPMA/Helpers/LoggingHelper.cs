using System;
using System.IO;

using Serilog;
using Serilog.Events;

namespace AutoHPMA.Helpers;

/// <summary>
/// 日志系统初始化工具。
/// 统一负责解析日志目录、配置 Serilog 全局 Logger。
/// </summary>
public static class LoggingHelper
{
    /// <summary>
    /// 日志文件所在目录（绝对路径）。未调用 <see cref="ConfigureSerilog"/> 前也可安全读取。
    /// </summary>
    public static string LogDirectory { get; } = ResolveLogDirectory();

    /// <summary>
    /// 配置 Serilog 静态 Logger。应在 Host 构建之前调用一次。
    /// </summary>
    public static void ConfigureSerilog()
    {
        Directory.CreateDirectory(LogDirectory);

        const string outputTemplate =
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Debug(outputTemplate: outputTemplate)
            .WriteTo.File(
                path: Path.Combine(LogDirectory, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: outputTemplate)
            .CreateLogger();
    }

    /// <summary>
    /// 程序退出时刷新并释放 Logger。
    /// </summary>
    public static void CloseAndFlush() => Log.CloseAndFlush();

    private static string ResolveLogDirectory()
    {
        // 统一写入 %LocalAppData%\AutoHPMA\logs，
        // MSIX 打包模式下会被虚拟化到包的本地存储，未打包模式下直接使用。
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "AutoHPMA", "logs");
    }
}
