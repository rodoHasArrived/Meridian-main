using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.Export;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using static Meridian.Ui.Shared.Evidence.EvidenceContributionHelpers;

namespace Meridian.Ui.Shared.Evidence;

public sealed class JournalEntryEvidenceContributor : IEvidenceContributor
{
    private readonly IServiceProvider _services;

    public JournalEntryEvidenceContributor(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string ContributorId => "journal-entry";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.JournalEntryKind, StringComparison.OrdinalIgnoreCase);

    public async Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var service = _services.GetService<IManualJournalEntryWorkbenchService>();
        if (service is null)
        {
            return Empty("Manual journal entry workbench service is not registered.");
        }

        if (!Guid.TryParse(context.Subject.SubjectId, out var journalEntryId))
        {
            return Empty($"Journal entry subject id '{context.Subject.SubjectId}' is not a journal entry identifier.");
        }

        var draft = await EvidenceSubjectResolver.FindManualJournalEntryDraftAsync(
            service,
            journalEntryId,
            context.Subject.LedgerBookId,
            context.CancellationToken).ConfigureAwait(false);
        if (draft is null)
        {
            return Empty($"Journal entry '{context.Subject.SubjectId}' was not found in the manual journal workbench.");
        }

        var generatedAt = DateTimeOffset.UtcNow;
        var rootId = NodeId(context.Subject, "journal-entry");
        var evidenceId = NodeId(context.Subject, "retained-evidence");
        var approvalId = NodeId(context.Subject, "approval-state");
        var attachments = draft.EvidenceAttachments ?? [];
        var evidenceCount = draft.EvidenceLinks.Count + attachments.Count;

        var nodes = new List<EvidenceNodeDto>
        {
            Node(
                context.Subject,
                rootId,
                "manual-journal-entry",
                MapEntryStatus(draft.Status),
                $"Manual journal entry {draft.JournalEntryId:D} is {draft.Status} for {draft.AccountingDate:yyyy-MM-dd} with {draft.TotalDebits} {draft.Currency} debits and {draft.TotalCredits} {draft.Currency} credits.",
                "ManualJournalEntryWorkbenchService",
                draft.UpdatedAtUtc,
                artifacts:
                [
                    Artifact(
                        $"{rootId}:detail-route",
                        "journal-entry-detail-route",
                        route: context.Subject.Route,
                        generatedAt: generatedAt)
                ])
        };
        var edges = new List<EvidenceEdgeDto>();
        var required = new List<string> { rootId };

        nodes.Add(Node(
            context.Subject,
            evidenceId,
            "retained-evidence",
            evidenceCount == 0 ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            evidenceCount == 0
                ? "No retained evidence link or attachment supports this journal entry."
                : $"{evidenceCount} retained evidence item(s) support this journal entry.",
            "ManualJournalEntryWorkbenchService",
            draft.UpdatedAtUtc,
            artifacts: BuildEvidenceArtifacts(draft, attachments, evidenceId, generatedAt)));
        edges.Add(new EvidenceEdgeDto(rootId, evidenceId, "supported-by", "Retained evidence supports the journal entry posting."));
        required.Add(evidenceId);

        nodes.Add(Node(
            context.Subject,
            approvalId,
            "approval-state",
            MapApprovalStatus(draft.Status),
            BuildApprovalSummary(draft),
            "ManualJournalEntryWorkbenchService",
            draft.ApprovedAtUtc ?? draft.UpdatedAtUtc));
        edges.Add(new EvidenceEdgeDto(rootId, approvalId, "approved-by", "Approval state gates posting and close reliance on this journal entry."));
        required.Add(approvalId);

        var warnings = draft.ValidationIssues
            .Select(static issue => $"{issue.Code}: {issue.Message}")
            .ToArray();

        return new EvidenceContribution(nodes, edges, [], required, warnings);
    }

    private static EvidenceContribution Empty(string warning)
        => new([], [], [], [], [warning]);

    private static EvidenceStatusDto MapEntryStatus(ManualJournalEntryStatusDto status)
        => status switch
        {
            ManualJournalEntryStatusDto.Posted or
            ManualJournalEntryStatusDto.CloseLocked or
            ManualJournalEntryStatusDto.Reversed or
            ManualJournalEntryStatusDto.Rebooked => EvidenceStatusDto.Ready,
            ManualJournalEntryStatusDto.Rejected or ManualJournalEntryStatusDto.NeedsFix => EvidenceStatusDto.Blocked,
            _ => EvidenceStatusDto.ReviewRequired
        };

    private static EvidenceStatusDto MapApprovalStatus(ManualJournalEntryStatusDto status)
        => status switch
        {
            ManualJournalEntryStatusDto.Approved or
            ManualJournalEntryStatusDto.Posted or
            ManualJournalEntryStatusDto.CloseLocked or
            ManualJournalEntryStatusDto.Reversed or
            ManualJournalEntryStatusDto.Rebooked => EvidenceStatusDto.Ready,
            ManualJournalEntryStatusDto.Rejected or ManualJournalEntryStatusDto.NeedsFix => EvidenceStatusDto.Blocked,
            _ => EvidenceStatusDto.ReviewRequired
        };

    private static string BuildApprovalSummary(ManualJournalEntryDraftDto draft)
    {
        if (draft.ApprovedBy is not null)
        {
            return $"Journal entry approval is {draft.Status}, approved by {draft.ApprovedBy}.";
        }

        return draft.ApprovalId is null
            ? $"Journal entry approval state is {draft.Status}."
            : $"Journal entry approval {draft.ApprovalId} is {draft.Status}.";
    }

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildEvidenceArtifacts(
        ManualJournalEntryDraftDto draft,
        IReadOnlyList<ManualJournalEntryEvidenceAttachmentDto> attachments,
        string evidenceId,
        DateTimeOffset generatedAt)
    {
        var artifacts = new List<EvidenceArtifactRefDto>();
        foreach (var attachment in attachments)
        {
            artifacts.Add(BuildEvidenceArtifact(
                $"{evidenceId}:attachment-{SanitizeNodePart(attachment.AttachmentId)}",
                attachment.Uri,
                generatedAt));
        }

        for (var index = 0; index < draft.EvidenceLinks.Count; index++)
        {
            artifacts.Add(BuildEvidenceArtifact(
                $"{evidenceId}:retained-{index + 1}",
                draft.EvidenceLinks[index],
                generatedAt));
        }

        return artifacts;
    }

    private static EvidenceArtifactRefDto BuildEvidenceArtifact(
        string artifactId,
        string link,
        DateTimeOffset generatedAt)
        => link.StartsWith("/", StringComparison.OrdinalIgnoreCase) ||
           link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
           link.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? Artifact(artifactId, "retained-evidence-link", route: link, generatedAt: generatedAt, retained: true)
            : Artifact(artifactId, "retained-evidence-link", path: link, generatedAt: generatedAt, retained: true);

    private static string SanitizeNodePart(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "artifact"
            : string.Join("-", value.Trim().Split(
                Path.GetInvalidFileNameChars().Concat([':', '/', '\\', '?', '&', '=']).Distinct().ToArray(),
                StringSplitOptions.RemoveEmptyEntries));
}
