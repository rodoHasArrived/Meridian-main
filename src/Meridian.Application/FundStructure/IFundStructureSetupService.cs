using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

public interface IFundStructureSetupService
{
    Task<FundStructureSetupPreviewDto> PreviewAsync(
        FundStructureSetupDraftRequest request,
        CancellationToken ct = default);

    Task<FundStructureSetupCommitResultDto> CommitAsync(
        FundStructureSetupDraftRequest request,
        CancellationToken ct = default);
}
