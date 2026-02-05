using AutoHPMA.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System;
using Wpf.Ui.Controls;

namespace AutoHPMA.Helpers
{
    /// <summary>
    /// Snackbar 辅助类，提供统一的 Snackbar 调用接口
    /// </summary>
    public static class SnackbarHelper
    {
        private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

        /// <summary>
        /// 显示成功 Snackbar
        /// </summary>
        public static void ShowSuccess(string title, string message, TimeSpan? duration = null)
        {
            Show(title, message, ControlAppearance.Success,
                 new SymbolIcon(SymbolRegular.CheckmarkCircle24, 36), duration);
        }

        /// <summary>
        /// 显示信息 Snackbar
        /// </summary>
        public static void ShowInfo(string title, string message, TimeSpan? duration = null)
        {
            Show(title, message, ControlAppearance.Info,
                 new SymbolIcon(SymbolRegular.Info24, 36), duration);
        }

        /// <summary>
        /// 显示警告 Snackbar
        /// </summary>
        public static void ShowWarning(string title, string message, TimeSpan? duration = null)
        {
            Show(title, message, ControlAppearance.Caution,
                 new SymbolIcon(SymbolRegular.Warning24, 36), duration);
        }

        /// <summary>
        /// 显示错误 Snackbar
        /// </summary>
        public static void ShowError(string title, string message, TimeSpan? duration = null)
        {
            Show(title, message, ControlAppearance.Danger,
                 new SymbolIcon(SymbolRegular.ErrorCircle24, 36), duration);
        }

        /// <summary>
        /// 显示自定义 Snackbar
        /// </summary>
        public static void Show(string title, string message,
                                ControlAppearance appearance,
                                IconElement? icon = null,
                                TimeSpan? duration = null)
        {
            var snackbarInfo = new SnackbarInfo
            {
                Title = title,
                Message = message,
                Appearance = appearance,
                Icon = icon,
                Duration = duration ?? DefaultDuration
            };
            WeakReferenceMessenger.Default.Send(new ShowSnackbarMessage(snackbarInfo));
        }
    }
}
