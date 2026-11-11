using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

// Operations handoffs: owners, impacted outputs and routes.
public sealed partial class SecurityMasterWorkbenchQueryService
{
    private static IReadOnlyList<InstrumentPassportOperationsHandoffDto> BuildOperationsHandoffs(
        SecurityMasterTrustSnapshotDto snapshot)
    {
        var handoffs = new List<InstrumentPassportOperationsHandoffDto>();

        handoffs.AddRange(snapshot.DownstreamImpact.Links.Select((link, index) =>
            new InstrumentPassportOperationsHandoffDto(
                HandoffId: $"impact-{index + 1}",
                Target: link.Target,
                Title: link.Label,
                Detail: link.Summary,
                Status: link.Severity.ToString(),
                IsEnabled: link.IsActive)
            {
                Owner = ResolveHandoffOwner(link.Target),
                BlockerReason = link.IsActive
                    ? link.Summary
                    : "Downstream impact is not active for the selected Security Master scope.",
                ImpactedOutputs = ResolveImpactedOutputs(link.Target),
                LinkedCases = [],
                Route = ResolveHandoffRoute(link.Target)
            }));

        handoffs.AddRange(snapshot.RecommendedActions.Select((action, index) =>
            new InstrumentPassportOperationsHandoffDto(
                HandoffId: $"action-{index + 1}",
                Target: action.Target ?? action.Kind.ToString(),
                Title: action.Title,
                Detail: action.Detail,
                Status: action.IsPrimary ? "Primary" : "Available",
                IsEnabled: action.IsEnabled)
            {
                Owner = ResolveActionOwner(action.Kind),
                BlockerReason = action.Detail,
                ImpactedOutputs = ResolveImpactedOutputs(action.Kind),
                LinkedCases = action.ConflictId.HasValue ? [action.ConflictId.Value.ToString("D")] : [],
                Route = ResolveActionRoute(action)
            }));

        if (handoffs.Count == 0)
        {
            handoffs.Add(new InstrumentPassportOperationsHandoffDto(
                HandoffId: "downstream-impact",
                Target: "SecurityMasterDetail",
                Title: "Retain Security Master context",
                Detail: snapshot.DownstreamImpact.Summary,
                Status: snapshot.DownstreamImpact.Severity.ToString(),
                IsEnabled: true)
            {
                Owner = "Security Master steward",
                BlockerReason = snapshot.DownstreamImpact.Summary,
                ImpactedOutputs = ResolveImpactedOutputs(snapshot.DownstreamImpact),
                LinkedCases = [],
                Route = "/workstation/accounting/security-master"
            });
        }

        return handoffs
            .OrderByDescending(static handoff => handoff.IsEnabled)
            .ThenBy(static handoff => handoff.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveHandoffOwner(string? target)
    {
        return NormalizeOptional(target)?.ToLowerInvariant() switch
        {
            "portfolio" => "Portfolio operations",
            "ledger" => "Accounting operations",
            "reconciliation" => "Reconciliation owner",
            "reportpack" => "Reporting controller",
            "securitymasterdetail" => "Security Master steward",
            _ => "Security Master steward"
        };
    }

    private static string ResolveActionOwner(SecurityMasterRecommendedActionKind kind) =>
        kind switch
        {
            SecurityMasterRecommendedActionKind.OpenPortfolioImpact => "Portfolio operations",
            SecurityMasterRecommendedActionKind.OpenLedgerImpact => "Accounting operations",
            SecurityMasterRecommendedActionKind.OpenReconciliationImpact => "Reconciliation owner",
            SecurityMasterRecommendedActionKind.OpenReportPackImpact => "Reporting controller",
            SecurityMasterRecommendedActionKind.ResolveSelectedConflict or
            SecurityMasterRecommendedActionKind.BulkResolveLowRiskConflicts or
            SecurityMasterRecommendedActionKind.EditSelectedSecurity or
            SecurityMasterRecommendedActionKind.RefreshTrustSnapshot => "Security Master steward",
            SecurityMasterRecommendedActionKind.BackfillTradingParameters or
            SecurityMasterRecommendedActionKind.ReviewCorporateActions => "Reference-data steward",
            _ => "Security Master steward"
        };

    private static IReadOnlyList<string> ResolveImpactedOutputs(SecurityMasterRecommendedActionKind kind) =>
        kind switch
        {
            SecurityMasterRecommendedActionKind.OpenPortfolioImpact => ["Portfolio"],
            SecurityMasterRecommendedActionKind.OpenLedgerImpact => ["Ledger", "Accounting"],
            SecurityMasterRecommendedActionKind.OpenReconciliationImpact => ["Reconciliation", "Close"],
            SecurityMasterRecommendedActionKind.OpenReportPackImpact => ["Reporting", "Close"],
            SecurityMasterRecommendedActionKind.BackfillTradingParameters => ["Valuation", "Portfolio"],
            SecurityMasterRecommendedActionKind.ReviewCorporateActions => ["Valuation", "Ledger", "Close"],
            SecurityMasterRecommendedActionKind.ResolveSelectedConflict or
            SecurityMasterRecommendedActionKind.BulkResolveLowRiskConflicts => ["Portfolio", "Accounting", "Reconciliation", "Close", "Reporting"],
            _ => ["Security Master"]
        };

    private static IReadOnlyList<string> ResolveImpactedOutputs(string? target) =>
        NormalizeOptional(target)?.ToLowerInvariant() switch
        {
            "portfolio" => ["Portfolio"],
            "ledger" => ["Ledger", "Accounting"],
            "reconciliation" => ["Reconciliation", "Close"],
            "reportpack" => ["Reporting", "Close"],
            _ => ["Security Master"]
        };

    private static IReadOnlyList<string> ResolveImpactedOutputs(SecurityMasterDownstreamImpactDto impact)
    {
        var outputs = new List<string>();
        if (impact.PortfolioExposureCount > 0)
        {
            outputs.Add("Portfolio");
        }

        if (impact.LedgerExposureCount > 0)
        {
            outputs.Add("Ledger");
            outputs.Add("Accounting");
        }

        if (impact.ReconciliationExposureCount > 0)
        {
            outputs.Add("Reconciliation");
            outputs.Add("Close");
        }

        if (impact.ReportPackExposureCount > 0)
        {
            outputs.Add("Reporting");
        }

        return outputs.Count == 0 ? ["Security Master"] : outputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolveActionRoute(SecurityMasterRecommendedActionDto action)
    {
        if (!string.IsNullOrWhiteSpace(action.Target) && action.Target.StartsWith('/'))
        {
            return action.Target;
        }

        return action.Kind switch
        {
            SecurityMasterRecommendedActionKind.OpenPortfolioImpact => "/workstation/portfolio",
            SecurityMasterRecommendedActionKind.OpenLedgerImpact => "/workstation/accounting/ledger",
            SecurityMasterRecommendedActionKind.OpenReconciliationImpact => "/workstation/accounting/reconciliation",
            SecurityMasterRecommendedActionKind.OpenReportPackImpact => "/workstation/reporting/evidence",
            SecurityMasterRecommendedActionKind.BackfillTradingParameters => "/workstation/data/backfills",
            SecurityMasterRecommendedActionKind.ResolveSelectedConflict when action.ConflictId.HasValue =>
                $"/workstation/accounting/security-master#conflict-{action.ConflictId.Value:D}",
            SecurityMasterRecommendedActionKind.BulkResolveLowRiskConflicts => "/workstation/accounting/security-master#conflicts",
            SecurityMasterRecommendedActionKind.ReviewCorporateActions => "/workstation/accounting/security-master#corporate-actions",
            SecurityMasterRecommendedActionKind.EditSelectedSecurity => "/workstation/accounting/security-master#passport",
            _ => "/workstation/accounting/security-master"
        };
    }

    private static string ResolveHandoffRoute(string? target) =>
        NormalizeOptional(target)?.ToLowerInvariant() switch
        {
            "portfolio" => "/workstation/portfolio",
            "ledger" => "/workstation/accounting/ledger",
            "reconciliation" => "/workstation/accounting/reconciliation",
            "reportpack" => "/workstation/reporting/evidence",
            _ => "/workstation/accounting/security-master"
        };
}
