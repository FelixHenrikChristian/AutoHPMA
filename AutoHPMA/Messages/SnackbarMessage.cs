using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wpf.Ui.Controls;

namespace AutoHPMA.Messages
{
    public class SnackbarInfo
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public ControlAppearance Appearance { get; set; }
        public IconElement? Icon { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class ShowSnackbarMessage : ValueChangedMessage<SnackbarInfo>
    {
        public ShowSnackbarMessage(SnackbarInfo info) : base(info) { }
    }
}
