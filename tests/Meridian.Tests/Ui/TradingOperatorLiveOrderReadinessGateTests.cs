using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class TradingOperatorLiveOrderReadinessGateTests
{
    private static readonly Guid FundAccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task EvaluateAsync_WhenReadinessIsReadyForRequestedRun_ShouldApproveWithRetainedEvidence()
    {
        var provider = new StaticReadinessProvider(CreateReadiness());
        var gate = CreateGate(provider);

        var decision = await gate.EvaluateAsync(CreateRequest());

        decision.IsApproved.Should().BeTrue();
        decision.EvidenceReference.Should().Be("live-readiness:AUDIT-LIVE-001;snapshot:snapshot-live-001");
        decision.Reason.Should().BeNull();
        provider.LastFundAccountId.Should().Be(FundAccountId);
        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateAsync_WhenFundAccountScopeIsMissing_ShouldRejectBeforeLoadingReadiness()
    {
        var provider = new StaticReadinessProvider(CreateReadiness());
        var gate = CreateGate(provider);

        var decision = await gate.EvaluateAsync(CreateRequest(includeFundAccountId: false));

        decision.IsApproved.Should().BeFalse();
        decision.Reason.Should().Contain("requires fundAccountId");
        provider.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateAsync_WhenApprovedPromotionTargetsDifferentRun_ShouldReject()
    {
        var provider = new StaticReadinessProvider(CreateReadiness(targetRunId: "run-other"));
        var gate = CreateGate(provider);

        var decision = await gate.EvaluateAsync(CreateRequest());

        decision.IsApproved.Should().BeFalse();
        decision.Reason.Should().Contain("does not match approved live promotion target 'run-other'");
    }

    [Fact]
    public async Task EvaluateAsync_WhenLiveRequirementsAreBlocked_ShouldRejectWithRequirementBlockers()
    {
        var requirements = new[]
        {
            CreateRequirement(
                status: TradingAcceptanceGateStatusDto.Blocked,
                evidenceReference: null,
                blockerCode: "governance-signoff")
        };
        var provider = new StaticReadinessProvider(CreateReadiness(
            readyForLiveOperation: false,
            blockers: ["governance-signoff"],
            requirements: requirements));
        var gate = CreateGate(provider);

        var decision = await gate.EvaluateAsync(CreateRequest());

        decision.IsApproved.Should().BeFalse();
        decision.Reason.Should().Contain("governance-signoff");
    }

    private static TradingOperatorLiveOrderReadinessGate CreateGate(StaticReadinessProvider provider) =>
        new(provider, NullLogger<TradingOperatorLiveOrderReadinessGate>.Instance);

    private static LiveOrderReadinessRequest CreateRequest(Guid? fundAccountId = null, bool includeFundAccountId = true) =>
        new(
            RunId: "run-live",
            BrokerName: "alpaca",
            Symbol: "AAPL",
            Side: OrderSide.Buy,
            OrderType: OrderType.Market,
            Quantity: 10m,
            StrategyId: "strategy-001",
            Actor: "operator",
            FundAccountId: includeFundAccountId ? fundAccountId ?? FundAccountId : null);

    private static TradingOperatorReadinessDto CreateReadiness(
        bool readyForLiveOperation = true,
        string targetRunId = "run-live",
        string? auditReference = "AUDIT-LIVE-001",
        IReadOnlyList<string>? blockers = null,
        IReadOnlyList<TradingLiveOperationRequirementDto>? requirements = null) =>
        new(
            AsOf: DateTimeOffset.UtcNow,
            ActiveSession: null,
            Sessions: [],
            Replay: null,
            Controls: new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 0,
                DefaultMaxPositionSize: 100m),
            Promotion: new TradingPromotionReadinessDto(
                State: "Approved",
                Reason: "Approved for live operation.",
                RequiresReview: false,
                SourceRunId: "run-paper",
                TargetRunId: targetRunId,
                SuggestedNextMode: "Live",
                AuditReference: auditReference,
                ApprovalStatus: "Approved",
                ManualOverrideId: null,
                ApprovedBy: "governance"),
            TrustGate: new TradingTrustGateReadinessDto(
                GateId: "dk1",
                Status: "Ready",
                ReadyForOperatorReview: true,
                OperatorSignoffRequired: false,
                OperatorSignoffStatus: "NotRequired",
                GeneratedAt: DateTimeOffset.UtcNow,
                PacketPath: "evidence/dk1.json",
                SourceSummary: "Trusted data reviewed.",
                RequiredSampleCount: 1,
                ReadySampleCount: 1,
                ValidatedEvidenceDocumentCount: 1,
                RequiredOwners: [],
                Blockers: [],
                Detail: "Trusted data ready."),
            BrokerageSync: null,
            WorkItems: [],
            Warnings: [])
        {
            OverallStatus = readyForLiveOperation
                ? TradingAcceptanceGateStatusDto.Ready
                : TradingAcceptanceGateStatusDto.Blocked,
            ReadyForPaperOperation = true,
            ReadyForLiveOperation = readyForLiveOperation,
            LiveOperationBlockers = blockers ?? [],
            LiveOperationRequirements = requirements ?? [CreateRequirement()],
            SnapshotVersion = "snapshot-live-001"
        };

    private static TradingLiveOperationRequirementDto CreateRequirement(
        TradingAcceptanceGateStatusDto status = TradingAcceptanceGateStatusDto.Ready,
        string? evidenceReference = "AUDIT-LIVE-001",
        string? blockerCode = null) =>
        new(
            RequirementId: "governance-signoff",
            Label: "Governance sign-off",
            Status: status,
            Detail: "Governance sign-off evidence.",
            ChecklistItem: "GOVERNANCE_SIGNOFF_REVIEWED",
            EvidenceReference: evidenceReference,
            ChecklistSatisfied: status == TradingAcceptanceGateStatusDto.Ready,
            EvidenceSatisfied: !string.IsNullOrWhiteSpace(evidenceReference),
            BlockerCode: blockerCode);

    private sealed class StaticReadinessProvider : ITradingOperatorReadinessProvider
    {
        private readonly TradingOperatorReadinessDto _readiness;

        public StaticReadinessProvider(TradingOperatorReadinessDto readiness)
        {
            _readiness = readiness;
        }

        public Guid? LastFundAccountId { get; private set; }

        public int CallCount { get; private set; }

        public Task<TradingOperatorReadinessDto> GetAsync(Guid? fundAccountId = null, CancellationToken ct = default)
        {
            LastFundAccountId = fundAccountId;
            CallCount++;
            return Task.FromResult(_readiness);
        }
    }
}
