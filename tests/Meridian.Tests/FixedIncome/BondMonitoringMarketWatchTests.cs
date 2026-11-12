using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.FixedIncome;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.FixedIncome;

public sealed class BondMonitoringMarketWatchTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly Guid Security = Guid.Parse("cb235ab8-9927-4d64-909a-d1d3954d3d12");
    private static readonly Guid Account = Guid.Parse("e82c5817-1011-4a0e-8cc4-41b54daa91b8");
    private static readonly Guid Batch = Guid.Parse("e76cd672-450d-4917-a9d9-c39614a41438");
    private static readonly AccountCashForecastScopeDto Scope = new("tenant-a", "company", Account.ToString("D"), Guid.NewGuid().ToString("D"), "USD");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bond-market-watch-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new() { Now = Now.AddHours(-2) };
    private BondMarketDataService Market => new(new FileBondMarketDataStore(_root), _clock);

    [Fact]
    public async Task ImportedAgencyRevisionsAndFactorReachHeldAccountWithoutForeignSources()
    {
        await Market.ImportAsync("tenant-a", "operator", new([Rating("moodys", "Moody's", "Aa3"),
            Rating("sp", "S&P", "BBB"), Factor(.9m), Rating("foreign-security", "Other", "CCC") with { SecurityId = Guid.NewGuid() }]));
        _clock.Now = Now.AddHours(-1);
        await Market.ImportAsync("tenant-a", "operator", new([Rating("moodys", "Moody's", "A3") with { Revision = 2 }]));
        await Market.ImportAsync("tenant-b", "operator", new([Rating("foreign-tenant", "Fitch", "D")]));
        var plan = await Planner().PlanAsync(Request());
        plan.MonitoringInputs.MarketWatch!.Observations.Should().HaveCount(4);
        var snapshot = BondMonitoringEngine.Build(plan.MonitoringInputs);
        snapshot.Issues.Should().BeEmpty();
        snapshot.Watch.Should().Contain(row => row.Kind == "RatingRevision" && row.Agency == "Moody's");
        snapshot.Watch.Should().Contain(row => row.Kind == "Rating" && row.Agency == "S&P" && row.Summary.Contains("BBB"));
        snapshot.Watch.Should().Contain(row => row.Kind == "FactorConflict" && row.RequiresAttention && row.AffectedObligationIds.Count == 1);
        snapshot.Watch.Should().NotContain(row => row.Agency == "Fitch" || row.Agency == "Other");
    }

    [Fact]
    public async Task KnowledgeCutoffKeepsEarlierRatingAndFactorEvidenceForReplay()
    {
        var first = await Market.ImportAsync("tenant-a", "operator", new([Rating("rating", "S&P", "A"), Factor(1m)]));
        var cutoff = _clock.Now;
        _clock.Now = Now.AddHours(-1);
        await Market.ImportAsync("tenant-a", "operator", new([Rating("rating", "S&P", "BBB") with { Revision = 2 }, Factor(.8m) with { Revision = 2 }]));
        var plan = await Planner().PlanAsync(Request(cutoff));
        plan.MonitoringInputs.MarketWatch!.Observations.Select(row => row.ObservationId)
            .Should().BeEquivalentTo(first.Observations.Select(row => row.ObservationId));
        BondMonitoringEngine.Build(plan.MonitoringInputs).Watch.Should().NotContain(row => row.Kind == "RatingRevision" || row.Kind == "FactorConflict");
    }

    [Fact]
    public async Task RatingsRemainWatchOnlyWhileFactorConflictsBlockDecisionReadyCash()
    {
        await Market.ImportAsync("tenant-a", "operator", new([Rating("rating", "S&P", "BBB")]));
        var rated = await Planner().PlanAsync(Request(withOpening: true));
        rated.BaseCurrencyLadder.IsDecisionReady.Should().BeTrue();
        rated.Blockers.Should().BeEmpty();
        await Market.ImportAsync("tenant-a", "operator", new([Factor(1m),
            Factor(.9m) with { ObservationKey = "other-factor", ProviderId = "other-provider" }]));
        var conflicted = await Planner().PlanAsync(Request(withOpening: true));
        conflicted.Blockers.Should().Contain(row => row.Contains("FactorConflict"));
        conflicted.Blockers.Should().Contain(row => row.Contains("FactorSourceConflict"));
        conflicted.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        conflicted.CurrencyLadders.Should().OnlyContain(row => !row.IsDecisionReady);
    }

    [Fact]
    public async Task SupersededCalculationDependencyBlocksCashEvenWhenRevisedFactorValueMatchesHoldings()
    {
        var first = await Market.ImportAsync("tenant-a", "operator", new([Factor(1m)]));
        _clock.Now = Now.AddHours(-1);
        await Market.ImportAsync("tenant-a", "operator", new([Factor(1m) with { Revision = 2, SourceName = "Corrected factor evidence" }]));
        var planning = new AccountBondIncomePlanningService(new Holdings(), new Bonds(first.Observations[0].ContentSha256),
            new BondMonitoringService(new BondMonitoringFileStore(_root)), new Clock { Now = Now }, market: Market);
        var plan = await planning.PlanAsync(Request(withOpening: true));
        plan.Blockers.Should().ContainSingle(row => row.Contains("MarketDependencyStale"));
        plan.Blockers.Should().NotContain(row => row.Contains("FactorConflict"));
        plan.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureRequeriesExactTenantSourceAndRejectsForeignOrOmittedMarketEvidence()
    {
        await Market.ImportAsync("tenant-a", "operator", new([Rating("rating", "S&P", "A")]));
        var foreign = await Market.ImportAsync("tenant-b", "operator", new([Rating("rating", "S&P", "D")]));
        var monitor = new BondMonitoringService(new BondMonitoringFileStore(_root), new Holdings(), new Bonds(), Market);
        var plan = await Planner(monitor).PlanAsync(Request());
        Func<Task> omit = () => monitor.CaptureAsync(plan.MonitoringInputs with { MarketWatch = null });
        await omit.Should().ThrowAsync<InvalidOperationException>();
        var forged = plan.MonitoringInputs with
        { MarketWatch = plan.MonitoringInputs.MarketWatch! with { Observations = foreign.Observations } };
        Func<Task> captureForeign = () => monitor.CaptureAsync(forged);
        await captureForeign.Should().ThrowAsync<InvalidOperationException>();
        (await monitor.GetHistoryAsync(Scope)).Should().BeEmpty();
        var captured = await monitor.CaptureAsync(plan.MonitoringInputs);
        captured.Watch.Should().Contain(row => row.Kind == "Rating" && row.Summary.Contains("A;"));
        (await monitor.GetHistoryAsync(Scope)).Should().ContainSingle();
    }

    [Fact]
    public async Task RevisedMarketDependencyMarksAffectedRetainedCalculationForRebuild()
    {
        var first = await Market.ImportAsync("tenant-a", "operator", new([Factor(1m)]));
        _clock.Now = Now.AddHours(-1);
        await Market.ImportAsync("tenant-a", "operator", new([Factor(.9m) with { Revision = 2 }]));
        var plan = await Planner().PlanAsync(Request());
        var retained = plan.MonitoringInputs with
        {
            Obligations = plan.MonitoringInputs.Obligations.Select(row => row with
            { Inputs = row.Inputs.Append(new BondCalculationInput("Observation", "factor", ContentSha256: first.Observations[0].ContentSha256)).ToArray() }).ToArray()
        };
        BondMonitoringEngine.Build(retained).Watch.Should().ContainSingle(row => row.Kind == "MarketDependencyStale"
            && row.AffectedObligationIds.SequenceEqual(retained.Obligations.Select(value => value.ObligationId)));
    }

    [Fact]
    public void ExistingReceiptFormatOmitsNewNullableMarketField()
    {
        var legacy = new BondMonitoringCaptureRequest("old", Scope, Now, [], [], [], [], []);
        JsonSerializer.Serialize(legacy, BondMonitoringJsonContext.Default.BondMonitoringCaptureRequest).Should().NotContain("MarketWatch");
        BondMonitoringEngine.Build(legacy).Inputs.MarketWatch.Should().BeNull();
    }

    private AccountBondIncomePlanningService Planner(IBondMonitoringService? monitor = null) => new(new Holdings(), new Bonds(),
        monitor ?? new BondMonitoringService(new BondMonitoringFileStore(_root)), new Clock { Now = Now }, market: Market);
    private static AccountBondIncomePlanningRequest Request(DateTimeOffset? cutoff = null, bool withOpening = false)
    {
        var hash = Sha256Digest.ComputeUtf8("opening");
        var source = new AccountCashProvenanceDto("SyntheticCustodian", "opening", hash, AccountCashEvidenceRole.Observation,
            new("opening", "https://evidence.example.test/opening", hash, "SyntheticCustodian", "opening", "Accepted",
                "reviewer", Now.AddHours(-2), Today, 1, Now.AddHours(-1), "retainer", "AccountCash", Scope.AccountId));
        return new(Scope, Today, Today.AddYears(1), new(Now, KnownAt: cutoff ?? Now),
            withOpening ? [new(Scope.AccountId, Scope.BookId, Today, 3000m, "USD", true, source)] : [], []);
    }
    private static BondMarketObservationInput Rating(string key, string agency, string rating) => new(key, 1,
        BondMarketObservationKind.CreditRating, "synthetic", "credit", Today.AddDays(-1), BondMarketValueUnit.Rating,
        null, "Synthetic rating source", Sha256Digest.ComputeUtf8(key + rating), "https://evidence.example.test/" + key,
        SecurityId: Security, Rating: rating, RatingAgency: agency);
    private static BondMarketObservationInput Factor(decimal value) => new("factor", 1,
        BondMarketObservationKind.PrincipalFactor, "synthetic", "principal-factor", Today.AddDays(-1), BondMarketValueUnit.PrincipalFactor,
        value, "Synthetic factor source", Sha256Digest.ComputeUtf8(value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        "https://evidence.example.test/factor", SecurityId: Security);
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Holdings : IAccountBondHoldingsSource
    {
        public Task<AccountBondHoldingsSnapshotDto?> GetHoldingsAsync(Guid accountId, DateOnly asOf, DateTimeOffset? knownAt = null, CancellationToken ct = default) =>
            Task.FromResult<AccountBondHoldingsSnapshotDto?>(new(Account, Today, Batch, Sha256Digest.ComputeUtf8("held"), "synthetic",
                [new(Guid.NewGuid(), Batch, Account, Today, "bond", "ISIN", 2000m, 2000m, "USD", "Held bond", "Bond", false,
                    SecurityId: Security, Bond: new(2000m, 2000m, 1m, 1m, BondHoldingQuoteBasis.PercentOfCurrentFace, 100m, false, 0m,
                        InvoiceBasis: BondHoldingInvoiceBasis.Nominal))]));
    }
    private sealed class Bonds(string? priorDependency = null) : IBondWorkflowService
    {
        public Task<BondWorkflowResult?> CalculateAsync(BondWorkflowRequest request, CancellationToken ct = default) =>
            Task.FromResult<BondWorkflowResult?>(new(Security, "USD", "synthetic", request.Context with { SecurityVersion = 1 }, 2000m, 2000m,
                0m, null, null, 0m, null, null, null, null, null, null, null,
                [new(Today.AddDays(30), Today, Today.AddDays(30), 10m, 0m, 2000m, 1m, false, [])],
                new BondCalculationInput[] { new("SecurityTerms", "terms", 1, Sha256Digest.ComputeUtf8("terms")) }
                    .Concat(priorDependency is null ? [] : new BondCalculationInput[]
                        { new("Observation", "prior-factor", ContentSha256: priorDependency) }).ToArray(), [], []));
        public Task<BondWorkflowResult?> CalculateIncomeScheduleAsync(BondWorkflowRequest request, DateOnly freezeIndexAt,
            CancellationToken ct = default) => CalculateAsync(request, ct);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
