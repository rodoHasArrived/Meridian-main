using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.TestSupport.CorporateActions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Characterization tests pinning the adjustment-side entries of the known-defect registry
/// (tests/fixtures/corporate-actions/golden/KNOWN-DEFECTS.md). Each test asserts TODAY'S
/// defective behavior so CI stays green and any accidental behavior change — fix or
/// regression — fails loudly. When the fixing lane lands, flip the assertion to the TARGET
/// and update the registry in the same PR.
/// </summary>
[Trait("Category", "KnownDefect")]
public sealed class CorporateActionKnownDefectTests
{
    [Fact]
    public async Task DividendFactor_MissingPriorBar_CurrentBehavior_SilentlyUnadjusted_CA_DEF_001()
    {
        // TARGET (Idea #2 remainder): a dividend whose ex-date precedes the first available
        // bar must degrade explicitly — warning + machine-readable adjustment report —
        // instead of returning an unadjusted series with no signal.
        var scenario = GoldenCorporateActionScenarioLoader.Load("dividend-missing-prior-bar");
        var service = CreateService(scenario);
        var raw = CorporateActionSeriesInvariants.ToHistoricalBars(scenario);

        var adjusted = await service.AdjustAsync(raw, scenario.Ticker);

        adjusted.Should().BeEquivalentTo(raw, options => options.WithStrictOrdering(),
            "CURRENT defective behavior: no prior close -> factor silently skipped");
    }

    [Fact]
    public async Task FactorPaydown_PositionAdjustment_CurrentBehavior_RatioSubtractedFromBasis_CA_DEF_004()
    {
        // TARGET (Idea #3): a pool-factor delta reduces basis proportionally to
        // factor x face value; it is a dimensionless ratio, not an absolute money amount.
        var scenario = GoldenCorporateActionScenarioLoader.Load("mbs-factor-paydown");
        var service = CreateService(scenario);

        var adjustment = await service.AdjustPositionAsync(
            scenario.Ticker,
            quantity: 100_000m,
            costBasis: 99.5m,
            positionOpenedAt: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

        adjustment.ActionCount.Should().Be(1);
        adjustment.AdjustedQuantity.Should().Be(100_000m);
        adjustment.AdjustedCostBasis.Should().Be(99.5m - 0.007125m,
            "CURRENT defective behavior: DistributionRatio subtracted from cost basis as an absolute amount");
    }

    private static CorporateActionAdjustmentService CreateService(GoldenCorporateActionScenario scenario)
        => new(
            new StubQueryService(scenario.Actions),
            new StubResolver(scenario.SecurityId),
            NullLogger<CorporateActionAdjustmentService>.Instance);

    private sealed class StubResolver(Guid securityId) : ISecurityResolver
    {
        public Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default)
            => Task.FromResult<Guid?>(securityId);
    }

    private sealed class StubQueryService(IReadOnlyList<CorporateActionDto> actions) : ISecurityMasterQueryService
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
