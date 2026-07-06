using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.TestSupport.CorporateActions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Golden-scenario regression sweep for <see cref="CorporateActionAdjustmentService"/>:
/// every fixture tagged with a series invariant is run through the real adjustment path and
/// checked against <see cref="CorporateActionSeriesInvariants"/>. Fixtures live under
/// tests/fixtures/corporate-actions/golden/ (see KNOWN-DEFECTS.md there for the registry).
/// </summary>
[Trait("Category", "Unit")]
public sealed class CorporateActionGoldenAdjustmentTests
{
    public static TheoryData<string> ContinuityScenarios()
        => GoldenCorporateActionScenarioLoader.ScenarioIds(GoldenInvariantTags.SeriesContinuity);

    public static TheoryData<string> MonotonicityScenarios()
        => GoldenCorporateActionScenarioLoader.ScenarioIds(GoldenInvariantTags.FactorMonotonicity);

    public static TheoryData<string> NotionalScenarios()
        => GoldenCorporateActionScenarioLoader.ScenarioIds(GoldenInvariantTags.NotionalConservation);

    public static TheoryData<string> BasisScenarios()
        => GoldenCorporateActionScenarioLoader.ScenarioIds(GoldenInvariantTags.PositionBasisConserved);

    [Theory]
    [MemberData(nameof(ContinuityScenarios))]
    public async Task AdjustAsync_GoldenScenario_SeriesContinuityHolds(string scenarioId)
    {
        var (scenario, service) = CreateServiceFor(scenarioId);
        var raw = CorporateActionSeriesInvariants.ToHistoricalBars(scenario);

        var adjusted = await service.AdjustAsync(raw, scenario.Ticker);

        CorporateActionSeriesInvariants.AssertSeriesContinuity(raw, adjusted, EffectiveActions(scenario));
    }

    [Theory]
    [MemberData(nameof(MonotonicityScenarios))]
    public async Task AdjustAsync_GoldenScenario_ImpliedFactorIsPiecewiseAndTerminatesAtOne(string scenarioId)
    {
        var (scenario, service) = CreateServiceFor(scenarioId);
        var raw = CorporateActionSeriesInvariants.ToHistoricalBars(scenario);

        var adjusted = await service.AdjustAsync(raw, scenario.Ticker);

        CorporateActionSeriesInvariants.AssertImpliedFactorMonotonicity(raw, adjusted, EffectiveActions(scenario));
    }

    [Theory]
    [MemberData(nameof(NotionalScenarios))]
    public async Task AdjustAsync_GoldenSplitScenario_ConservesNotional(string scenarioId)
    {
        var (scenario, service) = CreateServiceFor(scenarioId);
        var raw = CorporateActionSeriesInvariants.ToHistoricalBars(scenario);

        var adjusted = await service.AdjustAsync(raw, scenario.Ticker);

        CorporateActionSeriesInvariants.AssertNotionalConservation(raw, adjusted);
    }

    [Theory]
    [MemberData(nameof(BasisScenarios))]
    public async Task AdjustPositionAsync_GoldenSplitScenario_ConservesTotalBasis(string scenarioId)
    {
        var (scenario, service) = CreateServiceFor(scenarioId);
        var openedAt = new DateTimeOffset(
            scenario.Bars.Min(static bar => bar.SessionDate).AddDays(-1),
            TimeOnly.MinValue,
            TimeSpan.Zero);

        var adjustment = await service.AdjustPositionAsync(scenario.Ticker, quantity: 100m, costBasis: 50m, openedAt);

        adjustment.ActionCount.Should().BeGreaterThan(0, "the scenario declares at least one split after position open");
        var originalTotal = adjustment.OriginalQuantity * adjustment.OriginalCostBasis;
        var adjustedTotal = adjustment.AdjustedQuantity * adjustment.AdjustedCostBasis;
        adjustedTotal.Should().BeApproximately(originalTotal, 0.0001m,
            "quantity x per-share basis must be conserved across price-scaling actions");
    }

    // ── Supersede-fold semantics ──────────────────────────────────────────────

    [Fact]
    public async Task AdjustAsync_AmendedDividend_UsesSupersedingTerms()
    {
        var (scenario, service) = CreateServiceFor("dividend-amended-supersede");
        var raw = CorporateActionSeriesInvariants.ToHistoricalBars(scenario);

        var adjusted = await service.AdjustAsync(raw, scenario.Ticker);

        // Effective dividend is the amended 0.88, not the announced 0.85:
        // prior close 40.00 -> factor 1 - 0.88/40 = 0.978 -> adjusted close 39.12.
        var priorExBar = adjusted.Single(static bar => bar.SessionDate == new DateOnly(2026, 2, 9));
        priorExBar.Close.Should().BeApproximately(39.12m, 0.0001m);
    }

    [Fact]
    public async Task AdjustAsync_CancelledDividend_LeavesSeriesUnadjusted()
    {
        var (scenario, service) = CreateServiceFor("dividend-cancelled-after-announce");
        var raw = CorporateActionSeriesInvariants.ToHistoricalBars(scenario);

        var adjusted = await service.AdjustAsync(raw, scenario.Ticker);

        adjusted.Should().BeEquivalentTo(raw, options => options.WithStrictOrdering(),
            "a cancelled chain must contribute no adjustment");
    }

    // ── Alias normalization end-to-end ────────────────────────────────────────

    [Fact]
    public async Task AdjustAsync_ProviderAliasSpellings_ResolveThroughCatalog()
    {
        var (scenario, service) = CreateServiceFor("alias-normalization-mixed");
        var raw = CorporateActionSeriesInvariants.ToHistoricalBars(scenario);

        var adjusted = await service.AdjustAsync(raw, scenario.Ticker);

        // 'Split' resolves to StockSplit: the first bar's volume doubles.
        adjusted[0].Volume.Should().Be(raw[0].Volume * 2);
        // 'MysteryEvent' stays unresolved and must not disturb the resolvable actions.
        CorporateActionTypeDescriptorCatalog.TryNormalize("MysteryEvent", out _).Should().BeFalse();
        CorporateActionTypeDescriptorCatalog.TryNormalize("Split", out var split).Should().BeTrue();
        split.CanonicalName.Should().Be(CorporateActionEventTypes.StockSplit);
        CorporateActionTypeDescriptorCatalog.TryNormalize("DIV", out var dividend).Should().BeTrue();
        dividend.CanonicalName.Should().Be(CorporateActionEventTypes.Dividend);
        CorporateActionTypeDescriptorCatalog.TryNormalize("spin-off", out var spinOff).Should().BeTrue();
        spinOff.CanonicalName.Should().Be(CorporateActionEventTypes.SpinOff);
    }

    // ── Wiring ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<CorporateActionDto> EffectiveActions(GoldenCorporateActionScenario scenario)
        => CorporateActionEffectiveStateProjector.ProjectEffectiveActions(scenario.Actions, DateTimeOffset.UtcNow);

    private static (GoldenCorporateActionScenario Scenario, CorporateActionAdjustmentService Service) CreateServiceFor(
        string scenarioId)
    {
        var scenario = GoldenCorporateActionScenarioLoader.Load(scenarioId);
        var resolver = new GoldenSecurityResolver(scenario.SecurityId);
        var queryService = new GoldenSecurityMasterQueryService(scenario.Actions);
        var service = new CorporateActionAdjustmentService(
            queryService, resolver, NullLogger<CorporateActionAdjustmentService>.Instance);
        return (scenario, service);
    }

    private sealed class GoldenSecurityResolver(Guid securityId) : ISecurityResolver
    {
        public Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default)
            => Task.FromResult<Guid?>(securityId);
    }

    private sealed class GoldenSecurityMasterQueryService(IReadOnlyList<CorporateActionDto> actions)
        : ISecurityMasterQueryService
    {
        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(actions);

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
