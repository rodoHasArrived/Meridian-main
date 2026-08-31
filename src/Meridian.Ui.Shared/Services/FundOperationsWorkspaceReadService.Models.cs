using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FundOperationsWorkspaceReadService
{
    private sealed record AccountWorkspaceProjection(
        FundAccountSummary Summary,
        AccountBalanceSnapshotDto? LatestSnapshot,
        Guid? FundId);
}
