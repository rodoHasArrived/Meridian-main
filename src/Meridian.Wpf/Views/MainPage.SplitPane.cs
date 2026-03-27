using System.Windows;

namespace Meridian.Wpf.Views;

// Partial extension of MainPage wiring the split-pane host to NavigationService.
// Fields _navigationService, _viewModel, and _currentPageTag are declared in MainPage.xaml.cs.
public partial class MainPage
{
    private void OnSplitPaneHostLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not SplitPaneHostControl host) return;

        var firstFrame = host.GetPaneFrame(0);
        if (firstFrame is null) return;

        // Redirect NavigationService from the collapsed ContentFrame to the live pane
        _navigationService.Initialize(firstFrame);

        // Re-navigate to the page that was loaded before we took over
        _navigationService.NavigateTo(_currentPageTag);

        if (_viewModel is not null)
        {
            // When user changes layout, redirect active-pane navigation
            _viewModel.SplitPane.LayoutChanged += (_, _) =>
            {
                var pane = host.GetPaneFrame(_viewModel.SplitPane.ActivePaneIndex);
                if (pane is not null)
                    _navigationService.Initialize(pane);
            };

            // When active pane index changes, redirect navigation to that pane
            _viewModel.SplitPane.ActivePaneChanged += (_, idx) =>
            {
                var pane = host.GetPaneFrame(idx);
                if (pane is not null)
                    _navigationService.Initialize(pane);
            };
        }
    }
}
