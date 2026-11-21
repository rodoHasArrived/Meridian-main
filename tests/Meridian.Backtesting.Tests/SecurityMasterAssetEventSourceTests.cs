using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Backtesting.Tests;

public sealed class SecurityMasterAssetEventSourceTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task GetAssetEventsAsync_MapsConfirmedDividendsAndSplitsInsideTheWindow()
    {
        var source = CreateSource(
            Action("Dividend", new DateOnly(2024, 2, 1), dividendPerShare: 0.25m),
            Action("StockSplit", new DateOnly(2024, 3, 1), splitRatio: 2m),
            Action("ReverseStockSplit", new DateOnly(2024, 4, 1), splitRatio: 0.1m),
            Action("ReturnOfCapital", new DateOnly(2024, 5, 1), dividendPerShare: 1.5m),
            Action("Dividend", new DateOnly(2025, 1, 2), dividendPerShare: 0.30m));

        var events = await source.GetAssetEventsAsync(["spy"], new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        events.Select(e => (e.Symbol, e.EventType, e.CashPerShare, e.PositionFactor, Date: DateOnly.FromDateTime(e.EffectiveAt.UtcDateTime)))
            .Should().Equal(
                ("SPY", AssetEventType.Dividend, 0.25m, 1m, new DateOnly(2024, 2, 1)),
                ("SPY", AssetEventType.Split, 0m, 2m, new DateOnly(2024, 3, 1)),
                ("SPY", AssetEventType.ReverseSplit, 0m, 0.1m, new DateOnly(2024, 4, 1)),
                ("SPY", AssetEventType.ReturnOfCapital, 1.5m, 1m, new DateOnly(2024, 5, 1)));
    }

    [Fact]
    public async Task GetAssetEventsAsync_SkipsUnconfirmedAndUnmappedActions()
    {
        var source = CreateSource(
            Action("Dividend", new DateOnly(2024, 2, 1), dividendPerShare: 0.25m, lifecycleState: CorporateActionLifecycleStates.Announced),
            Action("MergerAbsorption", new DateOnly(2024, 3, 1)),
            Action("StockSplit", new DateOnly(2024, 4, 1), splitRatio: null));

        var events = await source.GetAssetEventsAsync(["SPY"], new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAssetEventsAsync_ReturnsNothing_WhenSymbolIsNotInSecurityMaster()
    {
        var source = new SecurityMasterAssetEventSource(
            new StubActions(Action("Dividend", new DateOnly(2024, 2, 1), dividendPerShare: 0.25m)),
            new StubResolver(null),
            NullLogger<SecurityMasterAssetEventSource>.Instance);

        var events = await source.GetAssetEventsAsync(["UNKNOWN"], new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        events.Should().BeEmpty();
    }

    private static SecurityMasterAssetEventSource CreateSource(params CorporateActionDto[] actions) =>
        new(new StubActions(actions), new StubResolver(SecurityId), NullLogger<SecurityMasterAssetEventSource>.Instance);

    private static CorporateActionDto Action(
        string eventType,
        DateOnly exDate,
        decimal? dividendPerShare = null,
        decimal? splitRatio = null,
        string? lifecycleState = CorporateActionLifecycleStates.Confirmed) =>
        new(Guid.NewGuid(), SecurityId, eventType, exDate, null,
            dividendPerShare, null, splitRatio, null, null, null, null, null, null,
            LifecycleState: lifecycleState);

    private sealed class StubActions(params CorporateActionDto[] actions) : ISecurityCorporateActionsQueryService
    {
        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>(actions);
    }

    private sealed class StubResolver(Guid? result) : ISecurityResolver
    {
        public Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default)
            => Task.FromResult(result);
    }
}
