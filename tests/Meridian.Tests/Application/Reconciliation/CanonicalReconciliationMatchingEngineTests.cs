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

    private static MatchingTolerances CreateTolerances() => new(0m, 0m, 0m, 0m, TimeSpan.FromMinutes(5));

    private static ReconciliationRun CreateRun(IReadOnlyList<DataSourceSnapshot> snapshots) =>
        new(Guid.NewGuid(), new DateOnly(2026, 5, 28), DateTimeOffset.UtcNow, false, 1, "recon:2026-05-28", snapshots);

    private static DataSourceSnapshot CreateSnapshot(string id, ReconciliationSourceType sourceType, IReadOnlyList<NormalizedPosition> positions) =>
        new(id, sourceType, DateTimeOffset.UtcNow, "v1", positions, Array.Empty<NormalizedCashEntry>());

    private static NormalizedPosition CreatePosition(string id, string canonicalId, decimal quantity, decimal price, decimal marketValue) =>
        new(id, canonicalId, null, null, canonicalId, null, quantity, price, marketValue, "USD", DateTimeOffset.UtcNow, id);
}
