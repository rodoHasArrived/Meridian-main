using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for NavigationService singleton service.
/// Validates navigation functionality, page registration, and history tracking.
/// </summary>
public sealed class NavigationServiceTests
{
    /// <summary>
    /// Runs an action on a dedicated STA thread. Required for tests that create WPF UI objects.
    /// </summary>
    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        captured?.Throw();
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
        RunOnSta(() =>
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
        RunOnSta(() =>
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
    [InlineData("LeanIntegration")]
    [InlineData("PortfolioImport")]
    [InlineData("TradingHours")]
    public void IsPageRegistered_TradingWorkflowPages_ShouldReturnTrue(string pageTag)
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        var isRegistered = service.IsPageRegistered(pageTag);

        // Assert
        isRegistered.Should().BeTrue($"trading workflow page '{pageTag}' must be registered so it is reachable from primary navigation");
    }

    [Theory]
    [InlineData("Dashboard")]
    [InlineData("LiveData")]
    [InlineData("Charts")]
    [InlineData("OrderBook")]
    [InlineData("Watchlist")]
    public void IsPageRegistered_ResearchSectionPages_ShouldReturnTrue(string pageTag)
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        var isRegistered = service.IsPageRegistered(pageTag);

        // Assert
        isRegistered.Should().BeTrue($"research section page '{pageTag}' must be registered and reachable");
    }

    [Theory]
    [InlineData("Provider")]
    [InlineData("Symbols")]
    [InlineData("Backfill")]
    [InlineData("Storage")]
    [InlineData("DataExport")]
    [InlineData("PackageManager")]
    public void IsPageRegistered_DataOpsSectionPages_ShouldReturnTrue(string pageTag)
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        var isRegistered = service.IsPageRegistered(pageTag);

        // Assert
        isRegistered.Should().BeTrue($"data ops section page '{pageTag}' must be registered and reachable");
    }

    [Theory]
    [InlineData("DataQuality")]
    [InlineData("ProviderHealth")]
    [InlineData("SystemHealth")]
    [InlineData("Diagnostics")]
    [InlineData("Settings")]
    [InlineData("AdminMaintenance")]
    public void IsPageRegistered_GovernanceSectionPages_ShouldReturnTrue(string pageTag)
    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        var isRegistered = service.IsPageRegistered(pageTag);

        // Assert
        isRegistered.Should().BeTrue($"governance section page '{pageTag}' must be registered and reachable");
    }

    {
        // Arrange
        var service = NavigationService.Instance;

        // Act
        var isRegistered = service.IsPageRegistered("NonExistentPage12345");

        // Assert
        isRegistered.Should().BeFalse("non-existent page should not be registered");
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
        RunOnSta(() =>
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
        RunOnSta(() =>
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
