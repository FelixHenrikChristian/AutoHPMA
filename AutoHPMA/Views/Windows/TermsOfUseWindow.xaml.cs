using System.Windows;
using Wpf.Ui.Controls;

namespace AutoHPMA.Views.Windows
{
    public partial class TermsOfUseWindow : FluentWindow
    {
        public TermsOfUseWindow()
        {
            InitializeComponent();
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
