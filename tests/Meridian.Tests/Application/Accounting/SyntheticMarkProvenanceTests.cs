using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Operations;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Application.Accounting;

/// <summary>
/// Guards the honesty boundary between fabricated market data and governed valuation evidence.
///
/// The failure these cover is not that Meridian prices positions off a synthetic provider — an
/// offline or demo deployment legitimately does — but that it used to do so silently: a seeded
/// random walk was stamped as an ASC 820 Level 1 observable input, carried no origin mark, and a
/// NAV derived from it validated as an approvable deliverable. A fabricated number that announces
/// itself is a feature; one that presents as an exchange close is the product's core promise
/// inverted.
/// </summary>
public sealed class SyntheticMarkProvenanceTests
{
    private static readonly DateOnly AsOf = new(2026, 7, 3);
    private static readonly DateTimeOffset AsOfUtc = new(2026, 7, 3, 21, 0, 0, TimeSpan.Zero);

    private static HistoricalBar Bar(string source, decimal close = 187.42m)
        => new("AAPL", AsOf, 186m, 188m, 185m, close, 1_000_000, Source: source);

    [Fact]
    public async Task SyntheticProvider_MarkIsNeverStampedLevel1()
    {
        var source = new HistoricalCloseMarkPriceSource(new StubProvider("synthetic", Bar("synthetic"), isSimulated: true));

        var quote = await source.GetMarkPriceAsync("AAPL", AsOf);

        quote.Should().NotBeNull();
        quote!.Provenance.Should().Be(DataProvenance.Simulated);
        quote.Level.Should().Be(
            FairValueLevel.Level3,
            "a seeded walk is a model output with unobservable inputs, not a quoted price in an active market");
    }

    [Fact]
    public async Task RealProvider_StillMarksAtLevel1()
    {
        var source = new HistoricalCloseMarkPriceSource(new StubProvider("polygon", Bar("polygon")));

        var quote = await source.GetMarkPriceAsync("AAPL", AsOf);

        quote.Should().NotBeNull();
        quote!.Provenance.Should().Be(DataProvenance.Real);
        quote.Level.Should().Be(FairValueLevel.Level1, "a genuine exchange close is an observable Level 1 input");
    }

    [Fact]
    public async Task AggregatorServingSyntheticBar_IsClassifiedFromTheBarNotTheAggregator()
    {
        // The composite provider is not itself simulated, so the provider-level flag says nothing.
        // The bar's own source tag is what betrays the fabrication.
        var source = new HistoricalCloseMarkPriceSource(new StubProvider("composite", Bar("synthetic")));

        var quote = await source.GetMarkPriceAsync("AAPL", AsOf);

        quote.Should().NotBeNull();
        quote!.Provenance.Should().Be(DataProvenance.Simulated);
        quote.Level.Should().Be(FairValueLevel.Level3);
    }

    [Fact]
    public async Task RealVendorWhoseNameContainsASimulatedWord_IsNotMisclassified()
    {
        // Token matching is exact, never substring: a real custodian must not be branded simulated.
        var source = new HistoricalCloseMarkPriceSource(new StubProvider("sample-custodian", Bar("Sample Custodian")));

        var quote = await source.GetMarkPriceAsync("AAPL", AsOf);

        quote.Should().NotBeNull();
        quote!.Provenance.Should().Be(DataProvenance.Real);
        quote.Level.Should().Be(FairValueLevel.Level1);
    }

    [Fact]
    public async Task RealSyntheticHistoricalProvider_DeclaresItselfSimulated()
    {
        // Exercises the shipped provider rather than a stub, so the declaration cannot rot.
        using var provider = new SyntheticHistoricalDataProvider();
        provider.IsSimulated.Should().BeTrue();

        var quote = await new HistoricalCloseMarkPriceSource(provider).GetMarkPriceAsync("AAPL", AsOf);

        quote.Should().NotBeNull();
        quote!.Provenance.Should().Be(DataProvenance.Simulated);
        quote.Level.Should().Be(FairValueLevel.Level3);
        quote.EvidenceReference.Should().Contain("synthetic");
    }

    [Fact]
    public async Task FundPolicyDefault_CannotUpgradeASimulatedMarkToLevel1()
    {
        // A fund whose policy defaults to Level 1 must not launder a fabricated mark through it.
        var policy = new DailyPortfolioPricingPolicy(
            fundId: "fund-alpha",
            policyId: "vp-1",
            policyName: "Daily fair value",
            valuationMethod: "market-close",
            approvedBy: "cfo",
            approvedAtUtc: AsOfUtc.AddDays(-30),
            defaultFairValueLevel: FairValueLevel.Level1);

        var run = await new DailyMarkToMarketService(new FixedQuoteSource(new MarkPriceQuote(
                Price: 160m,
                Source: "synthetic",
                EvidenceReference: "daily-close:AAPL:2026-07-03:synthetic",
                Level: FairValueLevel.Unclassified,
                PriceAsOf: AsOf,
                Confidence: DailyPortfolioPriceConfidence.High,
                Provenance: DataProvenance.Simulated)))
            .PrepareAsync(new DailyMarkToMarketRequest(
                policy,
                PeriodId: "2026-07",
                AsOfUtc,
                BaseCurrency: "USD",
                Positions: [new MarkToMarketPosition("AAPL", Quantity: 100m, CostPrice: 150m)],
                Actor: "ops",
                Reason: "daily close marks"));

        var line = run.Projection!.Lines.Should().ContainSingle().Subject;
        line.Provenance.Should().Be(DataProvenance.Simulated, "the origin must survive the projection");
        line.FairValueLevel.Should().Be(
            FairValueLevel.Level3,
            "the fund's default level classifies unclassified real marks, not fabricated ones");
    }

    [Fact]
    public async Task SimulatedValuationDraft_CarriesTheOriginAndADistinctIdempotencyKey()
    {
        var simulated = await BuildDraftAsync(DataProvenance.Simulated);
        var real = await BuildDraftAsync(DataProvenance.Real);

        simulated.Metadata.Tags![ValuationProvenanceTag.Key].Should().Be("simulated");
        real.Metadata.Tags![ValuationProvenanceTag.Key].Should().Be("real");

        simulated.Metadata.IdempotencyKey.Should().NotBe(
            real.Metadata.IdempotencyKey,
            "a simulated valuation run must never collide with the real run for the same scope and period");

        ValuationProvenanceTag.Read(simulated.Metadata).Should().Be(DataProvenance.Simulated);
        ValuationProvenanceTag.Read(real.Metadata).Should().Be(DataProvenance.Real);
    }

    private static async Task<AutomatedJournalDraft> BuildDraftAsync(DataProvenance provenance)
    {
        var policy = new DailyPortfolioPricingPolicy(
            fundId: "fund-alpha",
            policyId: "vp-1",
            policyName: "Daily fair value",
            valuationMethod: "market-close",
            approvedBy: "cfo",
            approvedAtUtc: AsOfUtc.AddDays(-30));

        var run = await new DailyMarkToMarketService(new FixedQuoteSource(new MarkPriceQuote(
                Price: 160m,
                // Identical price, source, and evidence in both runs: origin is the only declared
                // difference, so a colliding key would mean provenance is absent from the key.
                Source: "daily-close-provider",
                EvidenceReference: "daily-close:AAPL:2026-07-03",
                Level: FairValueLevel.Level1,
                PriceAsOf: AsOf,
                Confidence: DailyPortfolioPriceConfidence.High,
                Provenance: provenance)))
            .PrepareAsync(new DailyMarkToMarketRequest(
                policy,
                PeriodId: "2026-07",
                AsOfUtc,
                BaseCurrency: "USD",
                Positions: [new MarkToMarketPosition("AAPL", Quantity: 100m, CostPrice: 150m)],
                Actor: "ops",
                Reason: "daily close marks"));

        return run.Approvals.Should().ContainSingle().Subject.Draft;
    }

    private sealed class FixedQuoteSource : IMarkPriceSource
    {
        private readonly MarkPriceQuote _quote;

        public FixedQuoteSource(MarkPriceQuote quote) => _quote = quote;

        public Task<MarkPriceQuote?> GetMarkPriceAsync(string symbol, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult<MarkPriceQuote?>(_quote);
    }

    private sealed class StubProvider : IHistoricalDataProvider
    {
        private readonly HistoricalBar _bar;

        public StubProvider(string name, HistoricalBar bar, bool isSimulated = false)
        {
            Name = DisplayName = name;
            _bar = bar;
            IsSimulated = isSimulated;
        }

        public string Name { get; }
        public string DisplayName { get; }
        public string Description => string.Empty;
        public bool IsSimulated { get; }

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HistoricalBar>>([_bar]);
    }
}
