using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.Services;

/// <summary>
/// Traverses fund structure relationships to retrieve the applicable accounts.
/// </summary>
public interface IFundAccountTraversalQueryService
{
    Task<IReadOnlyList<AccountSummaryDto>> GetFundAccountsAsync(
        Guid fundId,
        AccountTypeDto? accountType,
        bool activeOnly,
        CancellationToken ct = default);
}
