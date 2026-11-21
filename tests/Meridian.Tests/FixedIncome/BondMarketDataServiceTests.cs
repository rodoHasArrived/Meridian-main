using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.FixedIncome;

[Trait("Category", "Unit")]
public sealed class BondMarketDataServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-bond-market-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
    private BondMarketDataService Service => new(new FileBondMarketDataStore(_root), _clock);

    [Fact]
    public async Task CorrectedFixing_RetainsOriginalRevisionAtEarlierKnowledgeCutoff()
    {
        var first = await Service.ImportAsync("tenant-a", "operator", new([Fixing(1, 4.25m)]));
        var firstRecorded = _clock.Now;
        _clock.Now = _clock.Now.AddHours(1);
        var second = await Service.ImportAsync("tenant-a", "operator", new([Fixing(2, 4.35m)]));

        var old = await Service.QueryAsync("tenant-a", new(Kind: BondMarketObservationKind.Fixing, SeriesKey: "SOFR", EconomicDate: new(2026, 11, 10), KnownAtUtc: firstRecorded));
        var latest = await Service.QueryAsync("tenant-a", new(SeriesKey: "SOFR"));
        var history = await Service.QueryAsync("tenant-a", new(IncludeSuperseded: true));
        old.Should().ContainSingle().Which.ObservationId.Should().Be(first.Observations[0].ObservationId);
        latest.Should().ContainSingle().Which.ObservationId.Should().Be(second.Observations[0].ObservationId);
        BondMarketDataRules.AnnualFraction(old[0]).Should().Be(0.0425m);
        history.Should().HaveCount(2);
        (await Service.GetAsync("tenant-a", first.Observations[0].ObservationId)).Should().Be(first.Observations[0]);
        (await Service.QueryAsync("tenant-a", new(EconomicDate: new(2026, 11, 11)))).Should().BeEmpty("the prior day's fixing cannot substitute for a missing reset");
    }

    [Fact]
    public async Task ImportRetry_PreservesFirstIdentityAndActor_ConflictingRevisionWritesNothing()
    {
        var first = await Service.ImportAsync("tenant-a", "original-operator", new([Fixing(1, 4.25m)]));
        var retry = await Service.ImportAsync("tenant-a", "another-operator", new([Fixing(1, 4.25m)]));
        retry.AppendedCount.Should().Be(0);
        retry.Observations.Should().Equal(first.Observations);

        var extra = Fixing(1, 3.5m) with { ObservationKey = "new-row", SeriesKey = "OTHER" };
        Func<Task> conflict = () => Service.ImportAsync("tenant-a", "operator", new([extra, Fixing(1, 9m)]));
        await conflict.Should().ThrowAsync<InvalidOperationException>();
        (await Service.QueryAsync("tenant-a", new(IncludeSuperseded: true))).Should().ContainSingle();
    }

    [Fact]
    public async Task ConcurrentImportRetries_AreAtomicAcrossStoreInstances()
    {
        var imports = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Service.ImportAsync("tenant-a", "operator", new([Fixing(1, -0.5m)]))));
        imports.Sum(result => result.AppendedCount).Should().Be(1);
        imports.Select(result => result.Observations[0].ObservationId).Distinct().Should().ContainSingle();
        BondMarketDataRules.AnnualFraction(imports[0].Observations[0]).Should().Be(-0.005m, "observed negative fixings must survive without a fabricated floor");
    }

    [Fact]
    public async Task TenantPartitions_KeepIdenticalSourceKeysAndObservationLookupsIsolated()
    {
        var tenantA = await Service.ImportAsync("tenant-a", "a", new([Fixing(1, 4.25m)]));
        await Service.ImportAsync("tenant-b", "b", new([Fixing(1, 1.25m)]));
        (await Service.QueryAsync("tenant-b", new())).Should().ContainSingle().Which.Input.Value.Should().Be(1.25m);
        (await Service.GetAsync("tenant-b", tenantA.Observations[0].ObservationId)).Should().BeNull();
    }

    [Fact]
    public async Task CorruptRetainedFile_FailsClosedRatherThanReturningMissingOrChangedInputs()
    {
        await Service.ImportAsync("tenant-a", "operator", new([Fixing(1, 4.25m)]));
        var path = Directory.GetFiles(Path.Combine(_root, "security-master", "bond-market-observations"), "*.json").Single();
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace("4.25", "9.25", StringComparison.Ordinal));
        Func<Task> read = () => Service.QueryAsync("tenant-a", new());
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Quote_RetainsCleanDirtyMeaningAndProviderYieldWithoutSelectingGoldenCopy()
    {
        var security = Guid.NewGuid();
        var quote = Fixing(1, 102.5m) with
        {
            ObservationKey = "quote:1",
            Kind = BondMarketObservationKind.Quote,
            SeriesKey = "CUSIP:TEST",
            Unit = BondMarketValueUnit.PercentOfPar,
            SecurityId = security,
            Currency = "USD",
            Quote = new(BondQuoteCategory.IndicativeQuote, BondQuotePriceBasis.Dirty, new(2026, 11, 13),
                AvailableFace: 100000m, ProviderYield: 0.043m, ProviderYieldConvention: "annual-fraction-ytw")
        };
        var result = await Service.ImportAsync("tenant-a", "operator", new([quote]));
        result.Observations[0].Input.Should().Be(quote);
        var invalid = quote with { Quote = quote.Quote with { ProviderYieldConvention = null } };
        Func<Task> missingConvention = () => Service.ImportAsync("tenant-a", "operator", new([invalid]));
        await missingConvention.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void InflationAndPrincipalFactor_HaveSeparateUnitsAndValidation()
    {
        var cpi = Fixing(1, 312.5m) with { Kind = BondMarketObservationKind.InflationIndex, Unit = BondMarketValueUnit.IndexLevel };
        BondMarketDataRules.Normalize(cpi).Value.Should().Be(312.5m);
        Action wrongUnit = () => BondMarketDataRules.Normalize(cpi with { Unit = BondMarketValueUnit.PrincipalFactor });
        wrongUnit.Should().Throw<ArgumentException>();
        var factor = cpi with { Kind = BondMarketObservationKind.PrincipalFactor, Unit = BondMarketValueUnit.PrincipalFactor, Value = 0.8m, SecurityId = Guid.NewGuid() };
        BondMarketDataRules.Normalize(factor).Value.Should().Be(0.8m);
    }

    [Fact]
    public async Task WorkflowSource_NormalizesRateUnitsAndRefusesCompetingFixings()
    {
        await Service.ImportAsync("tenant-a", "operator", new([Fixing(1, 4.25m)]));
        var adapter = new BondMarketWorkflowObservationSource(Service, () => "tenant-a");
        var rows = await adapter.GetAsync("SOFR", new(2026, 11, 10), new(2026, 11, 10), _clock.Now);
        rows.Should().ContainSingle().Which.Value.Should().Be(0.0425m);
        await Service.ImportAsync("tenant-a", "operator", new([Fixing(1, 0.0426m) with
        { ProviderId = "second-source", Unit = BondMarketValueUnit.AnnualFraction }]));
        Func<Task> competing = () => adapter.GetAsync("SOFR", new(2026, 11, 10), new(2026, 11, 10), _clock.Now);
        await competing.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FutureFixing_CannotBeImportedAsAnObservedCouponInput()
    {
        Func<Task> request = () => Service.ImportAsync("tenant-a", "operator", new([Fixing(1, 4.25m) with
        { EconomicDate = new(2026, 11, 13) }]));
        await request.Should().ThrowAsync<ArgumentException>().WithMessage("*forecasts*");
    }

    [Fact]
    public async Task WorkflowTypedSource_SameIndexNameCannotMixInflationLevelsAndAnnualRates()
    {
        var fixing = Fixing(1, 4.25m);
        var inflation = fixing with
        {
            ObservationKey = "CPI:2026-11-10",
            Kind = BondMarketObservationKind.InflationIndex,
            Unit = BondMarketValueUnit.IndexLevel,
            Value = 312.5m
        };
        await Service.ImportAsync("tenant-a", "operator", new([fixing, inflation]));
        IBondWorkflowTypedObservationSource adapter = new BondMarketWorkflowObservationSource(Service, () => "tenant-a");
        var date = new DateOnly(2026, 11, 10);
        var rates = await adapter.GetAsync("SOFR", BondMarketObservationKind.Fixing, date, date, _clock.Now);
        var levels = await adapter.GetAsync("SOFR", BondMarketObservationKind.InflationIndex, date, date, _clock.Now);
        rates.Should().ContainSingle().Which.Value.Should().Be(0.0425m);
        levels.Should().ContainSingle().Which.Value.Should().Be(312.5m);
        (await adapter.GetAsync("SOFR", date, date, _clock.Now)).Should().ContainSingle().Which.Value.Should().Be(0.0425m);
    }

    [Theory]
    [InlineData(BondMarketObservationKind.Fixing)]
    [InlineData(BondMarketObservationKind.InflationIndex)]
    public async Task WorkflowTypedSource_WrongKindOnlyCannotSatisfyRequestedRole(BondMarketObservationKind wanted)
    {
        var row = wanted == BondMarketObservationKind.Fixing
            ? Fixing(1, 312.5m) with { Kind = BondMarketObservationKind.InflationIndex, Unit = BondMarketValueUnit.IndexLevel }
            : Fixing(1, 4.25m);
        await Service.ImportAsync("tenant-a", "operator", new([row]));
        IBondWorkflowTypedObservationSource adapter = new BondMarketWorkflowObservationSource(Service, () => "tenant-a");
        var date = new DateOnly(2026, 11, 10);
        (await adapter.GetAsync("SOFR", wanted, date, date, _clock.Now)).Should().BeEmpty();
    }

    private static BondMarketObservationInput Fixing(long revision, decimal value) => new(
        "SOFR:2026-11-10", revision, BondMarketObservationKind.Fixing, "official-import", "SOFR", new(2026, 11, 10),
        BondMarketValueUnit.Percent, value, "Synthetic central bank fixing fixture", Sha256Digest.ComputeUtf8("source:" + revision + ":" + value),
        "synthetic:fixing", Currency: "USD");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
