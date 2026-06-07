using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Identity;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Meridian.Tests.Identity.FundStructure;

public sealed class FundAccountTraversalQueryServiceTests
{
    [Fact]
    public async Task MultipleFundsWithSameInstitution_OnlyReturnsAccountsOwnedByRequestedFund()
    {
        var fundA = Guid.NewGuid();
        var fundB = Guid.NewGuid();
        var accountA = CreateAccount(Guid.NewGuid(), fundA, AccountTypeDto.Bank, true, "SharedBank");
        var accountB = CreateAccount(Guid.NewGuid(), fundB, AccountTypeDto.Bank, true, "SharedBank");
        var service = CreateSut(BuildGraph(new[] { fundA, fundB }, new[] { accountA, accountB }));

        var result = await service.GetFundAccountsAsync(fundA, AccountTypeDto.Bank, activeOnly: true);

        result.Should().ContainSingle().Which.AccountId.Should().Be(accountA.AccountId);
    }

    [Fact]
    public async Task SharedEntitySeparateAccounts_ReturnsEachOwnedAccount()
    {
        var fundId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var account1 = CreateAccount(Guid.NewGuid(), fundId, AccountTypeDto.Custody, true, "Prime", entityId);
        var account2 = CreateAccount(Guid.NewGuid(), fundId, AccountTypeDto.Brokerage, true, "Prime", entityId);
        var service = CreateSut(BuildGraph(new[] { fundId }, new[] { account1, account2 }));

        var result = await service.GetFundAccountsAsync(fundId, null, activeOnly: true);

        result.Select(a => a.AccountId).Should().BeEquivalentTo(new[] { account1.AccountId, account2.AccountId });
    }

    [Fact]
    public async Task NonFundNodeWithOwnsLink_ReturnsNoAccounts()
    {
        var fundId = Guid.NewGuid();
        var nonFundNodeId = Guid.NewGuid();
        var account = CreateAccount(Guid.NewGuid(), fundId, AccountTypeDto.Bank, true, "Sensitive");

        var graph = BuildGraph(new[] { fundId }, new[] { account }) with
        {
            Clients = new[]
            {
                new ClientSummaryDto(
                    nonFundNodeId,
                    Guid.Empty,
                    "CLIENT-1",
                    "Client",
                    "USD",
                    true,
                    DateTimeOffset.UtcNow,
                    null,
                    Array.Empty<Guid>())
            },
            OwnershipLinks = new[]
            {
                new OwnershipLinkDto(Guid.NewGuid(), nonFundNodeId, account.AccountId, OwnershipRelationshipTypeDto.Owns, null, true, DateTimeOffset.UtcNow, null, null)
            }
        };

        var service = CreateSut(graph);

        var result = await service.GetFundAccountsAsync(nonFundNodeId, AccountTypeDto.Bank, activeOnly: true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task MixedStates_ActiveOnlyFiltersSuspendedAndClosed()
    {
        var fundId = Guid.NewGuid();
        var active = CreateAccount(Guid.NewGuid(), fundId, AccountTypeDto.Bank, true, "A");
        var suspended = CreateAccount(Guid.NewGuid(), fundId, AccountTypeDto.Bank, false, "A");
        var service = CreateSut(BuildGraph(new[] { fundId }, new[] { active, suspended }));

        var activeOnly = await service.GetFundAccountsAsync(fundId, AccountTypeDto.Bank, activeOnly: true);
        var allStatuses = await service.GetFundAccountsAsync(fundId, AccountTypeDto.Bank, activeOnly: false);

        activeOnly.Should().ContainSingle().Which.AccountId.Should().Be(active.AccountId);
        allStatuses.Should().HaveCount(2);
    }

    private static IFundAccountTraversalQueryService CreateSut(OrganizationStructureGraphDto graph)
    {
        var fundStructure = Substitute.For<IFundStructureService>();
        fundStructure.GetOrganizationStructureAsync(Arg.Any<OrganizationStructureQuery>(), Arg.Any<CancellationToken>())
            .Returns(graph);

        return new FundAccountTraversalQueryService(fundStructure, new MemoryCache(new MemoryCacheOptions()));
    }

    private static OrganizationStructureGraphDto BuildGraph(IEnumerable<Guid> fundIds, IReadOnlyList<AccountSummaryDto> accounts)
    {
        var funds = fundIds.Select(id => new FundSummaryDto(
            id,
            Guid.Empty,
            $"F-{id:N}",
            "Fund",
            "USD",
            true,
            DateTimeOffset.UtcNow,
            null,
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            accounts.Where(a => a.FundId == id).Select(a => a.AccountId).ToArray())).ToArray();
        var ownership = accounts.Select(a => new OwnershipLinkDto(Guid.NewGuid(), a.FundId!.Value, a.AccountId, OwnershipRelationshipTypeDto.Owns, null, true, DateTimeOffset.UtcNow, null, null)).ToArray();

        return new OrganizationStructureGraphDto(
            Array.Empty<OrganizationSummaryDto>(),
            Array.Empty<BusinessSummaryDto>(),
            Array.Empty<ClientSummaryDto>(),
            funds,
            Array.Empty<SleeveSummaryDto>(),
            Array.Empty<VehicleSummaryDto>(),
            Array.Empty<LegalEntitySummaryDto>(),
            Array.Empty<InvestmentPortfolioSummaryDto>(),
            accounts,
            Array.Empty<FundStructureNodeDto>(),
            ownership,
            Array.Empty<FundStructureAssignmentDto>());
    }

    private static AccountSummaryDto CreateAccount(Guid accountId, Guid fundId, AccountTypeDto type, bool isActive, string institution, Guid? entityId = null)
        => new(accountId, type, entityId, fundId, null, null, $"ACC-{accountId:N}", "Account", "USD", institution, isActive, DateTimeOffset.UtcNow, null, null, null, null, null);
}
