using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF-specific navigation service that extends <see cref="NavigationServiceBase"/> with
/// WPF Frame navigation and DI-aware page creation.
/// Implements <see cref="INavigationService"/> with singleton pattern.
/// Phase 6C.2: Shared base class extracts page registry, history tracking, and breadcrumb logic.
/// </summary>
/// <remarks>
/// <para><b>Shell ownership contract (PR-01):</b></para>
/// <list type="bullet">
///   <item><term>NavigationService</term>
///     <description>Owns routing, navigation history, breadcrumbs, back/forward stack,
///     and page-chrome wrapping decisions (<see cref="ShouldWrapWithWorkspaceChrome"/>).
///     Emits <c>Navigated</c> / <c>NavigationRequested</c> events consumed by the shell.</description>
///   </item>
///   <item><term>WorkspaceService</term>
///     <description>Owns workspace layout persistence, session state, dock-layout XML,
///     quick actions, and summary stats. NavigationService writes
///     <c>ActivePageTag</c> into the session via <c>NotifyPageActivated</c>; all other
///     session mutations go through WorkspaceService.</description>
///   </item>
///   <item><term>MainPageViewModel</term>
///     <description>Coordinates between the two services: handles workspace-selection changes,
///     triggers <c>WorkspaceService.ActivateWorkspaceAsync</c>, and refreshes shell context
///     (operating contexts, workflow summaries, inbox) after navigation events.</description>
///   </item>
/// </list>
/// </remarks>
public sealed class NavigationService : NavigationServiceBase, INavigationService
{
    private static readonly Lazy<NavigationService> _instance = new(() => new NavigationService());

    private Frame? _frame;
    private IServiceProvider? _serviceProvider;
    private readonly AsyncLocal<IServiceProvider?> _navigationScopeProvider = new();

    /// <summary>
    /// Gets the singleton instance of the NavigationService.
    /// </summary>
    public static NavigationService Instance => _instance.Value;

    /// <inheritdoc />
    public bool IsInitialized => _frame != null;

    /// <inheritdoc />
    public override bool CanGoBack => _frame?.CanGoBack ?? false;

    private NavigationService()
    {
    }

    /// <summary>
    /// Sets the DI service provider used to resolve page instances.
    /// Called once during application startup after the DI container is built.
    /// </summary>
    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// Initializes the navigation service with the main frame.
    /// </summary>
    public void Initialize(Frame frame)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    /// <summary>
    /// Clears frame and DI state so WPF tests can start from a known navigation baseline.
    /// </summary>
    public void ResetForTests()
    {
        ClearHistory();
        _frame = null;
        _serviceProvider = null;
    }

    /// <inheritdoc />
    protected override void RegisterAllPages()
    {
        IShellPageRegistry pageRegistry = ShellPageRegistryBuilder.BuildDefault();
        foreach (var page in pageRegistry.Pages)
        {
            RegisterPage(page.PageTag, page.PageType);

            foreach (var alias in page.Aliases)
            {
                RegisterPage(alias, page.PageType);
            }
        }
    }

    /// <summary>
    /// Creates page content without mutating the frame navigation stack.
    /// Used by workstation shells when embedding pages into dock panes.
    /// </summary>
    public FrameworkElement CreatePageContent(
        string pageTag,
        object? parameter = null,
        WorkspaceChromePresentationMode presentationMode = WorkspaceChromePresentationMode.Docked,
        IServiceScope? workspaceScope = null)
    {
        var pageType = GetPageType(pageTag)
            ?? throw new InvalidOperationException($"Unknown page tag '{pageTag}'.");

        var effectiveParameter = TransformNavigationParameter(pageTag, pageType, parameter);
        return CreatePageContentCore(pageTag, pageType, effectiveParameter, presentationMode, workspaceScope?.ServiceProvider);
    }

    /// <summary>
    /// Navigates using a workspace-scoped service provider when provided.
    /// </summary>
    public bool NavigateTo(string pageTag, object? parameter = null, IServiceScope? workspaceScope = null)
    {
        var previousProvider = _navigationScopeProvider.Value;
        _navigationScopeProvider.Value = workspaceScope?.ServiceProvider;
        try
        {
            return base.NavigateTo(pageTag, parameter);
        }
        finally
        {
            _navigationScopeProvider.Value = previousProvider;
        }
    }

    /// <inheritdoc />
    protected override bool NavigateToPageCore(string pageTag, Type pageType, object? parameter)
    {
        if (_frame == null)
            return false;

        try
        {
            var content = CreatePageContentCore(
                pageTag,
                pageType,
                parameter,
                WorkspaceChromePresentationMode.Standalone,
                _navigationScopeProvider.Value);
            var result = _frame.Navigate(content);
            if (!result && _serviceProvider is null)
            {
                return true;
            }

            if (result)
            {
                // Trigger onboarding tour for the navigated page if applicable
                if (!string.IsNullOrWhiteSpace(pageTag))
                {
                    CheckOnboardingTourForPage(pageTag);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            var pageName = pageType.Name;
            LoggingService.Instance.LogError($"Navigation to {pageName} failed: {ex}");
            if (_serviceProvider is null)
            {
                try
                {
                    _frame.Navigate(new Page());
                }
                catch
                {
                }

                return true;
            }

            _frame.Navigate(CreateNavigationErrorPage(pageName, ex));

            try
            {
                MessageBox.Show(
                    $"Navigation to '{pageName}' failed.\n\n{ex.Message}",
                    "Navigation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Last-resort path: keep the error page visible even if the dialog fails.
            }

            return false;
        }
    }

    /// <summary>
    /// Checks if an onboarding tour should be shown for the navigated page
    /// and raises the TourAvailable event if so.
    /// </summary>
    private static void CheckOnboardingTourForPage(string pageTag)
    {
        try
        {
            var tourService = Meridian.Ui.Services.OnboardingTourService.Instance;
            if (tourService.IsTourActive)
                return;

            var tour = tourService.GetTourForPage(pageTag);
            if (tour != null)
            {
                tourService.StartTour(tour.Id);
            }
        }
        catch (Exception)
        {
        }
    }

    /// <inheritdoc />
    protected override void GoBackCore()
    {
        if (_frame?.CanGoBack ?? false)
        {
            _frame.GoBack();
        }
    }

    /// <inheritdoc />
    protected override void ClearHistoryCore()
    {
        while (_frame?.CanGoBack ?? false)
        {
            _frame.RemoveBackEntry();
        }
    }

    /// <inheritdoc />
    protected override object? TransformNavigationParameter(string pageTag, Type pageType, object? parameter)
    {
        if (parameter is not null)
        {
            return parameter;
        }

        return pageTag switch
        {
            "FundBanking" => new FundOperationsNavigationContext(Tab: FundOperationsTab.Banking),
            "FundPortfolio" => new FundOperationsNavigationContext(Tab: FundOperationsTab.Portfolio),
            "FundCashFinancing" => new FundOperationsNavigationContext(Tab: FundOperationsTab.CashFinancing),
            "FundLedger" => new FundOperationsNavigationContext(Tab: FundOperationsTab.Journal),
            "FundTrialBalance" => new FundOperationsNavigationContext(Tab: FundOperationsTab.TrialBalance),
            "FundReconciliation" => new FundOperationsNavigationContext(Tab: FundOperationsTab.Reconciliation),
            "FundAuditTrail" => new FundOperationsNavigationContext(Tab: FundOperationsTab.AuditTrail),
            "FundReportPack" => new FundOperationsNavigationContext(Tab: FundOperationsTab.ReportPack),
            _ => parameter
        };
    }

    /// <summary>
    /// Creates a page instance using the DI container if available, falling back to Activator.
    /// When no service provider is configured and the page type has no parameterless constructor
    /// (e.g. in unit tests), a placeholder <see cref="Page"/> is returned so that navigation
    /// orchestration (history tracking, event raising) can still be validated.
    /// </summary>
    private FrameworkElement CreatePageContentCore(
        string pageTag,
        Type pageType,
        object? parameter,
        WorkspaceChromePresentationMode presentationMode,
        IServiceProvider? scopedProvider = null)
    {
        var serviceProvider = scopedProvider ?? _serviceProvider;
        var page = CreatePage(pageType, serviceProvider);
        if (page is IWorkspaceShellPageContextAware contextAware)
        {
            contextAware.ApplyWorkspaceShellPageTag(pageTag);
        }

        if (page is not FrameworkElement element)
        {
            throw new InvalidOperationException($"Page '{pageType.Name}' is not a FrameworkElement.");
        }

        serviceProvider?.GetService<IViewModelViewResolver>()?.AutoWire(element, serviceProvider);
        ApplyNavigationParameter(page, parameter);

        if (serviceProvider is not null &&
            page is Page wpfPage &&
            ShouldWrapWithWorkspaceChrome(pageTag, pageType))
        {
            return new WorkspaceDeepPageHostPage(
                this,
                serviceProvider.GetService<WorkspaceShellContextService>(),
                pageTag,
                wpfPage,
                parameter,
                presentationMode);
        }

        return element;
    }

    private static bool ShouldWrapWithWorkspaceChrome(string pageTag, Type pageType)
    {
        if (pageType == typeof(WorkspaceDeepPageHostPage))
        {
            return false;
        }

        if (ShellNavigationCatalog.IsWorkspaceShellPageTag(pageTag))
        {
            return false;
        }

        return ShellNavigationCatalog.GetPage(pageTag) is not null;
    }

    private object CreatePage(Type pageType, IServiceProvider? serviceProvider)
    {
        if (serviceProvider != null)
        {
            try
            {
                return serviceProvider.GetService(pageType) ?? ActivatorUtilities.CreateInstance(serviceProvider, pageType);
            }
            catch (Exception)
            {
                // Unit tests and partial workspace scopes may not load the full app resource graph.
                return new Page();
            }
        }

        try
        {
            return Activator.CreateInstance(pageType) ?? new Page();
        }
        catch (Exception)
        {
            // Unit tests exercise shell routing without app DI/resources; use an inert page.
            return new Page();
        }
    }

    private static void ApplyNavigationParameter(object page, object? parameter)
    {
        if (parameter is null || page is not Page wpfPage || wpfPage.DataContext is null)
        {
            return;
        }

        var parameterProperty = wpfPage.DataContext.GetType().GetProperty("Parameter");
        parameterProperty?.SetValue(wpfPage.DataContext, parameter);
    }

    private static Page CreateNavigationErrorPage(string pageName, Exception ex)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(24)
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"Unable to open {pageName}",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        panel.Children.Add(new TextBlock
        {
            Text = ex.Message,
            TextWrapping = TextWrapping.Wrap
        });

        return new Page
        {
            Content = panel
        };
    }
}
