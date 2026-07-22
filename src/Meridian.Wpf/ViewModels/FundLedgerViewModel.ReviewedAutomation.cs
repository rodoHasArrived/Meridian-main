using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

public sealed partial class FundLedgerViewModel
{
    private static WorkstationStateModel BuildReviewedAutomationState(OperationsReviewedAutomationSummaryDto? reviewedAutomation)
    {
        if (reviewedAutomation is null)
        {
            return WorkstationStateModel.Empty(
                "Reviewed automation unavailable",
                "Operations Continuity has not returned reviewed automation posture for this fund context.",
                "Open Operations Continuity",
                "OperationsContinuity");
        }

        var requiresReview = reviewedAutomation.RequiresHumanReview ||
            reviewedAutomation.Status is EvidenceStatusDto.ReviewRequired or EvidenceStatusDto.Stale;
        var statusLabel = FormatEvidenceStatusLabel(reviewedAutomation.Status);
        var kind = reviewedAutomation.Status switch
        {
            EvidenceStatusDto.Ready when !reviewedAutomation.RequiresHumanReview => WorkstationStateKind.Ready,
            EvidenceStatusDto.Stale => WorkstationStateKind.Stale,
            EvidenceStatusDto.Unknown => WorkstationStateKind.Empty,
            EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing => WorkstationStateKind.Blocked,
            _ => requiresReview ? WorkstationStateKind.Stale : WorkstationStateKind.Blocked
        };
        var readinessTone = reviewedAutomation.Status switch
        {
            EvidenceStatusDto.Ready when !reviewedAutomation.RequiresHumanReview => WorkstationReadinessTone.EvidenceLinked,
            EvidenceStatusDto.Stale => WorkstationReadinessTone.Stale,
            EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing => WorkstationReadinessTone.Blocked,
            _ => requiresReview ? WorkstationReadinessTone.SignoffRequired : WorkstationReadinessTone.Neutral
        };
        var tone = reviewedAutomation.Status switch
        {
            EvidenceStatusDto.Ready when !reviewedAutomation.RequiresHumanReview => WorkspaceTone.Success,
            EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing => WorkspaceTone.Danger,
            EvidenceStatusDto.ReviewRequired or EvidenceStatusDto.Stale => WorkspaceTone.Warning,
            _ => requiresReview ? WorkspaceTone.Warning : WorkspaceTone.Neutral
        };
        var actionPosture = new WorkstationActionPostureModel(
            requiresReview ? "Review automation" : "Review retained evidence",
            "Open Operations Continuity to inspect reviewed automation posture, retained evidence, and material-action guardrails.",
            "OperationsContinuity",
            "Accounting operator",
            readinessTone,
            tone);
        var evidenceLinks = reviewedAutomation.EvidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceId))
            .Select(link => new WorkstationEvidenceLinkModel(
                string.IsNullOrWhiteSpace(link.Label) ? link.EvidenceId : link.Label,
                MapReviewedAutomationRouteTarget(link.Route),
                link.EvidenceId,
                string.IsNullOrWhiteSpace(link.Source) ? "Reviewed automation evidence" : link.Source!))
            .Take(6)
            .ToArray();
        var recoveryActions = reviewedAutomation.RequiredActions
            .Where(static action => !string.IsNullOrWhiteSpace(action))
            .Select(static action => new WorkstationRecoveryActionModel(
                action,
                "Review generated commentary, audit requests, retained evidence, and guardrails before material Financial Operations action.",
                "OperationsContinuity"))
            .Take(3)
            .ToArray();

        if (requiresReview && recoveryActions.Length == 0)
        {
            recoveryActions =
            [
                new WorkstationRecoveryActionModel(
                    "Review automation evidence",
                    "Review generated commentary, audit requests, retained evidence, and guardrails before approval, posting, publication, payment release, or evidence deletion.",
                    "OperationsContinuity")
            ];
        }

        var stage = string.IsNullOrWhiteSpace(reviewedAutomation.Stage)
            ? "Reviewed automation"
            : reviewedAutomation.Stage;
        var summary = string.IsNullOrWhiteSpace(reviewedAutomation.Summary)
            ? "Shared reviewed automation posture returned without summary text."
            : reviewedAutomation.Summary;
        var allowed = FormatReviewedAutomationList(reviewedAutomation.AllowedUseCases, "No allowed use cases returned");
        var prohibited = FormatReviewedAutomationList(reviewedAutomation.ProhibitedActions, "No prohibited actions returned");
        var required = FormatRequiredActions(reviewedAutomation.RequiredActions);

        return new WorkstationStateModel(
            kind,
            requiresReview ? $"Reviewed automation {statusLabel.ToLowerInvariant()}" : "Reviewed automation retained",
            $"{stage}: {summary} Allowed: {allowed}. Prohibited: {prohibited}. Required: {required}.",
            actionPosture.Label,
            actionPosture.Target,
            evidenceLinks.Length == 0 ? "No retained review evidence" : $"{evidenceLinks.Length} retained review evidence link{(evidenceLinks.Length == 1 ? string.Empty : "s")}",
            "\uE9D9",
            tone,
            readinessTone,
            actionPosture,
            evidenceLinks,
            recoveryActions,
            new WorkstationSignoffRequirementModel(
                "Accounting operator",
                requiresReview ? "Human review required" : "Human review retained",
                "Automation remains advisory; material Financial Operations actions require human operator origin.",
                requiresReview ? WorkspaceTone.Warning : WorkspaceTone.Success));
    }

    private static string FormatReviewedAutomationList(IReadOnlyList<string>? values, string fallback)
    {
        var items = (values ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();

        if (items.Length == 0)
        {
            return fallback;
        }

        const int visibleLimit = 5;
        if (items.Length <= visibleLimit)
        {
            return string.Join(", ", items);
        }

        return $"{string.Join(", ", items.Take(visibleLimit))}, +{items.Length - visibleLimit} more";
    }

    private static string MapReviewedAutomationRouteTarget(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "OperationsContinuity";
        }

        return route.Contains("evidence", StringComparison.OrdinalIgnoreCase)
            ? "EvidenceWorkbench"
            : MapAccountingRecordRouteTarget(route);
    }
}
