using FluentAssertions;
using Meridian.Application.Reconciliation;
using Meridian.Domain.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

public sealed class CanonicalReconciliationMatchingEngineTests
{
    [Fact]
    public void Run_WhenSinglePositionExists_EmitsTrueBreak()
    {
        var engine = new ReconciliationMatchingEngine();
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, [CreatePosition("p1", "AAPL", 10m, 100m, 1000m)])
        ]);

        var result = engine.Run(run, CreateTolerances());

        result.matches.Should().BeEmpty();
        result.breaks.Should().ContainSingle(b => b.Classification == BreakClassification.TrueBreak && b.RuleId == "true-break-position-v1");
    }

    [Fact]
    public void Run_WhenInstrumentHasMismatchedPositionTuples_EmitsTrueBreak()
    {
        var engine = new ReconciliationMatchingEngine();
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, [CreatePosition("p1", "AAPL", 10m, 100m, 1000m)]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, [CreatePosition("p2", "AAPL", 11m, 100m, 1100m)])
        ]);

        var result = engine.Run(run, CreateTolerances());

        result.matches.Should().BeEmpty();
        result.breaks.Should().ContainSingle(b => b.Classification == BreakClassification.TrueBreak && b.RuleId == "true-break-position-v1");
    }


    [Fact]
    public void Run_WhenPositionFallsInsideToleranceProfile_RecordsProfileAndRuleEvidence()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = new StatementToleranceProfile(
            "month-end-close",
            3,
            [],
            [new PositionToleranceRule("pos-close-qty-price-mv-v3", 1m, 25m, 0.05m)],
            []);
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, [CreatePosition("p1", "AAPL", 10m, 100m, 1000m)]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, [CreatePosition("p2", "AAPL", 10.5m, 100.01m, 1005m)])
        ]);

        var result = engine.Run(run, profile);

        result.breaks.Should().BeEmpty();
        result.matches.Should().ContainSingle(m =>
            m.Classification == BreakClassification.MatchedWithinTolerance &&
            m.ToleranceProfileId == "month-end-close" &&
            m.ToleranceProfileVersion == 3 &&
            m.ToleranceRuleId == "pos-close-qty-price-mv-v3");
        result.evidence.Should().ContainSingle(e =>
            e.RuleId == "pos-close-qty-price-mv-v3" &&
            e.Attributes["toleranceRuleId"] == "pos-close-qty-price-mv-v3");
    }

    [Fact]
    public void Run_WhenCashFallsInsideBasisPointTolerance_RecordsCashRuleEvidence()
    {
        var engine = new ReconciliationMatchingEngine();
        var profile = new StatementToleranceProfile(
            "cash-bps-close",
            2,
            [new CashToleranceRule("cash-bps-5-v2", 0.01m, 5m, TimeSpan.FromDays(1))],
            [],
            []);
        var asOf = new DateTimeOffset(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);
        var run = CreateRun([
            CreateSnapshot("prime", ReconciliationSourceType.Prime, [], [CreateCash("c1", 10000m, asOf)]),
            CreateSnapshot("custodian", ReconciliationSourceType.Custodian, [], [CreateCash("c2", 10004m, asOf.AddHours(12))])
        ]);

        var result = engine.Run(run, profile);

        result.breaks.Should().BeEmpty();
        result.matches.Should().Contain(m =>
            m.Classification == BreakClassification.MatchedWithinTolerance &&
            m.ToleranceProfileId == "cash-bps-close" &&
            m.ToleranceProfileVersion == 2 &&
            m.ToleranceRuleId == "cash-bps-5-v2");
        result.evidence.Should().Contain(e =>
            e.RuleId == "cash-bps-5-v2" &&
            e.Narrative.Contains("cash-bps-5-v2", StringComparison.Ordinal));
    }


    [Fact]
    public async Task GetProfileAsync_WhenProfileRegistered_ReturnsLatestVersion()
    {
        var provider = new InMemoryStatementToleranceProfileProvider([
            StatementToleranceProfile.Default,
            new StatementToleranceProfile("ops", 1, [], [], []),
            new StatementToleranceProfile("ops", 2, [], [], [])
        ]);

        var profile = await provider.GetProfileAsync("ops");

        profile.Version.Should().Be(2);
    }

    [Fact]
    public async Task GetProfileAsync_WhenProfileMissing_ThrowsKeyNotFoundException()
    {
        var provider = new InMemoryStatementToleranceProfileProvider();

        var act = async () => await provider.GetProfileAsync("missing-profile");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetProfileAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var provider = new InMemoryStatementToleranceProfileProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await provider.GetProfileAsync(StatementToleranceProfile.DefaultProfileId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static MatchingTolerances CreateTolerances() => new(0m, 0m, 0m, 0m, TimeSpan.FromMinutes(5));

    private static ReconciliationRun CreateRun(IReadOnlyList<DataSourceSnapshot> snapshots) =>
        new(Guid.NewGuid(), new DateOnly(2026, 5, 28), DateTimeOffset.UtcNow, false, 1, "recon:2026-05-28", snapshots);

    private static DataSourceSnapshot CreateSnapshot(string id, ReconciliationSourceType sourceType, IReadOnlyList<NormalizedPosition> positions) =>
        CreateSnapshot(id, sourceType, positions, []);

    private static DataSourceSnapshot CreateSnapshot(string id, ReconciliationSourceType sourceType, IReadOnlyList<NormalizedPosition> positions, IReadOnlyList<NormalizedCashEntry> cashEntries) =>
        new(id, sourceType, DateTimeOffset.UtcNow, "v1", positions, cashEntries);

    private static NormalizedPosition CreatePosition(string id, string canonicalId, decimal quantity, decimal price, decimal marketValue) =>
        new(id, canonicalId, null, null, canonicalId, null, quantity, price, marketValue, "USD", DateTimeOffset.UtcNow, id);

    private static NormalizedCashEntry CreateCash(string id, decimal amountBase, DateTimeOffset postedAtUtc) =>
        new(id, "acct", amountBase, "USD", amountBase, "USD", postedAtUtc, new DateOnly(2026, 5, 28), id);
}
