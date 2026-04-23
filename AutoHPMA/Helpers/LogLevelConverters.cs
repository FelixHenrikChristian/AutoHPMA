using System;

using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

using Serilog.Events;

using Windows.UI;

namespace AutoHPMA.Helpers;

/// <summary>
/// 将 <see cref="LogEventLevel"/> 转换为用于着色的 Brush。
/// </summary>
public class LogLevelToBrushConverter : IValueConverter
{
    // 颜色参考 VS Code / Console 习惯。
    private static readonly SolidColorBrush DebugBrush = new(Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF));       // 灰
    private static readonly SolidColorBrush InformationBrush = new(Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA)); // 蓝
    private static readonly SolidColorBrush WarningBrush = new(Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B));     // 橙
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromArgb(0xFF, 0xEF, 0x44, 0x44));       // 红
    private static readonly SolidColorBrush FatalBrush = new(Color.FromArgb(0xFF, 0xDC, 0x26, 0x26));       // 深红
    private static readonly SolidColorBrush DefaultBrush = new(Colors.Gray);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is LogEventLevel level)
        {
            return level switch
            {
                LogEventLevel.Verbose => DebugBrush,
                LogEventLevel.Debug => DebugBrush,
                LogEventLevel.Information => InformationBrush,
                LogEventLevel.Warning => WarningBrush,
                LogEventLevel.Error => ErrorBrush,
                LogEventLevel.Fatal => FatalBrush,
                _ => DefaultBrush,
            };
        }
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}
