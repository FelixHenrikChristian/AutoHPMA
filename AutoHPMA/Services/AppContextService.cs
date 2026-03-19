using AutoHPMA.Helpers.CaptureHelper;
using AutoHPMA.Services.Interface;
using AutoHPMA.Views.Windows;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoHPMA.Services
{
    public class AppContextService : IAppContextService
    {
        // 单例实例
        private static readonly AppContextService _instance = new AppContextService();
        public static AppContextService Instance => _instance;

        // 构造函数设为 private 防止外部实例化
        private AppContextService() { }

        #region 共享数据属性
        #region Dashboard
        private IntPtr _displayHwnd;
        public IntPtr DisplayHwnd
        {
            get => _displayHwnd;
            set
            {
                if (_displayHwnd != value)
                {
                    _displayHwnd = value;
                    OnPropertyChanged(nameof(DisplayHwnd));
                }
            }
        }

        private IntPtr _gameHwnd;
        public IntPtr GameHwnd
        {
            get => _gameHwnd;
            set
            {
                if (_gameHwnd != value)
                {
                    _gameHwnd = value;
                    OnPropertyChanged(nameof(GameHwnd));
                }
            }
        }

        private LogWindow _logWindow;
        public LogWindow LogWindow
        {
            get => _logWindow;
            set
            {
                if (_logWindow != value)
                {
                    _logWindow = value;
                    OnPropertyChanged(nameof(LogWindow));
                }
            }
        }

        private MaskWindow _maskWindow;
        public MaskWindow MaskWindow
        {
            get => _maskWindow;
            set
            {
                if (_maskWindow != value)
                {
                    _maskWindow = value;
                    OnPropertyChanged(nameof(MaskWindow));
                }
            }
        }

        private WindowsGraphicsCapture _capture;
        public WindowsGraphicsCapture Capture
        {
            get => _capture;
            set
            {
                if (_capture != value)
                {
                    _capture = value;
                    OnPropertyChanged(nameof(Capture));
                }
            }
        }
        #endregion

        #region Task
        private int _stateMonitorInterval = 200;
        public int StateMonitorInterval
        {
            get => _stateMonitorInterval;
            set
            {
                if (_stateMonitorInterval != value)
                {
                    _stateMonitorInterval = value;
                    OnPropertyChanged(nameof(StateMonitorInterval));
                }
            }
        }
        #endregion
        #endregion

        #region INotifyPropertyChanged 实现

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
