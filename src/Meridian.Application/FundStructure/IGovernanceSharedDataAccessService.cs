using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Builds shared Security Master, historical price, and backfill accessibility
/// summaries for governance structure views without coupling those views to
/// position-level holdings data.
/// </summary>
public interface IGovernanceSharedDataAccessService
{
    Task<FundStructureSharedDataAccessDto> GetSharedDataAccessAsync(
        CancellationToken ct = default);
}
