using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Unit tests for <see cref="CorporateActionAdjustmentService"/>.
/// </summary>
public sealed class CorporateActionAdjustmentServiceTests
{
    private readonly CorporateActionAdjustmentService _service;
    private readonly MockSecurityResolver _mockResolver;
    private readonly MockSecurityMasterQueryService _mockQueryService;

    public CorporateActionAdjustmentServiceTests()
    {
        _mockResolver = new MockSecurityResolver();
        _mockQueryService = new MockSecurityMasterQueryService();
        _service = new CorporateActionAdjustmentService(_mockQueryService, _mockResolver, NullLogger<CorporateActionAdjustmentService>.Instance);
    }

    [Fact]
    public async Task AdjustAsync_EmptyBars_ReturnsOriginalBars()
    {
        var result = await _service.AdjustAsync([], "SPY");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AdjustAsync_SecurityNotFound_ReturnsOriginalBars()
    {
        _mockResolver.SetResolveResult(null);
        var bars = new[] { CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m) };

        var result = await _service.AdjustAsync(bars, "SPY");

        result.Should().HaveCount(1);
        result[0].Close.Should().Be(105m);
    }

    [Fact]
    public async Task AdjustAsync_NoCorporateActions_ReturnsOriginalBars()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        _mockQueryService.SetCorporateActions([]);
        
        var bars = new[] { CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m) };

        var result = await _service.AdjustAsync(bars, "SPY");

        result.Should().HaveCount(1);
        result[0].Close.Should().Be(105m);
    }

    [Fact]
    public async Task AdjustAsync_StockSplit_AdjustsPrices()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        
        var split = new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: "StockSplit",
            ExDate: new DateOnly(2024, 2, 1),
            PayDate: null,
            DividendPerShare: null,
            Currency: null,
            SplitRatio: 2m,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);
        
        _mockQueryService.SetCorporateActions([split]);
        
        var bar = CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m, 1000);

        var result = await _service.AdjustAsync([bar], "SPY");

        result.Should().HaveCount(1);
        result[0].Open.Should().Be(50m);      // 100 / 2
        result[0].High.Should().Be(55m);      // 110 / 2
        result[0].Low.Should().Be(45m);       // 90 / 2
        result[0].Close.Should().Be(52.5m);   // 105 / 2
        result[0].Volume.Should().Be(2000);   // 1000 * 2
    }

    [Fact]
    public async Task AdjustAsync_Dividend_AdjustsPrices()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        
        var dividend = new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: "Dividend",
            ExDate: new DateOnly(2024, 2, 1),
            PayDate: null,
            DividendPerShare: 1m,
            Currency: "USD",
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);
        
        _mockQueryService.SetCorporateActions([dividend]);
        
        var bar = CreateBar("SPY", new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m);

        var result = await _service.AdjustAsync([bar], "SPY");

        result.Should().HaveCount(1);
        result[0].Open.Should().Be(99m);      // 100 - 1
        result[0].High.Should().Be(109m);     // 110 - 1
        result[0].Low.Should().Be(89m);       // 90 - 1
        result[0].Close.Should().Be(104m);    // 105 - 1
        result[0].Volume.Should().Be(bar.Volume);
    }

    [Fact]
    public async Task AdjustAsync_MultipleSplits_CombinesFactors()
    {
        var securityId = Guid.NewGuid();
        _mockResolver.SetResolveResult(securityId);
        
        var splits = new[]
        {
            new CorporateActionDto(
                CorpActId: Guid.NewGuid(),
                SecurityId: securityId,
                EventType: "StockSplit",
                ExDate: new DateOnly(2024, 2, 1),
                PayDate: null,
                DividendPerShare: null,
                Currency: null,
                SplitRatio: 2m,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null),
            new CorporateActionDto(
                CorpActId: Guid.NewGuid(),
                SecurityId: securityId,
                EventType: "StockSplit",
                ExDate: new DateOnly(2024, 3, 1),
                PayDate: null,
                DividendPerShare: null,
                Currency: null,
                SplitRatio: 3m,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null)
        };
        
        _mockQueryService.SetCorporateActions(splits);
        
        var bar = CreateBar("SPY", new DateOnly(2024, 1, 1), 600m, 660m, 540m, 630m, 1000);

        var result = await _service.AdjustAsync([bar], "SPY");

        result.Should().HaveCount(1);
        result[0].Open.Should().Be(100m);     // 600 / 6
        result[0].High.Should().Be(110m);     // 660 / 6
        result[0].Low.Should().Be(90m);       // 540 / 6
        result[0].Close.Should().Be(105m);    // 630 / 6
        result[0].Volume.Should().Be(6000);   // 1000 * 6
    }

    private static HistoricalBar CreateBar(
        string symbol,
        DateOnly date,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume = 1000000)
    {
        return new HistoricalBar(symbol, date, open, high, low, close, volume);
    }

    // Mock implementations
    private sealed class MockSecurityResolver : ISecurityResolver
    {
        private Guid? _result;

        public void SetResolveResult(Guid? result) => _result = result;

        public Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class MockSecurityMasterQueryService : ISecurityMasterQueryService
    {
        private IReadOnlyList<CorporateActionDto> _actions = [];

        public void SetCorporateActions(IReadOnlyList<CorporateActionDto> actions) => _actions = actions;

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(_actions);

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
