using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards the paper-to-live promotion scenario where retained W7 evidence is the last control
/// before live broker orders can leave the OMS.
/// </summary>
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
    public async Task Scenario_PaperToLivePromotion_WithRetainedW7Evidence_ShouldApproveLiveBrokerOrder()
    {
        var requirements = CreateLiveRequirements(
            CreateRequirement(
                PromotionApprovalChecklist.AccountingRecordsReviewed,
                evidenceReference: $"{PromotionApprovalChecklist.AccountingRecordsReviewed}:ledger-journal/run-live"),
            CreateRequirement(
                PromotionApprovalChecklist.GovernedReportingReviewed,
                evidenceReference: $"{PromotionApprovalChecklist.GovernedReportingReviewed}:fund-report-pack/report-live"));
        var provider = new StaticReadinessProvider(CreateReadiness(requirements: requirements));
        var gate = CreateGate(provider);

        var decision = await gate.EvaluateAsync(CreateRequest());

        decision.IsApproved.Should().BeTrue();
        decision.EvidenceReference.Should().Be("live-readiness:AUDIT-LIVE-001;snapshot:snapshot-live-001");
        requirements
            .Where(static requirement => PromotionApprovalChecklist
                .CreateRequiredFor(RunType.Live)
                .Contains(requirement.ChecklistItem, StringComparer.OrdinalIgnoreCase))
            .Should()
            .OnlyContain(static requirement =>
                requirement.Status == TradingAcceptanceGateStatusDto.Ready &&
                requirement.ChecklistSatisfied &&
                requirement.EvidenceSatisfied &&
                !string.IsNullOrWhiteSpace(requirement.EvidenceReference));
        requirements.Should().ContainSingle(requirement =>
            requirement.ChecklistItem == PromotionApprovalChecklist.AccountingRecordsReviewed &&
            requirement.EvidenceReference == $"{PromotionApprovalChecklist.AccountingRecordsReviewed}:ledger-journal/run-live");
        requirements.Should().ContainSingle(requirement =>
            requirement.ChecklistItem == PromotionApprovalChecklist.GovernedReportingReviewed &&
            requirement.EvidenceReference == $"{PromotionApprovalChecklist.GovernedReportingReviewed}:fund-report-pack/report-live");
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
        var requirements = CreateLiveRequirements(
            CreateRequirement(
                requirementId: "governance-signoff",
                checklistItem: PromotionApprovalChecklist.GovernanceSignoffReviewed,
                status: TradingAcceptanceGateStatusDto.Blocked,
                evidenceReference: null,
                blockerCode: "governance-signoff"));
        var provider = new StaticReadinessProvider(CreateReadiness(
            readyForLiveOperation: false,
            blockers: ["governance-signoff"],
            requirements: requirements));
        var gate = CreateGate(provider);

        var decision = await gate.EvaluateAsync(CreateRequest());

        decision.IsApproved.Should().BeFalse();
        decision.Reason.Should().Contain("governance-signoff");
    }

    [Fact]
    public async Task EvaluateAsync_WhenLiveRequirementMatrixOmitsChecklistItem_ShouldRejectWithMissingRequirement()
    {
        var requirements = PromotionApprovalChecklist.CreateRequiredFor(RunType.Live)
            .Where(static checklistItem => checklistItem != PromotionApprovalChecklist.AuditRetentionReviewed)
            .Select(static checklistItem => CreateRequirement(checklistItem))
            .ToArray();
        var provider = new StaticReadinessProvider(CreateReadiness(requirements: requirements));
        var gate = CreateGate(provider);

        var decision = await gate.EvaluateAsync(CreateRequest());

        decision.IsApproved.Should().BeFalse();
        decision.Reason.Should().Contain("liveOperationRequirements:missing:AUDIT_RETENTION_REVIEWED");
    }

    [Fact]
    public async Task EvaluateAsync_WhenLiveRequirementLacksRetainedEvidence_ShouldRejectWithEvidenceBlocker()
    {
        var requirements = CreateLiveRequirements(
            CreateRequirement(
                PromotionApprovalChecklist.GovernedReportingReviewed,
                evidenceReference: "",
                evidenceSatisfied: false));
        var provider = new StaticReadinessProvider(CreateReadiness(requirements: requirements));
        var gate = CreateGate(provider);

        var decision = await gate.EvaluateAsync(CreateRequest());

        decision.IsApproved.Should().BeFalse();
        decision.Reason.Should().Contain("liveOperationRequirements:evidence-unsatisfied:GOVERNED_REPORTING_REVIEWED");
    }

    [Fact]
    public async Task Scenario_LiveKillSwitchOpen_ShouldBlockLiveOrderWithReviewableEvidence()
    {
        var provider = new StaticReadinessProvider(CreateReadiness(
            readyForLiveOperation: false,
            blockers:
            [
                "acceptanceGate:execution-controls",
                "execution-circuit-breaker-open",
                "promotion:evidenceReferences:EXCEPTION_HANDLING_REVIEWED"
            ],
            controls: CreateControls(circuitBreakerOpen: true)));
        var gate = CreateGate(provider);

        var decision = await gate.EvaluateAsync(CreateRequest());

        decision.IsApproved.Should().BeFalse();
        decision.Reason.Should().Contain("execution-circuit-breaker-open");
        decision.Reason.Should().Contain("promotion:evidenceReferences:EXCEPTION_HANDLING_REVIEWED");
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

    private static TradingLiveOperationRequirementDto[] CreateLiveRequirements(
        params TradingLiveOperationRequirementDto[] overrides)
    {
        var requirementsByChecklistItem = PromotionApprovalChecklist.CreateRequiredFor(RunType.Live)
            .Select(static checklistItem => CreateRequirement(checklistItem))
            .ToDictionary(
                static requirement => requirement.ChecklistItem,
                StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in overrides)
        {
            requirementsByChecklistItem[requirement.ChecklistItem] = requirement;
        }

        return requirementsByChecklistItem.Values.ToArray();
    }

    private static TradingOperatorReadinessDto CreateReadiness(
        bool readyForLiveOperation = true,
        string targetRunId = "run-live",
        string? auditReference = "AUDIT-LIVE-001",
        IReadOnlyList<string>? blockers = null,
        IReadOnlyList<TradingLiveOperationRequirementDto>? requirements = null,
        TradingControlReadinessDto? controls = null) =>
        new(
            AsOf: DateTimeOffset.UtcNow,
            ActiveSession: null,
            Sessions: [],
            Replay: null,
            Controls: controls ?? CreateControls(),
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
            LiveOperationRequirements = requirements ?? CreateLiveRequirements(),
            SnapshotVersion = "snapshot-live-001"
        };

    private static TradingLiveOperationRequirementDto CreateRequirement(
        string checklistItem,
        string? requirementId = null,
        TradingAcceptanceGateStatusDto status = TradingAcceptanceGateStatusDto.Ready,
        string? evidenceReference = null,
        bool? checklistSatisfied = null,
        bool? evidenceSatisfied = null,
        string? blockerCode = null) =>
        new(
            RequirementId: requirementId ?? ToRequirementId(checklistItem),
            Label: checklistItem.Replace('_', ' '),
            Status: status,
            Detail: $"{checklistItem} evidence.",
            ChecklistItem: checklistItem,
            EvidenceReference: evidenceReference ?? $"{checklistItem}:retained-evidence",
            ChecklistSatisfied: checklistSatisfied ?? status == TradingAcceptanceGateStatusDto.Ready,
            EvidenceSatisfied: evidenceSatisfied ?? status == TradingAcceptanceGateStatusDto.Ready,
            BlockerCode: blockerCode);

    private static TradingControlReadinessDto CreateControls(bool circuitBreakerOpen = false) =>
        new(
            CircuitBreakerOpen: circuitBreakerOpen,
            CircuitBreakerReason: circuitBreakerOpen
                ? "Governance kill-switch is open while exception evidence is reviewed."
                : null,
            CircuitBreakerChangedBy: circuitBreakerOpen ? "governance" : null,
            CircuitBreakerChangedAt: circuitBreakerOpen ? DateTimeOffset.UtcNow : null,
            ManualOverrideCount: 0,
            SymbolLimitCount: 0,
            DefaultMaxPositionSize: 100m);

    private static string ToRequirementId(string checklistItem) =>
        checklistItem
            .ToLowerInvariant()
            .Replace("_reviewed", string.Empty)
            .Replace('_', '-');

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
