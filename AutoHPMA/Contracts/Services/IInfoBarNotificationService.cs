using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.Contracts.Services;

/// <summary>
/// 在 Shell 中通过 <see cref="InfoBar"/> 显示简短应用内提示（默认自动收起、无关闭按钮）。
/// </summary>
public interface IInfoBarNotificationService
{
    /// <summary>
    /// 绑定页面上的 InfoBar 宿主；应在 Shell 加载时调用一次。
    /// </summary>
    void Register(InfoBar presenter);

    /// <param name="autoDismiss">为 <c>null</c> 时使用默认时长；为 <see cref="TimeSpan.Zero"/> 时不自动关闭。</param>
    void Show(InfoBarSeverity severity, string title, string message, TimeSpan? autoDismiss = null);
}
