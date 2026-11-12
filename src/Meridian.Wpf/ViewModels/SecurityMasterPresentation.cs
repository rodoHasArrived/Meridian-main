using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using ISmQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ISmService = Meridian.Contracts.SecurityMaster.ISecurityMasterService;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>Pure presentation projections for the SecurityMasterViewModel workspace; no mutable view-model state.</summary>
internal static class SecurityMasterPresentation
{

    internal static string BuildHistoryText(IReadOnlyList<SecurityMasterEventEnvelope> history)
    {
        var ordered = history
            .OrderByDescending(item => item.EventTimestamp)
            .ToArray();
        return ordered.Length == 0
            ? "(no history)"
            : string.Join(Environment.NewLine, ordered.Select(item =>
                $"[{item.EventTimestamp:yyyy-MM-dd HH:mm}] {item.EventType}  v{item.StreamVersion} • {item.Actor}"));
    }

    internal static string FormatGoldenCopySourceText(SecurityMasterEconomicDefinitionDrillInDto economicDefinition)
    {
        var source = string.IsNullOrWhiteSpace(economicDefinition.WinningSourceSystem)
            ? "Unknown source"
            : economicDefinition.WinningSourceSystem;
        var recordIdText = string.IsNullOrWhiteSpace(economicDefinition.WinningSourceRecordId)
            ? string.Empty
            : $" • record {economicDefinition.WinningSourceRecordId}";
        var asOfText = economicDefinition.WinningSourceAsOf.HasValue
            ? $" • {FormatHelpers.FormatUtcTimestamp(economicDefinition.WinningSourceAsOf.Value)}"
            : string.Empty;

        return $"{source}{recordIdText}{asOfText}";
    }

    internal static string BuildProvenanceSummaryText(SecurityMasterTrustSnapshotDto snapshot)
    {
        var challengerCount = snapshot.ProvenanceCandidates.Count(candidate => !candidate.IsWinningSource);
        var sourceText = FormatGoldenCopySourceText(snapshot.EconomicDefinition);
        var reasonText = string.IsNullOrWhiteSpace(snapshot.EconomicDefinition.WinningSourceReason)
            ? string.Empty
            : $" • {snapshot.EconomicDefinition.WinningSourceReason}";

        return challengerCount == 0
            ? $"Winning source {sourceText}{reasonText}"
            : $"Winning source {sourceText}{reasonText} • {challengerCount} challenger row(s) remain open";
    }

    internal static string BuildCorporateActionImpactSummary(SecurityMasterTrustSnapshotDto snapshot)
    {
        if (snapshot.CorporateActions.Count == 0)
        {
            return "No corporate actions recorded for the selected security.";
        }

        var upcomingCount = snapshot.CorporateActions.Count(action => action.ExDate >= DateOnly.FromDateTime(DateTime.UtcNow));
        return upcomingCount == 0
            ? $"{snapshot.CorporateActions.Count} corporate action(s) are recorded with no upcoming events in the current review window."
            : $"{upcomingCount} upcoming corporate action(s) require review across {snapshot.CorporateActions.Count} recorded event(s).";
    }

    internal static bool HasProviderMapping(SecurityMasterWorkstationDto result)
        => !string.IsNullOrWhiteSpace(GetMatchedProvider(result)) &&
           !string.IsNullOrWhiteSpace(result.Classification.MatchedIdentifierValue);

    internal static string GetMatchedProvider(SecurityMasterWorkstationDto result)
        => result.Classification.MatchedProvider?.Trim() ?? string.Empty;

    internal static string BuildValidationSummaryText(SecurityMasterTrustSnapshotDto snapshot)
    {
        var report = snapshot.ValidationReport;
        if (report is null)
        {
            return "Validation report unavailable.";
        }

        if (!report.Issues.Any(static issue => issue.Severity != SecurityValidationSeverityDto.Info))
        {
            return "No validation issues detected.";
        }

        var blockingCount = report.CriticalIssueCount + report.ErrorIssueCount;
        var advisoryCount = Math.Max(0, report.Issues.Count(static issue => issue.Severity != SecurityValidationSeverityDto.Info) - blockingCount);
        if (blockingCount > 0 && advisoryCount > 0)
        {
            return $"{blockingCount} blocking issue(s) • {advisoryCount} advisory issue(s)";
        }

        return blockingCount > 0
            ? $"{blockingCount} blocking issue(s)"
            : $"{advisoryCount} advisory issue(s)";
    }

    internal static string BuildIdentifierCoverageSummaryText(SecurityMasterTrustSnapshotDto snapshot)
        => snapshot.IdentifierSummary?.Summary ?? "Identifier summary unavailable.";

    internal static string BuildScheduleSummaryText(SecurityMasterTrustSnapshotDto snapshot)
        => snapshot.ScheduleBook?.Summary ?? snapshot.ScheduleSummary?.Summary ?? "Schedule summary unavailable.";

    internal static string BuildLotModelSummaryText(SecurityMasterTrustSnapshotDto snapshot)
        => snapshot.OpenLotReadModel?.Summary ?? snapshot.LotModel?.Summary ?? "Lot model summary unavailable.";

    internal static string BuildSchemaCompatibilitySummaryText(SecurityMasterTrustSnapshotDto snapshot)
        => snapshot.SchemaCompatibility?.Summary ?? "Schema compatibility unavailable.";

    internal static string FormatNullableDate(DateOnly? value)
        => value?.ToString("yyyy-MM-dd") ?? "Unavailable";

    internal static string FormatNullableDecimal(decimal? value)
        => value?.ToString("G29") ?? "Unavailable";

    internal static SecurityMasterRecommendedActionDto CreateSelectedConflictAction(SecurityMasterConflictAssessmentDto assessment)
        => new(
            Kind: SecurityMasterRecommendedActionKind.ResolveSelectedConflict,
            Title: $"Resolve {FormatFieldLabel(assessment.Conflict.FieldPath)}",
            Detail: $"{assessment.RecommendedWinner} {assessment.ImpactSummary}",
            IsPrimary: true,
            IsEnabled: !string.IsNullOrWhiteSpace(assessment.RecommendedResolution),
            ConflictId: assessment.Conflict.ConflictId);

    internal static int GetImpactSeverityRank(SecurityMasterImpactSeverity severity)
        => severity switch
        {
            SecurityMasterImpactSeverity.High => 3,
            SecurityMasterImpactSeverity.Medium => 2,
            SecurityMasterImpactSeverity.Low => 1,
            SecurityMasterImpactSeverity.None => 0,
            _ => -1
        };

    internal static int GetImpactLinkOrder(SecurityMasterImpactLinkDto link)
        => link.Target switch
        {
            "reconciliation" => 0,
            "ledger" => 1,
            "reportPack" => 2,
            "portfolio" => 3,
            _ => 4
        };

    internal static int GetRecommendedActionOrder(SecurityMasterRecommendedActionKind kind)
        => kind switch
        {
            SecurityMasterRecommendedActionKind.BulkResolveLowRiskConflicts => 1,
            SecurityMasterRecommendedActionKind.BackfillTradingParameters => 2,
            SecurityMasterRecommendedActionKind.ReviewCorporateActions => 3,
            SecurityMasterRecommendedActionKind.OpenReconciliationImpact => 4,
            SecurityMasterRecommendedActionKind.OpenLedgerImpact => 5,
            SecurityMasterRecommendedActionKind.OpenReportPackImpact => 6,
            SecurityMasterRecommendedActionKind.OpenPortfolioImpact => 7,
            SecurityMasterRecommendedActionKind.EditSelectedSecurity => 8,
            _ => 9
        };

    internal static SecurityConflictSecurityContext ToSecurityContext(SecurityMasterWorkstationDto detail)
        => new(
            DisplayName: detail.DisplayName,
            PrimaryIdentifier: string.IsNullOrWhiteSpace(detail.Classification.PrimaryIdentifierValue)
                ? detail.SecurityId.ToString("N")[..8].ToUpperInvariant()
                : $"{detail.Classification.PrimaryIdentifierKind}: {detail.Classification.PrimaryIdentifierValue}",
            AssetClass: detail.Classification.AssetClass);

    internal static SecurityConflictSecurityContext ToSecurityContext(SecurityDetailDto detail)
    {
        var primaryIdentifier = detail.Identifiers.FirstOrDefault(identifier => identifier.IsPrimary);
        return new SecurityConflictSecurityContext(
            DisplayName: detail.DisplayName,
            PrimaryIdentifier: primaryIdentifier is null
                ? detail.SecurityId.ToString("N")[..8].ToUpperInvariant()
                : $"{primaryIdentifier.Kind}: {primaryIdentifier.Value}",
            AssetClass: detail.AssetClass);
    }

    internal static SecurityConflictAssessment AssessConflict(SecurityMasterConflict conflict)
    {
        var category = SecurityMasterConflictFieldCategories.For(conflict);
        var touchesIdentifier = category.HasFlag(SecurityMasterConflictFieldCategory.Identifier);
        var touchesLifecycle = category.HasFlag(SecurityMasterConflictFieldCategory.Lifecycle);
        var touchesTradingParameters = category.HasFlag(SecurityMasterConflictFieldCategory.TradingParameter);
        var touchesCorporateAction = category.HasFlag(SecurityMasterConflictFieldCategory.CorporateAction);
        var touchesClassification = category.HasFlag(SecurityMasterConflictFieldCategory.Classification);
        var touchesCurrency = category.HasFlag(SecurityMasterConflictFieldCategory.Currency);
        var touchesDisplay = category.HasFlag(SecurityMasterConflictFieldCategory.Display);

        var normalizedValueA = NormalizeConflictValue(conflict.ValueA);
        var normalizedValueB = NormalizeConflictValue(conflict.ValueB);
        var sameAfterNormalization =
            normalizedValueA.Length > 0 &&
            normalizedValueA == normalizedValueB;
        var oneSideMissing = string.IsNullOrWhiteSpace(conflict.ValueA) ^ string.IsNullOrWhiteSpace(conflict.ValueB);
        var looksLikeMinorTypo = LooksLikeMinorTypo(normalizedValueA, normalizedValueB);

        var routesToFundReview = true;
        var routesToReconciliation = false;
        var routesToCashFlow = false;
        var routesToReportPack = false;
        var requiresTradingBackfill = false;
        var severityRank = 1;
        var severityLabel = "Review";
        var severityTone = "Info";
        var impactDetail = "Review this mismatch before publishing the selected security into downstream Accounting and Reporting workflows.";

        if (touchesIdentifier || touchesLifecycle || touchesTradingParameters || touchesCurrency)
        {
            severityRank = 3;
            severityLabel = "Critical";
            severityTone = "Error";
            routesToReconciliation = true;
            routesToReportPack = true;
            routesToCashFlow = touchesCorporateAction;
            requiresTradingBackfill = touchesTradingParameters;
            impactDetail = touchesTradingParameters
                ? "Order validation, fill-price rounding, and execution readiness can drift until the authoritative trading parameters are restored."
                : "Portfolio and ledger joins can resolve to the wrong instrument, leaving reconciliation and report-pack coverage open.";
        }
        else if (touchesCorporateAction || touchesClassification)
        {
            severityRank = 2;
            severityLabel = "Elevated";
            severityTone = "Warning";
            routesToReconciliation = touchesCorporateAction || touchesCurrency;
            routesToCashFlow = touchesCorporateAction;
            routesToReportPack = true;
            impactDetail = touchesCorporateAction
                ? "Cash-flow timing, holdings continuity, and report-pack timelines can drift until the corporate action definition is confirmed."
                : "Classification, valuation context, and reporting labels can drift across fund review and report-pack workflows.";
        }
        else if (touchesDisplay)
        {
            routesToReportPack = true;
            impactDetail = "Display-name and alias differences usually do not break joins, but they can leak inconsistent labels into review and reporting workflows.";
        }

        var confidenceLabel = "Manual review required";
        var autoResolveHint = "No safe auto-resolve signal detected. Compare provenance and downstream impact before choosing a provider value.";
        var isAutoResolveSafe = false;
        if (sameAfterNormalization)
        {
            confidenceLabel = "High-confidence normalization match";
            autoResolveHint = "Safe to auto-resolve after confirming provider precedence. The values differ only by casing, whitespace, or punctuation.";
            isAutoResolveSafe = true;
        }
        else if (oneSideMissing)
        {
            confidenceLabel = "Medium-confidence populated value";
            autoResolveHint = "One provider is blank. Keeping the populated side is often safe if provenance confirms that source is authoritative.";
        }
        else if (looksLikeMinorTypo)
        {
            confidenceLabel = "Low-confidence near match";
            autoResolveHint = "Values are close but not equivalent. Treat this as a likely typo and verify against provenance before resolving.";
        }

        var impactSummary = BuildImpactLaneSummary(routesToFundReview, routesToReconciliation, routesToCashFlow, routesToReportPack);
        var nextStepSummary = severityRank >= 3
            ? $"Resolve this before {impactSummary}."
            : $"Review this before {impactSummary}.";

        return new SecurityConflictAssessment(
            SeverityLabel: severityLabel,
            SeverityTone: severityTone,
            SeverityRank: severityRank,
            ConfidenceLabel: confidenceLabel,
            AutoResolveHint: autoResolveHint,
            ImpactSummary: impactSummary,
            ImpactDetail: impactDetail,
            NextStepSummary: nextStepSummary,
            RoutesToFundReview: routesToFundReview,
            RoutesToReconciliation: routesToReconciliation,
            RoutesToCashFlow: routesToCashFlow,
            RoutesToReportPack: routesToReportPack,
            RequiresTradingBackfill: requiresTradingBackfill,
            IsAutoResolveSafe: isAutoResolveSafe);
    }

    internal static string BuildConflictGroupSummary(IReadOnlyList<SecurityConflictLaneEntry> entries)
    {
        if (entries.Count == 0)
        {
            return "No open mismatches remain.";
        }

        var routesToFundReview = entries.Any(entry => entry.RoutesToFundReview);
        var routesToReconciliation = entries.Any(entry => entry.RoutesToReconciliation);
        var routesToCashFlow = entries.Any(entry => entry.RoutesToCashFlow);
        var routesToReportPack = entries.Any(entry => entry.RoutesToReportPack);
        var safeHints = entries.Count(entry => entry.IsAutoResolveSafe);
        var routeSummary = BuildImpactLaneSummary(routesToFundReview, routesToReconciliation, routesToCashFlow, routesToReportPack);
        var safeHintText = safeHints > 0
            ? $" • {safeHints} safe normalization hint{(safeHints == 1 ? string.Empty : "s")}"
            : string.Empty;

        return $"{entries.Count} open mismatch{(entries.Count == 1 ? string.Empty : "es")} • {routeSummary}{safeHintText}";
    }

    internal static string BuildImpactLaneSummary(
        bool routesToFundReview,
        bool routesToReconciliation,
        bool routesToCashFlow,
        bool routesToReportPack)
    {
        var lanes = new List<string>();
        if (routesToFundReview)
        {
            lanes.Add("fund review");
        }

        if (routesToReconciliation)
        {
            lanes.Add("reconciliation");
        }

        if (routesToCashFlow)
        {
            lanes.Add("cash-flow review");
        }

        if (routesToReportPack)
        {
            lanes.Add("report pack");
        }

        return lanes.Count switch
        {
            0 => "manual review",
            1 => lanes[0],
            2 => $"{lanes[0]} and {lanes[1]}",
            _ => $"{string.Join(", ", lanes.Take(lanes.Count - 1))}, and {lanes[^1]}"
        };
    }

    internal static string FormatFieldLabel(string fieldPath)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return "Unknown field";
        }

        var normalized = fieldPath.Trim();
        if (normalized.Contains("Identifiers.Primary", StringComparison.OrdinalIgnoreCase))
        {
            return "Primary identifier";
        }

        if (normalized.Contains("DisplayName", StringComparison.OrdinalIgnoreCase))
        {
            return "Display name";
        }

        if (normalized.Contains("Currency", StringComparison.OrdinalIgnoreCase))
        {
            return "Currency";
        }

        return normalized.Replace(".", " > ", StringComparison.Ordinal);
    }

    internal static string NormalizeConflictValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    internal static bool LooksLikeMinorTypo(string valueA, string valueB)
    {
        if (string.IsNullOrWhiteSpace(valueA) || string.IsNullOrWhiteSpace(valueB))
        {
            return false;
        }

        if (Math.Abs(valueA.Length - valueB.Length) > 1)
        {
            return false;
        }

        if (valueA.Length == valueB.Length)
        {
            var differences = 0;
            for (var index = 0; index < valueA.Length; index++)
            {
                if (valueA[index] != valueB[index] && ++differences > 2)
                {
                    return false;
                }
            }

            return differences > 0;
        }

        var shorter = valueA.Length < valueB.Length ? valueA : valueB;
        var longer = valueA.Length < valueB.Length ? valueB : valueA;
        var shortIndex = 0;
        var longIndex = 0;
        var edits = 0;

        while (shortIndex < shorter.Length && longIndex < longer.Length)
        {
            if (shorter[shortIndex] == longer[longIndex])
            {
                shortIndex++;
                longIndex++;
                continue;
            }

            if (++edits > 1)
            {
                return false;
            }

            longIndex++;
        }

        return true;
    }

    internal static string? TryGetJsonString(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object &&
           json.TryGetProperty(propertyName, out var property) &&
           property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    internal static DateTimeOffset? TryGetJsonDateTimeOffset(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object ||
            !json.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(property.GetString(), out var value) ? value : null;
    }
}
