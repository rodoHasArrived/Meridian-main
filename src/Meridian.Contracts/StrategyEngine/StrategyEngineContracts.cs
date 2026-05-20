using System.Text.Json.Serialization;

namespace Meridian.Contracts.StrategyEngine;

/// <summary>
/// Stable strategy category used by the Strategy Engine registry and browser workstation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyEngineStrategyType>))]
public enum StrategyEngineStrategyType : byte
{
    CoveredCall,
    Momentum,
    MeanReversion,
    FactorScreen,
    FixedIncomeCarry,
    DirectLendingScreen,
    RebalancingRule,
    RiskOverlay,
    PortfolioConstruction,
    CashDeployment,
    QuantScript,
    VisualDesigner,
    Custom
}

/// <summary>
/// Execution mode requested for a strategy run.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyEngineRunMode>))]
public enum StrategyEngineRunMode : byte
{
    Backtest,
    Replay,
    Simulation,
    PaperValidation,
    ReviewOnly,
    LiveDisabled
}

/// <summary>
/// Strategy parameter value type used for validation and browser rendering.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyEngineParameterType>))]
public enum StrategyEngineParameterType : byte
{
    String,
    Decimal,
    Integer,
    Boolean,
    Date,
    Enum,
    Symbol,
    SymbolList
}

/// <summary>
/// Declared data input class for a strategy.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyEngineDataDependencyKind>))]
public enum StrategyEngineDataDependencyKind : byte
{
    PriceBars,
    LiveQuotes,
    Fundamentals,
    ReferenceData,
    SecurityMaster,
    OptionsChain,
    Positions,
    OpenLots,
    Portfolio,
    Ledger,
    CorporateActions,
    ProviderBackfill,
    Custom
}

/// <summary>
/// How a strategy may proceed when a declared data dependency is incomplete.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyEngineMissingDataPolicy>))]
public enum StrategyEngineMissingDataPolicy : byte
{
    Block,
    AllowDegradedWithEvidence,
    Optional
}

/// <summary>
/// Promotion target used by Strategy Engine handoffs.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyEnginePromotionTarget>))]
public enum StrategyEnginePromotionTarget : byte
{
    Review,
    Replay,
    Paper,
    ReportPack,
    EvidenceWorkbench,
    TradingReadiness,
    LiveDisabled
}

/// <summary>
/// Operator-facing validation severity for strategy definitions, requests, data, risk, and promotion checks.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StrategyEngineValidationSeverity>))]
public enum StrategyEngineValidationSeverity : byte
{
    Info,
    Warning,
    Error,
    Blocker
}

public sealed record StrategyEngineParameterDefinition(
    string Name,
    StrategyEngineParameterType Type,
    string? DefaultValue,
    string? Description,
    string? DisplayLabel = null,
    string? CurrentValue = null,
    string? MinValue = null,
    string? MaxValue = null,
    IReadOnlyList<string>? AllowedValues = null,
    string? ValidationRule = null,
    string? SweepHint = null,
    bool IsRequired = true);

public sealed record StrategyEngineDataDependency(
    string DependencyId,
    StrategyEngineDataDependencyKind Kind,
    string Description,
    string? AssetClass = null,
    string? Granularity = null,
    DateTimeOffset? RequiredFrom = null,
    DateTimeOffset? RequiredTo = null,
    TimeSpan? FreshnessWindow = null,
    StrategyEngineMissingDataPolicy MissingDataPolicy = StrategyEngineMissingDataPolicy.Block,
    IReadOnlyList<string>? RequiredFields = null);

public sealed record StrategyEngineDefinition(
    string StrategyId,
    string Version,
    string Name,
    StrategyEngineStrategyType Type,
    string Description,
    IReadOnlyList<string> AssetUniverse,
    IReadOnlyList<StrategyEngineDataDependency> RequiredDataInputs,
    IReadOnlyList<StrategyEngineParameterDefinition> ParameterSchema,
    IReadOnlyList<StrategyEngineRunMode> SupportedModes,
    string Owner,
    string Source,
    IReadOnlyDictionary<string, string>? UiMetadata = null,
    IReadOnlyDictionary<string, string>? Extensions = null);

public sealed record StrategyEngineRunRequest(
    string StrategyId,
    string StrategyVersion,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string> Universe,
    DateTimeOffset From,
    DateTimeOffset To,
    string DataSource,
    StrategyEngineRunMode Mode,
    string? CostModel = null,
    string? SlippageModel = null,
    IReadOnlyDictionary<string, string>? RiskConstraints = null,
    string? RunReason = null,
    string? OperatorId = null);

public sealed record StrategyEngineDataAvailability(
    string DependencyId,
    bool IsAvailable,
    DateTimeOffset? AvailableFrom = null,
    DateTimeOffset? AvailableTo = null,
    DateTimeOffset? LastUpdatedAt = null,
    string? SourceReference = null,
    IReadOnlyList<string>? MissingFields = null,
    string? DegradationReason = null);

public sealed record StrategyEngineSignal(
    string Symbol,
    string SignalType,
    string Direction,
    decimal? Strength,
    decimal? Confidence,
    DateTimeOffset Timestamp,
    string Horizon,
    string Rationale,
    string SourceRunId,
    string? RequiredFollowUpAction = null);

public sealed record StrategyEngineRunEvidence(
    string EvidenceId,
    string StrategyId,
    string StrategyVersion,
    string InputHash,
    string? OutputHash,
    string DataSource,
    DateTimeOffset CapturedAt,
    IReadOnlyList<string> DataSourceReferences,
    IReadOnlyList<string> Warnings,
    string ReviewRoute,
    string EvidenceRoute,
    string? ReportPackRoute = null);

public sealed record StrategyEnginePromotionRequest(
    string SourceRunId,
    StrategyEnginePromotionTarget Target,
    string? OperatorAcknowledgement,
    IReadOnlyList<string> ReadinessChecks,
    IReadOnlyList<string> RiskChecks,
    string EvidencePacket,
    string? Rationale = null);

public sealed record StrategyEngineValidationFinding(
    string Code,
    StrategyEngineValidationSeverity Severity,
    string Target,
    string Message,
    string? HandoffRoute = null);

public sealed record StrategyEngineValidationResult(
    bool IsValid,
    bool IsDegraded,
    string InputHash,
    IReadOnlyList<StrategyEngineValidationFinding> Findings,
    StrategyEngineRunEvidence Evidence);
