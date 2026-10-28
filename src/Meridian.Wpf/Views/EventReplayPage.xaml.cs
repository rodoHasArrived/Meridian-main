using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Provides the desktop page to replay event streams to review sequence and signal behavior.
/// </summary>
public partial class EventReplayPage : Page
{
    private readonly EventReplayViewModel _viewModel = new();

    public EventReplayPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();
    }

    private void StartReplay_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StartReplay();
    }

    private void PauseReplay_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PauseReplay();
    }

    private void StopReplay_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StopReplay();
    }
}
