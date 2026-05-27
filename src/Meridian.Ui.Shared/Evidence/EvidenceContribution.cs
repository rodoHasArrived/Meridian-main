using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

public sealed record EvidenceContributionContext(
    EvidenceSubjectDto Subject,
    CancellationToken CancellationToken);

public sealed record EvidenceContribution(
    IReadOnlyList<EvidenceNodeDto> Nodes,
    IReadOnlyList<EvidenceEdgeDto> Edges,
    IReadOnlyList<WorkflowActionDto> Actions,
    IReadOnlyList<string> RequiredEvidenceIds,
    IReadOnlyList<string> Warnings);

public interface IEvidenceContributor
{
    string ContributorId { get; }

    bool Supports(EvidenceSubjectDto subject);

    Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context);
}
