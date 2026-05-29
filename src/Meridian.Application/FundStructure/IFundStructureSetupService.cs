using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

public interface IFundStructureSetupService
{
    Task<FundStructureSetupPreviewDto> PreviewSetupAsync(
        FundStructureSetupDraftRequest request,
        CancellationToken ct = default);

    Task<FundStructureSetupCommitResultDto> CommitSetupAsync(
        FundStructureSetupDraftRequest request,
        CancellationToken ct = default);
}
