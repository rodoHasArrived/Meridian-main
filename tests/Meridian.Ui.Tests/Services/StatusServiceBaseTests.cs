using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Ui.Services.Services;
using Xunit;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Concrete test implementation of StatusServiceBase.
/// </summary>
internal sealed class TestStatusService : StatusServiceBase
{
    private readonly HttpClient _httpClient;
    public string? LastLogMessage { get; private set; }

    public TestStatusService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    protected override HttpClient GetHttpClient() => _httpClient;

    protected override void LogInfo(string message, params (string key, string value)[] properties)
    {
        LastLogMessage = message;
    }
}

public sealed class StatusServiceBaseTests : IDisposable
{
    private readonly TestStatusService _sut;
    private readonly HttpClient _httpClient;

    public StatusServiceBaseTests()
    {
        // Use a handler that returns empty successful responses for any request
        var handler = new StubHttpMessageHandler();
        _httpClient = new HttpClient(handler);
        _sut = new TestStatusService(_httpClient);
    }

    public void Dispose()
    {
        _sut.StopLiveMonitoring();
        _httpClient.Dispose();
    }

    [Fact]
    public void CurrentStatus_DefaultsToReady()
    {
        _sut.CurrentStatus.Should().Be("Ready");
    }

    [Fact]
    public void UpdateStatus_ChangesCurrentStatus()
    {
        _sut.UpdateStatus("Processing");

        _sut.CurrentStatus.Should().Be("Processing");
    }

    [Fact]
    public void UpdateStatus_RaisesStatusChangedEvent()
    {
        StatusChangedEventArgs? received = null;
        _sut.StatusChanged += (_, e) => received = e;

        _sut.UpdateStatus("Busy");

        received.Should().NotBeNull();
        received!.PreviousStatus.Should().Be("Ready");
        received.NewStatus.Should().Be("Busy");
    }

    [Fact]
    public void UpdateStatus_DoesNotFireWhenSameStatus()
    {
        var eventCount = 0;
        _sut.StatusChanged += (_, _) => eventCount++;

        _sut.UpdateStatus("Ready"); // same as default

        eventCount.Should().Be(0);
    }

    [Fact]
    public void UpdateStatus_ThrowsOnNull()
    {
        var act = () => _sut.UpdateStatus(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SetBusy_SetsWorkingStatus()
    {
        _sut.SetBusy("Loading data");

        _sut.CurrentStatus.Should().Be("Working: Loading data...");
    }

    [Fact]
    public void SetReady_ResetsToReady()
    {
        _sut.SetBusy("something");
        _sut.SetReady();

        _sut.CurrentStatus.Should().Be("Ready");
    }

    [Fact]
    public void SetError_SetsErrorStatus()
    {
        _sut.SetError("Connection failed");

        _sut.CurrentStatus.Should().Be("Error: Connection failed");
    }

    [Fact]
    public void SetConnectionStatus_Connected()
    {
        _sut.SetConnectionStatus(true, "Alpaca");

        _sut.CurrentStatus.Should().Be("Connected to Alpaca");
    }

    [Fact]
    public void SetConnectionStatus_Disconnected()
    {
        _sut.SetConnectionStatus(false, "Polygon");

        _sut.CurrentStatus.Should().Be("Disconnected from Polygon");
    }

    [Fact]
    public void SetConnectionStatus_ConnectedNoProvider()
    {
        _sut.SetConnectionStatus(true);

        _sut.CurrentStatus.Should().Be("Connected");
    }

    [Fact]
    public void BaseUrl_DefaultsToLocalhost8080()
    {
        _sut.BaseUrl.Should().Be("http://localhost:8080");
    }

    [Fact]
    public void BaseUrl_CanBeChanged()
    {
        _sut.BaseUrl = "http://example.com:9090";

        _sut.BaseUrl.Should().Be("http://example.com:9090");
    }

    [Fact]
    public void IsDataStale_TrueWhenNoUpdate()
    {
        _sut.IsDataStale.Should().BeTrue();
    }

    [Fact]
    public void IsMonitoring_FalseByDefault()
    {
        _sut.IsMonitoring.Should().BeFalse();
    }

    [Fact]
    public void StartLiveMonitoring_SetsIsMonitoringTrue()
    {
        _sut.StartLiveMonitoring(60); // long interval so it doesn't tick too often

        _sut.IsMonitoring.Should().BeTrue();

        _sut.StopLiveMonitoring();
    }

    [Fact]
    public void StopLiveMonitoring_SetsIsMonitoringFalse()
    {
        _sut.StartLiveMonitoring(60);
        _sut.StopLiveMonitoring();

        _sut.IsMonitoring.Should().BeFalse();
    }

    [Fact]
    public void StatusChangedEventArgs_IncludesTimestamp()
    {
        var before = DateTime.UtcNow;

        var args = new StatusChangedEventArgs("A", "B");

        args.PreviousStatus.Should().Be("A");
        args.NewStatus.Should().Be("B");
        args.Timestamp.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void SecondsSinceLastUpdate_NullWhenNoUpdate()
    {
        _sut.SecondsSinceLastUpdate.Should().BeNull();
    }

    /// <summary>
    /// Stub HTTP handler for tests that returns empty responses.
    /// </summary>
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
