using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF-specific navigation service that extends <see cref="NavigationServiceBase"/> with
/// WPF Frame navigation and DI-aware page creation.
/// Implements <see cref="INavigationService"/> with singleton pattern.
/// Phase 6C.2: Shared base class extracts page registry, history tracking, and breadcrumb logic.
/// </summary>
public sealed class NavigationService : NavigationServiceBase, INavigationService
{
    private static readonly Lazy<NavigationService> _instance = new(() => new NavigationService());

    private Frame? _frame;
    private IServiceProvider? _serviceProvider;

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
        foreach (var page in ShellNavigationCatalog.Pages)
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
        WorkspaceChromePresentationMode presentationMode = WorkspaceChromePresentationMode.Docked)
    {
        var pageType = GetPageType(pageTag)
            ?? throw new InvalidOperationException($"Unknown page tag '{pageTag}'.");

        var effectiveParameter = TransformNavigationParameter(pageTag, pageType, parameter);
        return CreatePageContentCore(pageTag, pageType, effectiveParameter, presentationMode);
    }

    /// <inheritdoc />
    protected override bool NavigateToPageCore(string pageTag, Type pageType, object? parameter)
    {
        if (_frame == null) return false;

        try
        {
            var content = CreatePageContentCore(pageTag, pageType, parameter, WorkspaceChromePresentationMode.Standalone);
            var result = _frame.Navigate(content);

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
            if (tourService.IsTourActive) return;

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
        WorkspaceChromePresentationMode presentationMode)
    {
        var page = CreatePage(pageType);
        ApplyNavigationParameter(page, parameter);

        if (page is not FrameworkElement element)
        {
            throw new InvalidOperationException($"Page '{pageType.Name}' is not a FrameworkElement.");
        }

        if (_serviceProvider is not null &&
            page is Page wpfPage &&
            ShouldWrapWithWorkspaceChrome(pageTag, pageType))
        {
            return new WorkspaceDeepPageHostPage(
                this,
                _serviceProvider?.GetService<WorkspaceShellContextService>(),
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

        if (pageTag is "ResearchShell" or "TradingShell" or "DataOperationsShell" or "GovernanceShell")
        {
            return false;
        }

        return ShellNavigationCatalog.GetPage(pageTag) is not null;
    }

    private object CreatePage(Type pageType)
    {
        if (_serviceProvider != null)
        {
            return _serviceProvider.GetService(pageType) ?? ActivatorUtilities.CreateInstance(_serviceProvider, pageType);
        }

        try
        {
            return Activator.CreateInstance(pageType) ?? new Page();
        }
        catch (MissingMethodException)
        {
            // Page requires constructor injection but no DI container is available.
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
