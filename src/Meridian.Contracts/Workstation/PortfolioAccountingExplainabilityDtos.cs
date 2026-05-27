namespace Meridian.Contracts.Workstation;

public sealed record PortfolioOverviewQuery(
    string FundProfileId,
    DateTimeOffset? AsOf = null,
    string? Currency = null);

public sealed record PortfolioOverviewDto(
    string FundProfileId,
    string FundDisplayName,
    string BaseCurrency,
    DateTimeOffset AsOf,
    int TotalAccounts,
    decimal TotalCash,
    decimal GrossExposure,
    decimal NetExposure,
    decimal TotalEquity,
    int OpenReconciliationBreaks,
    int ReconciliationRuns,
    int SecurityResolvedCount,
    int SecurityMissingCount);

public sealed record LedgerDrillDownQuery(
    string FundProfileId,
    DateTimeOffset? AsOf = null,
    FundLedgerScope ScopeKind = FundLedgerScope.Consolidated,
    string? ScopeId = null,
    IReadOnlyList<string>? SelectedLedgerIds = null);

public sealed record LedgerDrillDownDto(
    string FundProfileId,
    string FundDisplayName,
    DateTimeOffset AsOf,
    FundLedgerSummary Ledger,
    IReadOnlyList<FundAccountSummary> Accounts);

public sealed record FinancingAnalysisQuery(
    string FundProfileId,
    DateTimeOffset? AsOf = null,
    string? Currency = null);

public sealed record FinancingAnalysisDto(
    string FundProfileId,
    string FundDisplayName,
    DateTimeOffset AsOf,
    CashFinancingSummary CashFinancing,
    string Summary);

public sealed record EquityChangeQuery(
    string FundProfileId,
    DateTimeOffset From,
    DateTimeOffset To,
    string? Currency = null);

public sealed record EquityChangeComponentDto(
    string Label,
    decimal Amount,
    bool IsMaterial,
    string Tone,
    string? Note = null);

public sealed record EquityChangeExplanationDto(
    string FundProfileId,
    string FundDisplayName,
    string Currency,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal OpeningEquity,
    decimal ClosingEquity,
    decimal NetChange,
    decimal ExplainedChange,
    decimal ResidualDelta,
    bool IsResidualMaterial,
    string Status,
    IReadOnlyList<EquityChangeComponentDto> Components,
    IReadOnlyList<string> Warnings,
    bool HasSufficientEvidence);

public sealed record PnlReconciliationQuery(
    string FundProfileId,
    DateTimeOffset From,
    DateTimeOffset To,
    string? Currency = null);

public sealed record PnlReconciliationLineDto(
    string Label,
    decimal Amount,
    decimal RunningTotal,
    string Tone,
    string? Note = null);

public sealed record PnlReconciliationDto(
    string FundProfileId,
    string FundDisplayName,
    string Currency,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal PortfolioPnl,
    decimal LedgerPnl,
    decimal ExplainedPnl,
    decimal Gap,
    decimal Tolerance,
    bool IsWithinTolerance,
    string Status,
    IReadOnlyList<PnlReconciliationLineDto> Lines,
    IReadOnlyList<string> Warnings,
    bool HasSufficientEvidence);
