using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<OperatorWorkItemKindDto>))]
public enum OperatorWorkItemKindDto
{
    PaperReplay = 0,
    PromotionReview = 1,
    BrokerageSync = 2,
    SecurityMasterCoverage = 3,
    ReconciliationBreak = 4,
    ReportPackApproval = 5,
    ProviderTrustGate = 6,
    ExecutionControl = 7
}

[JsonConverter(typeof(JsonStringEnumConverter<OperatorWorkItemToneDto>))]
public enum OperatorWorkItemToneDto
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Critical = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<TradingAcceptanceGateStatusDto>))]
public enum TradingAcceptanceGateStatusDto
{
    Ready = 0,
    ReviewRequired = 1,
    Blocked = 2
}

public sealed record OperatorWorkItemDto(
    string WorkItemId,
    OperatorWorkItemKindDto Kind,
    string Label,
    string Detail,
    OperatorWorkItemToneDto Tone,
    DateTimeOffset CreatedAt,
    string? RunId = null,
    Guid? FundAccountId = null,
    string? AuditReference = null);

public sealed record TradingAcceptanceGateDto(
    string GateId,
    string Label,
    TradingAcceptanceGateStatusDto Status,
    string Detail,
    string? SessionId = null,
    string? RunId = null,
    string? AuditReference = null);

public sealed record TradingPaperSessionReadinessDto(
    string SessionId,
    string StrategyId,
    string? StrategyName,
    bool IsActive,
    decimal InitialCash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    int SymbolCount,
    int OrderCount,
    int PositionCount,
    decimal? PortfolioValue)
{
    public int FillCount { get; init; }

    public int LedgerEntryCount { get; init; }

    public DateTimeOffset? LastFillAt { get; init; }

    public DateTimeOffset? LastOrderUpdatedAt { get; init; }
}

public sealed record TradingReplayReadinessDto(
    string SessionId,
    string ReplaySource,
    bool IsConsistent,
    int ComparedFillCount,
    int ComparedOrderCount,
    int ComparedLedgerEntryCount,
    DateTimeOffset VerifiedAt,
    DateTimeOffset? LastPersistedFillAt,
    DateTimeOffset? LastPersistedOrderUpdateAt,
    string? VerificationAuditId,
    IReadOnlyList<string> MismatchReasons);

public sealed record TradingControlEvidenceDto(
    string AuditId,
    string Category,
    string Action,
    string Outcome,
    DateTimeOffset OccurredAt,
    string? Actor,
    string Scope,
    string Reason,
    bool IsExplained,
    IReadOnlyList<string> MissingFields,
    string? RunId = null,
    string? Symbol = null,
    string? OrderId = null,
    string? CorrelationId = null);

public sealed record TradingControlReadinessDto(
    bool CircuitBreakerOpen,
    string? CircuitBreakerReason,
    string? CircuitBreakerChangedBy,
    DateTimeOffset? CircuitBreakerChangedAt,
    int ManualOverrideCount,
    int SymbolLimitCount,
    decimal? DefaultMaxPositionSize)
{
    public IReadOnlyList<TradingControlEvidenceDto> RecentEvidence { get; init; } = [];

    public int ExplainableEvidenceCount { get; init; }

    public int UnexplainedEvidenceCount { get; init; }

    public IReadOnlyList<string> ExplainabilityWarnings { get; init; } = [];
}

public sealed record TradingPromotionReadinessDto(
    string State,
    string Reason,
    bool RequiresReview,
    string? SourceRunId,
    string? TargetRunId,
    string? SuggestedNextMode,
    string? AuditReference,
    string? ApprovalStatus,
    string? ManualOverrideId,
    string? ApprovedBy,
    IReadOnlyList<string>? ApprovalChecklist = null);

public sealed record TradingOperatorSignoffReadinessDto(
    string Status,
    bool RequiredBeforeDk1Exit,
    IReadOnlyList<string> RequiredOwners,
    IReadOnlyList<string> SignedOwners,
    IReadOnlyList<string> MissingOwners,
    DateTimeOffset? CompletedAt,
    string? SourcePath);

public sealed record TradingTrustGateSampleReviewDto(
    string SampleId,
    string Provider,
    string RequiredStep,
    string StepStatus,
    string Status,
    bool Observed,
    IReadOnlyList<string> MissingRequirements,
    IReadOnlyList<string> EvidenceAnchors,
    string AcceptanceCheck);

public sealed record TradingTrustGateEvidenceDocumentDto(
    string Name,
    string Gate,
    string Path,
    bool Exists,
    string Status,
    IReadOnlyList<string> MissingRequirements);

public sealed record TradingTrustGateContractReadinessDto(
    string ContractId,
    string DocumentPath,
    string Status,
    IReadOnlyList<string> RequiredPayloadFields,
    IReadOnlyList<string> RequiredReasonCodes,
    IReadOnlyList<string> RequiredMetrics,
    bool? FpFnReviewRequired,
    IReadOnlyList<string> MissingRequirements);

public sealed record TradingTrustGateReadinessDto(
    string GateId,
    string Status,
    bool ReadyForOperatorReview,
    bool OperatorSignoffRequired,
    string OperatorSignoffStatus,
    DateTimeOffset? GeneratedAt,
    string? PacketPath,
    string? SourceSummary,
    int RequiredSampleCount,
    int ReadySampleCount,
    int ValidatedEvidenceDocumentCount,
    IReadOnlyList<string> RequiredOwners,
    IReadOnlyList<string> Blockers,
    string Detail,
    TradingOperatorSignoffReadinessDto? OperatorSignoff = null)
{
    public IReadOnlyList<TradingTrustGateSampleReviewDto> SampleReviews { get; init; } = [];

    public IReadOnlyList<TradingTrustGateEvidenceDocumentDto> EvidenceDocuments { get; init; } = [];

    public TradingTrustGateContractReadinessDto? TrustRationaleContract { get; init; }

    public TradingTrustGateContractReadinessDto? BaselineThresholdContract { get; init; }
}

public sealed record TradingOperatorReadinessDto(
    DateTimeOffset AsOf,
    TradingPaperSessionReadinessDto? ActiveSession,
    IReadOnlyList<TradingPaperSessionReadinessDto> Sessions,
    TradingReplayReadinessDto? Replay,
    TradingControlReadinessDto Controls,
    TradingPromotionReadinessDto? Promotion,
    TradingTrustGateReadinessDto TrustGate,
    WorkstationBrokerageSyncStatusDto? BrokerageSync,
    IReadOnlyList<OperatorWorkItemDto> WorkItems,
    IReadOnlyList<string> Warnings)
{
    public TradingAcceptanceGateStatusDto OverallStatus { get; init; } = TradingAcceptanceGateStatusDto.ReviewRequired;

    public bool ReadyForPaperOperation { get; init; }

    public IReadOnlyList<TradingAcceptanceGateDto> AcceptanceGates { get; init; } = [];
}

public sealed record StrategyRunReviewPacketDto(
    string RunId,
    DateTimeOffset GeneratedAt,
    StrategyRunDetail Run,
    StrategyRunContinuityDetail? Continuity,
    RunFillSummary? Fills,
    RunAttributionSummary? Attribution,
    WorkstationBrokerageSyncStatusDto? BrokerageSync,
    IReadOnlyList<OperatorWorkItemDto> WorkItems,
    IReadOnlyList<string> Warnings);
