using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<GovernanceReportKindDto>))]
public enum GovernanceReportKindDto
{
    TrialBalance = 0,
    NavSummary = 1,
    AssetAllocation = 2,
    ReconciliationPack = 3
}

/// <summary>
/// Query for the shared governance and fund-operations workspace projection.
/// </summary>
/// <remarks>
/// Selection semantics mirror <see cref="FundLedgerQuery"/>:
/// null/empty selections read the full consolidated fund, while non-empty
/// selections constrain ledger consolidation to the specified ledger IDs.
/// Unknown IDs are treated as no matches.
/// </remarks>
public sealed record FundOperationsWorkspaceQuery(
    string FundProfileId,
    DateTimeOffset? AsOf = null,
    string? Currency = null,
    FundLedgerScope ScopeKind = FundLedgerScope.Consolidated,
    string? ScopeId = null,
    IReadOnlyList<string>? SelectedLedgerIds = null);

/// <summary>
/// Asset-class contribution within a NAV attribution summary.
/// </summary>
public sealed record FundNavAssetClassExposureDto(
    string AssetClass,
    decimal NetBalance);

/// <summary>
/// Governance-facing NAV attribution summary for one fund workspace.
/// </summary>
public sealed record FundNavAttributionSummaryDto(
    string Currency,
    decimal TotalNav,
    int ComponentCount,
    int EntityCount,
    int SleeveCount,
    int VehicleCount,
    IReadOnlyList<FundNavAssetClassExposureDto> AssetClassExposure);

/// <summary>
/// Reporting profile metadata exposed to governance workflows.
/// </summary>
public sealed record FundReportingProfileDto(
    string Id,
    string Name,
    string TargetTool,
    string Format,
    string Description,
    bool LoaderScript,
    bool DataDictionary);

/// <summary>
/// Report/export posture for the governance workspace.
/// </summary>
public sealed record FundReportingSummaryDto(
    int ProfileCount,
    IReadOnlyList<string> RecommendedProfiles,
    IReadOnlyList<string> ReportPackTargets,
    IReadOnlyList<FundReportingProfileDto> Profiles,
    string Summary);

/// <summary>
/// Shared governance workspace payload combining ledger, banking, cash, reconciliation,
/// NAV, and reporting posture for one fund profile.
/// </summary>
public sealed record FundOperationsWorkspaceDto(
    string FundProfileId,
    string DisplayName,
    string BaseCurrency,
    DateTimeOffset AsOf,
    int RecordedRunCount,
    IReadOnlyList<string> RelatedRunIds,
    FundWorkspaceSummary Workspace,
    FundLedgerSummary Ledger,
    IReadOnlyList<FundAccountSummary> Accounts,
    IReadOnlyList<BankAccountSnapshot> BankSnapshots,
    CashFinancingSummary CashFinancing,
    ReconciliationSummary Reconciliation,
    FundNavAttributionSummaryDto Nav,
    FundReportingSummaryDto Reporting);

/// <summary>
/// Request to build a preview of a governance report pack for one fund profile.
/// </summary>
public sealed record FundReportPackPreviewRequestDto(
    string FundProfileId,
    GovernanceReportKindDto ReportKind = GovernanceReportKindDto.TrialBalance,
    DateTimeOffset? AsOf = null,
    string? Currency = null);

/// <summary>
/// Asset-class total included in a report-pack preview.
/// </summary>
public sealed record FundReportAssetClassSectionDto(
    string AssetClass,
    decimal Total);

/// <summary>
/// Preview of a generated governance report pack without writing the artifact to disk.
/// </summary>
public sealed record FundReportPackPreviewDto(
    Guid ReportId,
    string FundProfileId,
    string DisplayName,
    GovernanceReportKindDto ReportKind,
    string Currency,
    DateTimeOffset AsOf,
    DateTimeOffset GeneratedAt,
    decimal TotalNetAssets,
    int TrialBalanceLineCount,
    int AssetClassSectionCount,
    IReadOnlyList<FundReportAssetClassSectionDto> AssetClassSections);
