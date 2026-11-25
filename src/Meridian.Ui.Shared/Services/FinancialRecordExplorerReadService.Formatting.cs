using System.Globalization;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    private static int DistinctNonEmptyCount(IEnumerable<string?> values)
        => values
            .Where(HasText)
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static bool IsSameLineKey(string? left, string? right)
        => HasText(left) && HasText(right) && string.Equals(left!.Trim(), right!.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool HasText(string? value)
        => !string.IsNullOrWhiteSpace(value);

    private static bool ContainsToken(string? value, string token)
        => !string.IsNullOrWhiteSpace(value) &&
           value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string EmptyFallback(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Plural(int count)
        => count == 1 ? string.Empty : "s";

    private static FinancialRecordExplorerTone ToneFromRunStatus(StrategyRunStatus status)
        => status switch
        {
            StrategyRunStatus.Completed => FinancialRecordExplorerTone.Success,
            StrategyRunStatus.Failed or StrategyRunStatus.Cancelled => FinancialRecordExplorerTone.Danger,
            StrategyRunStatus.Running or StrategyRunStatus.Paused => FinancialRecordExplorerTone.Warning,
            _ => FinancialRecordExplorerTone.Default
        };

    private static FinancialRecordExplorerTone ToneFromBalance(decimal value)
        => value > 0 ? FinancialRecordExplorerTone.Success :
            value < 0 ? FinancialRecordExplorerTone.Warning :
            FinancialRecordExplorerTone.Default;

    private static FinancialRecordExplorerTone ToneFromCoverage(WorkstationSecurityCoverageStatus? status)
        => status switch
        {
            WorkstationSecurityCoverageStatus.Resolved => FinancialRecordExplorerTone.Success,
            WorkstationSecurityCoverageStatus.Partial => FinancialRecordExplorerTone.Warning,
            WorkstationSecurityCoverageStatus.Missing or WorkstationSecurityCoverageStatus.Unavailable => FinancialRecordExplorerTone.Danger,
            _ => FinancialRecordExplorerTone.Warning
        };

    private static FinancialRecordExplorerTone ToneFromTrustPosture(SecurityMasterTrustTone tone)
        => tone switch
        {
            SecurityMasterTrustTone.Trusted => FinancialRecordExplorerTone.Success,
            SecurityMasterTrustTone.Review => FinancialRecordExplorerTone.Warning,
            SecurityMasterTrustTone.Blocked => FinancialRecordExplorerTone.Danger,
            _ => FinancialRecordExplorerTone.Default
        };

    private static FinancialRecordExplorerTone ToneFromIdentifierConfidence(InstrumentPassportDto passport)
    {
        var confidence = SelectBestProviderConfidence(passport);
        if (confidence is not null)
        {
            return ToneFromConfidence(confidence.ConfidenceScore);
        }

        return passport.IdentifierSummary.HasPrimaryIdentifier
            ? FinancialRecordExplorerTone.Info
            : FinancialRecordExplorerTone.Warning;
    }

    private static FinancialRecordExplorerTone ToneFromConfidence(decimal score)
    {
        var percentage = score <= 1m ? score * 100m : score;
        return percentage >= 80m ? FinancialRecordExplorerTone.Success :
            percentage >= 50m ? FinancialRecordExplorerTone.Warning :
            FinancialRecordExplorerTone.Danger;
    }

    private static FinancialRecordExplorerTone ToneFromAssetOperationsReadiness(AssetOperationsReadinessDto? readiness)
    {
        if (readiness is null)
        {
            return FinancialRecordExplorerTone.Default;
        }

        if (readiness.MissingCapabilities.Count == 0 &&
            readiness.Warnings.Count == 0 &&
            ContainsStatusToken(readiness.Status, "ready"))
        {
            return FinancialRecordExplorerTone.Success;
        }

        return ToneFromStatusText(readiness.Status, FinancialRecordExplorerTone.Warning);
    }

    private static FinancialRecordExplorerTone ToneFromDirectLending(SecurityInstrumentEnrichment enrichment)
    {
        if (enrichment.DirectLendingHealth.Count == 0)
        {
            return FinancialRecordExplorerTone.Default;
        }

        if (enrichment.DirectLendingHealth.Any(static health => IsNegativeStatus(health.HealthStatus)))
        {
            return FinancialRecordExplorerTone.Warning;
        }

        return enrichment.DirectLendingHealth.All(static health => ContainsStatusToken(health.HealthStatus, "ready"))
            ? FinancialRecordExplorerTone.Success
            : FinancialRecordExplorerTone.Info;
    }

    private static FinancialRecordExplorerTone ToneFromCashFlows(AssetOperationsDetailDto operations)
        => operations.ProjectedCashFlows.Count == 0
            ? FinancialRecordExplorerTone.Warning
            : operations.ProjectedCashFlows.Any(static flow => IsNegativeStatus(flow.Status))
                ? FinancialRecordExplorerTone.Warning
                : FinancialRecordExplorerTone.Success;

    private static FinancialRecordExplorerTone ToneFromLedgerProjections(AssetOperationsDetailDto operations)
        => operations.LedgerProjections.Count == 0
            ? FinancialRecordExplorerTone.Warning
            : operations.LedgerProjections.Any(static projection => IsNegativeStatus(projection.Status))
                ? FinancialRecordExplorerTone.Warning
                : FinancialRecordExplorerTone.Success;

    private static FinancialRecordExplorerTone ToneFromReconciliationResults(AssetOperationsDetailDto operations)
        => operations.ReconciliationResults.Count == 0
            ? FinancialRecordExplorerTone.Default
            : operations.ReconciliationResults.Any(static result => IsNegativeStatus(result.MatchStatus))
                ? FinancialRecordExplorerTone.Warning
                : FinancialRecordExplorerTone.Success;

    private static FinancialRecordExplorerTone ToneFromStatusText(string? status, FinancialRecordExplorerTone fallback)
    {
        if (!HasText(status))
        {
            return fallback;
        }

        return IsNegativeStatus(status) ? FinancialRecordExplorerTone.Warning :
            ContainsStatusToken(status, "ready") ||
            ContainsStatusToken(status, "matched") ||
            ContainsStatusToken(status, "complete") ||
            ContainsStatusToken(status, "posted") ||
            ContainsStatusToken(status, "projected") ||
            ContainsStatusToken(status, "generated")
                ? FinancialRecordExplorerTone.Success
                : fallback;
    }

    private static bool IsNegativeStatus(string? status)
        => ContainsStatusToken(status, "block") ||
           ContainsStatusToken(status, "missing") ||
           ContainsStatusToken(status, "fail") ||
           ContainsStatusToken(status, "break") ||
           ContainsStatusToken(status, "mismatch") ||
           ContainsStatusToken(status, "error");

    private static bool ContainsStatusToken(string? status, string token)
        => !string.IsNullOrWhiteSpace(status) &&
           status.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static FinancialRecordExplorerTone ToneFromReportPackState(ReportPackWorkflowStateDto state)
        => state switch
        {
            ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Archived => FinancialRecordExplorerTone.Success,
            ReportPackWorkflowStateDto.Restated => FinancialRecordExplorerTone.Warning,
            ReportPackWorkflowStateDto.Rejected => FinancialRecordExplorerTone.Danger,
            ReportPackWorkflowStateDto.Approved or ReportPackWorkflowStateDto.PendingApproval => FinancialRecordExplorerTone.Info,
            ReportPackWorkflowStateDto.InReview or ReportPackWorkflowStateDto.Validated => FinancialRecordExplorerTone.Warning,
            _ => FinancialRecordExplorerTone.Default
        };

    private static FinancialRecordExplorerTone ToneFromDeliveryState(ReportPackDeliveryStateDto state)
        => state switch
        {
            ReportPackDeliveryStateDto.Delivered => FinancialRecordExplorerTone.Success,
            ReportPackDeliveryStateDto.Failed => FinancialRecordExplorerTone.Danger,
            _ => FinancialRecordExplorerTone.Warning
        };

    private static FinancialRecordExplorerTone ToneFromReconciliationOutcome(string? outcome)
    {
        if (!HasText(outcome))
        {
            return FinancialRecordExplorerTone.Warning;
        }

        var value = outcome!.Trim();
        return value.Contains("match", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("cleared", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("resolved", StringComparison.OrdinalIgnoreCase)
            ? FinancialRecordExplorerTone.Success
            : FinancialRecordExplorerTone.Warning;
    }

    private static string FormatCurrency(decimal value)
        => value.ToString("C2", CultureInfo.CurrentCulture);

    private static string NormalizeExplorerId(string explorerId)
        => explorerId.Trim().ToLowerInvariant();

    private static string Slugify(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? "view" : slug;
    }

}
