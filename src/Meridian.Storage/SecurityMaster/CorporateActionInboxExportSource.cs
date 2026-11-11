using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>One repeatable-read export cut: global actionable observations plus exact scoped cases.</summary>
public sealed record CorporateActionInboxExportSource(
    IReadOnlyList<CorporateActionSourceProposalDto> Proposals,
    IReadOnlyList<CorporateActionProcessingCaseDto> Cases);
