using System;
using System.IO;
using System.Reflection;

using AutoHPMA.Services.Logging;

using Serilog;
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
    /// 内存日志缓冲，用于日志页实时展示。
    /// 在 <see cref="ConfigureSerilog"/> 执行后即可使用。
    /// </summary>
    public static InMemoryLogSink LogBuffer { get; } = new(2000);

    /// <summary>
    /// 配置 Serilog 静态 Logger。应在 Host 构建之前调用一次。
    /// </summary>
    public static void ConfigureSerilog()
    {
        Directory.CreateDirectory(LogDirectory);

        const string outputTemplate =
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Debug)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Debug(outputTemplate: outputTemplate)
            .WriteTo.Sink(LogBuffer)
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
        Log.Information("版本: v{Version:l}  运行模式: {Mode:l}  架构: {Arch}", version, isMsix ? "MSIX" : "Unpackaged", arch);
        Log.Information("运行时: {Framework:l}", framework);
        Log.Information("操作系统: {OS:l}", os);
        Log.Information("日志目录: {LogDirectory:l}", LogDirectory);
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
