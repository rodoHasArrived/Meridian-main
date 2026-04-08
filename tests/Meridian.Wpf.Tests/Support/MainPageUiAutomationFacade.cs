using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Support;

internal sealed class MainPageUiAutomationFacade : IDisposable
{
    private readonly string _runMatRootDirectory;
    private readonly ServiceProvider _serviceProvider;
    private bool _disposed;

    public MainPageUiAutomationFacade(FundContextService? fundContextService = null)
    {
        RunMatUiAutomationFacade.EnsureApplicationResources();

        var fixtureModeDetector = FixtureModeDetector.Instance;
        fixtureModeDetector.SetFixtureMode(false);
        fixtureModeDetector.UpdateBackendReachability(true);

        var navigationService = NavigationService.Instance;
        navigationService.ResetForTests();

        _runMatRootDirectory = Path.Combine(Path.GetTempPath(), "mainpage-ui-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_runMatRootDirectory);

        var runMatService = new RunMatService(_runMatRootDirectory);
        _serviceProvider = (ServiceProvider)RunMatUiAutomationFacade.CreateMainPageServiceProvider(runMatService, fundContextService);
        navigationService.SetServiceProvider(_serviceProvider);

        Page = _serviceProvider.GetRequiredService<MainPage>();
        Page.ApplyTemplate();
        Page.UpdateLayout();
        RunMatUiAutomationFacade.InvokeMainPageLoaded(Page);
        Page.UpdateLayout();
        RunMatUiAutomationFacade.DrainDispatcher();
    }

    public MainPage Page { get; }

    public MainPageViewModel ViewModel => Page.DataContext.Should().BeOfType<MainPageViewModel>().Subject;

    public Grid CommandPaletteOverlay => GetRequired<Grid>("CommandPaletteOverlay");

    public TextBox CommandPaletteTextBox => GetRequired<TextBox>("CommandPaletteTextBox");

    public ListBox CommandPaletteResults => GetRequired<ListBox>("CommandPaletteResults");

    public Button ResearchWorkspaceButton => GetRequired<Button>("ResearchWorkspaceButton");

    public Button TradingWorkspaceButton => GetRequired<Button>("TradingWorkspaceButton");

    public Button DataOperationsWorkspaceButton => GetRequired<Button>("DataOperationsWorkspaceButton");

    public Button GovernanceWorkspaceButton => GetRequired<Button>("GovernanceWorkspaceButton");

    public TextBlock RecentPagesEmptyText => GetRequired<TextBlock>("RecentPagesEmptyText");

    public Button TickerStripToggleButton => GetRequired<Button>("TickerStripToggleButton");

    public TextBlock TickerStripToggleLabelText => GetRequired<TextBlock>("TickerStripToggleLabelText");

    public Border FixtureModeBanner => GetRequired<Border>("FixtureModeBanner");

    public TextBlock FixtureModeLabel => GetRequired<TextBlock>("FixtureModeLabel");

    public Button FixtureModeDismissButton => GetRequired<Button>("FixtureModeDismissButton");

    public Button BackButton => GetRequired<Button>("BackButton");

    public TextBlock ShellAutomationStateText => GetRequired<TextBlock>("ShellAutomationStateText");

    public TextBlock PageTitleText => GetRequired<TextBlock>("PageTitleTextBlock");

    public void ShowCommandPalette()
    {
        Page.ShowCommandPaletteOverlay();
        UpdateLayout();
    }

    public void SetText(TextBox textBox, string value)
    {
        textBox.Text = value;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        UpdateLayout();
    }

    public void SelectCommandPalettePage(string pageTag)
    {
        ViewModel.SelectedCommandPalettePage = pageTag;
        UpdateLayout();
    }

    public void OpenSelectedCommandPalettePage()
    {
        ViewModel.OpenSelectedCommandPalettePageCommand.Execute(null);
        UpdateLayout();
    }

    public Button GetRecentPageButton(string pageTag)
    {
        return FindByAutomationId<Button>($"RecentPage{pageTag}");
    }

    public void Click(Button button)
    {
        if (button.Command is not null && button.Command.CanExecute(button.CommandParameter))
        {
            button.Command.Execute(button.CommandParameter);
        }
        else
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }

        UpdateLayout();
    }

    public void SetFixtureMode(bool enabled)
    {
        var fixtureModeDetector = FixtureModeDetector.Instance;
        fixtureModeDetector.SetFixtureMode(enabled);
        fixtureModeDetector.UpdateBackendReachability(true);
        UpdateLayout();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        FixtureModeDetector.Instance.SetFixtureMode(false);
        FixtureModeDetector.Instance.UpdateBackendReachability(true);

        if (Page.DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _serviceProvider.Dispose();
        NavigationService.Instance.ResetForTests();

        try
        {
            if (Directory.Exists(_runMatRootDirectory))
            {
                Directory.Delete(_runMatRootDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for the isolated main-page automation workspace.
        }
    }

    private T GetRequired<T>(string name) where T : FrameworkElement
    {
        Page.FindName(name).Should().BeOfType<T>($"{name} should be declared on MainPage");
        return (T)Page.FindName(name)!;
    }

    private T FindByAutomationId<T>(string automationId) where T : FrameworkElement
    {
        UpdateLayout();

        foreach (var descendant in EnumerateDescendants(Page))
        {
            if (descendant is T typed &&
                string.Equals(AutomationProperties.GetAutomationId(typed), automationId, StringComparison.Ordinal))
            {
                return typed;
            }
        }

        throw new Xunit.Sdk.XunitException($"Unable to locate {typeof(T).Name} with AutomationId '{automationId}'.");
    }

    private void UpdateLayout()
    {
        Page.UpdateLayout();
        RunMatUiAutomationFacade.DrainDispatcher();
    }

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;

            foreach (var descendant in EnumerateDescendants(child))
            {
                yield return descendant;
            }
        }
    }
}
