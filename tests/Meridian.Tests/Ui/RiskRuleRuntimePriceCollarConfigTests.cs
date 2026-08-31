using System.Text.Json;
using FluentAssertions;
using Meridian.Execution.Services;
using Meridian.Risk.Rules;
using Meridian.Ui.Shared.Services;
using Meridian.Tests.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Covers the price collar's configuration wiring: the path from an operator's update through
/// durable state to the threshold the enforced rule reads.
/// <para>
/// The rule itself is covered elsewhere. What is under test here is that a configured collar is
/// actually <em>reachable</em> — a rule wired to a threshold nothing can set approves every order
/// while the panel reports a control in place, which is worse than having no collar at all,
/// because the desk believes it has one.
/// </para>
/// </summary>
public sealed class RiskRuleRuntimePriceCollarConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-risk-collar-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// The whole point of the wiring: a value set by an operator reaches the accessor the
    /// registered rule closes over. Asserting the read DTO alone would pass even if the live field
    /// the rule reads were never published.
    /// </summary>
    [Fact]
    public async Task SettingTheCollar_ReachesTheThresholdTheRuleReads()
    {
        var service = BuildService();

        var config = await service.UpdateConfigAsync(
            "PriceCollar",
            new RiskRuleConfigUpdateRequest(PriceCollarPercent: 3m, Reason: "desk band"),
            actor: "operator");

        config!.PriceCollarPercent.Should().Be(3m);
        service.PriceCollarThresholds.CollarPercent.Should()
            .Be(3m, "the rule closes over this accessor, not over the DTO");
    }

    /// <summary>
    /// Lowercase, hyphenless, and mixed-case spellings all reach the same rule, matching every
    /// other entry in the catalogue. An unrecognised name returns null rather than silently
    /// succeeding against nothing.
    /// </summary>
    [Theory]
    [InlineData("pricecollar")]
    [InlineData("PRICECOLLAR")]
    [InlineData("PriceCollar")]
    public async Task RuleName_IsAcceptedInTheSameSpellingsAsEveryOtherRule(string ruleName)
    {
        var service = BuildService();

        var config = await service.UpdateConfigAsync(
            ruleName,
            new RiskRuleConfigUpdateRequest(PriceCollarPercent: 2m),
            actor: "operator");

        config!.RuleName.Should().Be("PriceCollar");
    }

    /// <summary>
    /// A collar of 100 or more is refused for the same reason the fat-finger band is: a sell's
    /// aggressive deviation is (reference - price) / reference, which for any positive price is
    /// strictly under 100%. Accepting one would leave the panel reporting a configured collar that
    /// can never park a sell, however far through the bid it is priced.
    /// </summary>
    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    public async Task Collar_AtOrAbove100_IsRefusedRatherThanHalfEnforced(decimal collarPercent)
    {
        var service = BuildService();

        var attempt = () => service.UpdateConfigAsync(
            "PriceCollar",
            new RiskRuleConfigUpdateRequest(PriceCollarPercent: collarPercent),
            actor: "operator");

        await attempt.Should().ThrowAsync<ArgumentOutOfRangeException>();
        service.PriceCollarThresholds.CollarPercent.Should()
            .BeNull("a refused update must not leave a partially applied threshold");
    }

    /// <summary>
    /// Zero clears the collar, matching <c>NormalizeThreshold</c> everywhere else: an operator
    /// disabling a control does so by setting it to zero, not by being told zero is invalid.
    /// </summary>
    [Fact]
    public async Task Collar_SetToZero_ClearsTheBandRatherThanFailing()
    {
        var service = BuildService();
        await service.UpdateConfigAsync(
            "PriceCollar",
            new RiskRuleConfigUpdateRequest(PriceCollarPercent: 4m),
            actor: "operator");

        var config = await service.UpdateConfigAsync(
            "PriceCollar",
            new RiskRuleConfigUpdateRequest(PriceCollarPercent: 0m),
            actor: "operator");

        config!.PriceCollarPercent.Should().BeNull();
        service.PriceCollarThresholds.CollarPercent.Should().BeNull();
    }

    /// <summary>
    /// The two price controls are configured independently and must stay that way. Setting one
    /// through a shared update path that cleared the other would disable a live rail as a side
    /// effect of tightening its neighbour.
    /// </summary>
    [Fact]
    public async Task SettingTheCollar_LeavesTheFatFingerBandsAlone()
    {
        var service = BuildService();
        await service.UpdateConfigAsync(
            "FatFinger",
            new RiskRuleConfigUpdateRequest(MaxOrderQuantity: 500m, MaxPriceDeviationPercent: 10m),
            actor: "operator");

        await service.UpdateConfigAsync(
            "PriceCollar",
            new RiskRuleConfigUpdateRequest(PriceCollarPercent: 3m),
            actor: "operator");

        var fatFinger = service.FatFingerThresholds;
        fatFinger.MaxOrderQuantity.Should().Be(500m);
        fatFinger.MaxPriceDeviationPercent.Should().Be(10m);
        service.PriceCollarThresholds.CollarPercent.Should().Be(3m);
    }

    /// <summary>
    /// A collar the desk configured must survive a restart. Persisting it and then failing to
    /// restore it would silently drop the control at the moment nobody is watching for it.
    /// </summary>
    [Fact]
    public async Task Collar_SurvivesARestart()
    {
        var snapshotPath = Path.Combine(_root, "risk-rules.json");
        await BuildService(snapshotPath: snapshotPath).UpdateConfigAsync(
            "PriceCollar",
            new RiskRuleConfigUpdateRequest(PriceCollarPercent: 6m),
            actor: "operator");

        var reloaded = BuildService(snapshotPath: snapshotPath);

        reloaded.PriceCollarThresholds.CollarPercent.Should().Be(6m);
    }

    /// <summary>
    /// A snapshot carrying a collar that can never park a sell fails the host closed rather than
    /// hydrating. The update path refuses such a value, so one can only reach the file by
    /// hand-editing or by a future schema change; normalizing it to null would resurrect the
    /// half-dead control the update path exists to refuse, and skipping the rest of the load would
    /// leave the rails above it applied and the ones below at their defaults.
    /// </summary>
    [Theory]
    [InlineData(100)]
    [InlineData(-1)]
    public void Snapshot_WithAnUnenforceableCollar_FailsTheHostClosed(int collarPercent)
    {
        var snapshotPath = WriteSnapshot(
            $$"""
            {"MaxDrawdownPercent":10,"MaxOrdersPerMinute":60,"UpdatedAt":"2026-08-11T00:00:00+00:00",
             "UpdatedBy":"test","Reason":null,"PriceCollarPercent":{{collarPercent}}}
            """);

        var construct = () => BuildService(snapshotPath: snapshotPath);

        construct.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// The ordinary case: a snapshot written before the collar existed carries no collar field at
    /// all, and must hydrate as unconfigured rather than failing a host that was running fine.
    /// </summary>
    [Fact]
    public void Snapshot_PredatingTheCollar_HydratesTheRestUnchanged()
    {
        var snapshotPath = WriteSnapshot(
            """
            {"MaxDrawdownPercent":10,"MaxOrdersPerMinute":60,"UpdatedAt":"2026-08-11T00:00:00+00:00",
             "UpdatedBy":"test","Reason":null,"MaxPriceDeviationPercent":12}
            """);

        var service = BuildService(snapshotPath: snapshotPath);

        service.PriceCollarThresholds.CollarPercent.Should().BeNull();
        service.FatFingerThresholds.MaxPriceDeviationPercent.Should()
            .Be(12m, "an absent collar must not disturb the rails hydrated beside it");
    }

    /// <summary>
    /// The panel must show the collar as its own rule rather than folding it into the fat-finger
    /// row: the two produce different outcomes — one parks for approval, the other rejects — and a
    /// single row could only claim one of them.
    /// </summary>
    [Fact]
    public async Task Status_ReportsTheCollarAsAnEscalatingRuleOfItsOwn()
    {
        await using var audit = NewAudit();
        var service = BuildService(audit);

        var unconfigured = await service.GetStatusAsync("PriceCollar");
        unconfigured!.Severity.Should().Be("Escalate", "a collar parks orders, it does not reject them");
        unconfigured.State.Should().Be("Observe");
        unconfigured.Threshold.Should().Be("unconfigured");

        await service.UpdateConfigAsync(
            "PriceCollar",
            new RiskRuleConfigUpdateRequest(PriceCollarPercent: 3m),
            actor: "operator");

        var configured = await service.GetStatusAsync("PriceCollar");
        configured!.State.Should().Be("Healthy");
        configured.Threshold.Should().Contain("3");
        configured.IsBreached.Should().BeFalse();
    }

    /// <summary>
    /// The collar's refusal message shares the phrase "has no reference price" with the fat-finger
    /// band's, so a status query keyed on wording would let each rule claim the other's pricing
    /// gaps. This pins the split on the structured code in both directions.
    /// </summary>
    [Fact]
    public async Task Status_DoesNotClaimTheFatFingerBandsPricingGaps()
    {
        await using var audit = NewAudit();
        await audit.RecordAsync(RejectionEntry(
            rule: "FatFinger",
            code: FatFingerRule.UnmeasurableCode,
            message: "Fat-finger band: AAPL has no reference price to measure the order price against."));

        var service = BuildService(audit);
        await service.UpdateConfigAsync(
            "PriceCollar",
            new RiskRuleConfigUpdateRequest(PriceCollarPercent: 3m),
            actor: "operator");

        var status = await service.GetStatusAsync("PriceCollar");

        status!.State.Should().Be("Healthy", "that refusal belongs to the fat-finger band");
        status.RecentViolations.Should().BeEmpty();
    }

    /// <summary>
    /// And the converse: the collar's own pricing gap does reach its row, as an Observe condition
    /// rather than a breach. A collar that refused an order it could not price and then reported
    /// itself healthy would hide the one outcome an operator has to act on.
    /// </summary>
    [Fact]
    public async Task Status_ReportsItsOwnPricingGapWithoutCallingItABreach()
    {
        await using var audit = NewAudit();
        await audit.RecordAsync(RejectionEntry(
            rule: "PriceCollar",
            code: PriceCollarRule.UnmeasurableCode,
            message: "Price collar: AAPL has no reference price to measure the order price against."));

        var service = BuildService(audit);
        await service.UpdateConfigAsync(
            "PriceCollar",
            new RiskRuleConfigUpdateRequest(PriceCollarPercent: 3m),
            actor: "operator");

        var status = await service.GetStatusAsync("PriceCollar");

        status!.State.Should().Be("Observe");
        status.IsBreached.Should().BeFalse("the rule refused an order it could not price, it did not measure one past a band");
        status.Summary.Should().Contain("no reference price");
    }

    /// <summary>
    /// An update naming no collar value is refused rather than treated as a no-op success, so an
    /// operator who mistyped the field name is told rather than shown a confirmation for a change
    /// that did not happen.
    /// </summary>
    [Fact]
    public async Task Update_WithNoCollarValue_IsRefused()
    {
        var service = BuildService();

        var attempt = () => service.UpdateConfigAsync(
            "PriceCollar",
            new RiskRuleConfigUpdateRequest(MaxPriceDeviationPercent: 10m),
            actor: "operator");

        await attempt.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// A collar at or above the fat-finger band can never park an order, because the harder rule
    /// refuses first. That is accepted rather than refused — either configuration order can produce
    /// it — so the evidence has to be emitted, and from <em>both</em> update paths: setting a wide
    /// collar and narrowing the band beneath an existing one leave the same dead control, and a
    /// warning on only one of them would be silent exactly when an operator tightened a rail and
    /// stranded its neighbour without touching it.
    /// </summary>
    [Theory]
    [InlineData("PriceCollar")]
    [InlineData("FatFinger")]
    public async Task Collar_StrandedBehindTheFatFingerBand_IsReportedFromEitherUpdatePath(string secondUpdate)
    {
        var logger = new CapturingLogger<RiskRuleRuntimeService>();
        var service = BuildService(logger: logger);

        // Whichever rail is set second, the end state is the same: a 10% collar behind a 10% band.
        if (secondUpdate == "PriceCollar")
        {
            await service.UpdateConfigAsync(
                "FatFinger",
                new RiskRuleConfigUpdateRequest(MaxPriceDeviationPercent: 10m),
                actor: "operator");
            await service.UpdateConfigAsync(
                "PriceCollar",
                new RiskRuleConfigUpdateRequest(PriceCollarPercent: 10m),
                actor: "operator");
        }
        else
        {
            await service.UpdateConfigAsync(
                "PriceCollar",
                new RiskRuleConfigUpdateRequest(PriceCollarPercent: 10m),
                actor: "operator");
            await service.UpdateConfigAsync(
                "FatFinger",
                new RiskRuleConfigUpdateRequest(MaxPriceDeviationPercent: 10m),
                actor: "operator");
        }

        logger.Entries.Should().Contain(
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can never park an order"),
            "an operator who has configured a control that cannot fire must be told");

        // Warned, not refused: both values are still live, so the desk keeps the rail it asked for.
        service.PriceCollarThresholds.CollarPercent.Should().Be(10m);
        service.FatFingerThresholds.MaxPriceDeviationPercent.Should().Be(10m);
    }

    /// <summary>
    /// The converse, so the warning is not simply always emitted: a collar tighter than the band
    /// is the intended configuration and must pass without comment.
    /// </summary>
    [Fact]
    public async Task Collar_InsideTheFatFingerBand_IsNotReportedAsStranded()
    {
        var logger = new CapturingLogger<RiskRuleRuntimeService>();
        var service = BuildService(logger: logger);

        await service.UpdateConfigAsync(
            "FatFinger",
            new RiskRuleConfigUpdateRequest(MaxPriceDeviationPercent: 10m),
            actor: "operator");
        await service.UpdateConfigAsync(
            "PriceCollar",
            new RiskRuleConfigUpdateRequest(PriceCollarPercent: 3m),
            actor: "operator");

        logger.Entries.Should().NotContain(entry => entry.Message.Contains("can never park an order"));
    }

    private ExecutionAuditTrailService NewAudit() => new(
        Path.Combine(_root, "audit"),
        NullLogger<ExecutionAuditTrailService>.Instance);

    private static ExecutionAuditEntry RejectionEntry(string rule, string code, string message) => new(
        AuditId: Guid.NewGuid().ToString("N"),
        Category: "Order",
        Action: "OrderRejected",
        Outcome: "Rejected",
        OccurredAt: DateTimeOffset.UtcNow,
        Symbol: "AAPL",
        Reason: code,
        Metadata: new Dictionary<string, string>
        {
            ["decisionSource"] = "risk",
            ["violation.count"] = "1",
            ["violation.0.rule"] = rule,
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
        string? snapshotPath = null,
        ILogger<RiskRuleRuntimeService>? logger = null)
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
            logger ?? NullLogger<RiskRuleRuntimeService>.Instance,
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
