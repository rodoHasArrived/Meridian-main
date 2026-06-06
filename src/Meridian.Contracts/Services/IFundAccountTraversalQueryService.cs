using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.Services;

public interface IFundAccountTraversalQueryService
{
    Task<IReadOnlyList<AccountSummaryDto>> GetFundAccountsAsync(
        Guid fundId,
        AccountTypeDto? accountType,
        bool activeOnly,
        CancellationToken ct = default);
}
