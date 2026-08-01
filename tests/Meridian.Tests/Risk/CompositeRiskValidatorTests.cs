using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Risk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Risk;

public sealed class CompositeRiskValidatorTests
{
    [Fact]
    public async Task ValidateOrderAsync_WithBlockingRule_ReturnsRejectedResult()
    {
        var validator = Build(
            new StubRiskRule("first"),
            new StubRiskRule("second", Finding("BLOCKED", "blocked")));

        var outcome = await validator.ValidateOrderAsync(CreateOrder());

        outcome.Result.IsApproved.Should().BeFalse();
        outcome.Result.Decision.Should().Be(RiskDecisionKind.Rejected);
        outcome.Result.RejectReason.Should().Be("blocked");
        outcome.Result.RejectCode.Should().Be("BLOCKED");
    }

    /// <summary>
    /// Replaces the removed short-circuit test. The engine's whole purpose is that an order
    /// breaching several limits reports all of them.
    /// </summary>
    [Fact]
    public async Task ValidateOrderAsync_WhenEarlyRuleBlocks_StillEvaluatesLaterRules()
    {
        var first = new StubRiskRule("first", Finding("FIRST", "first failed"), priority: 0);
        var second = new StubRiskRule("second", Finding("SECOND", "second failed"), priority: 1);

        var outcome = await Build(first, second).ValidateOrderAsync(CreateOrder());

        first.EvaluateCalls.Should().Be(1);
        second.EvaluateCalls.Should().Be(1);
        outcome.Result.Violations.Should().HaveCount(2);
    }

    [Fact]
    public async Task ValidateOrderAsync_OrdersViolationsBySeverityThenPriority()
    {
        var outcome = await Build(
                new StubRiskRule("warn", Finding("W", "w"), priority: 0, severity: RiskRuleSeverity.Warning),
                new StubRiskRule("critical", Finding("C", "c"), priority: 9, severity: RiskRuleSeverity.Critical),
                new StubRiskRule("error", Finding("E", "e"), priority: 5, severity: RiskRuleSeverity.Error))
            .ValidateOrderAsync(CreateOrder());

        outcome.Result.Violations.Select(v => v.Code).Should().ContainInOrder("C", "E", "W");
    }

    [Theory]
    [InlineData(RiskRuleSeverity.Info, RiskDecisionKind.ApprovedWithWarnings)]
    [InlineData(RiskRuleSeverity.Warning, RiskDecisionKind.ApprovedWithWarnings)]
    [InlineData(RiskRuleSeverity.Error, RiskDecisionKind.Rejected)]
    [InlineData(RiskRuleSeverity.Critical, RiskDecisionKind.Rejected)]
    public async Task ValidateOrderAsync_ResolvesDecisionFromDeclaredSeverity(
        RiskRuleSeverity severity,
        RiskDecisionKind expected)
    {
        var outcome = await Build(new StubRiskRule("rule", Finding("C", "m"), severity: severity))
            .ValidateOrderAsync(CreateOrder());

        outcome.Result.Decision.Should().Be(expected);
        outcome.Result.IsApproved.Should().Be(expected != RiskDecisionKind.Rejected);
    }

    [Fact]
    public async Task ValidateOrderAsync_WithAcknowledgementRequestOnNonBlockingRule_Escalates()
    {
        var finding = new RiskFinding("ACK", "needs sign-off", RequiresAcknowledgement: true);

        var outcome = await Build(new StubRiskRule("rule", finding, severity: RiskRuleSeverity.Warning))
            .ValidateOrderAsync(CreateOrder());

        outcome.Result.Decision.Should().Be(RiskDecisionKind.Escalated);
        outcome.Result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateOrderAsync_RejectReasonSelectsBlockingViolationNotFirstViolation()
    {
        // A Critical acknowledgement-flagged finding sorts first, but the reject reason must come
        // from a violation that actually blocks.
        var outcome = await Build(
                new StubRiskRule("warn", new RiskFinding("W", "warning", RequiresAcknowledgement: true),
                    severity: RiskRuleSeverity.Warning),
                new StubRiskRule("block", Finding("B", "the blocker"), severity: RiskRuleSeverity.Error))
            .ValidateOrderAsync(CreateOrder());

        outcome.Result.Decision.Should().Be(RiskDecisionKind.Rejected);
        outcome.Result.BlockingViolation!.Code.Should().Be("B");
        outcome.Result.RejectReason.Should().Be("the blocker");
    }

    [Fact]
    public async Task ValidateOrderAsync_WhenRuleThrows_FailsClosedWithNonNullReason()
    {
        var outcome = await Build(
                new StubRiskRule("boom", severity: RiskRuleSeverity.Info) { Throw = new InvalidOperationException("x") })
            .ValidateOrderAsync(CreateOrder());

        outcome.Result.Decision.Should().Be(RiskDecisionKind.Rejected);
        // An Info rule that throws must still block, and must still produce an attributable reason.
        outcome.Result.BlockingViolation.Should().NotBeNull();
        outcome.Result.RejectCode.Should().Be(CompositeRiskValidator.EvaluationFailedCode);
        outcome.Result.RejectReason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Cancelling before the call would exit at the first <c>ThrowIfCancellationRequested</c>
    /// before any rule ran, leaving the partial-reservation cleanup path untested and the
    /// assertion vacuously true. Cancellation has to happen while a later rule is mid-evaluation,
    /// after an earlier rule has already taken capacity.
    /// </summary>
    [Fact]
    public async Task ValidateOrderAsync_WhenCancelledAfterReserving_ReleasesPartialReservations()
    {
        using var cts = new CancellationTokenSource();
        var reserving = new StubReservingRule("throttle") { Priority = 0 };
        var canceller = new StubRiskRule("late", priority: 1) { CancelDuringEvaluation = cts };
        var validator = Build(reserving, canceller);

        var act = async () => await validator.ValidateOrderAsync(CreateOrder(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        reserving.Reservations.Should().ContainSingle()
            .Which.RolledBack.Should().BeTrue("capacity taken before cancellation must be released");
    }

    [Fact]
    public async Task ValidateOrderAsync_WhenAdmitted_TransfersReservationsUnsettled()
    {
        var reserving = new StubReservingRule("throttle");

        var outcome = await Build(reserving).ValidateOrderAsync(CreateOrder());

        outcome.Reservations.Should().HaveCount(1);
        reserving.Reservations.Should().OnlyContain(r => !r.Committed && !r.RolledBack);
    }

    [Fact]
    public async Task ValidateOrderAsync_WhenBlocked_RollsBackReservationsAndReturnsNone()
    {
        var reserving = new StubReservingRule("throttle");
        var blocking = new StubRiskRule("block", Finding("B", "no"));

        var outcome = await Build(reserving, blocking).ValidateOrderAsync(CreateOrder());

        outcome.Reservations.Should().BeEmpty();
        reserving.Reservations.Should().OnlyContain(r => r.RolledBack);
    }

    [Fact]
    public async Task ValidateOrderAsync_WithSyncFastPath_DoesNotCallAsyncPath()
    {
        var rule = new StubRiskRule("sync", syncFinding: Finding("S", "sync")) { HasSync = true };

        var outcome = await Build(rule).ValidateOrderAsync(CreateOrder());

        rule.EvaluateCalls.Should().Be(0);
        rule.SyncEvaluateCalls.Should().Be(1);
        outcome.Result.Violations.Should().ContainSingle(v => v.Code == "S");
    }

    /// <summary>
    /// A rule that ignores its token is abandoned mid-evaluation, so the validator never receives
    /// the reservation it eventually takes. Nothing downstream can release a handle it was never
    /// given, so the release has to be arranged before abandoning the evaluation — otherwise the
    /// slot is consumed forever and repeated cancellations starve the rule.
    /// </summary>
    [Fact]
    public async Task ValidateOrderAsync_WhenCancelledWhileAReservingRuleIsInFlight_RollsBackItsLateReservation()
    {
        var evaluation = new TaskCompletionSource<RiskRuleReservationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reservation = new StubReservation();
        var validator = Build(new StubDetachedReservingRule(evaluation.Task));

        using var cts = new CancellationTokenSource();
        var validation = validator.ValidateOrderAsync(CreateOrder(), cts.Token);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validation);

        // Only now does the abandoned rule finish, holding capacity nobody is tracking.
        evaluation.SetResult(new RiskRuleReservationResult(null, reservation));

        await WaitUntilAsync(() => reservation.RolledBack);
        reservation.RolledBack.Should().BeTrue("an abandoned evaluation must not keep its slot");
        reservation.Committed.Should().BeFalse();
    }

    /// <summary>
    /// Cleanup runs on the failure path, so one faulty rule must neither strand its neighbours'
    /// capacity nor replace the rejection the caller is actually waiting for.
    /// </summary>
    [Fact]
    public async Task ValidateOrderAsync_WhenARollbackThrows_StillReleasesTheOthersAndReportsTheRejection()
    {
        var healthy = new StubReservingRule("healthy") { Priority = 2 };
        var validator = Build(
            new StubThrowingRollbackRule("faulty") { Priority = 1 },
            healthy,
            new StubRiskRule("blocker", Finding("BLOCKED", "blocked"), priority: 3));

        var outcome = await validator.ValidateOrderAsync(CreateOrder());

        outcome.Result.RejectCode.Should().Be("BLOCKED", "a failed rollback must not mask the decision");
        healthy.Reservations.Should().ContainSingle().Which.RolledBack.Should()
            .BeTrue("a faulty rule must not strand another rule's capacity");
    }

    /// <summary>
    /// A rule that declares both must still reserve. Taking the sync fast path first would skip
    /// <c>EvaluateAndReserveAsync</c> entirely, admitting concurrent orders without consuming any of
    /// the finite capacity the rule exists to protect — and doing it silently.
    /// </summary>
    [Fact]
    public async Task ValidateOrderAsync_WithAReservingRuleThatAlsoDeclaresASyncFastPath_StillReserves()
    {
        var rule = new StubReservingRule("both") { AlsoDeclaresSyncFastPath = true };

        var outcome = await Build(rule).ValidateOrderAsync(CreateOrder());

        rule.Reservations.Should().ContainSingle("the reserving path is the stronger contract");
        outcome.Reservations.Should().ContainSingle();
    }

    /// <summary>
    /// The engine-failure code is public, so a rule may legitimately report it. Promoting on the
    /// code would make it a second admission lever and let a Warning rule reject an order — the
    /// contradiction between severity and outcome this design exists to remove.
    /// </summary>
    [Fact]
    public async Task ValidateOrderAsync_WhenARuleReportsTheFailureCodeItself_KeepsItsDeclaredSeverity()
    {
        var validator = Build(new StubRiskRule(
            "reporter",
            Finding(CompositeRiskValidator.EvaluationFailedCode, "recoverable lookup issue"),
            severity: RiskRuleSeverity.Warning));

        var outcome = await validator.ValidateOrderAsync(CreateOrder());

        outcome.Result.Decision.Should().Be(RiskDecisionKind.ApprovedWithWarnings);
        outcome.Result.IsApproved.Should().BeTrue();
        outcome.Result.Violations.Should().ContainSingle()
            .Which.Severity.Should().Be(RiskRuleSeverity.Warning);
    }

    /// <summary>
    /// Nothing enforces that rule names are unique, so ordering must not recover priority by name.
    /// Here the earlier rule sharing the duplicated name produces no finding, and recovering by name
    /// would hand its priority to the late duplicate — sorting it ahead of the genuinely
    /// higher-priority rule and reporting the wrong violation as the rejection reason.
    /// </summary>
    [Fact]
    public async Task ValidateOrderAsync_WithDuplicateRuleNames_AttributesTheRejectionToPriority()
    {
        var validator = Build(
            new StubRiskRule("Shared", priority: 1),
            new StubRiskRule("Middle", Finding("MIDDLE", "middle blocked"), priority: 5),
            new StubRiskRule("Shared", Finding("LATE", "late blocked"), priority: 10));

        var outcome = await validator.ValidateOrderAsync(CreateOrder());

        outcome.Result.RejectCode.Should().Be(
            "MIDDLE",
            "priority 5 outranks priority 10 regardless of which rule shares a name");
        outcome.Result.Violations.Select(violation => violation.Code)
            .Should().ContainInOrder("MIDDLE", "LATE");
    }

    /// <summary>
    /// A timeout the timer refuses would otherwise throw from <c>CancelAfter</c> before the
    /// fail-closed handler is entered, and the OMS calls the validator outside its own submission
    /// handler — so every order would come back as an unstructured exception rather than a risk
    /// decision. A gate that cannot be configured correctly should not start.
    /// </summary>
    [Theory]
    [InlineData(-2)]
    [InlineData(-1000)]
    public void Constructor_WithATimeoutBelowInfinite_Throws(int milliseconds)
    {
        var act = () => new CompositeRiskValidator(
            [],
            NullLogger<CompositeRiskValidator>.Instance,
            TimeSpan.FromMilliseconds(milliseconds));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("perRuleTimeout");
    }

    [Fact]
    public void Constructor_WithInfiniteTimeout_IsAccepted()
    {
        var act = () => new CompositeRiskValidator(
            [],
            NullLogger<CompositeRiskValidator>.Instance,
            Timeout.InfiniteTimeSpan);

        act.Should().NotThrow("infinite is the documented way to disable the bound");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        // The release runs on a continuation of the abandoned task, so it is not observable the
        // instant the awaited call throws.
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    private static CompositeRiskValidator Build(params IRiskRule[] rules) =>
        new(rules, NullLogger<CompositeRiskValidator>.Instance);

    private static RiskFinding Finding(string code, string message) => new(code, message);

    private static OrderRequest CreateOrder() => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 10m
    };

    private sealed class StubRiskRule(
        string ruleName,
        RiskFinding? finding = null,
        int priority = 0,
        RiskRuleSeverity severity = RiskRuleSeverity.Error,
        RiskFinding? syncFinding = null) : IRiskRule
    {
        public string RuleName => ruleName;

        public int Priority => priority;

        public RiskRuleSeverity Severity => severity;

        public bool HasSync { get; init; }

        public bool HasSyncFastPath => HasSync;

        public Exception? Throw { get; init; }

        /// <summary>Cancels mid-evaluation so earlier rules have already reserved capacity.</summary>
        public CancellationTokenSource? CancelDuringEvaluation { get; init; }

        public int EvaluateCalls { get; private set; }

        public int SyncEvaluateCalls { get; private set; }

        public RiskFinding? TryEvaluate(OrderRequest request)
        {
            SyncEvaluateCalls++;
            return syncFinding;
        }

        public Task<RiskFinding?> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
        {
            EvaluateCalls++;
            if (CancelDuringEvaluation is not null)
            {
                CancelDuringEvaluation.Cancel();
                ct.ThrowIfCancellationRequested();
            }

            if (Throw is not null)
            {
                throw Throw;
            }

            return Task.FromResult(finding);
        }
    }

    private sealed class StubReservingRule(string ruleName) : IReservingRiskRule
    {
        public string RuleName => ruleName;

        public int Priority { get; init; }

        public RiskRuleSeverity Severity => RiskRuleSeverity.Error;

        /// <summary>The unsupported-looking combination the validator has to handle safely.</summary>
        public bool AlsoDeclaresSyncFastPath { get; init; }

        public bool HasSyncFastPath => AlsoDeclaresSyncFastPath;

        public List<StubReservation> Reservations { get; } = [];

        public Task<RiskFinding?> EvaluateAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult<RiskFinding?>(null);

        public Task<RiskRuleReservationResult> EvaluateAndReserveAsync(
            OrderRequest request,
            CancellationToken ct = default)
        {
            var reservation = new StubReservation();
            Reservations.Add(reservation);
            return Task.FromResult(new RiskRuleReservationResult(null, reservation));
        }
    }

    /// <summary>
    /// A reserving rule that ignores its cancellation token and completes only when the test says
    /// so — the contract violation the abandonment path exists to survive.
    /// </summary>
    private sealed class StubDetachedReservingRule(Task<RiskRuleReservationResult> evaluation)
        : IReservingRiskRule
    {
        public string RuleName => "detached";

        public int Priority => 0;

        public RiskRuleSeverity Severity => RiskRuleSeverity.Error;

        public Task<RiskFinding?> EvaluateAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult<RiskFinding?>(null);

        public Task<RiskRuleReservationResult> EvaluateAndReserveAsync(
            OrderRequest request,
            CancellationToken ct = default) => evaluation;
    }

    /// <summary>A reserving rule whose reservation refuses to be released.</summary>
    private sealed class StubThrowingRollbackRule(string ruleName) : IReservingRiskRule
    {
        public string RuleName => ruleName;

        public int Priority { get; init; }

        public RiskRuleSeverity Severity => RiskRuleSeverity.Error;

        public Task<RiskFinding?> EvaluateAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult<RiskFinding?>(null);

        public Task<RiskRuleReservationResult> EvaluateAndReserveAsync(
            OrderRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(new RiskRuleReservationResult(null, new ThrowingReservation()));

        private sealed class ThrowingReservation : IRiskReservation
        {
            public void Commit() => throw new InvalidOperationException("commit failed");

            public void Rollback() => throw new InvalidOperationException("rollback failed");
        }
    }

    private sealed class StubReservation : IRiskReservation
    {
        public bool Committed { get; private set; }

        public bool RolledBack { get; private set; }

        public void Commit() => Committed = true;

        public void Rollback() => RolledBack = true;
    }
}
