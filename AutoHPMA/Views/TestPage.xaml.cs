using System;
using AutoHPMA.ViewModels;
using AutoHPMA.Views.TestPages;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace AutoHPMA.Views;

public sealed partial class TestPage : Page
{
    public TestViewModel ViewModel { get; }

    private int _previousSelectedIndex = 0;

    public TestPage()
    {
        ViewModel = App.GetService<TestViewModel>();
        InitializeComponent();
    }

    private void TestSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var selectedItem = sender.SelectedItem;
        int currentSelectedIndex = sender.Items.IndexOf(selectedItem);
        var effect = currentSelectedIndex - _previousSelectedIndex > 0
            ? SlideNavigationTransitionEffect.FromRight
            : SlideNavigationTransitionEffect.FromLeft;

        NavigateToIndex(currentSelectedIndex, effect);
        _previousSelectedIndex = currentSelectedIndex;
    }

    private void NavigateToIndex(int index, SlideNavigationTransitionEffect effect)
    {
        Type pageType = index switch
        {
            0 => typeof(ScreenshotTestPage),
            1 => typeof(MouseSimulationPage),
            2 => typeof(TextRecognitionPage),
            3 => typeof(TemplateMatchingPage),
            4 => typeof(ContourDetectionPage),
            5 => typeof(ColorFilterPage),
            6 => typeof(TaskCoordinateEditorPage),
            _ => typeof(ScreenshotTestPage),
        };

        ContentFrame.Navigate(pageType, null, new SlideNavigationTransitionInfo { Effect = effect });
    }
}

