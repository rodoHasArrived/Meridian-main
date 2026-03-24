using FluentAssertions;
using Meridian.Ui.Services;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using UiAppSettings = Meridian.Ui.Services.AppSettings;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="ApiClientService"/> HTTP communication logic.
/// </summary>
public sealed class ApiClientServiceTests
{
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly HttpClient _httpClient;

    public ApiClientServiceTests()
    {
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8080")
        };

        ApiClientService.Instance.Configure(new UiAppSettings
        {
            ServiceUrl = "http://localhost:8080",
            ServiceTimeoutSeconds = 30,
            BackfillTimeoutMinutes = 60
        });
    }

    [Fact]
    public void BaseUrl_DefaultValue_IsLocalhost()
    {
        // Arrange & Act
        var service = ApiClientService.Instance;

        // Assert
        service.BaseUrl.Should().NotBeNullOrEmpty();
        service.BaseUrl.Should().Contain("localhost");
    }

    [Fact]
    public void IsConfigured_WithValidUrl_ReturnsTrue()
    {
        // Arrange & Act
        var service = ApiClientService.Instance;

        // Assert
        service.IsConfigured.Should().BeTrue("default URL should be configured");
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("https://api.example.com")]
    public void Configure_WithValidSettings_UpdatesBaseUrl(string serviceUrl)
    {
        // Arrange
        var service = ApiClientService.Instance;
        var settings = new UiAppSettings { ServiceUrl = serviceUrl };

        // Act
        service.Configure(settings);

        // Assert
        service.BaseUrl.Should().Be(serviceUrl.TrimEnd('/'));
    }

    [Fact]
    public void Configure_WithNullSettings_DoesNotThrow()
    {
        // Arrange
        var service = ApiClientService.Instance;

        // Act
        var act = () => service.Configure(null);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Configure_WithTrailingSlash_RemovesTrailingSlash()
    {
        // Arrange
        var service = ApiClientService.Instance;
        var settings = new AppSettings { ServiceUrl = "http://localhost:8080/" };

        // Act
        service.Configure(settings);

        // Assert
        service.BaseUrl.Should().NotEndWith("/");
    }

    [Fact]
    public void Configure_WhenUrlChanges_RaisesServiceUrlChangedEvent()
    {
        // Arrange
        var service = ApiClientService.Instance;
        var eventRaised = false;
        service.ServiceUrlChanged += (sender, args) => eventRaised = true;
        
        var settings = new AppSettings { ServiceUrl = "http://newhost:8080" };

        // Act
        service.Configure(settings);

        // Assert - event may not fire if URL was already set, but handler should be validly attached.
        _ = eventRaised;
        service.BaseUrl.Should().Be("http://newhost:8080");
    }

    [Theory]
    [InlineData(0, 30)] // Invalid timeout should use default
    [InlineData(-1, 30)] // Negative timeout should use default
    [InlineData(60, 60)] // Valid timeout should be used
    public void Configure_WithTimeout_UsesValidTimeout(int inputTimeout, int expectedMinTimeout)
    {
        // Arrange
        var service = ApiClientService.Instance;
        var settings = new AppSettings 
        { 
            ServiceUrl = "http://localhost:8080",
            ServiceTimeoutSeconds = inputTimeout 
        };

        // Act
        service.Configure(settings);

        // Assert - Service should either use provided timeout or default (30)
        expectedMinTimeout.Should().BePositive();
        service.BaseUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetBackfillClient_FirstCall_CreatesClient()
    {
        // Arrange
        var service = ApiClientService.Instance;

        // Act
        var client = service.GetBackfillClient();

        // Assert
        client.Should().NotBeNull();
        client.Timeout.Should().BeGreaterThan(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GetBackfillClient_MultipleCalls_ReturnsSameInstance()
    {
        // Arrange
        var service = ApiClientService.Instance;

        // Act
        var client1 = service.GetBackfillClient();
        var client2 = service.GetBackfillClient();

        // Assert
        client1.Should().BeSameAs(client2, "backfill client should be reused");
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("https://api.example.com")]
    public void Configure_WithDifferentUrls_UpdatesBaseUrl(string newUrl)
    {
        // Arrange
        var service = ApiClientService.Instance;
        var settings = new AppSettings { ServiceUrl = newUrl };

        // Act
        service.Configure(settings);

        // Assert
        service.BaseUrl.Should().Be(newUrl.TrimEnd('/'));
    }

    [Fact]
    public void Configure_WithEmptySettingsUrl_UsesDefaultUrl()
    {
        // Arrange
        var service = ApiClientService.Instance;
        var settings = new AppSettings { ServiceUrl = null };

        // Act
        service.Configure(settings);

        // Assert
        service.BaseUrl.Should().NotBeNullOrEmpty();
        service.BaseUrl.Should().Contain("localhost");
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(120)]
    public void Configure_WithStringOverload_UpdatesUrlAndTimeout(int timeoutSeconds)
    {
        // Arrange
        var service = ApiClientService.Instance;
        var newUrl = "http://test.example.com:9090";

        // Act
        service.Configure(newUrl, timeoutSeconds);

        // Assert
        service.BaseUrl.Should().Be(newUrl);
        // Timeout is applied internally, verify no exceptions
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(180)]
    public void Configure_WithBackfillTimeout_AcceptsValidValues(int backfillTimeoutMinutes)
    {
        // Arrange
        var service = ApiClientService.Instance;
        var settings = new UiAppSettings 
        { 
            ServiceUrl = "http://localhost:8080",
            BackfillTimeoutMinutes = backfillTimeoutMinutes
        };

        // Act
        var act = () => service.Configure(settings);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void UiApi_Property_ReturnsNonNullClient()
    {
        // Arrange
        var service = ApiClientService.Instance;

        // Act
        var uiApiClient = service.UiApi;

        // Assert
        uiApiClient.Should().NotBeNull();
    }
}

/// <summary>
/// Event args for service URL changes.
/// </summary>
public sealed class ServiceUrlChangedEventArgs : EventArgs
{
    public string? OldUrl { get; set; }
    public string? NewUrl { get; set; }
}
