using System.Globalization;
using System.Text;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Canonical projection from reconciliation source evidence into governed queue obligations.
/// HTTP reads and statement intake both use this projection so opening a screen is never required
/// to create authoritative casework.
/// </summary>
internal static class ReconciliationBreakQueueProjection
{
    public static ReconciliationBreakQueueItem ProjectStatement(
        StatementBreakDto statementBreak,
        StatementAccountingScope? accountingScope = null,
        string? sourceFundAccountId = null,
        ReconciliationBreakQueueScope? accessScope = null)
    {
        ArgumentNullException.ThrowIfNull(statementBreak);
        var observedAt = statementBreak.LastObservedAtUtc ?? statementBreak.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var variance = Math.Abs(
            statementBreak.Delta
            ?? ((statementBreak.StatementAmount ?? 0m) - (statementBreak.BookAmount ?? 0m)));
        var category = MapStatementBreakCategory(statementBreak.BreakType);
        var severity = MapStatementBreakSeverity(statementBreak.Severity, variance, statementBreak.Tolerance);
        var routing = ResolveRouting(category, severity, variance);
        var fingerprint = ComputeStatementBreakFingerprint(statementBreak);
        var sourceImportId =
            ExtractStatementImportId(statementBreak.StatementReference)
            ?? NormalizeOptional(statementBreak.InternalReference);
        var authorityFingerprint = ComputeSourceFingerprint(
            "statement-case",
            fingerprint,
            sourceImportId,
            accessScope?.TenantId,
            accessScope?.CompanyId,
            accountingScope?.FundProfileId,
            accountingScope?.LedgerBookId.ToString("D"),
            accountingScope?.AccountingPeriodId.ToString("D"),
            accountingScope?.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var sourceReference =
            NormalizeOptional(statementBreak.StatementReference)
            ?? NormalizeOptional(statementBreak.InternalReference)
            ?? statementBreak.BreakId;

        return new ReconciliationBreakQueueItem(
            BreakId: $"statement:{authorityFingerprint}",
            RunId: NormalizeOptional(statementBreak.InternalReference) ?? "statement-reconciliation",
            StrategyName: "Statement reconciliation",
            Category: category,
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: variance,
            Reason: NormalizeOptional(statementBreak.Description)
                ?? $"Statement {statementBreak.BreakType?.ToString() ?? "break"} requires review.",
            AssignedTo: NormalizeOptional(statementBreak.Owner),
            DetectedAt: statementBreak.CreatedAtUtc ?? observedAt,
            LastUpdatedAt: observedAt,
            Severity: severity,
            ExceptionRoute: routing.ExceptionRoute,
            ToleranceProfileId: routing.ToleranceProfileId,
            ToleranceBand: statementBreak.Tolerance ?? routing.ToleranceBand,
            RequiredSignoffRole: routing.RequiredSignoffRole,
            SignoffStatus: routing.SignoffStatus,
            FundAccountId: string.IsNullOrWhiteSpace(sourceFundAccountId)
                ? null
                : sourceFundAccountId.Trim(),
            ExplainabilitySummary: statementBreak.Description,
            RoutingTarget: "/accounting/reconciliation/statements",
            RoutingDetail:
                $"Review statement reconciliation break {sourceReference ?? statementBreak.BreakId ?? fingerprint} in accounting queue.",
            RecommendedAction: NormalizeOptional(statementBreak.RecommendedAction) ?? "ReviewAndResolve",
            SourceType: "statement",
            SourceSystem: "statement-reconciliation",
            SourceReference: sourceReference,
            SourceImportId: sourceImportId,
            SourceBreakId: statementBreak.BreakId,
            SourceFingerprint: fingerprint,
            EvidenceLinks: string.IsNullOrWhiteSpace(statementBreak.EvidenceLink)
                ? null
                : [statementBreak.EvidenceLink],
            EvidenceCount: string.IsNullOrWhiteSpace(statementBreak.EvidenceLink) ? 0 : 1,
            LedgerBookId: accountingScope?.LedgerBookId,
            Measures: statementBreak.Measures ?? BuildStatementBreakMeasures(statementBreak, variance),
            BlockedOutputs: accountingScope is null
                ? ["reconciliation-scope-resolution"]
                : ["FinalReport", "PeriodClose", "ClientDelivery"],
            AccountingPeriodId: accountingScope?.AccountingPeriodId.ToString("D"),
            AsOfDate: accountingScope?.AsOfDate)
        {
            FundProfileId = accountingScope?.FundProfileId,
            TenantId = accessScope?.TenantId,
            CompanyId = accessScope?.CompanyId
        };
    }

    public static bool IsOpenStatementBreak(string? status)
        => string.IsNullOrWhiteSpace(status)
           || status.Equals("open", StringComparison.OrdinalIgnoreCase)
           || status.Equals("review", StringComparison.OrdinalIgnoreCase)
           || status.Equals("inreview", StringComparison.OrdinalIgnoreCase)
           || status.Equals("in-review", StringComparison.OrdinalIgnoreCase);

    public static string ComputeStatementBreakLegacyFingerprint(StatementBreakDto statementBreak)
        => ComputeSourceFingerprint(
            "statement",
            NormalizeStatementReference(statementBreak.StatementReference),
            statementBreak.BreakType?.ToString(),
            statementBreak.Currency,
            (statementBreak.Delta ?? 0m).ToString(CultureInfo.InvariantCulture),
            (statementBreak.Tolerance ?? 0m).ToString(CultureInfo.InvariantCulture),
            NormalizeOptional(statementBreak.Description));

    public static IReadOnlyList<ReconciliationBreakMeasureDto> BuildDefaultMeasures(
        decimal? expected,
        decimal? actual,
        decimal variance,
        decimal? tolerance,
        string unit)
    {
        var hasCompleteComparison = expected.HasValue && actual.HasValue;
        var signedVariance = hasCompleteComparison
            ? actual.GetValueOrDefault() - expected.GetValueOrDefault()
            : (decimal?)null;
        return
        [
            new ReconciliationBreakMeasureDto(
                ReconciliationBreakMeasureKindDto.Value,
                hasCompleteComparison ? expected : null,
                hasCompleteComparison ? actual : null,
                signedVariance,
                tolerance,
                unit,
                hasCompleteComparison
                    ? null
                    : "The source break does not provide a complete expected and actual value pair."),
            new ReconciliationBreakMeasureDto(
                ReconciliationBreakMeasureKindDto.Quantity,
                null,
                null,
                null,
                null,
                "units",
                "The source break does not provide comparable position quantities."),
            new ReconciliationBreakMeasureDto(
                ReconciliationBreakMeasureKindDto.CostBasis,
                null,
                null,
                null,
                null,
                unit,
                "The source break does not provide comparable cost-basis values.")
        ];
    }

    public static string ComputeSourceFingerprint(params string?[] parts)
    {
        var payload = string.Join(
            "|",
            parts.Select(static part => part?.Trim().ToUpperInvariant() ?? string.Empty));
        return Sha256Digest.ComputeUtf8(payload);
    }

    public static ReconciliationExceptionRouting ResolveRouting(
        ReconciliationBreakCategory category,
        ReconciliationBreakSeverity severity,
        decimal variance)
    {
        if (severity == ReconciliationBreakSeverity.Critical)
        {
            return new ReconciliationExceptionRouting(
                category is ReconciliationBreakCategory.MissingLedgerCoverage
                    or ReconciliationBreakCategory.MissingBankCoverage
                    ? "accounting-coverage-escalation"
                    : "accounting-variance-escalation",
                "critical-zero-tolerance",
                0m,
                "Accounting sign-off",
                "pending-signoff");
        }

        if (severity == ReconciliationBreakSeverity.High)
        {
            return new ReconciliationExceptionRouting(
                "fund-ops-review",
                "high-variance-watch",
                Math.Max(100m, Math.Round(variance * 0.05m, 2)),
                "Fund operations lead",
                "pending-signoff");
        }

        if (category is ReconciliationBreakCategory.ClassificationGap
            or ReconciliationBreakCategory.MissingPortfolioCoverage)
        {
            return new ReconciliationExceptionRouting(
                "security-master-accounting-review",
                "coverage-classification-review",
                0m,
                "Accounting analyst",
                "routing-review");
        }

        if (severity is ReconciliationBreakSeverity.Low or ReconciliationBreakSeverity.Info)
        {
            return new ReconciliationExceptionRouting(
                "ops-monitor",
                "low-variance-watch",
                500m,
                "Operations reviewer",
                "monitor");
        }

        return new ReconciliationExceptionRouting(
            "operations-triage",
            "standard-recon-tolerance",
            Math.Max(250m, Math.Round(variance * 0.02m, 2)),
            "Operations reviewer",
            "pending-signoff");
    }

    private static IReadOnlyList<ReconciliationBreakMeasureDto> BuildStatementBreakMeasures(
        StatementBreakDto statementBreak,
        decimal variance)
    {
        var unit = NormalizeOptional(statementBreak.Currency) ?? "currency";
        var valueAvailable = statementBreak.StatementAmount.HasValue && statementBreak.BookAmount.HasValue;
        var quantityBreak = statementBreak.BreakType == StatementBreakType.PositionQuantityMismatch;
        var signedVariance = statementBreak.BookAmount.HasValue && statementBreak.StatementAmount.HasValue
            ? statementBreak.StatementAmount.Value - statementBreak.BookAmount.Value
            : statementBreak.Delta ?? variance;
        return
        [
            new ReconciliationBreakMeasureDto(
                ReconciliationBreakMeasureKindDto.Value,
                valueAvailable && !quantityBreak ? statementBreak.BookAmount : null,
                valueAvailable && !quantityBreak ? statementBreak.StatementAmount : null,
                valueAvailable && !quantityBreak ? signedVariance : null,
                statementBreak.Tolerance,
                unit,
                valueAvailable && !quantityBreak
                    ? null
                    : "The source break does not provide a complete expected and actual market-value pair."),
            new ReconciliationBreakMeasureDto(
                ReconciliationBreakMeasureKindDto.Quantity,
                quantityBreak && valueAvailable ? statementBreak.BookAmount : null,
                quantityBreak && valueAvailable ? statementBreak.StatementAmount : null,
                quantityBreak && valueAvailable ? signedVariance : null,
                quantityBreak ? statementBreak.Tolerance : null,
                "units",
                quantityBreak && valueAvailable
                    ? null
                    : "The source break does not provide a complete expected and actual position-quantity pair."),
            new ReconciliationBreakMeasureDto(
                ReconciliationBreakMeasureKindDto.CostBasis,
                null,
                null,
                null,
                null,
                unit,
                "The source break does not provide comparable cost-basis values.")
        ];
    }

    private static ReconciliationBreakCategory MapStatementBreakCategory(StatementBreakType? breakType)
        => breakType switch
        {
            StatementBreakType.MissingStatementPosition
                or StatementBreakType.MissingBookPosition
                or StatementBreakType.PositionQuantityMismatch
                or StatementBreakType.PositionMarketValueMismatch
                => ReconciliationBreakCategory.MissingPortfolioCoverage,
            StatementBreakType.MissingStatementCash
                or StatementBreakType.MissingBookCash
                or StatementBreakType.CashBalanceMismatch
                => ReconciliationBreakCategory.CashMismatch,
            StatementBreakType.SecurityIdentifierMismatch
                or StatementBreakType.ClassificationMismatch
                => ReconciliationBreakCategory.ClassificationGap,
            StatementBreakType.ValidationFailure
                or StatementBreakType.DuplicateStatementItem
                => ReconciliationBreakCategory.MissingLedgerCoverage,
            _ => ReconciliationBreakCategory.ExternalStatementMismatch
        };

    private static ReconciliationBreakSeverity MapStatementBreakSeverity(
        StatementValidationSeverity? severity,
        decimal variance,
        decimal? tolerance)
        => severity switch
        {
            StatementValidationSeverity.Critical => ReconciliationBreakSeverity.Critical,
            StatementValidationSeverity.Error => ReconciliationBreakSeverity.High,
            StatementValidationSeverity.Warning => ReconciliationBreakSeverity.Medium,
            StatementValidationSeverity.Info => ReconciliationBreakSeverity.Info,
            _ when tolerance.HasValue && variance > tolerance.Value * 10m
                => ReconciliationBreakSeverity.High,
            _ => ReconciliationBreakSeverity.Medium
        };

    private static string ComputeStatementBreakFingerprint(StatementBreakDto statementBreak)
    {
        var delta = statementBreak.Delta
                    ?? ((statementBreak.StatementAmount ?? 0m) - (statementBreak.BookAmount ?? 0m));
        return ComputeSourceFingerprint(
            "statement",
            NormalizeStatementReference(statementBreak.StatementReference),
            statementBreak.BreakType?.ToString(),
            statementBreak.Currency,
            delta.ToString(CultureInfo.InvariantCulture),
            (statementBreak.Tolerance ?? 0m).ToString(CultureInfo.InvariantCulture),
            NormalizeOptional(statementBreak.Description));
    }

    private static string? ExtractStatementImportId(string? value)
    {
        var normalized = NormalizeOptional(value);
        var separator = normalized?.IndexOf(':');
        return separator is > 0 ? normalized![..separator.Value] : null;
    }

    private static string NormalizeStatementReference(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return string.Empty;
        }

        var separator = normalized.LastIndexOf(':');
        return separator >= 0 && separator + 1 < normalized.Length
            ? normalized[(separator + 1)..]
            : normalized;
    }

    internal sealed record ReconciliationExceptionRouting(
        string ExceptionRoute,
        string ToleranceProfileId,
        decimal ToleranceBand,
        string RequiredSignoffRole,
        string SignoffStatus);
}
