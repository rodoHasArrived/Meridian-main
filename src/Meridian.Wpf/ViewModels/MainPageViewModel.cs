using Meridian.Wpf.Contracts;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the main shell page. Coordinates workspace navigation and split-pane layout.
/// </summary>
public sealed class MainPageViewModel : BindableBase
{
    private readonly INavigationService _navigationService;

    /// <summary>Gets the split-pane layout view model.</summary>
    public SplitPaneViewModel SplitPane { get; } = new();

    public MainPageViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }
}
