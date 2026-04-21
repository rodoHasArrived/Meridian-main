using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Exceptions;
using Meridian.Infrastructure.Adapters.Robinhood;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for <see cref="RobinhoodHistoricalDataProvider"/>.
/// All tests use a stub HTTP handler — no real network calls are made.
/// </summary>
public sealed class RobinhoodHistoricalDataProviderTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static RobinhoodHistoricalDataProvider CreateSut(
        HttpMessageHandler handler,
        string accessToken = "test-token")
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.robinhood.com/")
        };
        return new RobinhoodHistoricalDataProvider(
            accessToken: accessToken,
            httpClient: client);
    }

    private static StringContent BuildSuccessResponse(string symbol, int barCount = 5)
    {
        var bars = Enumerable.Range(0, barCount).Select(i =>
        {
            var date = new DateTime(2024, 1, 2).AddDays(i).ToString("yyyy-MM-ddT05:00:00Z");
            return new
            {
                begins_at = date,
                open_price = "185.00",
                close_price = "186.00",
                high_price = "187.00",
                low_price = "184.00",
                volume = 1_000_000L,
                session = "reg",
                interpolated = false
            };
        }).ToArray();

        var payload = new
        {
            results = new[]
            {
                new
                {
                    symbol,
                    historicals = bars,
                    span = "year",
                    interval = "day",
                    bounds = "regular"
                }
            }
        };

        return new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
    }

    // ---------------------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetDailyBarsAsync_ValidSymbol_ReturnsBars()
    {
        // Arrange
        var handler = new StubHttpHandler(
            HttpStatusCode.OK,
            BuildSuccessResponse("AAPL", barCount: 5));

        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var result = await sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        // Assert
        result.Should().HaveCount(5);
        result[0].Symbol.Should().Be("AAPL");
        result[0].Open.Should().Be(185.00m);
        result[0].Close.Should().Be(186.00m);
    }

    [Fact]
    public async Task GetDailyBarsAsync_DateRange_FiltersClientSide()
    {
        // Arrange — 5 bars starting 2024-01-02; request only first 3
        var handler = new StubHttpHandler(
            HttpStatusCode.OK,
            BuildSuccessResponse("MSFT", barCount: 5));

        var sut = CreateSut(handler);
        var from = new DateOnly(2024, 1, 2);
        var to = new DateOnly(2024, 1, 4);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var result = await sut.GetDailyBarsAsync("MSFT", from, to, cts.Token);

        // Assert
        result.Should().HaveCount(3);
        result.All(b => b.SessionDate >= from && b.SessionDate <= to).Should().BeTrue();
    }

    [Fact]
    public async Task GetDailyBarsAsync_EmptyHistoricals_ReturnsEmptyList()
    {
        // Arrange
        var payload = new
        {
            results = new[]
            {
                new { symbol = "AAPL", historicals = Array.Empty<object>(), span = "year", interval = "day", bounds = "regular" }
            }
        };
        var handler = new StubHttpHandler(
            HttpStatusCode.OK,
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var result = await sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        // Assert
        result.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // Error handling
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetDailyBarsAsync_Unauthorized_ThrowsConnectionException()
    {
        // Arrange
        var handler = new StubHttpHandler(HttpStatusCode.Unauthorized, new StringContent("{}"));
        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var act = () => sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        // Assert
        await act.Should().ThrowAsync<ConnectionException>();
    }

    [Fact]
    public async Task GetDailyBarsAsync_ServerError_ThrowsDataProviderException()
    {
        // Arrange
        var handler = new StubHttpHandler(HttpStatusCode.InternalServerError, new StringContent("{}"));
        var sut = CreateSut(handler);

        // Act
        var act = () => sut.GetDailyBarsAsync("AAPL", null, null, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DataProviderException>();
    }

    [Fact]
    public async Task GetDailyBarsAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var handler = new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}"));
        var sut = CreateSut(handler);

        // Act
        var act = () => sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetDailyBarsAsync_NoToken_ThrowsConnectionException()
    {
        // Arrange — create provider with no token, bypassing env var
        var handler = new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}"));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.robinhood.com/") };
        var sut = new RobinhoodHistoricalDataProvider(accessToken: "", httpClient: client);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var act = () => sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        // Assert
        await act.Should().ThrowAsync<ConnectionException>()
            .WithMessage("*ROBINHOOD_ACCESS_TOKEN*");
    }

    // ---------------------------------------------------------------------------
    // Provider metadata
    // ---------------------------------------------------------------------------

    [Fact]
    public void Name_ReturnsRobinhood()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}"));
        var sut = CreateSut(handler);
        sut.Name.Should().Be("robinhood");
    }

    [Fact]
    public void Capabilities_SupportsDailyBarsForUs()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}"));
        var sut = CreateSut(handler);
        sut.Capabilities.SupportedMarkets.Should().Contain("US");
    }

    // ---------------------------------------------------------------------------
    // Stub handler
    // ---------------------------------------------------------------------------

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly Func<HttpContent> _contentFactory;

        public StubHttpHandler(HttpStatusCode statusCode, HttpContent singleContent)
            : this(statusCode, () =>
            {
                var raw = singleContent.ReadAsStringAsync().GetAwaiter().GetResult();
                return new StringContent(raw, System.Text.Encoding.UTF8, "application/json");
            })
        {
        }

        public StubHttpHandler(HttpStatusCode statusCode, Func<HttpContent> contentFactory)
        {
            _statusCode = statusCode;
            _contentFactory = contentFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = _contentFactory() });
        }
    }
}
