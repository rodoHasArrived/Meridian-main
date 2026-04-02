using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfNavigationEventArgs = System.Windows.Navigation.NavigationEventArgs;
using WpfNavigationService = Meridian.Wpf.Services.NavigationService;

namespace Meridian.Wpf.Views;

public partial class MainPage : Page
{
    private readonly WpfNavigationService _navigationService;
    private readonly MainPageViewModel _viewModel;

    public MainPage(MainPageViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigationService = (WpfNavigationService)_viewModel.NavigationService;

        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _navigationService.Initialize(ContentFrame);

        _viewModel.ActivateShell();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
    }

    private void CommandPaletteOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _viewModel.HideCommandPaletteCommand.Execute(null);
    }

    private void CommandPaletteBorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void CommandPaletteResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        _viewModel.OpenSelectedCommandPalettePageCommand.Execute(null);
    }

    private void OnOpenCommandPaletteClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ShowCommandPaletteCommand.Execute(null);
        CommandPaletteTextBox.Focus();
        CommandPaletteTextBox.SelectAll();
    }

    private void OnContentFrameNavigated(object sender, WpfNavigationEventArgs e)
    {
        _viewModel.SyncNavigationState();
    }
}
