using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Support;

internal sealed class MainPageUiAutomationFacade : IDisposable
{
    private readonly string _runMatRootDirectory;
    private readonly ServiceProvider _serviceProvider;
    private bool _disposed;

    public MainPageUiAutomationFacade(
        FundContextService? fundContextService = null,
        IWorkstationOperatorInboxApiClient? operatorInboxApiClient = null)
    {
        RunMatUiAutomationFacade.EnsureApplicationResources();

        var fixtureModeDetector = FixtureModeDetector.Instance;
        fixtureModeDetector.SetFixtureMode(false);
        fixtureModeDetector.UpdateBackendReachability(true);

        _runMatRootDirectory = Path.Combine(Path.GetTempPath(), "mainpage-ui-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_runMatRootDirectory);
        WorkspaceService.SetSettingsFilePathOverrideForTests(Path.Combine(_runMatRootDirectory, "workspace-data.json"));
        SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(Path.Combine(_runMatRootDirectory, "desktop-shell-preferences.json"));
        SettingsConfigurationService.Instance.SetShellDensityMode(ShellDensityMode.Standard);

        var navigationService = NavigationService.Instance;
        navigationService.ResetForTests();
        WorkspaceService.Instance.ResetForTests();

        var runMatService = new RunMatService(_runMatRootDirectory);
        _serviceProvider = (ServiceProvider)RunMatUiAutomationFacade.CreateMainPageServiceProvider(
            runMatService,
            fundContextService,
            operatorInboxApiClient);
        navigationService.SetServiceProvider(_serviceProvider);

        Page = _serviceProvider.GetRequiredService<MainPage>();
        Page.ApplyTemplate();
        UpdateLayout();
        RunMatUiAutomationFacade.InvokeMainPageLoaded(Page);
        UpdateLayout();
    }

    public MainPage Page { get; }

    public MainPageViewModel ViewModel => Page.DataContext.Should().BeOfType<MainPageViewModel>().Subject;

    public Grid CommandPaletteOverlay => GetRequired<Grid>("CommandPaletteOverlay");

    public TextBox CommandPaletteTextBox => GetRequired<TextBox>("CommandPaletteTextBox");

    public ListBox CommandPaletteResults => GetRequired<ListBox>("CommandPaletteResults");

    public TextBlock CommandPaletteSummaryText => GetRequired<TextBlock>("CommandPaletteSummaryText");

    public Button CommandPaletteClearButton => GetRequired<Button>("CommandPaletteClearButton");

    public Border CommandPaletteEmptyState => GetRequired<Border>("CommandPaletteEmptyState");

    public TextBlock CommandPaletteEmptyTitleText => GetRequired<TextBlock>("CommandPaletteEmptyTitleText");

    public ListBox WorkspacePrimaryNavList => GetRequired<ListBox>("WorkspacePrimaryNavList");

    public ListBox WorkspaceSecondaryNavList => GetRequired<ListBox>("WorkspaceSecondaryNavList");

    public ListBox WorkspaceOverflowNavList => GetRequired<ListBox>("WorkspaceOverflowNavList");

    public ListBox RelatedWorkflowNavList => GetRequired<ListBox>("RelatedWorkflowNavList");

    public Button ResearchWorkspaceButton => GetRequired<Button>("ResearchWorkspaceButton");

    public Button TradingWorkspaceButton => GetRequired<Button>("TradingWorkspaceButton");

    public Button DataOperationsWorkspaceButton => GetRequired<Button>("DataOperationsWorkspaceButton");

    public Button GovernanceWorkspaceButton => GetRequired<Button>("GovernanceWorkspaceButton");

    public TextBlock RecentPagesEmptyText => GetRequired<TextBlock>("RecentPagesEmptyText");

    public Button RecentPagesEmptyActionButton => GetRequired<Button>("RecentPagesEmptyActionButton");

    public TextBlock RecentPagesSummaryText => GetRequired<TextBlock>("RecentPagesSummaryText");

    public Button TickerStripToggleButton => GetRequired<Button>("TickerStripToggleButton");

    public TextBlock TickerStripToggleLabelText => GetRequired<TextBlock>("TickerStripToggleLabelText");

    public Button ShellDensityToggleButton => GetRequired<Button>("ShellDensityToggleButton");

    public TextBlock ShellDensityButtonLabelText => GetRequired<TextBlock>("ShellDensityButtonLabelText");

    public Button OperatorInboxButton => GetRequired<Button>("OperatorInboxButton");

    public TextBlock OperatorInboxButtonLabelText => GetRequired<TextBlock>("OperatorInboxButtonLabelText");

    public Border FixtureModeBanner => GetRequired<Border>("FixtureModeBanner");

    public TextBlock FixtureModeLabel => GetRequired<TextBlock>("FixtureModeLabel");

    public Button FixtureModeDismissButton => GetRequired<Button>("FixtureModeDismissButton");

    public Button BackButton => GetRequired<Button>("BackButton");

    public TextBlock ShellAutomationStateText => GetRequired<TextBlock>("ShellAutomationStateText");

    public TextBlock PageTitleText => GetRequired<TextBlock>("PageTitleTextBlock");

    public TextBlock PageSubtitleText => GetRequired<TextBlock>("PageSubtitleText");

    public Frame ContentFrame => GetRequired<Frame>("ContentFrame");

    public Page? InnermostContentPage => NavigationHostInspector.ResolveInnermostPage(ContentFrame.Content);

    public Border WorkflowSummaryStrip => GetRequired<Border>("WorkflowSummaryStrip");

    public ItemsControl WorkflowSummaryItemsControl => GetRequired<ItemsControl>("WorkflowSummaryItemsControl");

    public Button PrimaryWorkflowActionButton => FindByAutomationId<Button>("PrimaryWorkflowActionButton");

    public Button SecondaryWorkflowToggleButton => FindByAutomationId<Button>("SecondaryWorkflowToggleButton");

    public WorkspaceShellContextStripControl WorkspaceShellContextStrip => GetRequired<WorkspaceShellContextStripControl>("WorkspaceShellContextStrip");

    public TextBlock WorkspaceContextTitleText => FindByAutomationId<TextBlock>("WorkspaceContextTitleText");

    public TextBlock WorkspaceContextSubtitleText => FindByAutomationId<TextBlock>("WorkspaceContextSubtitleText");

    public Border WorkspaceContextAttentionBanner => FindByAutomationId<Border>("WorkspaceContextAttentionBanner");

    public TextBlock WorkspaceContextAttentionTitleText => FindByAutomationId<TextBlock>("WorkspaceContextAttentionTitle");

    public TextBlock WorkspaceContextAttentionDetailText => FindByAutomationId<TextBlock>("WorkspaceContextAttentionDetail");

    public void ShowCommandPalette()
    {
        UpdateLayout();
        Page.ShowCommandPaletteOverlay();
        UpdateLayout();

        if (CommandPaletteOverlay.Visibility != Visibility.Visible)
        {
            Page.ShowCommandPaletteOverlay();
            UpdateLayout();
        }
    }

    public void SetText(TextBox textBox, string value)
    {
        textBox.Text = value;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        UpdateLayout();
    }

    public void SelectCommandPalettePage(string pageTag)
    {
        var entry = ViewModel.CommandPalettePages
            .First(entry => string.Equals(entry.PageTag, pageTag, StringComparison.OrdinalIgnoreCase));
        CommandPaletteResults.SelectedItem = entry;
        ViewModel.SelectedCommandPalettePage = entry;
        UpdateLayout();
    }

    public void SelectWorkspaceNavigationPage(ListBox listBox, string pageTag)
    {
        listBox.SelectedValue = pageTag;
        UpdateLayout();
    }

    public void ClearWorkspaceNavigationSelection(ListBox listBox)
    {
        listBox.SelectedItem = null;
        UpdateLayout();
    }

    public void OpenSelectedCommandPalettePage()
    {
        EnsureNavigationBridge();

        var entry = CommandPaletteResults.SelectedItem as ShellCommandPaletteEntry ?? ViewModel.SelectedCommandPalettePage;
        if (entry is not null)
        {
            ViewModel.NavigateToPageCommand.Execute(entry.PageTag);
            ViewModel.HideCommandPaletteCommand.Execute(null);
        }

        UpdateLayout();
    }

    public void OpenCommandPalettePage(string pageTag)
    {
        EnsureNavigationBridge();
        ViewModel.NavigateToPageCommand.Execute(pageTag);
        ViewModel.HideCommandPaletteCommand.Execute(null);
        UpdateLayout();
    }

    public bool TryHandleCommandPaletteDirectionalKey(Key key)
    {
        var method = typeof(MainPage).GetMethod("TryHandleCommandPaletteDirectionalKey", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var handled = method!.Invoke(Page, [key]).Should().BeOfType<bool>().Subject;
        UpdateLayout();
        return handled;
    }

    public void OpenWorkspaceHome(string pageTag)
    {
        var suppressNavigationField = typeof(MainPageViewModel)
            .GetField("_suppressNavigation", BindingFlags.Instance | BindingFlags.NonPublic);

        suppressNavigationField?.SetValue(ViewModel, true);
        try
        {
            ViewModel.CurrentPageTag = pageTag;
        }
        finally
        {
            suppressNavigationField?.SetValue(ViewModel, false);
        }

        FlushUi();
    }

    public Button GetRecentPageButton(string pageTag)
    {
        return FindByAutomationId<Button>($"RecentPage{pageTag}");
    }

    public void Click(Button button)
    {
        if ((UIElementAutomationPeer.FromElement(button) ?? new ButtonAutomationPeer(button))
            .GetPattern(PatternInterface.Invoke) is IInvokeProvider invokeProvider)
        {
            invokeProvider.Invoke();
        }
        else if (button.Command is { } command)
        {
            var parameter = button.CommandParameter;
            if (command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
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
        typeof(MainPageViewModel)
            .GetMethod("UpdateFixtureModeBanner", BindingFlags.Instance | BindingFlags.NonPublic)?
            .Invoke(ViewModel, null);
        FlushUi();
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

        if (NavigationHostInspector.ResolveInnermostContent(ContentFrame.Content) is FrameworkElement hostedContent)
        {
            RaiseLifecycleEvent(hostedContent, FrameworkElement.UnloadedEvent);
        }

        if (ContentFrame.Content is FrameworkElement content)
        {
            RaiseLifecycleEvent(content, FrameworkElement.UnloadedEvent);
        }

        RaiseLifecycleEvent(Page, FrameworkElement.UnloadedEvent);

        if (Page.DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _serviceProvider.Dispose();
        NavigationService.Instance.ResetForTests();
        WorkspaceService.Instance.ResetForTests();
        WorkspaceService.SetSettingsFilePathOverrideForTests(null);
        SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(null);

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

        foreach (var descendant in EnumerateSearchRoots().SelectMany(EnumerateDescendants))
        {
            if (descendant is T typed &&
                string.Equals(AutomationProperties.GetAutomationId(typed), automationId, StringComparison.Ordinal))
            {
                return typed;
            }
        }

        throw new Xunit.Sdk.XunitException($"Unable to locate {typeof(T).Name} with AutomationId '{automationId}'.");
    }

    public T FindDescendantByAutomationId<T>(string automationId) where T : FrameworkElement
        => FindByAutomationId<T>(automationId);

    private void UpdateLayout()
    {
        EnsureNavigationBridge();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            Page.UpdateLayout();
            RunMatUiAutomationFacade.DrainDispatcher();

            var expectedPageType = NavigationService.Instance.GetPageType(ViewModel.CurrentPageTag);
            var currentContent = NavigationHostInspector.ResolveInnermostContent(ContentFrame.Content);
            if (expectedPageType is null || expectedPageType.IsInstanceOfType(currentContent))
            {
                return;
            }
        }
    }

    private void FlushUi()
    {
        Page.UpdateLayout();
        RunMatUiAutomationFacade.DrainDispatcher();
    }

    private void EnsureNavigationBridge()
    {
        var navigationService = NavigationService.Instance;
        navigationService.SetServiceProvider(_serviceProvider);
        navigationService.Initialize(ContentFrame);
    }

    private static void RaiseLifecycleEvent(FrameworkElement element, RoutedEvent routedEvent)
    {
        element.RaiseEvent(new RoutedEventArgs(routedEvent, element));
        RunMatUiAutomationFacade.DrainDispatcher();
    }

    private IEnumerable<DependencyObject> EnumerateSearchRoots()
    {
        yield return Page;

        if (NavigationHostInspector.ResolveInnermostContent(ContentFrame.Content) is DependencyObject hostedContent &&
            !ReferenceEquals(hostedContent, Page))
        {
            yield return hostedContent;
        }
    }

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        yield return root;

        var visualChildCount = root is Visual
            ? VisualTreeHelper.GetChildrenCount(root)
            : 0;

        for (var i = 0; i < visualChildCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;

            foreach (var descendant in EnumerateDescendants(child))
            {
                yield return descendant;
            }
        }

        foreach (var logicalChild in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return logicalChild;

            foreach (var descendant in EnumerateDescendants(logicalChild))
            {
                yield return descendant;
            }
        }
    }
}
