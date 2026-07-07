using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Statement reconciliation break-queue mapping helpers for the workstation API surface:
/// maps statement breaks to queue items, categorizes/severity-maps them, computes source
/// fingerprints, normalizes references/metadata, and resolves exception routing. Split out
/// of the WorkstationEndpoints core partial (async seed/publish methods remain in core).
/// </summary>
public static partial class WorkstationEndpoints
{
    private static ReconciliationBreakQueueItem MapStatementBreakToQueueItem(StatementBreakDto statementBreak)
    {
        var observedAt = statementBreak.LastObservedAtUtc ?? statementBreak.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var variance = Math.Abs(statementBreak.Delta ?? ((statementBreak.StatementAmount ?? 0m) - (statementBreak.BookAmount ?? 0m)));
        var category = MapStatementBreakCategory(statementBreak.BreakType);
        var severity = MapStatementBreakSeverity(statementBreak.Severity, variance, statementBreak.Tolerance);
        var routing = ResolveReconciliationExceptionRouting(category, severity, variance);
        var fingerprint = ComputeStatementBreakFingerprint(statementBreak);
        var sourceReference = NormalizeMetadata(statementBreak.StatementReference) ?? NormalizeMetadata(statementBreak.InternalReference) ?? statementBreak.BreakId;

        return new ReconciliationBreakQueueItem(
            BreakId: $"statement:{fingerprint}",
            RunId: NormalizeMetadata(statementBreak.InternalReference) ?? "statement-reconciliation",
            StrategyName: "Statement reconciliation",
            Category: category,
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: variance,
            Reason: NormalizeMetadata(statementBreak.Description) ?? $"Statement {statementBreak.BreakType?.ToString() ?? "break"} requires review.",
            AssignedTo: NormalizeMetadata(statementBreak.Owner),
            DetectedAt: statementBreak.CreatedAtUtc ?? observedAt,
            LastUpdatedAt: observedAt,
            Severity: severity,
            ExceptionRoute: routing.ExceptionRoute,
            ToleranceProfileId: routing.ToleranceProfileId,
            ToleranceBand: statementBreak.Tolerance ?? routing.ToleranceBand,
            RequiredSignoffRole: routing.RequiredSignoffRole,
            SignoffStatus: routing.SignoffStatus,
            ExplainabilitySummary: statementBreak.Description,
            RoutingTarget: "/accounting/reconciliation/statements",
            RoutingDetail: $"Review statement reconciliation break {sourceReference ?? statementBreak.BreakId ?? fingerprint} in accounting queue.",
            RecommendedAction: NormalizeMetadata(statementBreak.RecommendedAction) ?? "ReviewAndResolve",
            SourceType: "statement",
            SourceSystem: "statement-reconciliation",
            SourceReference: sourceReference,
            SourceImportId: ExtractStatementImportId(statementBreak.StatementReference),
            SourceBreakId: statementBreak.BreakId,
            SourceFingerprint: fingerprint,
            EvidenceLinks: string.IsNullOrWhiteSpace(statementBreak.EvidenceLink) ? null : [statementBreak.EvidenceLink],
            EvidenceCount: string.IsNullOrWhiteSpace(statementBreak.EvidenceLink) ? 0 : 1,
            LedgerBookId: null);
    }

    private static bool IsOpenStatementBreak(string? status)
        => string.IsNullOrWhiteSpace(status) ||
           status.Equals("open", StringComparison.OrdinalIgnoreCase) ||
           status.Equals("review", StringComparison.OrdinalIgnoreCase) ||
           status.Equals("inreview", StringComparison.OrdinalIgnoreCase) ||
           status.Equals("in-review", StringComparison.OrdinalIgnoreCase);

    private static ReconciliationBreakCategory MapStatementBreakCategory(StatementBreakType? breakType)
        => breakType switch
        {
            StatementBreakType.MissingStatementPosition or StatementBreakType.MissingBookPosition or StatementBreakType.PositionQuantityMismatch or StatementBreakType.PositionMarketValueMismatch
                => ReconciliationBreakCategory.MissingPortfolioCoverage,
            StatementBreakType.MissingStatementCash or StatementBreakType.MissingBookCash or StatementBreakType.CashBalanceMismatch
                => ReconciliationBreakCategory.CashMismatch,
            StatementBreakType.SecurityIdentifierMismatch or StatementBreakType.ClassificationMismatch
                => ReconciliationBreakCategory.ClassificationGap,
            StatementBreakType.ValidationFailure or StatementBreakType.DuplicateStatementItem
                => ReconciliationBreakCategory.MissingLedgerCoverage,
            _ => ReconciliationBreakCategory.ExternalStatementMismatch
        };

    private static ReconciliationBreakSeverity MapStatementBreakSeverity(StatementValidationSeverity? severity, decimal variance, decimal? tolerance)
        => severity switch
        {
            StatementValidationSeverity.Critical => ReconciliationBreakSeverity.Critical,
            StatementValidationSeverity.Error => ReconciliationBreakSeverity.High,
            StatementValidationSeverity.Warning => ReconciliationBreakSeverity.Medium,
            StatementValidationSeverity.Info => ReconciliationBreakSeverity.Info,
            _ when tolerance.HasValue && variance > tolerance.Value * 10m => ReconciliationBreakSeverity.High,
            _ => ReconciliationBreakSeverity.Medium
        };

    private static string ComputeStatementBreakFingerprint(StatementBreakDto statementBreak)
    {
        // Use the same null-Delta fallback as MapStatementBreakToQueueItem's variance so breaks
        // with distinct StatementAmount/BookAmount do not collide to the same BreakId.
        var delta = statementBreak.Delta ?? ((statementBreak.StatementAmount ?? 0m) - (statementBreak.BookAmount ?? 0m));
        return ComputeReconciliationSourceFingerprint(
            "statement",
            NormalizeStatementReference(statementBreak.StatementReference),
            statementBreak.BreakType?.ToString(),
            statementBreak.Currency,
            delta.ToString(CultureInfo.InvariantCulture),
            (statementBreak.Tolerance ?? 0m).ToString(CultureInfo.InvariantCulture),
            NormalizeMetadata(statementBreak.Description));
    }

    private static string? ExtractStatementImportId(string? value)
    {
        var normalized = NormalizeMetadata(value);
        var separator = normalized?.IndexOf(':');
        return separator is > 0 ? normalized![..separator.Value] : null;
    }

    private static string NormalizeStatementReference(string? value)
    {
        var normalized = NormalizeMetadata(value);
        if (normalized is null)
        {
            return string.Empty;
        }

        var separator = normalized.LastIndexOf(':');
        return separator >= 0 && separator + 1 < normalized.Length
            ? normalized[(separator + 1)..]
            : normalized;
    }

    private static string? NormalizeMetadata(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ComputeReconciliationSourceFingerprint(params string?[] parts)
    {
        var payload = string.Join("|", parts.Select(static part => part?.Trim().ToUpperInvariant() ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private sealed record ReconciliationExceptionRouting(
        string ExceptionRoute,
        string ToleranceProfileId,
        decimal ToleranceBand,
        string RequiredSignoffRole,
        string SignoffStatus);

    private static ReconciliationExceptionRouting ResolveReconciliationExceptionRouting(
        ReconciliationBreakCategory category,
        ReconciliationBreakSeverity severity,
        decimal variance)
    {
        if (severity == ReconciliationBreakSeverity.Critical)
        {
            return new ReconciliationExceptionRouting(
                ExceptionRoute: category is ReconciliationBreakCategory.MissingLedgerCoverage or ReconciliationBreakCategory.MissingBankCoverage
                    ? "accounting-coverage-escalation"
                    : "accounting-variance-escalation",
                ToleranceProfileId: "critical-zero-tolerance",
                ToleranceBand: 0m,
                RequiredSignoffRole: "Accounting sign-off",
                SignoffStatus: "pending-signoff");
        }

        if (severity == ReconciliationBreakSeverity.High)
        {
            return new ReconciliationExceptionRouting(
                ExceptionRoute: "fund-ops-review",
                ToleranceProfileId: "high-variance-watch",
                ToleranceBand: Math.Max(100m, Math.Round(variance * 0.05m, 2)),
                RequiredSignoffRole: "Fund operations lead",
                SignoffStatus: "pending-signoff");
        }

        if (category is ReconciliationBreakCategory.ClassificationGap or ReconciliationBreakCategory.MissingPortfolioCoverage)
        {
            return new ReconciliationExceptionRouting(
                ExceptionRoute: "security-master-accounting-review",
                ToleranceProfileId: "coverage-classification-review",
                ToleranceBand: 0m,
                RequiredSignoffRole: "Accounting analyst",
                SignoffStatus: "routing-review");
        }

        if (severity == ReconciliationBreakSeverity.Low || severity == ReconciliationBreakSeverity.Info)
        {
            return new ReconciliationExceptionRouting(
                ExceptionRoute: "ops-monitor",
                ToleranceProfileId: "low-variance-watch",
                ToleranceBand: 500m,
                RequiredSignoffRole: "Operations reviewer",
                SignoffStatus: "monitor");
        }

        return new ReconciliationExceptionRouting(
            ExceptionRoute: "operations-triage",
            ToleranceProfileId: "standard-recon-tolerance",
            ToleranceBand: Math.Max(250m, Math.Round(variance * 0.02m, 2)),
            RequiredSignoffRole: "Operations reviewer",
            SignoffStatus: "pending-signoff");
    }
}
