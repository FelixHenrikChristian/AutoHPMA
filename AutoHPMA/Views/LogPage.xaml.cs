using System.Collections.Specialized;
using AutoHPMA.Helpers;
using AutoHPMA.Models;
using AutoHPMA.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace AutoHPMA.Views;

public sealed partial class LogPage : Page
{
    public LogViewModel ViewModel { get; }

    public LogPage()
    {
        ViewModel = App.GetService<LogViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Attach(DispatcherQueue);
        ViewModel.Entries.CollectionChanged += OnEntriesCollectionChanged;
        ScrollToBottom();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Entries.CollectionChanged -= OnEntriesCollectionChanged;
        ViewModel.Detach();
        base.OnNavigatedFrom(e);
    }

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
        {
            ScrollToBottom();
        }
    }

    private void ScrollToBottom()
    {
        if (LogListView.Items.Count == 0)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (LogListView.Items.Count == 0)
            {
                return;
            }

            LogListView.UpdateLayout();
            LogListView.ScrollIntoView(LogListView.Items[^1], ScrollIntoViewAlignment.Leading);
            if (FindScrollViewer(LogListView) is { } scrollViewer)
            {
                scrollViewer.ChangeView(null, scrollViewer.ScrollableHeight, null, disableAnimation: true);
            }
        });
    }

    private void OnLogMessageTextLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock textBlock && textBlock.DataContext is LogEntry entry)
        {
            LogMessageFormatter.Apply(textBlock, entry.Message);
        }
    }

    private void OnLogMessageTextDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is TextBlock textBlock && args.NewValue is LogEntry entry)
        {
            LogMessageFormatter.Apply(textBlock, entry.Message);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer scrollViewer)
        {
            return scrollViewer;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } childScrollViewer)
            {
                return childScrollViewer;
            }
        }

        return null;
    }
}

