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

    private sealed class StubReservation : IRiskReservation
    {
        public bool Committed { get; private set; }

        public bool RolledBack { get; private set; }

        public void Commit() => Committed = true;

        public void Rollback() => RolledBack = true;
    }
}
