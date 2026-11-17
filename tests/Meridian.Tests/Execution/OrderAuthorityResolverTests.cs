using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Order authority at preflight (governed order command boundary, Phase 2). The failure modes
/// under guard: an order routing under authority it does not hold, an authority-source failure
/// read as approval, and any change to the legacy (unattributed) path every caller still uses.
/// </summary>
public sealed class OrderAuthorityResolverTests
{
    private static readonly Guid AccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
    private static readonly Guid OtherAccountId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);

    // ---- Legacy (unattributed) path: unchanged behavior ----

    [Fact]
    public async Task Paper_Unattributed_IsApprovedWithoutReadiness()
    {
        var readiness = new RecordingReadinessGate(approve: true);
        var resolver = Resolver(ExecutionMode.Paper, readiness);

        var decision = await resolver.EvaluatePreflightAsync(Prepared(Unattributed("run-1")), default);

        decision.IsApproved.Should().BeTrue();
        readiness.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Live_Unattributed_UsesMetadataRunIdForReadiness()
    {
        var readiness = new RecordingReadinessGate(approve: true);
        var resolver = Resolver(ExecutionMode.Live, readiness);

        var decision = await resolver.EvaluatePreflightAsync(Prepared(Unattributed("run-1"), runId: "run-1"), default);

        decision.IsApproved.Should().BeTrue();
        decision.Readiness!.EvidenceReference.Should().Be("audit://run-1");
        readiness.Requests.Should().ContainSingle().Which.RunId.Should().Be("run-1");
    }

    [Fact]
    public async Task Live_Unattributed_WithoutRunId_KeepsLegacyReadinessRefusal()
    {
        var resolver = Resolver(ExecutionMode.Live, new RecordingReadinessGate(approve: true));

        var decision = await resolver.EvaluatePreflightAsync(Prepared(Unattributed(null)), default);

        decision.Refusal!.Source.Should().Be("live order readiness gate");
        decision.Refusal.ReasonCode.Should().Be(OrderAuthorityReasonCodes.LiveOrderReadinessRejected);
        decision.Refusal.Reason.Should().Contain("runId metadata");
    }

    // ---- Kill switch ----

    [Theory]
    [InlineData(ExecutionMode.Paper)]
    [InlineData(ExecutionMode.Live)]
    public async Task KillSwitchAuthority_CannotSubmit(ExecutionMode mode)
    {
        var resolver = Resolver(mode, new RecordingReadinessGate(approve: true));

        var decision = await resolver.EvaluatePreflightAsync(
            Prepared(OrderSubmissionAuthority.ForKillSwitch("ops", "sweep-1")), default);

        decision.Refusal!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.KillSwitchSubmitNotAllowed);
    }

    // ---- Manual (operator ticket / position action) ----

    [Fact]
    public async Task Paper_OperatorTicket_WithMatchingFreshGrant_IsApproved()
    {
        var resolver = Resolver(ExecutionMode.Paper, new RecordingReadinessGate(approve: true));

        var decision = await resolver.EvaluatePreflightAsync(Prepared(Ticket()), default);

        decision.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task OperatorTicket_OrderForAnotherAccount_IsRefused()
    {
        var resolver = Resolver(ExecutionMode.Paper, new RecordingReadinessGate(approve: true));

        var decision = await resolver.EvaluatePreflightAsync(Prepared(Ticket(), fundAccountId: OtherAccountId), default);

        decision.Refusal!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.AccountGrantMismatch);
        decision.Refusal.Source.Should().Be("order authority");
    }

    [Fact]
    public async Task OperatorTicket_GrantOlderThanMaxAge_IsRefused()
    {
        var resolver = Resolver(ExecutionMode.Paper, new RecordingReadinessGate(approve: true),
            options: new OrderAuthorityOptions { MaxGrantAge = TimeSpan.FromMinutes(30) });

        var decision = await resolver.EvaluatePreflightAsync(
            Prepared(Ticket(resolvedAt: Now - TimeSpan.FromMinutes(31))), default);

        decision.Refusal!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.AccountGrantStale);
    }

    [Fact]
    public async Task Live_OperatorTicket_IsRefusedPendingManualIntentContract_WithoutCallingReadiness()
    {
        var readiness = new RecordingReadinessGate(approve: true);
        var resolver = Resolver(ExecutionMode.Live, readiness);

        var decision = await resolver.EvaluatePreflightAsync(Prepared(Ticket()), default);

        decision.Refusal!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.ManualLiveIntentContractNotReviewed);
        readiness.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Live_PositionAction_IsRefusedPendingManualIntentContract()
    {
        var resolver = Resolver(ExecutionMode.Live, new RecordingReadinessGate(approve: true));
        var authority = OrderSubmissionAuthority.ForPositionAction("ops", Grant("ops"), "close-1");

        var decision = await resolver.EvaluatePreflightAsync(Prepared(authority), default);

        decision.Refusal!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.ManualLiveIntentContractNotReviewed);
    }

    // ---- Strategy run ----

    [Fact]
    public async Task Paper_StrategyRun_IsApprovedWithoutReadingRunAuthority()
    {
        var runs = new StubRunAuthority(_ => throw new InvalidOperationException("must not be read in paper"));
        var resolver = Resolver(ExecutionMode.Paper, new RecordingReadinessGate(approve: true), runs);

        var decision = await resolver.EvaluatePreflightAsync(Prepared(StrategyRun()), default);

        decision.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Live_StrategyRun_BoundToPromotedRun_UsesAuthorityRunIdForReadiness()
    {
        var readiness = new RecordingReadinessGate(approve: true);
        var resolver = Resolver(ExecutionMode.Live, readiness, new StubRunAuthority(_ => Promoted()));

        // Metadata names a different run; the typed authority is what the readiness gate sees.
        var decision = await resolver.EvaluatePreflightAsync(Prepared(StrategyRun(), runId: "spoofed-run"), default);

        decision.IsApproved.Should().BeTrue();
        readiness.Requests.Should().ContainSingle().Which.RunId.Should().Be("run-live");
    }

    [Fact]
    public async Task Live_StrategyRun_WithoutRunAuthoritySource_IsRefused()
    {
        var resolver = Resolver(ExecutionMode.Live, new RecordingReadinessGate(approve: true));

        var decision = await resolver.EvaluatePreflightAsync(Prepared(StrategyRun()), default);

        decision.Refusal!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.AuthoritySourceUnavailable);
    }

    [Fact]
    public async Task Live_StrategyRun_WhenRunAuthoritySourceThrows_IsRefusedNotApproved()
    {
        var resolver = Resolver(ExecutionMode.Live, new RecordingReadinessGate(approve: true),
            new StubRunAuthority(_ => throw new IOException("store offline")));

        var decision = await resolver.EvaluatePreflightAsync(Prepared(StrategyRun()), default);

        decision.Refusal!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.AuthoritySourceUnavailable);
    }

    public static TheoryData<string, StrategyRunAuthoritySnapshot?> RunBindingFailures => new()
    {
        { OrderAuthorityReasonCodes.RunNotFound, null },
        { OrderAuthorityReasonCodes.RunNotActive, Promoted() with { IsActive = false } },
        { OrderAuthorityReasonCodes.RunNotPromotedTarget, Promoted() with { PromotionTargetRunId = null } },
        { OrderAuthorityReasonCodes.AccountGrantMismatch, Promoted() with { FundAccountId = OtherAccountId } },
        { OrderAuthorityReasonCodes.RunArtifactMismatch, Promoted() with { RunStrategyArtifactId = "sa1-other" } },
        { OrderAuthorityReasonCodes.RunArtifactMismatch, Promoted() with { PromotedStrategyArtifactId = "sa1-other" } }
    };

    [Theory]
    [MemberData(nameof(RunBindingFailures))]
    public async Task Live_StrategyRun_BindingFailure_IsRefusedBeforeReadiness(
        string expectedCode,
        StrategyRunAuthoritySnapshot? snapshot)
    {
        var readiness = new RecordingReadinessGate(approve: true);
        var resolver = Resolver(ExecutionMode.Live, readiness, new StubRunAuthority(_ => snapshot));

        var decision = await resolver.EvaluatePreflightAsync(Prepared(StrategyRun(artifact: "sa1-approved")), default);

        decision.Refusal!.ReasonCode.Should().Be(expectedCode);
        readiness.Requests.Should().BeEmpty();
    }

    // ---- Escalation release ----

    [Fact]
    public async Task Live_Release_IsJudgedByTheParkedAuthority()
    {
        var readiness = new RecordingReadinessGate(approve: true);
        var resolver = Resolver(ExecutionMode.Live, readiness, new StubRunAuthority(_ => Promoted()));

        var releasedTicket = OrderSubmissionAuthority.ForEscalationRelease(Ticket(), "risk-approver");
        var releasedRun = OrderSubmissionAuthority.ForEscalationRelease(StrategyRun(), "risk-approver");

        (await resolver.EvaluatePreflightAsync(Prepared(releasedTicket), default))
            .Refusal!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.ManualLiveIntentContractNotReviewed,
                "approving a parked manual ticket does not make it a supported live order");
        (await resolver.EvaluatePreflightAsync(Prepared(releasedRun), default)).IsApproved.Should().BeTrue();
    }

    // ---- Dispatch recheck (Phase 3) ----

    [Fact]
    public async Task Dispatch_Unattributed_IsNotRechecked()
    {
        var readiness = new RecordingReadinessGate(approve: false);
        var security = new StubSecurityGate(approve: false);
        var resolver = Resolver(ExecutionMode.Live, readiness, security: security);

        var refusal = await resolver.EvaluateDispatchAsync(Prepared(Unattributed("run-1"), runId: "run-1"), default);

        refusal.Should().BeNull("the legacy path keeps its pre-existing behavior, which has no dispatch recheck");
        readiness.Requests.Should().BeEmpty();
        security.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Dispatch_GrantAgedPastLimitSincePreflight_IsRefused()
    {
        var resolver = Resolver(ExecutionMode.Paper, new RecordingReadinessGate(approve: true),
            options: new OrderAuthorityOptions { MaxGrantAge = TimeSpan.FromMinutes(5) });

        var refusal = await resolver.EvaluateDispatchAsync(
            Prepared(Ticket(resolvedAt: Now - TimeSpan.FromMinutes(6))), default);

        refusal!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.AccountGrantStale);
        refusal.Source.Should().Be("order authority at dispatch");
    }

    [Fact]
    public async Task Dispatch_LiveStrategyRun_ReadinessWithdrawn_IsRefused()
    {
        var resolver = Resolver(ExecutionMode.Live, new RecordingReadinessGate(approve: false),
            new StubRunAuthority(_ => Promoted()));

        var refusal = await resolver.EvaluateDispatchAsync(Prepared(StrategyRun()), default);

        refusal!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.ReadinessChangedBeforeDispatch);
    }

    [Fact]
    public async Task Dispatch_LiveStrategyRun_RunEndedSincePreflight_IsRefused()
    {
        var resolver = Resolver(ExecutionMode.Live, new RecordingReadinessGate(approve: true),
            new StubRunAuthority(_ => Promoted() with { IsActive = false }));

        var refusal = await resolver.EvaluateDispatchAsync(Prepared(StrategyRun()), default);

        refusal!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.RunNotActive);
        refusal.Source.Should().Be("order authority at dispatch");
    }

    [Fact]
    public async Task Dispatch_TypedAuthority_SecurityMasterRefusal_IsRefused()
    {
        var resolver = Resolver(ExecutionMode.Paper, new RecordingReadinessGate(approve: true),
            security: new StubSecurityGate(approve: false));

        var refusal = await resolver.EvaluateDispatchAsync(Prepared(Ticket()), default);

        refusal!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.SecurityMasterRejectedAtDispatch);
    }

    [Fact]
    public async Task Dispatch_TypedAuthority_StillValid_Passes()
    {
        var security = new StubSecurityGate(approve: true);
        var resolver = Resolver(ExecutionMode.Live, new RecordingReadinessGate(approve: true),
            new StubRunAuthority(_ => Promoted()), security: security);

        (await resolver.EvaluateDispatchAsync(Prepared(StrategyRun()), default)).Should().BeNull();
        security.Calls.Should().Be(1);
    }

    // ---- Helpers ----

    private static OrderAuthorityResolver Resolver(
        ExecutionMode mode,
        ILiveOrderReadinessGate readiness,
        IStrategyRunAuthoritySource? runs = null,
        OrderAuthorityOptions? options = null,
        ISecurityMasterGate? security = null) =>
        new(mode, readiness, runs, options, new FixedClock(Now), security);

    private sealed class StubSecurityGate(bool approve) : ISecurityMasterGate
    {
        public int Calls { get; private set; }

        public Task<SecurityMasterGateResult> CheckAsync(string symbol, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new SecurityMasterGateResult(approve, approve ? null : "instrument suspended"));
        }
    }

    private static PreparedOrderAdmission Prepared(
        OrderSubmissionAuthority authority,
        Guid? fundAccountId = null,
        string? runId = null)
    {
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 100m,
            FundAccountId = fundAccountId ?? AccountId
        };
        return new PreparedOrderAdmission(
            "order-1", request, request, request, "test-broker",
            authority.Actor, authority.CorrelationId, runId ?? authority.RunId, authority.SessionId, authority);
    }

    private static OrderSubmissionAuthority Unattributed(string? runId) =>
        OrderSubmissionAuthority.Unattributed(runId is null
            ? new Dictionary<string, string> { ["actor"] = "ops" }
            : new Dictionary<string, string> { ["actor"] = "ops", ["runId"] = runId });

    private static OrderSubmissionAuthority Ticket(DateTimeOffset? resolvedAt = null) =>
        OrderSubmissionAuthority.ForOperatorTicket("ops", Grant("ops", resolvedAt), "corr-1");

    private static OrderSubmissionAuthority StrategyRun(string? artifact = null) =>
        OrderSubmissionAuthority.ForStrategyRun("strategy-runtime", Grant("strategy-runtime"), "run-live", artifact);

    private static ExecutionAccountGrant Grant(string grantedTo, DateTimeOffset? resolvedAt = null) =>
        new(AccountId, grantedTo, ExecutionAccountGrantSources.HttpPrincipal, resolvedAt ?? Now - TimeSpan.FromMinutes(1));

    private static StrategyRunAuthoritySnapshot Promoted() => new(
        RunId: "run-live",
        FundAccountId: AccountId,
        IsActive: true,
        PromotionId: "promo-1",
        PromotionTargetRunId: "run-live",
        PromotionAuditReference: "audit://promo-1",
        RunStrategyArtifactId: null,
        PromotedStrategyArtifactId: null);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingReadinessGate(bool approve) : ILiveOrderReadinessGate
    {
        public List<LiveOrderReadinessRequest> Requests { get; } = [];

        public Task<LiveOrderReadinessDecision> EvaluateAsync(LiveOrderReadinessRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(approve
                ? LiveOrderReadinessDecision.Approved($"audit://{request.RunId}")
                : LiveOrderReadinessDecision.Rejected("not ready"));
        }
    }

    private sealed class StubRunAuthority(Func<string, StrategyRunAuthoritySnapshot?> get) : IStrategyRunAuthoritySource
    {
        public Task<StrategyRunAuthoritySnapshot?> GetAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult(get(runId));
    }
}
