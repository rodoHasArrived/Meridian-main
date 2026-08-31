namespace Meridian.Ui.Shared.Contracts;

/// <summary>
/// Scoring mode for ranking candidate call options (mirrors
/// <c>Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite.OverwriteScoringMode</c>).
/// </summary>
public enum OverwriteScoringModeDto
{
    /// <summary>Score by raw bid premium dollars.</summary>
    Basic,

    /// <summary>Score by vega-dollar IV residual penalised by spread and boosted by depth.</summary>
    Relative
}

/// <summary>Operator-supplied parameters for a covered-call backtest run.</summary>
public sealed record CoveredCallBacktestRequest(
    string UnderlyingSymbol,
    DateOnly From,
    DateOnly To,
    decimal MinStrike,
    double OverwriteRatio = 0.75,
    double MaxDelta = 0.35,
    int MinDte = 7,
    int? MaxDte = 60,
    double MinIvPercentile = 50.0,
    long MinOpenInterest = 1_000,
    long MinVolume = 100,
    double MaxSpreadPct = 0.05,
    double TakeProfitCapture = 0.80,
    double RollDelta = 0.55,
    int ExDivWindowDays = 7,
    OverwriteScoringModeDto ScoringMode = OverwriteScoringModeDto.Relative,
    double DepthBonusWeight = 0.05,
    double RiskFreeRate = 0.04,
    decimal InitialCash = 100_000m,
    long InitialUnderlyingShares = 100,
    string? Label = null)
{
    private IReadOnlyList<string> _operatorAcceptanceCriteria = [];
    private IReadOnlyList<string> _retainedEvidenceReferences = [];
    private IReadOnlyList<string> _accountingRecordReferences = [];
    private IReadOnlyList<string> _approvalReferences = [];
    private IReadOnlyList<string> _paperValidationReferences = [];
    private IReadOnlyList<string> _governedReportReferences = [];

    public IReadOnlyList<string> OperatorAcceptanceCriteria
    {
        get => _operatorAcceptanceCriteria;
        init => _operatorAcceptanceCriteria = value ?? [];
    }

    public IReadOnlyList<string> RetainedEvidenceReferences
    {
        get => _retainedEvidenceReferences;
        init => _retainedEvidenceReferences = value ?? [];
    }

    public IReadOnlyList<string> AccountingRecordReferences
    {
        get => _accountingRecordReferences;
        init => _accountingRecordReferences = value ?? [];
    }

    public IReadOnlyList<string> ApprovalReferences
    {
        get => _approvalReferences;
        init => _approvalReferences = value ?? [];
    }

    public IReadOnlyList<string> PaperValidationReferences
    {
        get => _paperValidationReferences;
        init => _paperValidationReferences = value ?? [];
    }

    public IReadOnlyList<string> GovernedReportReferences
    {
        get => _governedReportReferences;
        init => _governedReportReferences = value ?? [];
    }
}

/// <summary>
/// Server-resolved ownership for a covered-call run. Endpoint callers must derive this scope from
/// the authenticated workstation session; it is never accepted from the browser request body.
/// </summary>
public sealed record CoveredCallRunScope(
    string TenantId,
    string CompanyId,
    string Actor);

/// <summary>Returned by <c>POST /api/strategies/covered-call/runs</c>.</summary>
public sealed record CoveredCallRunHandle(string RunId, DateTimeOffset QueuedAt);

/// <summary>Snapshot of a run's current lifecycle phase.</summary>
public sealed record CoveredCallRunStatusDto(
    string RunId,
    string Phase,
    double PercentComplete,
    DateOnly? CurrentBacktestDate,
    string? FailureMessage);

/// <summary>One point on the strategy/underlying equity curves.</summary>
public sealed record CoveredCallEquityPoint(
    DateOnly Date,
    decimal StrategyEquity,
    decimal UnderlyingEquity);

/// <summary>Closed-out short-call trade.</summary>
public sealed record CoveredCallTradeDto(
    decimal Strike,
    DateOnly Expiration,
    int Contracts,
    int Multiplier,
    DateOnly EntryDate,
    decimal EntryCredit,
    DateOnly ExitDate,
    decimal ExitDebit,
    string ExitReason,
    double? EntryImpliedVolatility,
    decimal NetPnlPerContract,
    decimal TotalNetPnl,
    int HoldingDays,
    bool IsWin,
    bool WasAssigned);

/// <summary>Open short-call position at end of run.</summary>
public sealed record CoveredCallOpenPositionDto(
    Guid PositionId,
    decimal Strike,
    DateOnly Expiration,
    int Contracts,
    int Multiplier,
    DateOnly EntryDate,
    decimal EntryCredit,
    decimal MarkToClose,
    double CurrentDelta,
    int CurrentDte,
    decimal UnrealisedPnl,
    double PremiumCaptured);

/// <summary>Performance metrics for a completed run (mirrors <c>OptionsOverwriteMetrics</c>).</summary>
public sealed record CoveredCallMetricsDto(
    double Cagr,
    double AnnualizedVolatility,
    double SharpeRatio,
    double SortinoRatio,
    double CalmarRatio,
    double MaxDrawdownPct,
    double WinRate,
    double AssignmentRate,
    double AverageHoldingDays,
    int TotalOptionTrades,
    int AssignedTrades,
    decimal TotalPremiumCollected,
    decimal TotalOptionPnl,
    double UpCapture,
    double DownCapture,
    double MonthlyVar1Pct,
    double MonthlyVar5Pct,
    double MonthlyCVar5Pct,
    double ReturnSkewness,
    double ReturnKurtosis,
    double AnnualizedTurnover);

/// <summary>Full result for a completed run.</summary>
public sealed record CoveredCallRunResult(
    string RunId,
    string UnderlyingSymbol,
    DateOnly From,
    DateOnly To,
    string? Label,
    CoveredCallMetricsDto Metrics,
    IReadOnlyList<CoveredCallEquityPoint> EquityCurve,
    IReadOnlyList<CoveredCallTradeDto> Trades,
    IReadOnlyList<CoveredCallOpenPositionDto> OpenPositionsAtEnd);

/// <summary>Row in the run library / history list.</summary>
public sealed record CoveredCallRunSummary(
    string RunId,
    string UnderlyingSymbol,
    DateOnly From,
    DateOnly To,
    string? Label,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    double? Cagr,
    double? SharpeRatio,
    double? WinRate);

/// <summary>Request body for chain-preview.</summary>
public sealed record CoveredCallChainPreviewRequest(
    string UnderlyingSymbol,
    DateOnly AsOf,
    decimal MinStrike,
    double MaxDelta = 0.35,
    int MinDte = 7,
    int? MaxDte = 60,
    long MinOpenInterest = 1_000,
    long MinVolume = 100,
    double MaxSpreadPct = 0.05);

/// <summary>One candidate call option after filtering, used by the chain-preview UI.</summary>
public sealed record CoveredCallChainRow(
    decimal Strike,
    DateOnly Expiration,
    int DaysToExpiration,
    decimal Bid,
    decimal Ask,
    double Delta,
    double? ImpliedVolatility,
    long OpenInterest,
    long Volume,
    bool MeetsAllFilters,
    string? RejectReason);

/// <summary>Chain-preview response: candidate set plus headline counts.</summary>
public sealed record CoveredCallChainPreview(
    string UnderlyingSymbol,
    DateOnly AsOf,
    decimal UnderlyingPrice,
    IReadOnlyList<CoveredCallChainRow> Candidates,
    int TotalContractsScanned,
    int FiltersPassed);

/// <summary>Standard problem-details payload returned on 4xx/5xx responses.</summary>
public sealed record CoveredCallProblemDetails(string Title, int Status, string? Detail = null);
