using Microsoft.Extensions.Caching.Memory;
using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Authoritative traversal for fund account discovery:
/// Fund -> OwnershipRelationship (Owns) -> AccountDefinition.
/// Filters are applied on <see cref="AccountSummaryDto.AccountType"/> and active status.
/// </summary>
public sealed class FundAccountTraversalQueryService : IFundAccountTraversalQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IFundStructureService _fundStructureService;
    private readonly IMemoryCache _cache;

    public FundAccountTraversalQueryService(IFundStructureService fundStructureService, IMemoryCache cache)
    {
        _fundStructureService = fundStructureService;
        _cache = cache;
    }

    public async Task<IReadOnlyList<AccountSummaryDto>> GetFundAccountsAsync(Guid fundId, AccountTypeDto? accountType, bool activeOnly, CancellationToken ct = default)
    {
        var cacheKey = $"fund-account-traversal:{fundId:N}";
        var snapshot = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await BuildSnapshotAsync(fundId, ct).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? Array.Empty<AccountSummaryDto>();

        return snapshot
            .Where(account => !activeOnly || account.IsActive)
            .Where(account => accountType is null || account.AccountType == accountType.Value)
            .ToArray();
    }

    private async Task<IReadOnlyList<AccountSummaryDto>> BuildSnapshotAsync(Guid fundId, CancellationToken ct)
    {
        var graph = await _fundStructureService.GetOrganizationStructureAsync(new OrganizationStructureQuery(), ct).ConfigureAwait(false);
        var accountNodeIds = graph.OwnershipLinks
            .Where(link => link.ParentNodeId == fundId)
            .Where(link => link.RelationshipType == OwnershipRelationshipTypeDto.Owns)
            .Select(link => link.ChildNodeId)
            .ToHashSet();

        if (accountNodeIds.Count == 0)
        {
            return Array.Empty<AccountSummaryDto>();
        }

        return graph.Accounts
            .Where(account => accountNodeIds.Contains(account.AccountId))
            .ToArray();
    }
}
