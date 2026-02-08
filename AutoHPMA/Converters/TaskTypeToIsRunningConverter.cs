using System;
using System.Globalization;
using System.Windows.Data;
using AutoHPMA.ViewModels.Pages;

namespace AutoHPMA.Converters
{
    /// <summary>
    /// 将 TaskType 转换为 bool，用于判断指定任务是否正在运行
    /// </summary>
    public class TaskTypeToIsRunningConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TaskType currentTask && parameter is string taskName)
            {
                return Enum.TryParse<TaskType>(taskName, out var targetTask) && currentTask == targetTask;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
