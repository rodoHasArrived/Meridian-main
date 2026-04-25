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
    ProviderTrustGate = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<OperatorWorkItemToneDto>))]
public enum OperatorWorkItemToneDto
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Critical = 3
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
    decimal? PortfolioValue);

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

public sealed record TradingControlReadinessDto(
    bool CircuitBreakerOpen,
    string? CircuitBreakerReason,
    string? CircuitBreakerChangedBy,
    DateTimeOffset? CircuitBreakerChangedAt,
    int ManualOverrideCount,
    int SymbolLimitCount,
    decimal? DefaultMaxPositionSize);

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
    string Detail);

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
    IReadOnlyList<string> Warnings);

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
