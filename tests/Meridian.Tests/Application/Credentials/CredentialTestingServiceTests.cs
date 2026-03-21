using System.Net;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Config.Credentials;
using Moq;
using Moq.Protected;
using Xunit;

namespace Meridian.Tests.Credentials;

public class CredentialTestingServiceTests : IAsyncLifetime
{
    private readonly string _testDataRoot;
    private CredentialTestingService? _service;

    public CredentialTestingServiceTests()
    {
        _testDataRoot = Path.Combine(Path.GetTempPath(), "mdc_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDataRoot);
    }

    public Task InitializeAsync()
    {
        _service = new CredentialTestingService(_testDataRoot);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_service != null)
        {
            await _service.DisposeAsync();
        }

        if (Directory.Exists(_testDataRoot))
        {
            try
            { Directory.Delete(_testDataRoot, recursive: true); }
            catch { /* Ignore cleanup errors */ }
        }
    }

    [Fact]
    public async Task TestCredentialAsync_NotConfigured_ReturnsNotConfiguredStatus()
    {
        // Arrange & Act
        var result = await _service!.TestCredentialAsync("TestProvider", null, null);

        // Assert
        result.Status.Should().Be(CredentialAuthStatus.NotConfigured);
        result.Message.Should().Contain("not configured");
        result.ProviderName.Should().Be("TestProvider");
    }

    [Fact]
    public async Task TestCredentialAsync_EmptyApiKey_ReturnsNotConfiguredStatus()
    {
        // Arrange & Act
        var result = await _service!.TestCredentialAsync("TestProvider", "", null);

        // Assert
        result.Status.Should().Be(CredentialAuthStatus.NotConfigured);
    }

    [Fact]
    public async Task TestCredentialAsync_UnknownProvider_ReturnsUnknownStatus()
    {
        // Arrange & Act
        var result = await _service!.TestCredentialAsync("UnknownProvider", "some_key", null);

        // Assert
        result.Status.Should().Be(CredentialAuthStatus.Unknown);
        result.Message.Should().Contain("Unknown provider");
    }

    [Fact]
    public async Task TestCredentialAsync_SetsTestedAt()
    {
        // Arrange
        var beforeTest = DateTimeOffset.UtcNow;

        // Act
        var result = await _service!.TestCredentialAsync("TestProvider", "key");

        // Assert
        result.TestedAt.Should().BeOnOrAfter(beforeTest);
        result.TestedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task TestCredentialAsync_MasksCredential()
    {
        // Arrange & Act
        var result = await _service!.TestCredentialAsync("TestProvider", "ABCD1234EFGH5678", null, "Environment");

        // Assert
        result.CredentialMasked.Should().NotBeNullOrEmpty();
        result.CredentialMasked.Should().NotBe("ABCD1234EFGH5678");
        result.CredentialMasked.Should().StartWith("ABCD");
        result.CredentialMasked.Should().EndWith("5678");
        result.CredentialMasked.Should().Contain("*");
    }

    [Fact]
    public async Task TestCredentialAsync_TracksCredentialSource()
    {
        // Arrange & Act
        var result = await _service!.TestCredentialAsync("TestProvider", "key", null, "Environment");

        // Assert
        result.CredentialSource.Should().Be("Environment");
    }

    [Fact]
    public void GetCachedStatus_ReturnsNullForUnknownProvider()
    {
        // Arrange & Act
        var status = _service!.GetCachedStatus("NonExistentProvider");

        // Assert
        status.Should().BeNull();
    }

    [Fact]
    public async Task GetCachedStatus_ReturnsStatusAfterTest()
    {
        // Arrange
        await _service!.TestCredentialAsync("TestProvider", "some_key");

        // Act
        var status = _service.GetCachedStatus("TestProvider");

        // Assert
        status.Should().NotBeNull();
        status!.ProviderName.Should().Be("TestProvider");
    }

    [Fact]
    public async Task GetAllCachedStatuses_ReturnsAllTestedProviders()
    {
        // Arrange
        await _service!.TestCredentialAsync("Provider1", "key1");
        await _service!.TestCredentialAsync("Provider2", "key2");

        // Act
        var statuses = _service.GetAllCachedStatuses();

        // Assert
        statuses.Should().HaveCount(2);
        statuses.Should().ContainKey("Provider1");
        statuses.Should().ContainKey("Provider2");
    }

    [Fact]
    public void GetLastSuccessfulAuth_ReturnsNullForUntestedProvider()
    {
        // Arrange & Act
        var lastAuth = _service!.GetLastSuccessfulAuth("UntestedProvider");

        // Assert
        lastAuth.Should().BeNull();
    }

    [Fact]
    public async Task TestAllCredentialsAsync_TestsConfiguredProviders()
    {
        // Arrange
        var config = new AppConfig(
            Alpaca: new AlpacaOptions { KeyId = "test_key", SecretKey = "test_secret" }
        );

        // Act - This will fail network calls but we're testing the flow
        var summary = await _service!.TestAllCredentialsAsync(config);

        // Assert
        summary.Should().NotBeNull();
        summary.TestedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TestAllCredentialsAsync_SkipsUnconfiguredProviders()
    {
        // Arrange
        var config = new AppConfig(
            Alpaca: null,
            Backfill: new BackfillConfig(
                Providers: new BackfillProvidersConfig(
                    Polygon: new PolygonConfig(ApiKey: null),
                    Tiingo: new TiingoConfig(ApiToken: "")
                )
            )
        );

        // Act
        var summary = await _service!.TestAllCredentialsAsync(config);

        // Assert
        summary.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task TestCredentialAsync_InvokesOnCredentialTestedEvent()
    {
        // Arrange
        CredentialTestResult? receivedResult = null;
        _service!.OnCredentialTested += result => receivedResult = result;

        // Act
        await _service.TestCredentialAsync("TestProvider", "key");

        // Assert
        receivedResult.Should().NotBeNull();
        receivedResult!.ProviderName.Should().Be("TestProvider");
    }

    [Fact]
    public async Task TestCredentialAsync_AlpacaWithoutSecret_ReturnsNotConfigured()
    {
        // Arrange & Act
        var result = await _service!.TestCredentialAsync("Alpaca", "key_only", null);

        // Assert
        result.Status.Should().Be(CredentialAuthStatus.NotConfigured);
        result.Message.Should().Contain("Secret key is required");
    }

    [Fact]
    public async Task TestCredentialAsync_IncludesResponseTime()
    {
        // Arrange & Act
        var result = await _service!.TestCredentialAsync("UnknownProvider", "key");

        // Assert
        result.ResponseTimeMs.Should().NotBeNull();
        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
    }
}

public class CredentialTestingServiceWithMockedHttpTests : IAsyncLifetime
{
    private readonly string _testDataRoot;
    private readonly Mock<HttpMessageHandler> _mockHandler;
    private readonly HttpClient _httpClient;
    private CredentialTestingService? _service;

    public CredentialTestingServiceWithMockedHttpTests()
    {
        _testDataRoot = Path.Combine(Path.GetTempPath(), "mdc_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDataRoot);

        _mockHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHandler.Object) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public Task InitializeAsync()
    {
        _service = new CredentialTestingService(_testDataRoot, httpClient: _httpClient);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_service != null)
        {
            await _service.DisposeAsync();
        }
        _httpClient.Dispose();

        if (Directory.Exists(_testDataRoot))
        {
            try
            { Directory.Delete(_testDataRoot, recursive: true); }
            catch { /* Ignore cleanup errors */ }
        }
    }

    [Fact]
    public async Task TestAlpacaCredentials_ValidCredentials_ReturnsValid()
    {
        // Arrange
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.Host.Contains("alpaca")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\": \"ACTIVE\"}")
            });

        // Act
        var result = await _service!.TestCredentialAsync("Alpaca", "valid_key", "valid_secret");

        // Assert
        result.Status.Should().Be(CredentialAuthStatus.Valid);
        result.Message.Should().Contain("valid");
    }

    [Fact]
    public async Task TestAlpacaCredentials_InvalidCredentials_ReturnsInvalid()
    {
        // Arrange
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.Host.Contains("alpaca")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        // Act
        var result = await _service!.TestCredentialAsync("Alpaca", "invalid_key", "invalid_secret");

        // Assert
        result.Status.Should().Be(CredentialAuthStatus.Invalid);
    }

    [Fact]
    public async Task TestPolygonCredentials_ValidCredentials_ReturnsValid()
    {
        // Arrange
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.Host.Contains("polygon")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"results\": []}")
            });

        // Act
        var result = await _service!.TestCredentialAsync("Polygon", "valid_key");

        // Assert
        result.Status.Should().Be(CredentialAuthStatus.Valid);
    }

    [Fact]
    public async Task TestTiingoCredentials_ValidCredentials_ReturnsValid()
    {
        // Arrange
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.Host.Contains("tiingo")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\": \"OK\"}")
            });

        // Act
        var result = await _service!.TestCredentialAsync("Tiingo", "valid_token");

        // Assert
        result.Status.Should().Be(CredentialAuthStatus.Valid);
    }

    [Fact]
    public async Task TestCredential_NetworkError_ReturnsTestFailed()
    {
        // Arrange
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _service!.TestCredentialAsync("Polygon", "key");

        // Assert
        result.Status.Should().Be(CredentialAuthStatus.TestFailed);
        result.Message.Should().Contain("Network error");
    }

    [Fact]
    public async Task TestCredential_Timeout_ReturnsTestFailed()
    {
        // Arrange
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Request timed out"));

        // Act
        var result = await _service!.TestCredentialAsync("Polygon", "key");

        // Assert
        result.Status.Should().Be(CredentialAuthStatus.TestFailed);
        result.Message.Should().Contain("timed out");
    }
}
