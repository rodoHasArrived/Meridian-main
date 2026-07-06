using FluentAssertions;
using FsCheck.Xunit;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Property-based invariants over <see cref="CorporateActionAdjustmentService"/>: FsCheck
/// supplies integer seeds, deterministic builders derive the domain values (same pattern as
/// the ledger scenario properties), so shrunk counterexamples stay reproducible.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CorporateActionAdjustmentPropertyTests
{
    private static readonly decimal[] SplitRatios = [2m, 3m, 4m, 5m, 10m, 0.5m, 0.25m, 0.125m];

    [Property(MaxTest = 100)]
    public void GeneratedSplitChain_ImpliedFactorsArePiecewiseAndNotionalConserved(
        int ratioSeed, int countSeed, int priceSeed)
    {
        var splitCount = Bounded(countSeed, 1, 3);
        var basePrice = Bounded(priceSeed, 20, 500);
        var raw = BuildFlatBars("PROP", new DateOnly(2025, 3, 3), count: 10, price: basePrice);

        var actions = new List<CorporateActionDto>();
        for (var i = 0; i < splitCount; i++)
        {
            var ratio = SplitRatios[Bounded(ratioSeed + (i * 17), 0, SplitRatios.Length - 1)];
            // Ex-dates land on distinct interior bar dates so each jump is observable.
            var exDate = raw[2 + (i * 3)].SessionDate;
            actions.Add(MakeSplit(exDate, ratio));
        }

        var service = CreateService(actions, out _);
        var adjusted = service.AdjustAsync(raw, "PROP").GetAwaiter().GetResult();

        CorporateActionSeriesInvariants.AssertImpliedFactorMonotonicity(raw, adjusted, actions);
        CorporateActionSeriesInvariants.AssertSeriesContinuity(raw, adjusted, actions);
        CorporateActionSeriesInvariants.AssertNotionalConservation(raw, adjusted);
    }

    [Property(MaxTest = 100)]
    public void GeneratedDividend_FactorStaysInUnitIntervalAndContinuityHolds(
        int priceSeed, int dividendSeed)
    {
        var prevClose = Bounded(priceSeed, 20, 200);
        // Dividend bounded to 40% of prior close: factor stays strictly inside (0, 1].
        var dividend = Math.Max(0.01m, prevClose * Bounded(dividendSeed, 1, 40) / 100m);
        var raw = BuildFlatBars("PROP", new DateOnly(2025, 6, 2), count: 6, price: prevClose);
        var actions = new List<CorporateActionDto> { MakeDividend(raw[3].SessionDate, dividend) };

        var service = CreateService(actions, out _);
        var adjusted = service.AdjustAsync(raw, "PROP").GetAwaiter().GetResult();

        var factor = 1m - (dividend / prevClose);
        factor.Should().BeInRange(0.000001m, 1m);
        adjusted[2].Close.Should().BeApproximately(prevClose * factor, 0.0001m,
            "the bar before the ex-date carries exactly the dividend factor");
        CorporateActionSeriesInvariants.AssertSeriesContinuity(raw, adjusted, actions);
    }

    [Property(MaxTest = 100)]
    public void GeneratedSplit_PositionTotalBasisIsConserved(int ratioSeed, int quantitySeed)
    {
        var ratio = SplitRatios[Bounded(ratioSeed, 0, SplitRatios.Length - 1)];
        var quantity = Bounded(quantitySeed, 1, 5000);
        var actions = new List<CorporateActionDto> { MakeSplit(new DateOnly(2025, 8, 15), ratio) };

        var service = CreateService(actions, out _);
        var adjustment = service.AdjustPositionAsync(
                "PROP", quantity, costBasis: 123.45m, new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero))
            .GetAwaiter().GetResult();

        var originalTotal = adjustment.OriginalQuantity * adjustment.OriginalCostBasis;
        var adjustedTotal = adjustment.AdjustedQuantity * adjustment.AdjustedCostBasis;
        adjustedTotal.Should().BeApproximately(originalTotal, 0.001m);
        adjustment.ActionCount.Should().Be(1);
    }

    // ── Deterministic builders ────────────────────────────────────────────────

    private static int Bounded(int seed, int lowInclusive, int highInclusive)
    {
        var span = highInclusive - lowInclusive + 1;
        var normalized = seed % span;
        if (normalized < 0)
            normalized += span;
        return lowInclusive + normalized;
    }

    private static IReadOnlyList<HistoricalBar> BuildFlatBars(string symbol, DateOnly start, int count, decimal price)
    {
        var bars = new List<HistoricalBar>(count);
        for (var i = 0; i < count; i++)
        {
            bars.Add(new HistoricalBar(
                symbol, start.AddDays(i), price, price, price, price, Volume: 1_000_000, Source: "property"));
        }

        return bars;
    }

    private static readonly Guid PropertySecurityId = Guid.Parse("99999999-9999-4999-8999-000000000001");

    private static CorporateActionDto MakeSplit(DateOnly exDate, decimal ratio)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: PropertySecurityId,
            EventType: ratio >= 1m ? CorporateActionEventTypes.StockSplit : CorporateActionEventTypes.ReverseStockSplit,
            ExDate: exDate,
            PayDate: null,
            DividendPerShare: null,
            Currency: null,
            SplitRatio: ratio,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);

    private static CorporateActionDto MakeDividend(DateOnly exDate, decimal dividendPerShare)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: PropertySecurityId,
            EventType: CorporateActionEventTypes.Dividend,
            ExDate: exDate,
            PayDate: exDate.AddDays(14),
            DividendPerShare: dividendPerShare,
            Currency: "USD",
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null,
            RecordDate: exDate.AddDays(-1));

    private static CorporateActionAdjustmentService CreateService(
        IReadOnlyList<CorporateActionDto> actions, out Guid securityId)
    {
        securityId = PropertySecurityId;
        return new CorporateActionAdjustmentService(
            new StubQueryService(actions),
            new StubResolver(securityId),
            NullLogger<CorporateActionAdjustmentService>.Instance);
    }

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
