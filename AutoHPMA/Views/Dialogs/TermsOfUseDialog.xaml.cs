using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.Views.Dialogs;

public sealed partial class TermsOfUseDialog : ContentDialog
{
    /// <summary>
    /// 用户是否同意了条款（true=同意，false=退出）。
    /// </summary>
    public bool Accepted { get; private set; }

    private bool _atBottom;

    private readonly Style _defaultRoundedStyle;
    private readonly Style _accentStyle;

    public TermsOfUseDialog()
    {
        InitializeComponent();

        // 预先缓存两种样式，避免重复查找
        _accentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
        _defaultRoundedStyle = (Style)PrimaryButtonStyle; // 即 XAML 中定义的圆角默认样式

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateState();
    }

    private void TermsScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateState();
    }

    private void UpdateState()
    {
        if (TermsScrollViewer == null)
        {
            return;
        }

        // 内容不足一屏时 ScrollableHeight≈0，视为已在底部
        var canScroll = TermsScrollViewer.ScrollableHeight > 1;
        var atBottom = !canScroll ||
                       TermsScrollViewer.VerticalOffset >= TermsScrollViewer.ScrollableHeight - 2;

        if (atBottom == _atBottom)
        {
            return;
        }

        _atBottom = atBottom;

        // 切换同意按钮的样式：底部=主题色，非底部=圆角默认
        PrimaryButtonStyle = atBottom ? _accentStyle : _defaultRoundedStyle;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!_atBottom)
        {
            // 未读完：取消关闭，弹出 TeachingTip 提示用户
            args.Cancel = true;
            ReadToEndTip.IsOpen = true;
            return;
        }

        Accepted = true;
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Accepted = false;
    }
}
