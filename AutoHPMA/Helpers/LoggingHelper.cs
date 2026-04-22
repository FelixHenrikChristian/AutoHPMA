using System;
using System.IO;
using System.Reflection;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using Windows.ApplicationModel;

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
    /// 运行时可调的全局最低日志级别开关。默认 <see cref="LogEventLevel.Information"/>。
    /// 打开"诊断模式"时切到 <see cref="LogEventLevel.Debug"/>，立即生效、无需重启。
    /// </summary>
    public static LoggingLevelSwitch LevelSwitch { get; } = new LoggingLevelSwitch(LogEventLevel.Information);

    /// <summary>
    /// 诊断模式下使用的级别（即"详细日志"的含义）。
    /// </summary>
    public const LogEventLevel DiagnosticLevel = LogEventLevel.Debug;

    /// <summary>
    /// 正常模式下的级别。
    /// </summary>
    public const LogEventLevel NormalLevel = LogEventLevel.Information;

    /// <summary>
    /// 设置或取消诊断模式。线程安全，立即生效。
    /// </summary>
    public static void SetDiagnosticMode(bool enabled)
    {
        LevelSwitch.MinimumLevel = enabled ? DiagnosticLevel : NormalLevel;
    }

    /// <summary>
    /// 配置 Serilog 静态 Logger。应在 Host 构建之前调用一次。
    /// </summary>
    public static void ConfigureSerilog()
    {
        Directory.CreateDirectory(LogDirectory);

        const string outputTemplate =
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

#if DEBUG
        // 开发构建默认放到 Debug，便于本地排查。
        LevelSwitch.MinimumLevel = DiagnosticLevel;
#endif

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
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

    /// <summary>
    /// 写入应用启动横幅（版本、运行模式、OS、日志目录等），便于事后定位问题环境。
    /// </summary>
    public static void LogStartupBanner()
    {
        var version = GetAppVersion();
        var isMsix = RuntimeHelper.IsMSIX;
        var os = Environment.OSVersion.VersionString;
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        var framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        Log.Information("==================== AutoHPMA 启动 ====================");
        Log.Information("版本: v{Version}  运行模式: {Mode}  架构: {Arch}", version, isMsix ? "MSIX" : "Unpackaged", arch);
        Log.Information("运行时: {Framework}", framework);
        Log.Information("操作系统: {OS}", os);
    }

    /// <summary>
    /// 记录应用正常退出日志。
    /// </summary>
    public static void LogShutdown()
    {
        Log.Information("==================== AutoHPMA 退出 ====================");
    }

    private static string GetAppVersion()
    {
        if (RuntimeHelper.IsMSIX)
        {
            var v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }

        var asmVer = Assembly.GetExecutingAssembly().GetName().Version;
        return asmVer?.ToString() ?? "0.0.0.0";
    }

    private static string ResolveLogDirectory()
    {
        // 统一写入 %LocalAppData%\AutoHPMA\logs，
        // MSIX 打包模式下会被虚拟化到包的本地存储，未打包模式下直接使用。
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "AutoHPMA", "logs");
    }
}
