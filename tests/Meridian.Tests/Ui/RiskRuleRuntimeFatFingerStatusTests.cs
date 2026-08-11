using System.Text.Json;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Services;
using Meridian.Risk.Rules;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

/// <summary>
/// Covers the operator-facing projection of the fat-finger gate: what the risk panel claims the
/// rule is doing, and what configuration the host will start on.
/// <para>
/// The claims matter as much as the enforcement. A panel that reports a measured band violation
/// when a quote was merely missing, or that stays Constrained on a misdated audit entry, holds the
/// readiness gate closed for a breach that never happened.
/// </para>
/// </summary>
public sealed class RiskRuleRuntimeFatFingerStatusTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-risk-fatfinger-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Evaluate-all lets one rejection carry several findings, and only the most severe reaches the
    /// headline. Behind a Critical exposure breach the fat-finger finding survives only in
    /// <c>violation.*</c> metadata — where the rule is stored as <c>FatFinger</c> and its codes as
    /// <c>FAT_FINGER_*</c>, neither of which contains the hyphenated token the status query uses.
    /// </summary>
    [Fact]
    public async Task FatFingerStatus_SurfacesABreachRecordedOnlyInViolationMetadata()
    {
        await using var audit = NewAudit();

        await audit.RecordAsync(new ExecutionAuditEntry(
            AuditId: Guid.NewGuid().ToString("N"),
            Category: "Order",
            Action: "OrderRejected",
            Outcome: "Rejected",
            OccurredAt: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Reason: "GROSS_EXPOSURE_EXCEEDED",
            Metadata: new Dictionary<string, string>
            {
                ["decisionSource"] = "risk",
                ["violation.count"] = "2",
                ["violation.0.rule"] = "GrossExposure",
                ["violation.0.code"] = "GROSS_EXPOSURE_EXCEEDED",
                ["violation.0.message"] = "Gross exposure would reach 4.1M against a 4M ceiling.",
                ["violation.1.rule"] = "FatFinger",
                ["violation.1.code"] = FatFingerRule.QuantityCode,
                ["violation.1.message"] = "Fat-finger quantity: 100000.00 on AAPL exceeds the 1000.00 per-order ceiling"
            },
            // Deliberately carries no fat-finger wording: metadata is the only evidence here.
            Message: "Gross exposure would reach 4.1M against a 4M ceiling."));

        var status = await BuildService(audit).GetStatusAsync("FatFinger");

        status.Should().NotBeNull();
        status!.IsBreached.Should().BeTrue();
        status.RecentViolations.Should().ContainSingle()
            .Which.Should().Contain("100000", "the panel quotes the fat-finger finding, not the headline");
    }

    /// <summary>
    /// A refusal the rule could not measure is not a measured breach. Reporting one as a band
    /// violation would claim a quantity or price ceiling was exceeded when the only thing that
    /// happened was a missing quote.
    /// </summary>
    [Fact]
    public async Task FatFingerStatus_ReportsAPricingGapRatherThanABreach()
    {
        await using var audit = NewAudit();

        await audit.RecordAsync(RejectionEntry(
            occurredAt: DateTimeOffset.UtcNow,
            code: FatFingerRule.UnmeasurableCode,
            message: "Fat-finger band: AAPL has no reference price to measure the order price against."));

        var status = await BuildService(audit).GetStatusAsync("FatFinger");

        status!.IsBreached.Should().BeFalse();
        status.Summary.Should().Contain("no reference price");
    }

    /// <summary>
    /// A rejected amendment is audited as <c>OrderModifyRejected</c>. Matching only
    /// <c>OrderRejected</c> reported the rule healthy while it was actively refusing modifications.
    /// </summary>
    [Fact]
    public async Task FatFingerStatus_IncludesRefusedAmendments()
    {
        await using var audit = NewAudit();

        await audit.RecordAsync(RejectionEntry(
            occurredAt: DateTimeOffset.UtcNow,
            code: FatFingerRule.PriceDeviationCode,
            message: "Fat-finger price: AAPL at 1.0000 is 99.00% through the 100.0000 reference, beyond the 10.00% band",
            action: "OrderModifyRejected"));

        var status = await BuildService(audit).GetStatusAsync("FatFinger");

        status!.IsBreached.Should().BeTrue();
        status.RecentViolations.Should().ContainSingle();
    }

    /// <summary>
    /// A wrong-side stop trigger is a fat-finger breach and must reach the rule's own status, not
    /// just the audit trail.
    /// </summary>
    [Fact]
    public async Task FatFingerStatus_SurfacesAWrongSideStopTrigger()
    {
        await using var audit = NewAudit();

        await audit.RecordAsync(RejectionEntry(
            occurredAt: DateTimeOffset.UtcNow,
            code: FatFingerRule.StopTriggerCode,
            message: "Fat-finger stop trigger: AAPL stop at 1.0000 is 99.00% on the wrong side of the 100.0000 market"));

        var status = await BuildService(audit).GetStatusAsync("FatFinger");

        status!.IsBreached.Should().BeTrue();
        status.RecentViolations.Should().ContainSingle()
            .Which.Should().Contain("wrong side");
    }

    /// <summary>
    /// A future-dated entry produces a negative age, which trivially satisfies a one-hour ceiling.
    /// Left unbounded, a backward clock step or a misdated retained entry would hold the rule — and
    /// the readiness gate reading it — Constrained until an hour past that timestamp, which for a
    /// badly skewed entry is years.
    /// </summary>
    [Fact]
    public async Task FatFingerStatus_DoesNotTreatAFutureDatedEntryAsLive()
    {
        await using var audit = NewAudit();

        await audit.RecordAsync(RejectionEntry(
            occurredAt: DateTimeOffset.UtcNow.AddYears(3),
            code: FatFingerRule.QuantityCode,
            message: "Fat-finger quantity: 100000.00 on AAPL exceeds the 1000.00 per-order ceiling"));

        var status = await BuildService(audit).GetStatusAsync("FatFinger");

        status!.IsBreached.Should().BeFalse("a misdated entry is not evidence of a recent breach");
    }

    /// <summary>
    /// Misdated entries are dropped before the five-entry truncation, not after. They sort
    /// newest-first, so five far-future rows would otherwise take every slot and evict a genuine
    /// breach from half an hour ago — and the five that survived would then all fail the liveness
    /// bound, reporting the rule Healthy at the exact moment it was constrained.
    /// </summary>
    [Fact]
    public async Task FatFingerStatus_KeepsARealBreachBehindFiveMisdatedEntries()
    {
        await using var audit = NewAudit();
        var now = DateTimeOffset.UtcNow;

        await audit.RecordAsync(RejectionEntry(
            occurredAt: now.AddMinutes(-30),
            code: FatFingerRule.QuantityCode,
            message: "Fat-finger quantity: 100000.00 on AAPL exceeds the 1000.00 per-order ceiling"));

        for (var i = 1; i <= 5; i++)
        {
            await audit.RecordAsync(RejectionEntry(
                occurredAt: now.AddYears(i),
                code: FatFingerRule.QuantityCode,
                message: "Fat-finger quantity: 999999.00 on AAPL exceeds the 1000.00 per-order ceiling"));
        }

        var status = await BuildService(audit).GetStatusAsync("FatFinger");

        status!.IsBreached.Should().BeTrue("the genuine breach must not be evicted by misdated rows");
        status.State.Should().Be("Constrained");
        status.RecentViolations.Should().ContainSingle()
            .Which.Should().Contain("100000");
    }

    /// <summary>
    /// Ordinary drift between a host and its audit sink is measured in seconds, and an entry a
    /// moment ahead of the clock is a real, live breach.
    /// </summary>
    [Fact]
    public async Task FatFingerStatus_TreatsSmallClockSkewAsLive()
    {
        await using var audit = NewAudit();

        await audit.RecordAsync(RejectionEntry(
            occurredAt: DateTimeOffset.UtcNow.AddSeconds(30),
            code: FatFingerRule.QuantityCode,
            message: "Fat-finger quantity: 100000.00 on AAPL exceeds the 1000.00 per-order ceiling"));

        var status = await BuildService(audit).GetStatusAsync("FatFinger");

        status!.IsBreached.Should().BeTrue();
    }

    /// <summary>
    /// A real breach followed by a burst of missing-quote refusals must still hold the rule
    /// Constrained: the unmeasurable split runs over the full audit set, before the five-entry
    /// truncation that would otherwise push the breach out of the window.
    /// </summary>
    [Fact]
    public async Task FatFingerStatus_KeepsARealBreachBehindNewerUnmeasurableRefusals()
    {
        await using var audit = NewAudit();
        var now = DateTimeOffset.UtcNow;

        await audit.RecordAsync(RejectionEntry(
            occurredAt: now.AddMinutes(-30),
            code: FatFingerRule.QuantityCode,
            message: "Fat-finger quantity: 100000.00 on AAPL exceeds the 1000.00 per-order ceiling"));

        for (var i = 0; i < 5; i++)
        {
            await audit.RecordAsync(RejectionEntry(
                occurredAt: now.AddMinutes(-i),
                code: FatFingerRule.UnmeasurableCode,
                message: "Fat-finger band: AAPL has no reference price to measure the order price against."));
        }

        var status = await BuildService(audit).GetStatusAsync("FatFinger");

        status!.IsBreached.Should().BeTrue();
        status.State.Should().Be("Constrained");
    }

    /// <summary>
    /// The status projection makes a <em>time</em> claim — a breach in the last hour holds the rule
    /// constrained — so reading a fixed number of newest entries silently drops that breach as soon
    /// as enough unrelated activity follows it. Two hundred events after a refusal is an ordinary
    /// morning on an active desk, and it turned a live breach into a healthy rule.
    /// </summary>
    [Fact]
    public async Task FatFingerStatus_SurvivesHeavyUnrelatedAuditTrafficInsideTheWindow()
    {
        await using var audit = NewAudit();
        var now = DateTimeOffset.UtcNow;

        await audit.RecordAsync(RejectionEntry(
            occurredAt: now.AddMinutes(-30),
            code: FatFingerRule.QuantityCode,
            message: "Fat-finger quantity: 100000.00 on AAPL exceeds the 1000.00 per-order ceiling"));

        // 250 unrelated submissions, all newer, all well inside the same hour.
        for (var i = 0; i < 250; i++)
        {
            await audit.RecordAsync(new ExecutionAuditEntry(
                AuditId: Guid.NewGuid().ToString("N"),
                Category: "Order",
                Action: "OrderSubmitted",
                Outcome: "Accepted",
                OccurredAt: now.AddMinutes(-20).AddSeconds(i),
                Symbol: "MSFT"));
        }

        var status = await BuildService(audit).GetStatusAsync("FatFinger");

        status!.IsBreached.Should().BeTrue("the breach is still inside the liveness window");
        status.State.Should().Be("Constrained");
    }

    // --- snapshot hydration ---

    /// <summary>
    /// The snapshot path is fail-closed by policy. A band of 100 or more can never reject a sell —
    /// its aggressive deviation is (reference - price) / reference, strictly under 100% for any
    /// positive price — so the update endpoint refuses it, and hydration must not be the back door
    /// that installs what the API rejects.
    /// </summary>
    [Fact]
    public void Snapshot_WithASellDisablingDeviationBand_RefusesToStart()
    {
        var path = WriteSnapshot("""
            {"MaxDrawdownPercent":10,"MaxOrdersPerMinute":60,"UpdatedAt":"2026-08-11T00:00:00+00:00",
             "UpdatedBy":"test","Reason":null,"MaxPriceDeviationPercent":150}
            """);

        var construct = () => BuildService(snapshotPath: path);

        construct.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// A negative ceiling is corruption, not an operator leaving a rail unset. Normalizing it to
    /// null would silently disable the quantity limb while the panel still reported the rule
    /// configured — the same failure the band check above exists to prevent.
    /// </summary>
    [Fact]
    public void Snapshot_WithANegativeQuantityCeiling_RefusesToStart()
    {
        var path = WriteSnapshot("""
            {"MaxDrawdownPercent":10,"MaxOrdersPerMinute":60,"UpdatedAt":"2026-08-11T00:00:00+00:00",
             "UpdatedBy":"test","Reason":null,"MaxOrderQuantity":-1}
            """);

        var construct = () => BuildService(snapshotPath: path);

        construct.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// An absent optional rail is the ordinary case — an operator who has not configured that limb
    /// — and must hydrate as unconfigured rather than fail the host.
    /// </summary>
    [Fact]
    public void Snapshot_WithNoFatFingerRails_StartsUnconfigured()
    {
        var path = WriteSnapshot("""
            {"MaxDrawdownPercent":10,"MaxOrdersPerMinute":60,"UpdatedAt":"2026-08-11T00:00:00+00:00",
             "UpdatedBy":"test","Reason":null}
            """);

        var service = BuildService(snapshotPath: path);

        service.MaxOrderQuantity.Should().BeNull();
        service.MaxPriceDeviationPercent.Should().BeNull();
    }

    [Fact]
    public void Snapshot_WithValidFatFingerRails_Hydrates()
    {
        var path = WriteSnapshot("""
            {"MaxDrawdownPercent":10,"MaxOrdersPerMinute":60,"UpdatedAt":"2026-08-11T00:00:00+00:00",
             "UpdatedBy":"test","Reason":null,"MaxOrderQuantity":1000,"MaxPriceDeviationPercent":10}
            """);

        var service = BuildService(snapshotPath: path);

        service.MaxOrderQuantity.Should().Be(1_000m);
        service.MaxPriceDeviationPercent.Should().Be(10m);
    }

    private ExecutionAuditTrailService NewAudit() => new(
        Path.Combine(_root, "audit"),
        NullLogger<ExecutionAuditTrailService>.Instance);

    private static ExecutionAuditEntry RejectionEntry(
        DateTimeOffset occurredAt,
        string code,
        string message,
        string action = "OrderRejected") => new(
        AuditId: Guid.NewGuid().ToString("N"),
        Category: "Order",
        Action: action,
        Outcome: "Rejected",
        OccurredAt: occurredAt,
        Symbol: "AAPL",
        Reason: code,
        Metadata: new Dictionary<string, string>
        {
            ["decisionSource"] = "risk",
            ["violation.count"] = "1",
            ["violation.0.rule"] = "FatFinger",
            ["violation.0.code"] = code,
            ["violation.0.message"] = message
        },
        Message: message);

    private string WriteSnapshot(string json)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"risk-rules-{Guid.NewGuid():N}.json");
        // Round-trip through the parser so a malformed literal fails the test rather than
        // exercising the catch-all as though it were the validation being asserted.
        using (var document = JsonDocument.Parse(json))
        {
            File.WriteAllText(path, document.RootElement.GetRawText());
        }

        return path;
    }

    private RiskRuleRuntimeService BuildService(
        ExecutionAuditTrailService? audit = null,
        string? snapshotPath = null)
    {
        var services = new ServiceCollection();
        if (audit is not null)
        {
            services.AddSingleton(audit);
        }

        // Point the snapshot at the temp root so the test never reads or writes the operator's
        // real risk-rule settings.
        return new RiskRuleRuntimeService(
            services.BuildServiceProvider(),
            NullLogger<RiskRuleRuntimeService>.Instance,
            new RiskRuleRuntimeOptions(snapshotPath ?? Path.Combine(_root, "risk-rules.json")));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must not fail the test run.
        }
    }
}
