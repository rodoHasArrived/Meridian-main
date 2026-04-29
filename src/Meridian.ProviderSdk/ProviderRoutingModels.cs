using System.Text.Json.Serialization;

namespace Meridian.ProviderSdk;

/// <summary>
/// Canonical capability kinds routed by the relationship-aware provider layer.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProviderCapabilityKind>))]
public enum ProviderCapabilityKind : byte
{
    RealtimeMarketData = 0,
    HistoricalBars = 1,
    HistoricalTrades = 2,
    HistoricalQuotes = 3,
    SymbolSearch = 4,
    ReferenceData = 5,
    SecurityMasterSeed = 6,
    CorporateActions = 7,
    OptionsChain = 8,
    OrderExecution = 9,
    ExecutionHistory = 10,
    AccountBalances = 11,
    AccountPositions = 12,
    CashTransactions = 13,
    BankStatements = 14,
    ReconciliationFeed = 15
}

/// <summary>
/// Safety modes applied when routing a capability.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProviderSafetyMode>))]
public enum ProviderSafetyMode : byte
{
    HealthAwareFailover = 0,
    NoAutomaticFailover = 1,
    SameInstitutionOnly = 2,
    ManualApprovalRequired = 3,
    ResearchOnly = 4
}

/// <summary>
/// High-level relationship type for a provider connection.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProviderConnectionType>))]
public enum ProviderConnectionType : byte
{
    Brokerage = 0,
    Custodian = 1,
    Bank = 2,
    DataVendor = 3,
    Exchange = 4,
    Other = 5
}

/// <summary>
/// Operational mode for a provider connection.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProviderConnectionMode>))]
public enum ProviderConnectionMode : byte
{
    ReadOnly = 0,
    Research = 1,
    Paper = 2,
    Live = 3
}

/// <summary>
/// Strongly typed provider connection identifier.
/// </summary>
public readonly record struct ProviderConnectionId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Scope attached to a provider connection.
/// </summary>
public sealed record ProviderConnectionScope(
    string? Workspace = null,
    string? FundProfileId = null,
    Guid? EntityId = null,
    Guid? SleeveId = null,
    Guid? VehicleId = null,
    Guid? AccountId = null)
{
    public bool IsGlobal =>
        string.IsNullOrWhiteSpace(Workspace) &&
        string.IsNullOrWhiteSpace(FundProfileId) &&
        EntityId is null &&
        SleeveId is null &&
        VehicleId is null &&
        AccountId is null;

    public int GetMatchScore(ProviderRouteContext context)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(Workspace))
        {
            if (!string.Equals(Workspace, context.Workspace, StringComparison.OrdinalIgnoreCase))
                return -1;

            score = Math.Max(score, 100);
        }

        if (!string.IsNullOrWhiteSpace(FundProfileId))
        {
            if (!string.Equals(FundProfileId, context.FundProfileId, StringComparison.OrdinalIgnoreCase))
                return -1;

            score = Math.Max(score, 300);
        }

        if (EntityId is Guid entityId)
        {
            if (context.EntityId != entityId)
                return -1;

            score = Math.Max(score, 350);
        }

        if (SleeveId is Guid sleeveId)
        {
            if (context.SleeveId != sleeveId)
                return -1;

            score = Math.Max(score, 400);
        }

        if (VehicleId is Guid vehicleId)
        {
            if (context.VehicleId != vehicleId)
                return -1;

            score = Math.Max(score, 450);
        }

        if (AccountId is Guid accountId)
        {
            if (context.AccountId != accountId)
                return -1;

            score = Math.Max(score, 500);
        }

        return score == 0 ? 1 : score;
    }
}

/// <summary>
/// Scope used when matching a capability binding to a route request.
/// </summary>
public sealed record ProviderBindingTarget(
    string? Workspace = null,
    string? FundProfileId = null,
    Guid? EntityId = null,
    Guid? SleeveId = null,
    Guid? VehicleId = null,
    Guid? AccountId = null)
{
    public bool IsGlobal =>
        string.IsNullOrWhiteSpace(Workspace) &&
        string.IsNullOrWhiteSpace(FundProfileId) &&
        EntityId is null &&
        SleeveId is null &&
        VehicleId is null &&
        AccountId is null;

    public int GetMatchScore(ProviderRouteContext context)
    {
        var scope = new ProviderConnectionScope(Workspace, FundProfileId, EntityId, SleeveId, VehicleId, AccountId);
        return scope.GetMatchScore(context);
    }
}

/// <summary>
/// Metadata describing one capability published by a provider family.
/// </summary>
public sealed record ProviderCapabilityDescriptor(
    ProviderCapabilityKind Kind,
    string Description,
    bool RequiresAccountBinding = false,
    bool SupportsFailover = true);

/// <summary>
/// Canonical routing request.
/// </summary>
public sealed record ProviderRouteContext(
    ProviderCapabilityKind Capability,
    string? Workspace = null,
    string? FundProfileId = null,
    Guid? EntityId = null,
    Guid? SleeveId = null,
    Guid? VehicleId = null,
    Guid? AccountId = null,
    Guid? SecurityId = null,
    string? Symbol = null,
    string? Market = null,
    string? AssetClass = null,
    bool RequireProductionReady = false);

/// <summary>
/// Effective safety policy for a routed capability.
/// </summary>
public sealed record ProviderSafetyPolicy(
    ProviderCapabilityKind Capability,
    ProviderSafetyMode Mode,
    bool RequireExplicitBinding = false,
    bool RequireProductionReady = false,
    IReadOnlyList<string>? AllowedFailoverConnectionIds = null,
    IReadOnlyList<string>? AllowedProviderFamilies = null)
{
    public static ProviderSafetyPolicy DefaultFor(ProviderCapabilityKind capability)
        => capability switch
        {
            ProviderCapabilityKind.OrderExecution or
            ProviderCapabilityKind.ExecutionHistory or
            ProviderCapabilityKind.AccountBalances or
            ProviderCapabilityKind.AccountPositions or
            ProviderCapabilityKind.CashTransactions or
            ProviderCapabilityKind.BankStatements
                => new ProviderSafetyPolicy(
                    capability,
                    ProviderSafetyMode.NoAutomaticFailover,
                    RequireExplicitBinding: true,
                    RequireProductionReady: capability is ProviderCapabilityKind.OrderExecution or ProviderCapabilityKind.BankStatements),

            ProviderCapabilityKind.ReconciliationFeed
                => new ProviderSafetyPolicy(
                    capability,
                    ProviderSafetyMode.SameInstitutionOnly,
                    RequireExplicitBinding: true),

            _ => new ProviderSafetyPolicy(capability, ProviderSafetyMode.HealthAwareFailover)
        };
}

/// <summary>
/// Health snapshot for a provider connection.
/// </summary>
public sealed record ProviderConnectionHealthSnapshot(
    string ConnectionId,
    string ProviderFamilyId,
    bool IsHealthy = true,
    string Status = "unknown",
    double Score = 100.0,
    DateTimeOffset? CheckedAt = null);

/// <summary>
/// One candidate routing decision.
/// </summary>
public sealed record ProviderRouteDecision(
    string ConnectionId,
    string ProviderFamilyId,
    ProviderCapabilityKind Capability,
    ProviderSafetyMode SafetyMode,
    int ScopeRank,
    int Priority,
    bool IsHealthy,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> FallbackConnectionIds,
    string? PolicyGate = null,
    double CompositeScore = 0,
    double HealthScore = 0,
    double LatencyScore = 0,
    double DataQualityScore = 0,
    double CoverageScore = 0,
    double PolicyGateScore = 0);

/// <summary>
/// Full route evaluation result including skipped candidates.
/// </summary>
public sealed record ProviderRouteResult(
    ProviderRouteContext Context,
    ProviderRouteDecision? SelectedDecision,
    IReadOnlyList<ProviderRouteDecision> Candidates,
    IReadOnlyList<string> SkippedCandidates,
    bool RequiresManualApproval = false,
    string? PolicyGate = null)
{
    public bool IsSuccess => SelectedDecision is not null && string.IsNullOrWhiteSpace(PolicyGate) && !RequiresManualApproval;
}

/// <summary>
/// Provider trust score snapshot used by governance surfaces.
/// </summary>
public sealed record ProviderTrustSnapshot(
    string ConnectionId,
    string ProviderFamilyId,
    double Score,
    bool IsHealthy,
    string HealthStatus,
    bool IsProductionReady,
    bool IsCertificationFresh,
    IReadOnlyList<string> Signals);

/// <summary>
/// Result of connection validation or certification probes.
/// </summary>
public sealed record ProviderConnectionTestResult(
    bool Success,
    IReadOnlyList<string> Checks,
    DateTimeOffset TestedAt,
    string Status = "unknown");

/// <summary>
/// Result returned by an explicit certification run.
/// </summary>
public sealed record ProviderCertificationRunResult(
    string ConnectionId,
    bool Success,
    string Status,
    IReadOnlyList<string> Checks,
    DateTimeOffset RanAt);
