using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Time.Testing;

namespace Meridian.Tests.Ui;

/// <summary>The alertable health series reflects unbooked broker fills, blocked runs and overdue schedules.</summary>
public sealed class ThreeWayReconciliationMetricsTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 11, 13, 23, 59, 59, TimeSpan.Zero);

    [Fact]
    public void MissingFillInLedgerBreak_IsUnhealthy()
    {
        var account = Guid.NewGuid();
        new ThreeWayReconciliationMetrics().RecordRun(Run(account, "Breaks",
            Check("Break", ThreeWayFillClassification.MissingFillInLedger)));

        ThreeWayReconciliationMetrics.HealthValue(account).Should().Be(ThreeWayReconciliationMetrics.Unhealthy);
    }

    [Fact]
    public void BlockedRun_IsDegraded_AndOtherBreaksStayHealthy()
    {
        var blocked = Guid.NewGuid();
        var priceBreak = Guid.NewGuid();
        var metrics = new ThreeWayReconciliationMetrics();

        metrics.RecordRun(Run(blocked, "Blocked", Check("Blocked", null)));
        metrics.RecordRun(Run(priceBreak, "Breaks", Check("Break", ThreeWayFillClassification.PriceMismatch)));

        ThreeWayReconciliationMetrics.HealthValue(blocked).Should().Be(ThreeWayReconciliationMetrics.Degraded);
        ThreeWayReconciliationMetrics.HealthValue(priceBreak).Should().Be(ThreeWayReconciliationMetrics.Healthy);
    }

    [Fact]
    public void ScheduleWithoutRecentRun_DegradesAfterMaximumRunAge()
    {
        var account = Guid.NewGuid();
        var clock = new FakeTimeProvider(Cutoff);
        var metrics = new ThreeWayReconciliationMetrics(clock);

        metrics.ObserveSchedule(account);
        ThreeWayReconciliationMetrics.HealthValue(account).Should().Be(ThreeWayReconciliationMetrics.Healthy);

        metrics.RecordRun(Run(account, "Matched", Check("Matched", ThreeWayFillClassification.FillMatched)));
        clock.Advance(ThreeWayReconciliationMetrics.MaximumRunAge + TimeSpan.FromMinutes(1));
        metrics.ObserveSchedule(account);

        ThreeWayReconciliationMetrics.HealthValue(account).Should().Be(ThreeWayReconciliationMetrics.Degraded);
    }

    private static ThreeWayReconciliationCheckDto Check(string status, string? classification)
        => new("fill:x", "fill", null, "AAPL", "ledger", "broker", 1m, 1m, 0m, status, "synthetic")
        {
            Classification = classification
        };

    private static ThreeWayReconciliationResultDto Run(Guid account, string status, ThreeWayReconciliationCheckDto check)
        => new("run", "op", new("tenant", "company", "fund", Guid.NewGuid(), account, Guid.NewGuid(), Guid.NewGuid(), "USD"),
            Cutoff, Cutoff, status, [], [check], [], "simulated");
}
