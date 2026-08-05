using System.Windows.Controls;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for NavigationService singleton service.
/// Validates navigation functionality, page registration, and history tracking.
/// </summary>
[CollectionDefinition("NavigationServiceSerialCollection", DisableParallelization = true)]
public sealed class NavigationServiceSerialCollection
{
}

[Collection("NavigationServiceSerialCollection")]
public sealed class NavigationServiceTests : IDisposable
{
    public NavigationServiceTests()
    {
        NavigationService.Instance.ResetForTests();
    }

    public void Dispose()
    {
        NavigationService.Instance.ResetForTests();
    }

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Arrange & Act
        var instance1 = NavigationService.Instance;
        var instance2 = NavigationService.Instance;

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2, "NavigationService should be a singleton");
    }

    [Fact]
    public void Initialize_WithValidFrame_ShouldSetFrame()
    {
        WpfTestThread.Run(() =>
        {
            // Arrange
            var service = NavigationService.Instance;
            var frame = new Frame();

            // Act
            service.Initialize(frame);

            // Assert
            service.IsInitialized.Should().BeTrue("frame was provided to Initialize");
            service.CanGoBack.Should().BeFalse("newly initialized frame should have no navigation history");
        });
    }

    [Fact]
    public void Initialize_WithNullFrame_ShouldThrowArgumentNullException()
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        Action act = () => service.Initialize(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("frame");
    }

    [Fact]
    public void IsPageRegistered_WithUnknownPage_ShouldReturnFalse()
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        var isRegistered = service.IsPageRegistered("NonExistentPage12345");

        // Assert
        isRegistered.Should().BeFalse("non-existent page should not be registered");
    }

    [Fact]
    public void CanGoBack_BeforeInitialization_ShouldReturnFalse()
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act & Assert – service is not yet initialized; both guards must hold
        service.IsInitialized.Should().BeFalse("Initialize has not been called yet");
        service.CanGoBack.Should().BeFalse("service without frame should not allow going back");
    }

    [Fact]
    public void NavigateTo_WithUnregisteredPageTag_ShouldReturnFalse()
    {
        WpfTestThread.Run(() =>
        {
            // Arrange
            var service = NavigationService.Instance;
            var frame = new Frame();
            service.Initialize(frame);

            // Act
            var result = service.NavigateTo("NonExistentPage");

            // Assert
            result.Should().BeFalse("navigation to unregistered page should fail");
        });
    }

    [Fact]
    public void GetRegisteredPages_ShouldReturnNonEmptyCollection()
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        var registeredPages = service.GetRegisteredPages();

        // Assert
        registeredPages.Should().NotBeNull();
        registeredPages.Should().NotBeEmpty("NavigationService should have registered pages");
    }

    [Fact]
    public void GetRegisteredPages_ShouldMatchShellNavigationCatalogIncludingAliases()
    {
        var service = NavigationService.Instance;

        var registeredPages = service.GetRegisteredPages().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var catalogPages = ShellNavigationCatalog.GetRegisteredPageTags().ToHashSet(StringComparer.OrdinalIgnoreCase);

        registeredPages.Should().BeEquivalentTo(catalogPages);
    }

    [Fact]
    public void CreatePageContent_WithWorkspaceScope_ShouldResolvePageFromScope()
    {
        WpfTestThread.Run(() =>
        {
            var service = NavigationService.Instance;
            var scopedPage = new WorkspaceCapabilityHomePage();
            var services = new ServiceCollection();
            services.AddSingleton(scopedPage);

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var content = service.CreatePageContent("PortfolioShell", workspaceScope: scope);

            content.Should().BeSameAs(scopedPage);
        });
    }

    [Fact]
    public void NavigateTo_WithWorkspaceScope_ShouldResolvePageFromScope()
    {
        WpfTestThread.Run(() =>
        {
            var service = NavigationService.Instance;
            var frame = new Frame();
            service.Initialize(frame);

            var rootPage = new WorkspaceCapabilityHomePage();
            var scopedPage = new WorkspaceCapabilityHomePage();

            var rootServices = new ServiceCollection();
            rootServices.AddSingleton<WorkspaceCapabilityHomePage>(rootPage);
            using var rootProvider = rootServices.BuildServiceProvider();
            service.SetServiceProvider(rootProvider);

            var scopedServices = new ServiceCollection();
            scopedServices.AddSingleton<WorkspaceCapabilityHomePage>(scopedPage);
            using var scopedProvider = scopedServices.BuildServiceProvider();
            using var scope = scopedProvider.CreateScope();

            service.CreatePageContent("PortfolioShell", workspaceScope: scope)
                .Should()
                .BeSameAs(scopedPage);

            var result = service.NavigateTo("PortfolioShell", workspaceScope: scope);

            result.Should().BeTrue();
            service.GetCurrentPageTag().Should().Be("PortfolioShell");

            service.CreatePageContent("PortfolioShell")
                .Should()
                .BeSameAs(rootPage);

            var fallbackResult = service.NavigateTo("PortfolioShell");

            fallbackResult.Should().BeTrue();
            service.GetCurrentPageTag().Should().Be("PortfolioShell");
        });
    }

    [Fact]
    public void IsPageRegistered_WithKnownPage_ShouldReturnTrue()
    {
        // Arrange
        var service = NavigationService.Instance;
        var registeredPages = service.GetRegisteredPages();
        var firstPage = registeredPages.FirstOrDefault();

        // Skip test if no pages are registered (shouldn't happen in production)
        if (firstPage == null)
        {
            return;
        }

        // Act
        var isRegistered = service.IsPageRegistered(firstPage);

        // Assert
        isRegistered.Should().BeTrue($"page '{firstPage}' should be registered");
    }

    [Theory]
    [InlineData("Backtest")]
    [InlineData("StrategyRuns")]
    [InlineData("RunDetail")]
    [InlineData("RunPortfolio")]
    [InlineData("RunLedger")]
    [InlineData("LeanIntegration")]
    [InlineData("PortfolioImport")]
    [InlineData("TradingHours")]
    public void IsPageRegistered_WorkflowPages_ShouldReturnTrue(string pageTag)
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        var isRegistered = service.IsPageRegistered(pageTag);

        // Assert
        isRegistered.Should().BeTrue($"workflow page '{pageTag}' must be registered so it is reachable from primary navigation");
    }

    [Theory]
    [InlineData("Dashboard")]
    [InlineData("LiveData")]
    [InlineData("Charts")]
    [InlineData("RunMat")]
    [InlineData("StrategyRuns")]
    [InlineData("OrderBook")]
    [InlineData("Watchlist")]
    public void IsPageRegistered_StrategySectionPages_ShouldReturnTrue(string pageTag)
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        var isRegistered = service.IsPageRegistered(pageTag);

        // Assert
        isRegistered.Should().BeTrue($"strategy section page '{pageTag}' must be registered and reachable");
    }

    [Theory]
    [InlineData("Provider")]
    [InlineData("ProviderHealth")]
    [InlineData("Symbols")]
    [InlineData("Backfill")]
    [InlineData("Storage")]
    [InlineData("DataExport")]
    [InlineData("PackageManager")]
    public void IsPageRegistered_DataSectionPages_ShouldReturnTrue(string pageTag)
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        var isRegistered = service.IsPageRegistered(pageTag);

        // Assert
        isRegistered.Should().BeTrue($"data section page '{pageTag}' must be registered and reachable");
    }

    [Theory]
    [InlineData("DataQuality")]
    [InlineData("SystemHealth")]
    [InlineData("Diagnostics")]
    [InlineData("Settings")]
    [InlineData("AdminMaintenance")]
    public void IsPageRegistered_SettingsSectionPages_ShouldReturnTrue(string pageTag)
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        var isRegistered = service.IsPageRegistered(pageTag);

        // Assert
        isRegistered.Should().BeTrue($"settings section page '{pageTag}' must be registered and reachable");
    }

    [Fact]
    public void GetPageType_WithRegisteredPage_ShouldReturnType()
    {
        // Arrange
        var service = NavigationService.Instance;
        var registeredPages = service.GetRegisteredPages();
        var firstPage = registeredPages.FirstOrDefault();

        // Skip test if no pages are registered
        if (firstPage == null)
        {
            return;
        }

        // Act
        var pageType = service.GetPageType(firstPage);

        // Assert
        pageType.Should().NotBeNull($"registered page '{firstPage}' should have a type");
    }

    [Theory]
    [InlineData("ResearchWorkspace", "StrategyShell")]
    [InlineData("ResearchShell", "StrategyShell")]
    [InlineData("DataOperationsShell", "DataShell")]
    [InlineData("GovernanceShell", "AccountingShell")]
    [InlineData("OperationsContinuity", "FundLedger")]
    [InlineData("OperationsClose", "FundLedger")]
    [InlineData("BacktestStudio", "Backtest")]
    [InlineData("RunBrowser", "StrategyRuns")]
    [InlineData("TradingWorkspace", "TradingShell")]
    [InlineData("Blotter", "PositionBlotter")]
    [InlineData("Providers", "Provider")]
    [InlineData("Alerts", "NotificationCenter")]
    [InlineData("Preferences", "Settings")]
    public void GetPageType_WithAlias_ShouldResolveCanonicalPageType(string alias, string canonicalPageTag)
    {
        var service = NavigationService.Instance;

        service.IsPageRegistered(alias).Should().BeTrue($"alias '{alias}' should be registered");
        service.GetPageType(alias).Should().Be(service.GetPageType(canonicalPageTag));
        ShellNavigationCatalog.GetCanonicalPageTag(alias).Should().Be(canonicalPageTag);
    }

    [Theory]
    [InlineData("ResearchShell", "StrategyShell")]
    [InlineData("DataOperationsShell", "DataShell")]
    [InlineData("GovernanceShell", "AccountingShell")]
    [InlineData("Blotter", "PositionBlotter")]
    public void NavigateTo_WithAlias_ShouldStoreCanonicalPageTag(string alias, string canonicalPageTag)
    {
        WpfTestThread.Run(() =>
        {
            var service = NavigationService.Instance;
            var frame = new Frame();
            service.Initialize(frame);
            string? navigatedPageTag = null;

            service.Navigated += (_, args) => navigatedPageTag = args.PageTag;

            var result = service.NavigateTo(alias);

            result.Should().BeTrue($"alias '{alias}' should remain routable");
            navigatedPageTag.Should().Be(canonicalPageTag);
            service.GetCurrentPageTag().Should().Be(canonicalPageTag);
            service.GetBreadcrumbs().Should().ContainSingle(entry => entry.PageTag == canonicalPageTag);
        });
    }

    [Fact]
    public void NavigateTo_WithOperationsCloseAlias_ShouldStoreFundLedgerAndKeepReportPackContext()
    {
        WpfTestThread.Run(() =>
        {
            var service = NavigationService.Instance;
            var frame = new Frame();
            service.Initialize(frame);
            object? navigatedParameter = null;

            service.Navigated += (_, args) => navigatedParameter = args.Parameter;

            var result = service.NavigateTo("OperationsClose");

            result.Should().BeTrue("OperationsClose should remain a WPF compatibility alias");
            service.GetCurrentPageTag().Should().Be("FundLedger");
            navigatedParameter.Should().BeOfType<FundOperationsNavigationContext>()
                .Which.Tab.Should().Be(FundOperationsTab.ReportPack);
        });
    }

    [Fact]
    public void NavigateTo_WithParameterizedEvidenceWorkbenchTarget_ShouldStoreEvidenceWorkbenchPage()
    {
        WpfTestThread.Run(() =>
        {
            var service = NavigationService.Instance;
            var frame = new Frame();
            service.Initialize(frame);
            string? navigatedPageTag = null;
            object? navigatedParameter = null;

            service.Navigated += (_, args) =>
            {
                navigatedPageTag = args.PageTag;
                navigatedParameter = args.Parameter;
            };

            var result = service.NavigateTo("EvidenceWorkbench:accounting-record/accounting-record-2026-05");

            result.Should().BeTrue("parameterized EvidenceWorkbench targets should route to the evidence workbench page");
            navigatedPageTag.Should().Be("EvidenceWorkbench");
            navigatedParameter.Should().BeOfType<string>()
                .Which.Should().Be("accounting-record/accounting-record-2026-05");
            service.GetCurrentPageTag().Should().Be("EvidenceWorkbench");
            service.GetBreadcrumbs().Should().ContainSingle(entry => entry.PageTag == "EvidenceWorkbench");
        });
    }

    [Fact]
    public void CreatePageContent_WithParameterizedEvidenceWorkbenchTarget_ShouldCreateEvidenceWorkbenchContent()
    {
        WpfTestThread.Run(() =>
        {
            var service = NavigationService.Instance;

            var content = service.CreatePageContent("EvidenceWorkbench:accounting-record/accounting-record-2026-05");

            content.Should().NotBeNull();
            ShellNavigationCatalog.GetCanonicalPageTag("EvidenceWorkbench:accounting-record/accounting-record-2026-05")
                .Should()
                .Be("EvidenceWorkbench");
        });
    }

    [Fact]
    public void GetPageType_WithUnregisteredPage_ShouldReturnNull()
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        var pageType = service.GetPageType("NonExistentPage12345");

        // Assert
        pageType.Should().BeNull("unregistered page should return null type");
    }

    [Fact]
    public void NavigateTo_WithValidPageTag_ShouldNavigateAndRaiseEvent()
    {
        WpfTestThread.Run(() =>
        {
            // Arrange
            var service = NavigationService.Instance;
            var frame = new Frame();
            service.Initialize(frame);

            var registeredPages = service.GetRegisteredPages();
            var pageTag = registeredPages.FirstOrDefault();

            // Skip test if no pages are registered
            if (pageTag == null)
            {
                return;
            }

            bool eventRaised = false;
            string? navigatedPageTag = null;

            service.Navigated += (sender, args) =>
            {
                eventRaised = true;
                navigatedPageTag = args.PageTag;
            };

            // Act
            var result = service.NavigateTo(pageTag);

            // Assert
            result.Should().BeTrue($"navigation to registered page '{pageTag}' should succeed");
            eventRaised.Should().BeTrue("Navigated event should be raised");
            navigatedPageTag.Should().Be(pageTag, "event should contain correct page tag");
        });
    }

    [Fact]
    public void GetBreadcrumbs_AfterNavigation_ShouldContainEntry()
    {
        WpfTestThread.Run(() =>
        {
            // Arrange
            var service = NavigationService.Instance;
            var frame = new Frame();
            service.Initialize(frame);

            var registeredPages = service.GetRegisteredPages();
            var pageTag = registeredPages.FirstOrDefault();

            // Skip test if no pages are registered
            if (pageTag == null)
            {
                return;
            }

            // Act
            service.NavigateTo(pageTag);
            var breadcrumbs = service.GetBreadcrumbs();

            // Assert
            breadcrumbs.Should().NotBeNull();
            breadcrumbs.Should().NotBeEmpty("breadcrumbs should contain navigation history");
            breadcrumbs.Should().Contain(b => b.PageTag == pageTag, "breadcrumbs should contain navigated page");
        });
    }
}
