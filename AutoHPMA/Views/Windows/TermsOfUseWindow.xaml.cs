using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace AutoHPMA.Views.Windows
{
    public partial class TermsOfUseWindow : FluentWindow
    {
        private DispatcherTimer? _infoBarAutoCloseTimer;

        public TermsOfUseWindow()
        {
            InitializeComponent();
            Loaded += TermsOfUseWindow_Loaded;
        }

        private async void TermsOfUseWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 等待布局完成后再计算 ScrollViewer 的可滚动高度
            await Dispatcher.InvokeAsync(UpdateAgreeButtonState, DispatcherPriority.Loaded);
        }

        private void TermsScrollViewer_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            UpdateAgreeButtonState();
        }

        private void UpdateAgreeButtonState()
        {
            if (AgreeButton == null || TermsScrollViewer == null)
                return;

            // 内容不足一屏时 ScrollableHeight=0，应允许直接同意
            var canScroll = TermsScrollViewer.ScrollableHeight > 0;
            var atBottom = !canScroll || TermsScrollViewer.VerticalOffset >= TermsScrollViewer.ScrollableHeight;

            AgreeButton.IsEnabled = atBottom;

            if (atBottom && ReadToEndInfoBar != null)
            {
                ReadToEndInfoBar.IsOpen = false;
                _infoBarAutoCloseTimer?.Stop();
            }
        }

        private void BottomBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (AgreeButton == null || ReadToEndInfoBar == null)
                return;

            // 按钮可用时让正常点击流程走 Button.Click
            if (AgreeButton.IsEnabled)
                return;

            // 捕获“点击了同意按钮区域”的意图（按钮禁用时自身不会触发事件）
            var p = e.GetPosition(AgreeButton);
            var hitAgreeButton =
                p.X >= 0 && p.Y >= 0 &&
                p.X <= AgreeButton.ActualWidth &&
                p.Y <= AgreeButton.ActualHeight;

            if (!hitAgreeButton)
                return;

            ReadToEndInfoBar.IsOpen = true;
            StartOrResetInfoBarAutoClose();
        }

        private void StartOrResetInfoBarAutoClose()
        {
            _infoBarAutoCloseTimer ??= new DispatcherTimer
            {
                Interval = System.TimeSpan.FromSeconds(4)
            };

            _infoBarAutoCloseTimer.Stop();
            _infoBarAutoCloseTimer.Tick -= InfoBarAutoCloseTimer_Tick;
            _infoBarAutoCloseTimer.Tick += InfoBarAutoCloseTimer_Tick;
            _infoBarAutoCloseTimer.Start();
        }

        private void InfoBarAutoCloseTimer_Tick(object? sender, System.EventArgs e)
        {
            _infoBarAutoCloseTimer?.Stop();

            if (ReadToEndInfoBar != null)
                ReadToEndInfoBar.IsOpen = false;
        }

        private void AgreeButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
