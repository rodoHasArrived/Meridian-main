using Meridian.Application.FundAccounts;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

public interface IFundReconciliationWorkbenchService
{
    Task<FundReconciliationWorkbenchSnapshot> GetSnapshotAsync(string fundProfileId, CancellationToken ct = default);

    Task<FundReconciliationDetailModel?> GetBreakDetailAsync(
        FundReconciliationBreakQueueRow breakRow,
        string baseCurrency,
        CancellationToken ct = default);

    Task<FundReconciliationDetailModel?> GetRunDetailAsync(
        FundReconciliationRunRow runRow,
        string baseCurrency,
        CancellationToken ct = default);

    Task<WorkstationReconciliationActionResult> StartReviewAsync(
        FundReconciliationBreakQueueRow breakRow,
        string operatorName,
        string? note,
        CancellationToken ct = default);

    Task<WorkstationReconciliationActionResult> ResolveAsync(
        FundReconciliationBreakQueueRow breakRow,
        string operatorName,
        string note,
        CancellationToken ct = default);

    Task<WorkstationReconciliationActionResult> DismissAsync(
        FundReconciliationBreakQueueRow breakRow,
        string operatorName,
        string note,
        CancellationToken ct = default);
}

public sealed class FundReconciliationWorkbenchService : IFundReconciliationWorkbenchService
{
    private readonly ReconciliationReadService _reconciliationReadService;
    private readonly IFundAccountService _fundAccountService;
    private readonly StrategyRunWorkspaceService _runWorkspaceService;
    private readonly IWorkstationReconciliationApiClient _apiClient;

    public FundReconciliationWorkbenchService(
        ReconciliationReadService reconciliationReadService,
        IFundAccountService fundAccountService,
        StrategyRunWorkspaceService runWorkspaceService,
        IWorkstationReconciliationApiClient apiClient)
    {
        _reconciliationReadService = reconciliationReadService ?? throw new ArgumentNullException(nameof(reconciliationReadService));
        _fundAccountService = fundAccountService ?? throw new ArgumentNullException(nameof(fundAccountService));
        _runWorkspaceService = runWorkspaceService ?? throw new ArgumentNullException(nameof(runWorkspaceService));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<FundReconciliationWorkbenchSnapshot> GetSnapshotAsync(string fundProfileId, CancellationToken ct = default)
    {
        var summaryTask = _reconciliationReadService.GetAsync(fundProfileId, ct);
        var calibrationSummaryTask = _apiClient.GetCalibrationSummaryAsync(ct);
        var breakQueueTask = _apiClient.GetBreakQueueAsync(ct);
        var runsTask = _runWorkspaceService.GetRecordedRunsAsync(ct);

        await Task.WhenAll(summaryTask, calibrationSummaryTask, breakQueueTask, runsTask).ConfigureAwait(false);

        var summary = await summaryTask.ConfigureAwait(false);
        var calibrationSummary = await calibrationSummaryTask.ConfigureAwait(false);
        var runs = await runsTask.ConfigureAwait(false);
        var relevantRuns = runs
            .Where(run => string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var runIds = relevantRuns
            .Select(run => run.RunId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runNames = relevantRuns.ToDictionary(run => run.RunId, run => run.StrategyName, StringComparer.OrdinalIgnoreCase);

        var breakQueue = await breakQueueTask.ConfigureAwait(false);
        var breakQueueItems = breakQueue
            .Where(item => runIds.Contains(item.RunId))
            .Select(item => MapBreakQueueRow(item, runNames))
            .OrderBy(static item => GetBreakQueuePriority(item.Status))
            .ThenByDescending(item => item.Variance)
            .ThenByDescending(item => item.DetectedAt)
            .ToArray();

        var runRows = summary.RecentRuns
            .Select(MapRunRow)
            .OrderBy(item => item.HasOpenExceptions ? 0 : 1)
            .ThenByDescending(item => item.SecurityIssueCount)
            .ThenByDescending(item => item.RequestedAt)
            .ToArray();
        var calibrationProfiles = calibrationSummary?.Profiles
            .Select(MapCalibrationProfileRow)
            .OrderByDescending(item => item.PendingSignoffCount)
            .ThenByDescending(item => item.ActiveBreakCount)
            .ThenBy(item => item.ToleranceProfileId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ExceptionRoute, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        return new FundReconciliationWorkbenchSnapshot(
            Summary: summary,
            CalibrationSummary: calibrationSummary,
            CalibrationProfiles: calibrationProfiles,
            BreakQueueItems: breakQueueItems,
            RunRows: runRows,
            RefreshedAt: DateTimeOffset.UtcNow,
            InReviewBreakCount: breakQueueItems.Count(item => item.Status == ReconciliationBreakQueueStatus.InReview));
    }

    public async Task<FundReconciliationDetailModel?> GetBreakDetailAsync(
        FundReconciliationBreakQueueRow breakRow,
        string baseCurrency,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(breakRow);

        var detail = await _apiClient.GetLatestRunDetailAsync(breakRow.RunId, ct).ConfigureAwait(false);
        return detail is null ? null : BuildStrategyDetail(detail, baseCurrency, breakRow);
    }

    public async Task<FundReconciliationDetailModel?> GetRunDetailAsync(
        FundReconciliationRunRow runRow,
        string baseCurrency,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runRow);

        if (runRow.SourceType == FundReconciliationSourceType.AccountRun)
        {
            var results = await _fundAccountService
                .GetReconciliationResultsAsync(runRow.ReconciliationRunId, ct)
                .ConfigureAwait(false);
            return BuildAccountDetail(runRow, results, baseCurrency);
        }

        ReconciliationRunDetail? detail = null;
        if (!string.IsNullOrWhiteSpace(runRow.RunId))
        {
            detail = await _apiClient.GetLatestRunDetailAsync(runRow.RunId, ct).ConfigureAwait(false);
        }

        detail ??= await _apiClient.GetRunDetailAsync(runRow.ReconciliationRunId.ToString("N"), ct).ConfigureAwait(false);
        return detail is null ? null : BuildStrategyDetail(detail, baseCurrency, focusBreak: null, runOverride: runRow);
    }

    public Task<WorkstationReconciliationActionResult> StartReviewAsync(
        FundReconciliationBreakQueueRow breakRow,
        string operatorName,
        string? note,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(breakRow);

        return _apiClient.ReviewBreakAsync(
            breakRow.BreakId,
            new ReviewReconciliationBreakRequest(
                BreakId: breakRow.BreakId,
                AssignedTo: operatorName,
                ReviewedBy: operatorName,
                ReviewNote: note),
            ct);
    }

    public Task<WorkstationReconciliationActionResult> ResolveAsync(
        FundReconciliationBreakQueueRow breakRow,
        string operatorName,
        string note,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(breakRow);

        return _apiClient.ResolveBreakAsync(
            breakRow.BreakId,
            new ResolveReconciliationBreakRequest(
                BreakId: breakRow.BreakId,
                Status: ReconciliationBreakQueueStatus.Resolved,
                ResolvedBy: operatorName,
                ResolutionNote: note),
            ct);
    }

    public Task<WorkstationReconciliationActionResult> DismissAsync(
        FundReconciliationBreakQueueRow breakRow,
        string operatorName,
        string note,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(breakRow);

        return _apiClient.ResolveBreakAsync(
            breakRow.BreakId,
            new ResolveReconciliationBreakRequest(
                BreakId: breakRow.BreakId,
                Status: ReconciliationBreakQueueStatus.Dismissed,
                ResolvedBy: operatorName,
                ResolutionNote: note),
            ct);
    }

    private static FundReconciliationBreakQueueRow MapBreakQueueRow(
        ReconciliationBreakQueueItem item,
        IReadOnlyDictionary<string, string> runNames)
    {
        var strategyName = string.IsNullOrWhiteSpace(item.StrategyName) && runNames.TryGetValue(item.RunId, out var resolvedName)
            ? resolvedName
            : item.StrategyName;

        return new FundReconciliationBreakQueueRow(
            BreakId: item.BreakId,
            RunId: item.RunId,
            StrategyName: strategyName ?? item.RunId,
            DisplayLabel: string.IsNullOrWhiteSpace(strategyName)
                ? item.RunId
                : $"{strategyName} · {TrimMiddle(item.RunId)}",
            Status: item.Status,
            StatusLabel: Humanize(item.Status),
            StatusIcon: GetQueueStatusIcon(item.Status),
            CategoryLabel: Humanize(item.Category),
            Variance: item.Variance,
            VarianceText: FormatCurrency(item.Variance, "USD"),
            Reason: item.Reason,
            AssignedToLabel: string.IsNullOrWhiteSpace(item.AssignedTo) ? "Unassigned" : item.AssignedTo,
            DetectedAt: item.DetectedAt,
            DetectedAtText: FormatTimestamp(item.DetectedAt),
            LastUpdatedAt: item.LastUpdatedAt,
            LastUpdatedAtText: FormatTimestamp(item.LastUpdatedAt),
            ReviewedBy: item.ReviewedBy,
            ReviewedAt: item.ReviewedAt,
            ResolvedBy: item.ResolvedBy,
            ResolvedAt: item.ResolvedAt,
            ResolutionNote: item.ResolutionNote,
            Severity: item.Severity,
            ExceptionRouteLabel: string.IsNullOrWhiteSpace(item.ExceptionRoute) ? "Unrouted" : item.ExceptionRoute,
            ToleranceProfileLabel: string.IsNullOrWhiteSpace(item.ToleranceProfileId) ? "Unassigned" : item.ToleranceProfileId,
            RequiredSignoffRoleLabel: string.IsNullOrWhiteSpace(item.RequiredSignoffRole) ? "Not configured" : item.RequiredSignoffRole,
            SignoffStatusLabel: Humanize(item.SignoffStatus));
    }

    private static FundReconciliationCalibrationProfileRow MapCalibrationProfileRow(
        ReconciliationCalibrationProfileSummaryDto profile)
    {
        var activeBreakCount = profile.OpenBreakCount + profile.InReviewBreakCount;
        return new FundReconciliationCalibrationProfileRow(
            ToleranceProfileId: profile.ToleranceProfileId,
            ExceptionRoute: profile.ExceptionRoute,
            HighestSeverity: profile.HighestSeverity,
            HighestSeverityLabel: Humanize(profile.HighestSeverity),
            MaxToleranceBand: profile.MaxToleranceBand,
            MaxToleranceBandText: profile.MaxToleranceBand.HasValue ? profile.MaxToleranceBand.Value.ToString("N2") : "-",
            TotalBreakCount: profile.TotalBreakCount,
            ActiveBreakCount: activeBreakCount,
            PendingSignoffCount: profile.PendingSignoffCount,
            SignedOffCount: profile.SignedOffCount,
            LastUpdatedAtText: FormatTimestamp(profile.LastUpdatedAt));
    }

    private static FundReconciliationRunRow MapRunRow(FundReconciliationItem item)
    {
        var sourceType = string.Equals(item.ScopeLabel, "Strategy Run", StringComparison.OrdinalIgnoreCase)
            ? FundReconciliationSourceType.StrategyRun
            : FundReconciliationSourceType.AccountRun;
        var primaryLabel = sourceType == FundReconciliationSourceType.StrategyRun
            ? item.StrategyName ?? item.AccountDisplayName
            : item.AccountDisplayName;
        var secondaryLabel = sourceType == FundReconciliationSourceType.StrategyRun
            ? TrimMiddle(item.RunId)
            : item.AccountId == Guid.Empty ? "Account run" : TrimMiddle(item.AccountId.ToString("N"));

        return new FundReconciliationRunRow(
            RowKey: sourceType == FundReconciliationSourceType.StrategyRun
                ? $"strategy:{item.RunId}"
                : $"account:{item.ReconciliationRunId:N}",
            ReconciliationRunId: item.ReconciliationRunId,
            AccountId: item.AccountId,
            SourceType: sourceType,
            ScopeLabel: sourceType == FundReconciliationSourceType.StrategyRun ? "Strategy" : "Account",
            ScopeIcon: sourceType == FundReconciliationSourceType.StrategyRun ? "\u2699" : "\u25A3",
            PrimaryLabel: primaryLabel,
            SecondaryLabel: secondaryLabel,
            Status: item.Status,
            StatusLabel: Humanize(item.Status),
            StatusIcon: GetRunStatusIcon(item),
            AsOfDate: item.AsOfDate,
            AsOfDateText: item.AsOfDate.ToString("yyyy-MM-dd"),
            TotalChecks: item.TotalChecks,
            TotalMatched: item.TotalMatched,
            TotalBreaks: item.TotalBreaks,
            BreakAmountTotal: item.BreakAmountTotal,
            BreakAmountText: FormatCurrency(item.BreakAmountTotal, "USD"),
            RequestedAt: item.RequestedAt,
            RequestedAtText: FormatTimestamp(item.RequestedAt),
            CompletedAt: item.CompletedAt,
            CompletedAtText: item.CompletedAt.HasValue ? FormatTimestamp(item.CompletedAt.Value) : "Pending",
            SecurityIssueCount: item.SecurityIssueCount,
            HasSecurityCoverageIssues: item.HasSecurityCoverageIssues,
            CoverageLabel: item.CoverageLabel,
            StrategyName: item.StrategyName,
            RunId: item.RunId);
    }

    private static FundReconciliationDetailModel BuildStrategyDetail(
        ReconciliationRunDetail detail,
        string baseCurrency,
        FundReconciliationBreakQueueRow? focusBreak,
        FundReconciliationRunRow? runOverride = null)
    {
        var strategyName = runOverride?.PrimaryLabel
            ?? focusBreak?.StrategyName
            ?? detail.Summary.RunId;
        var lastUpdated = detail.Summary.CreatedAt;
        var exceptionRows = detail.Breaks
            .Select(breakItem => new FundReconciliationCheckDetailRow(
                RowKey: breakItem.CheckId,
                CheckLabel: breakItem.Label,
                CategoryLabel: Humanize(breakItem.Category),
                StatusLabel: Humanize(breakItem.Status),
                StatusIcon: GetBreakStatusIcon(breakItem.Status),
                SourceLabel: breakItem.MissingSource,
                ExpectedAmountText: FormatCurrency(breakItem.ExpectedAmount, baseCurrency),
                ActualAmountText: FormatCurrency(breakItem.ActualAmount, baseCurrency),
                VarianceText: FormatCurrency(Math.Abs(breakItem.Variance), baseCurrency),
                Reason: breakItem.Reason,
                ExpectedAsOfText: FormatTimestamp(breakItem.ExpectedAsOf),
                ActualAsOfText: FormatTimestamp(breakItem.ActualAsOf),
                IsHighlighted: focusBreak?.BreakId.EndsWith($":{breakItem.CheckId}", StringComparison.OrdinalIgnoreCase) == true))
            .ToArray();
        var allChecks = detail.Matches
            .Select(match => new FundReconciliationCheckDetailRow(
                RowKey: $"match:{match.CheckId}",
                CheckLabel: match.Label,
                CategoryLabel: "Matched",
                StatusLabel: "Matched",
                StatusIcon: "\u2713",
                SourceLabel: $"{match.ExpectedSource} \u2192 {match.ActualSource}",
                ExpectedAmountText: FormatCurrency(match.ExpectedAmount, baseCurrency),
                ActualAmountText: FormatCurrency(match.ActualAmount, baseCurrency),
                VarianceText: FormatCurrency(Math.Abs(match.Variance), baseCurrency),
                Reason: "Matched",
                ExpectedAsOfText: FormatTimestamp(match.ExpectedAsOf),
                ActualAsOfText: FormatTimestamp(match.ActualAsOf)))
            .Concat(exceptionRows)
            .ToArray();

        var securityCoverage = (detail.SecurityCoverageIssues ?? [])
            .Select(issue => new FundReconciliationSecurityCoverageRow(
                Source: issue.Source,
                Symbol: issue.Symbol,
                AccountName: issue.AccountName ?? "\u2014",
                Reason: issue.Reason))
            .ToArray();

        var auditRows = new List<FundReconciliationAuditTrailRow>
        {
            new(
                Timestamp: detail.Summary.CreatedAt,
                TimestampText: FormatTimestamp(detail.Summary.CreatedAt),
                Title: "Reconciliation recorded",
                Description: $"{detail.Summary.MatchCount} matched checks and {detail.Summary.BreakCount} break(s) were captured.",
                ActorLabel: "workstation"),
            new(
                Timestamp: detail.Summary.PortfolioAsOf ?? detail.Summary.CreatedAt,
                TimestampText: FormatTimestamp(detail.Summary.PortfolioAsOf ?? detail.Summary.CreatedAt),
                Title: "Portfolio as of",
                Description: FormatTimestamp(detail.Summary.PortfolioAsOf),
                ActorLabel: "portfolio"),
            new(
                Timestamp: detail.Summary.LedgerAsOf ?? detail.Summary.CreatedAt,
                TimestampText: FormatTimestamp(detail.Summary.LedgerAsOf ?? detail.Summary.CreatedAt),
                Title: "Ledger as of",
                Description: FormatTimestamp(detail.Summary.LedgerAsOf),
                ActorLabel: "ledger")
        };

        if (focusBreak?.ReviewedAt is not null)
        {
            auditRows.Add(new FundReconciliationAuditTrailRow(
                Timestamp: focusBreak.ReviewedAt.Value,
                TimestampText: FormatTimestamp(focusBreak.ReviewedAt.Value),
                Title: "Break moved to review",
                Description: string.IsNullOrWhiteSpace(focusBreak.AssignedToLabel)
                    ? "Assigned for review."
                    : $"Assigned to {focusBreak.AssignedToLabel}.",
                ActorLabel: focusBreak.ReviewedBy ?? "desktop-user"));
        }

        if (focusBreak?.ResolvedAt is not null)
        {
            auditRows.Add(new FundReconciliationAuditTrailRow(
                Timestamp: focusBreak.ResolvedAt.Value,
                TimestampText: FormatTimestamp(focusBreak.ResolvedAt.Value),
                Title: "Break closed",
                Description: string.IsNullOrWhiteSpace(focusBreak.ResolutionNote)
                    ? "Resolution note was not captured."
                    : focusBreak.ResolutionNote,
                ActorLabel: focusBreak.ResolvedBy ?? "desktop-user"));
        }

        auditRows.Sort(static (left, right) => right.Timestamp.CompareTo(left.Timestamp));

        return new FundReconciliationDetailModel(
            SourceType: FundReconciliationSourceType.StrategyRun,
            DetailKey: runOverride?.RowKey ?? $"strategy:{detail.Summary.RunId}",
            Title: strategyName,
            Subtitle: $"Strategy run reconciliation · {detail.Summary.RunId}",
            StatusLabel: GetStrategyStatusLabel(detail.Summary),
            CoverageSummary: detail.Summary.HasSecurityCoverageIssues
                ? $"{detail.Summary.SecurityIssueCount} security coverage issue(s) remain open."
                : "Security Master coverage is aligned for this run.",
            LastUpdatedText: FormatTimestamp(lastUpdated),
            AccountId: null,
            ReconciliationRunId: Guid.TryParse(detail.Summary.ReconciliationRunId, out var parsedGuid) ? parsedGuid : Guid.Empty,
            RunId: detail.Summary.RunId,
            FocusBreakId: focusBreak?.BreakId,
            SupportsBreakActions: focusBreak is not null,
            TotalChecks: detail.Summary.MatchCount + detail.Summary.BreakCount,
            TotalMatched: detail.Summary.MatchCount,
            TotalBreaks: detail.Summary.BreakCount,
            BreakAmountTotal: detail.Breaks.Sum(item => Math.Abs(item.Variance)),
            SecurityIssueCount: detail.Summary.SecurityIssueCount,
            EmptyExceptionsText: "No open strategy-run exceptions remain for this reconciliation.",
            EmptySecurityCoverageText: "Security coverage is fully aligned for this strategy-run reconciliation.",
            ExceptionRows: exceptionRows,
            AllCheckRows: allChecks,
            SecurityCoverageRows: securityCoverage,
            AuditRows: auditRows);
    }

    private static FundReconciliationDetailModel BuildAccountDetail(
        FundReconciliationRunRow runRow,
        IReadOnlyList<AccountReconciliationResultDto> results,
        string baseCurrency)
    {
        var exceptionRows = results
            .Where(result => !result.IsMatch)
            .Select(result => MapAccountResult(result, baseCurrency))
            .ToArray();
        var allRows = results
            .Select(result => MapAccountResult(result, baseCurrency))
            .ToArray();
        var auditRows = new[]
        {
            new FundReconciliationAuditTrailRow(
                Timestamp: runRow.RequestedAt,
                TimestampText: runRow.RequestedAtText,
                Title: "Account reconciliation requested",
                Description: $"{runRow.TotalChecks} check(s) were evaluated for {runRow.PrimaryLabel}.",
                ActorLabel: "desktop-user"),
            new FundReconciliationAuditTrailRow(
                Timestamp: runRow.CompletedAt ?? runRow.RequestedAt,
                TimestampText: runRow.CompletedAtText,
                Title: "Account reconciliation status",
                Description: runRow.StatusLabel,
                ActorLabel: "account-service")
        };

        return new FundReconciliationDetailModel(
            SourceType: FundReconciliationSourceType.AccountRun,
            DetailKey: runRow.RowKey,
            Title: runRow.PrimaryLabel,
            Subtitle: $"Account reconciliation · {runRow.AsOfDateText}",
            StatusLabel: runRow.StatusLabel,
            CoverageSummary: "Account-level reconciliation does not currently expose Security Master coverage metadata.",
            LastUpdatedText: runRow.CompletedAtText,
            AccountId: runRow.AccountId == Guid.Empty ? null : runRow.AccountId,
            ReconciliationRunId: runRow.ReconciliationRunId,
            RunId: null,
            FocusBreakId: null,
            SupportsBreakActions: false,
            TotalChecks: runRow.TotalChecks,
            TotalMatched: runRow.TotalMatched,
            TotalBreaks: runRow.TotalBreaks,
            BreakAmountTotal: runRow.BreakAmountTotal,
            SecurityIssueCount: 0,
            EmptyExceptionsText: "No account-level exception rows are open for this reconciliation run.",
            EmptySecurityCoverageText: "Security coverage detail is only available on strategy-run reconciliation items.",
            ExceptionRows: exceptionRows,
            AllCheckRows: allRows,
            SecurityCoverageRows: [],
            AuditRows: auditRows);
    }

    private static FundReconciliationCheckDetailRow MapAccountResult(AccountReconciliationResultDto result, string baseCurrency)
        => new(
            RowKey: result.ResultId.ToString("N"),
            CheckLabel: result.CheckLabel,
            CategoryLabel: result.Category,
            StatusLabel: result.Status,
            StatusIcon: result.IsMatch ? "\u2713" : "\u26A0",
            SourceLabel: result.Category,
            ExpectedAmountText: FormatCurrency(result.ExpectedAmount, baseCurrency),
            ActualAmountText: FormatCurrency(result.ActualAmount, baseCurrency),
            VarianceText: FormatCurrency(result.Variance.HasValue ? Math.Abs(result.Variance.Value) : null, baseCurrency),
            Reason: result.Reason,
            ExpectedAsOfText: "\u2014",
            ActualAsOfText: "\u2014");

    private static int GetBreakQueuePriority(ReconciliationBreakQueueStatus status) => status switch
    {
        ReconciliationBreakQueueStatus.Open => 0,
        ReconciliationBreakQueueStatus.InReview => 1,
        ReconciliationBreakQueueStatus.Resolved => 2,
        ReconciliationBreakQueueStatus.Dismissed => 3,
        _ => 4
    };

    private static string GetStrategyStatusLabel(ReconciliationRunSummary summary)
    {
        if (summary.OpenBreakCount > 0)
        {
            return "Breaks open";
        }

        if (summary.HasSecurityCoverageIssues)
        {
            return "Security coverage open";
        }

        return "Matched";
    }

    private static string GetQueueStatusIcon(ReconciliationBreakQueueStatus status) => status switch
    {
        ReconciliationBreakQueueStatus.Open => "\u26A0",
        ReconciliationBreakQueueStatus.InReview => "\u25F7",
        ReconciliationBreakQueueStatus.Resolved => "\u2713",
        ReconciliationBreakQueueStatus.Dismissed => "\u2715",
        _ => "\u2022"
    };

    private static string GetRunStatusIcon(FundReconciliationItem item)
    {
        if (item.TotalBreaks > 0)
        {
            return "\u26A0";
        }

        if (item.HasSecurityCoverageIssues)
        {
            return "\u2691";
        }

        return "\u2713";
    }

    private static string GetBreakStatusIcon(ReconciliationBreakStatus status) => status switch
    {
        ReconciliationBreakStatus.Open => "\u26A0",
        ReconciliationBreakStatus.Investigating => "\u25F7",
        ReconciliationBreakStatus.Resolved => "\u2713",
        ReconciliationBreakStatus.Matched => "\u2713",
        _ => "\u2022"
    };

    private static string Humanize<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => Humanize(value.ToString());

    private static string Humanize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\u2014";
        }

        var buffer = new List<char>(value.Length * 2);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character is '_' or '-')
            {
                buffer.Add(' ');
                continue;
            }

            if (index > 0 &&
                char.IsUpper(character) &&
                buffer.Count > 0 &&
                buffer[^1] != ' ')
            {
                buffer.Add(' ');
            }

            buffer.Add(character);
        }

        return string.Join(' ',
            new string(buffer.ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(token => char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant()));
    }

    private static string FormatCurrency(decimal? amount, string currencyCode)
        => amount.HasValue
            ? $"{currencyCode} {amount.Value:N2}"
            : "\u2014";

    private static string FormatTimestamp(DateTimeOffset? value)
        => value.HasValue
            ? value.Value.LocalDateTime.ToString("g")
            : "\u2014";

    private static string TrimMiddle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\u2014";
        }

        if (value.Length <= 14)
        {
            return value;
        }

        return $"{value[..6]}\u2026{value[^6..]}";
    }
}
