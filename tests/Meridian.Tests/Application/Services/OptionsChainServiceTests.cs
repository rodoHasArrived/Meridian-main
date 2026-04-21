using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.Services;

/// <summary>
/// Unit tests for the OptionsChainService application service.
/// Tests chain fetching, expiration filtering, caching, and provider interaction.
/// </summary>
public sealed class OptionsChainServiceTests
{
    private readonly TestMarketEventPublisher _publisher = new();
    private readonly OptionDataCollector _collector;
    private readonly Mock<IOptionsChainProvider> _providerMock;
    private readonly ILogger<OptionsChainService> _logger;

    public OptionsChainServiceTests()
    {
        _collector = new OptionDataCollector(_publisher);
        _providerMock = new Mock<IOptionsChainProvider>();
        _logger = NullLogger<OptionsChainService>.Instance;
    }

    private OptionsChainService CreateService(IOptionsChainProvider? provider = null)
    {
        return new OptionsChainService(_collector, _logger, provider);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullCollector_ThrowsArgumentNullException()
    {
        var act = () => new OptionsChainService(null!, _logger);
        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("collector");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new OptionsChainService(_collector, null!);
        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("logger");
    }

    [Fact]
    public void IsProviderAvailable_WhenNoProvider_ReturnsFalse()
    {
        var sut = CreateService(provider: null);
        sut.IsProviderAvailable.Should().BeFalse();
    }

    [Fact]
    public void IsProviderAvailable_WhenProviderPresent_ReturnsTrue()
    {
        var sut = CreateService(_providerMock.Object);
        sut.IsProviderAvailable.Should().BeTrue();
    }

    [Fact]
    public void GetProviderStatus_WhenNoProvider_ReturnsUnavailable()
    {
        var sut = CreateService(provider: null);

        var status = sut.GetProviderStatus();

        status.ProviderId.Should().BeNull();
        status.ProviderDisplayName.Should().BeNull();
        status.Mode.Should().Be("Unavailable");
        status.IsFallback.Should().BeFalse();
        status.Message.Should().Be("No options provider configured.");
    }

    [Fact]
    public void GetProviderStatus_WhenSyntheticProvider_ReturnsFallback()
    {
        var sut = CreateService(new StubOptionsChainProvider("synthetic", "Synthetic Options"));

        var status = sut.GetProviderStatus();

        status.ProviderId.Should().Be("synthetic");
        status.ProviderDisplayName.Should().Be("Synthetic Options");
        status.Mode.Should().Be("Fallback");
        status.IsFallback.Should().BeTrue();
        status.Message.Should().Be("Synthetic Options fallback is active.");
    }

    [Fact]
    public void GetProviderStatus_WhenConfiguredProviderPresent_ReturnsConfigured()
    {
        var sut = CreateService(new StubOptionsChainProvider("robinhood", "Robinhood Options"));

        var status = sut.GetProviderStatus();

        status.ProviderId.Should().Be("robinhood");
        status.ProviderDisplayName.Should().Be("Robinhood Options");
        status.Mode.Should().Be("Configured");
        status.IsFallback.Should().BeFalse();
        status.Message.Should().Be("Robinhood Options is configured.");
    }

    #endregion

    #region Input Validation Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetExpirationsAsync_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        var sut = CreateService(_providerMock.Object);
        var act = () => sut.GetExpirationsAsync(symbol!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetStrikesAsync_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        var sut = CreateService(_providerMock.Object);
        var act = () => sut.GetStrikesAsync(symbol!, new DateOnly(2026, 3, 21));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FetchChainSnapshotAsync_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        var sut = CreateService(_providerMock.Object);
        var act = () => sut.FetchChainSnapshotAsync(symbol!, new DateOnly(2026, 3, 21));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task FetchOptionQuoteAsync_WithNullContract_ThrowsArgumentNullException()
    {
        var sut = CreateService(_providerMock.Object);
        var act = () => sut.FetchOptionQuoteAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetCachedChain_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        var sut = CreateService(_providerMock.Object);
        var act = () => sut.GetCachedChain(symbol!, new DateOnly(2026, 3, 21));
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetCachedChainsForUnderlying_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        var sut = CreateService(_providerMock.Object);
        var act = () => sut.GetCachedChainsForUnderlying(symbol!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetQuotesForUnderlying_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        var sut = CreateService(_providerMock.Object);
        var act = () => sut.GetQuotesForUnderlying(symbol!);
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region GetExpirationsAsync Tests

    [Fact]
    public async Task GetExpirationsAsync_WithNoProvider_ReturnsEmpty()
    {
        var sut = CreateService(provider: null);

        var result = await sut.GetExpirationsAsync("AAPL");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetExpirationsAsync_WithProvider_ReturnsExpirations()
    {
        var expirations = new[] { new DateOnly(2026, 3, 21), new DateOnly(2026, 4, 18) };
        _providerMock.Setup(p => p.GetExpirationsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expirations);

        var sut = CreateService(_providerMock.Object);
        var result = await sut.GetExpirationsAsync("AAPL");

        result.Should().HaveCount(2);
        result[0].Should().Be(new DateOnly(2026, 3, 21));
    }

    #endregion

    #region GetStrikesAsync Tests

    [Fact]
    public async Task GetStrikesAsync_WithNoProvider_ReturnsEmpty()
    {
        var sut = CreateService(provider: null);

        var result = await sut.GetStrikesAsync("AAPL", new DateOnly(2026, 3, 21));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStrikesAsync_WithProvider_ReturnsStrikes()
    {
        var strikes = new[] { 145m, 150m, 155m, 160m };
        _providerMock.Setup(p => p.GetStrikesAsync("AAPL", new DateOnly(2026, 3, 21), It.IsAny<CancellationToken>()))
            .ReturnsAsync(strikes);

        var sut = CreateService(_providerMock.Object);
        var result = await sut.GetStrikesAsync("AAPL", new DateOnly(2026, 3, 21));

        result.Should().HaveCount(4);
    }

    #endregion

    #region FetchChainSnapshotAsync Tests

    [Fact]
    public async Task FetchChainSnapshotAsync_WithNoProvider_ReturnsNull()
    {
        var sut = CreateService(provider: null);

        var result = await sut.FetchChainSnapshotAsync("AAPL", new DateOnly(2026, 3, 21));

        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchChainSnapshotAsync_WithProvider_ReturnsChainAndCaches()
    {
        var chain = CreateChainSnapshot("AAPL", new DateOnly(2026, 3, 21));
        _providerMock.Setup(p => p.GetChainSnapshotAsync("AAPL", new DateOnly(2026, 3, 21), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chain);

        var sut = CreateService(_providerMock.Object);
        var result = await sut.FetchChainSnapshotAsync("AAPL", new DateOnly(2026, 3, 21));

        result.Should().NotBeNull();
        result!.UnderlyingSymbol.Should().Be("AAPL");

        // Verify it was cached via collector
        var cached = sut.GetCachedChain("AAPL", new DateOnly(2026, 3, 21));
        cached.Should().NotBeNull();
    }

    [Fact]
    public async Task FetchChainSnapshotAsync_PassesStrikeRange()
    {
        var chain = CreateChainSnapshot("AAPL", new DateOnly(2026, 3, 21));
        _providerMock.Setup(p => p.GetChainSnapshotAsync("AAPL", new DateOnly(2026, 3, 21), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chain);

        var sut = CreateService(_providerMock.Object);
        await sut.FetchChainSnapshotAsync("AAPL", new DateOnly(2026, 3, 21), strikeRange: 10);

        _providerMock.Verify(p => p.GetChainSnapshotAsync("AAPL", new DateOnly(2026, 3, 21), 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region FetchOptionQuoteAsync Tests

    [Fact]
    public async Task FetchOptionQuoteAsync_WithNoProvider_ReturnsNull()
    {
        var sut = CreateService(provider: null);
        var contract = CreateContract("AAPL", 150m, OptionRight.Call);

        var result = await sut.FetchOptionQuoteAsync(contract);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchOptionQuoteAsync_WithProvider_ReturnsQuoteAndRoutesToCollector()
    {
        var contract = CreateContract("AAPL", 150m, OptionRight.Call);
        var quote = CreateOptionQuote(contract);

        _providerMock.Setup(p => p.GetOptionQuoteAsync(contract, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quote);

        var sut = CreateService(_providerMock.Object);
        var result = await sut.FetchOptionQuoteAsync(contract);

        result.Should().NotBeNull();
        result!.BidPrice.Should().Be(quote.BidPrice);

        // Verify the quote was routed through collector (published as event)
        _publisher.PublishedEvents.Should().HaveCount(1);
    }

    #endregion

    #region FetchConfiguredChainsAsync Tests

    [Fact]
    public async Task FetchConfiguredChainsAsync_WhenDisabled_ReturnsEmpty()
    {
        var config = new DerivativesConfig(Enabled: false);
        var sut = CreateService(_providerMock.Object);

        var result = await sut.FetchConfiguredChainsAsync(config);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchConfiguredChainsAsync_WithNullConfig_ThrowsArgumentNullException()
    {
        var sut = CreateService(_providerMock.Object);

        var act = () => sut.FetchConfiguredChainsAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task FetchConfiguredChainsAsync_FetchesChainsForAllUnderlyings()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expiry = today.AddDays(30);

        var config = new DerivativesConfig(
            Enabled: true,
            Underlyings: new[] { "AAPL", "SPY" },
            MaxDaysToExpiration: 90);

        _providerMock.Setup(p => p.GetExpirationsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { expiry });
        _providerMock.Setup(p => p.GetExpirationsAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { expiry });

        _providerMock.Setup(p => p.GetChainSnapshotAsync(It.IsAny<string>(), expiry, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sym, DateOnly exp, int? sr, CancellationToken _) => CreateChainSnapshot(sym, exp));

        var sut = CreateService(_providerMock.Object);
        var result = await sut.FetchConfiguredChainsAsync(config);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task FetchConfiguredChainsAsync_FiltersExpiredExpirations()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pastExpiry = today.AddDays(-5);
        var futureExpiry = today.AddDays(30);

        var config = new DerivativesConfig(
            Enabled: true,
            Underlyings: new[] { "AAPL" },
            MaxDaysToExpiration: 90);

        _providerMock.Setup(p => p.GetExpirationsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pastExpiry, futureExpiry });

        _providerMock.Setup(p => p.GetChainSnapshotAsync("AAPL", futureExpiry, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateChainSnapshot("AAPL", futureExpiry));

        var sut = CreateService(_providerMock.Object);
        var result = await sut.FetchConfiguredChainsAsync(config);

        result.Should().HaveCount(1);
        result[0].Expiration.Should().Be(futureExpiry);
    }

    #endregion

    #region Cache Query Tests

    [Fact]
    public void GetCachedChainsForUnderlying_ReturnsAllCachedChains()
    {
        var sut = CreateService(_providerMock.Object);

        // Directly add via collector
        _collector.OnChainSnapshot(CreateChainSnapshot("AAPL", new DateOnly(2026, 3, 21)));
        _collector.OnChainSnapshot(CreateChainSnapshot("AAPL", new DateOnly(2026, 4, 18)));

        var result = sut.GetCachedChainsForUnderlying("AAPL");
        result.Should().HaveCount(2);
    }

    [Fact]
    public void GetQuotesForUnderlying_ReturnsAllQuotes()
    {
        var sut = CreateService(_providerMock.Object);

        _collector.OnOptionQuote(CreateOptionQuote("AAPL", 150m, OptionRight.Call));
        _collector.OnOptionQuote(CreateOptionQuote("AAPL", 155m, OptionRight.Put));

        var result = sut.GetQuotesForUnderlying("AAPL");
        result.Should().HaveCount(2);
    }

    [Fact]
    public void GetSummary_ReturnsAccurateSummary()
    {
        var sut = CreateService(_providerMock.Object);

        _collector.OnOptionQuote(CreateOptionQuote("AAPL", 150m, OptionRight.Call));
        _collector.OnChainSnapshot(CreateChainSnapshot("AAPL", new DateOnly(2026, 3, 21)));

        var summary = sut.GetSummary();
        summary.TrackedContracts.Should().Be(1);
        summary.TrackedChains.Should().Be(1);
    }

    [Fact]
    public void GetTrackedUnderlyings_ReturnsAllUnderlyings()
    {
        var sut = CreateService(_providerMock.Object);

        _collector.OnOptionQuote(CreateOptionQuote("AAPL", 150m, OptionRight.Call));
        _collector.OnChainSnapshot(CreateChainSnapshot("SPY", new DateOnly(2026, 3, 21)));

        var result = sut.GetTrackedUnderlyings();
        result.Should().HaveCount(2);
        result.Should().Contain("AAPL");
        result.Should().Contain("SPY");
    }

    #endregion

    #region Helpers

    private static OptionContractSpec CreateContract(string underlying, decimal strike, OptionRight right)
    {
        return new OptionContractSpec(
            UnderlyingSymbol: underlying,
            Strike: strike,
            Expiration: new DateOnly(2026, 3, 21),
            Right: right,
            Style: OptionStyle.American,
            Multiplier: 100,
            Exchange: "SMART",
            Currency: "USD");
    }

    private static OptionQuote CreateOptionQuote(string underlying, decimal strike, OptionRight right)
    {
        var contract = CreateContract(underlying, strike, right);
        return CreateOptionQuote(contract);
    }

    private static OptionQuote CreateOptionQuote(OptionContractSpec contract)
    {
        return new OptionQuote(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: $"{contract.UnderlyingSymbol}260321C{contract.Strike:00000000}",
            Contract: contract,
            BidPrice: 2.5m,
            AskPrice: 2.6m,
            BidSize: 50,
            AskSize: 60,
            UnderlyingPrice: 155m);
    }

    private static OptionChainSnapshot CreateChainSnapshot(string underlying, DateOnly expiry)
    {
        var strikes = new[] { 145m, 150m, 155m };
        var calls = strikes.Select(s =>
        {
            var c = new OptionContractSpec(underlying, s, expiry, OptionRight.Call, OptionStyle.American, 100, "SMART", "USD");
            return new OptionQuote(DateTimeOffset.UtcNow, $"{underlying}C{s}", c, 2.5m, 50, 2.6m, 60, 155m);
        }).ToList();
        var puts = strikes.Select(s =>
        {
            var c = new OptionContractSpec(underlying, s, expiry, OptionRight.Put, OptionStyle.American, 100, "SMART", "USD");
            return new OptionQuote(DateTimeOffset.UtcNow, $"{underlying}P{s}", c, 1.5m, 40, 1.6m, 50, 155m);
        }).ToList();

        return new OptionChainSnapshot(DateTimeOffset.UtcNow, underlying, 155m, expiry, strikes, calls, puts);
    }

    #endregion

    private sealed class StubOptionsChainProvider(string providerId, string providerDisplayName) : IOptionsChainProvider
    {
        public string ProviderId { get; } = providerId;
        public string ProviderDisplayName { get; } = providerDisplayName;
        public string ProviderDescription => "Stub options chain provider";
        public int ProviderPriority => 100;
        public ProviderCapabilities ProviderCapabilities => ProviderCapabilities.OptionsChain();
        public OptionsChainCapabilities Capabilities => OptionsChainCapabilities.Basic;

        public Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string underlyingSymbol, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DateOnly>>(Array.Empty<DateOnly>());

        public Task<IReadOnlyList<decimal>> GetStrikesAsync(string underlyingSymbol, DateOnly expiration, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<decimal>>(Array.Empty<decimal>());

        public Task<OptionChainSnapshot?> GetChainSnapshotAsync(string underlyingSymbol, DateOnly expiration, int? strikeRange = null, CancellationToken ct = default) =>
            Task.FromResult<OptionChainSnapshot?>(null);

        public Task<OptionQuote?> GetOptionQuoteAsync(OptionContractSpec contract, CancellationToken ct = default) =>
            Task.FromResult<OptionQuote?>(null);
    }
}
