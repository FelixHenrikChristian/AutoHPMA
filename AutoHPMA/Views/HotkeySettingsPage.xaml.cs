using AutoHPMA.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AutoHPMA.Views;

public sealed partial class HotkeySettingsPage : Page
{
    public HotkeySettingsViewModel ViewModel { get; }

    public HotkeySettingsPage()
    {
        ViewModel = App.GetService<HotkeySettingsViewModel>();
        InitializeComponent();
    }
}

