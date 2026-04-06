using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class FundLedgerPage : Page
{
    private readonly FundLedgerViewModel _viewModel;

    public FundLedgerPage(FundLedgerViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
    }

    private async void OnReconciliationRefreshClick(object sender, RoutedEventArgs e)
    {
        await RunReconciliationOperationAsync(ct => _viewModel.RefreshReconciliationWorkbenchAsync(ct));
    }

    private async void OnStartReviewBreakClick(object sender, RoutedEventArgs e)
    {
        await RunReconciliationOperationAsync(ct => _viewModel.StartReviewSelectedBreakAsync(ct));
    }

    private async void OnResolveBreakClick(object sender, RoutedEventArgs e)
    {
        await RunReconciliationOperationAsync(ct => _viewModel.ResolveSelectedBreakAsync(ct));
    }

    private async void OnDismissBreakClick(object sender, RoutedEventArgs e)
    {
        await RunReconciliationOperationAsync(ct => _viewModel.DismissSelectedBreakAsync(ct));
    }

    private void OnPagePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ReconciliationSearchBox.Focus();
            ReconciliationSearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnReconciliationQueuePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReconciliationDetailTabs.Focus();
            e.Handled = true;
        }
    }

    private void OnReconciliationQueueMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ReconciliationDetailTabs.Focus();
    }

    private async Task RunReconciliationOperationAsync(Func<CancellationToken, Task> operation)
    {
        var focusedElement = Keyboard.FocusedElement as IInputElement;
        var breakQueueScrollState = CaptureScrollState(BreakQueueGrid);
        var runsScrollState = CaptureScrollState(RunsGrid);

        await operation(CancellationToken.None);

        await Dispatcher.InvokeAsync(() =>
        {
            RestoreScrollState(BreakQueueGrid, breakQueueScrollState);
            RestoreScrollState(RunsGrid, runsScrollState);

            if (focusedElement is UIElement element && element.Focusable && element.IsVisible)
            {
                element.Focus();
                return;
            }

            if (_viewModel.SelectedReconciliationQueueIndex == 0)
            {
                BreakQueueGrid.Focus();
            }
            else
            {
                RunsGrid.Focus();
            }
        }, DispatcherPriority.Background);
    }

    private static ScrollState CaptureScrollState(DependencyObject root)
    {
        var scrollViewer = FindDescendant<ScrollViewer>(root);
        return scrollViewer is null
            ? ScrollState.Empty
            : new ScrollState(scrollViewer.HorizontalOffset, scrollViewer.VerticalOffset);
    }

    private static void RestoreScrollState(DependencyObject root, ScrollState state)
    {
        var scrollViewer = FindDescendant<ScrollViewer>(root);
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ScrollToHorizontalOffset(state.HorizontalOffset);
        scrollViewer.ScrollToVerticalOffset(state.VerticalOffset);
    }

    private static TElement? FindDescendant<TElement>(DependencyObject? root)
        where TElement : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        if (root is TElement element)
        {
            return element;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var match = FindDescendant<TElement>(VisualTreeHelper.GetChild(root, index));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private readonly record struct ScrollState(double HorizontalOffset, double VerticalOffset)
    {
        public static ScrollState Empty => new(0d, 0d);
    }
}
