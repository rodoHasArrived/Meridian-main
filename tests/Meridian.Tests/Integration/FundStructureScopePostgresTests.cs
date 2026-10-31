using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Tests.Storage.FundAccounts;
using Xunit;

namespace Meridian.Tests.Integration;

/// <summary>
/// An operator filters a persisted organization/accounting hierarchy after tenant partitioning,
/// organization deactivation, or a historical-date change. Real stores and newly constructed
/// services must not recover unrelated entities/accounts from an empty selected branch.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class FundStructureScopePostgresTests
{
    private static readonly DateTimeOffset AsOf = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero).AddTicks(123450);
    private static readonly DateTimeOffset EffectiveFrom = AsOf.AddDays(-1);

    [FundAccountDatabaseFact]
    public async Task OrganizationSelection_AfterDatabaseReload_EmptyIntersectionsDoNotRecoverDescendants()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await using var data = await ScopeData.CreateAsync(timeout.Token);
        var ct = timeout.Token;

        // Every read creates both services and both stores again: the controls prove that SQL
        // materialization, tenant stamps, account links and ledger assignments are populated.
        AssertScope(await data.QueryAsync(ct), [data.A.OrganizationId, data.B.OrganizationId], data.A, data.B);
        AssertScope(await data.QueryAsync(ct, organizationId: data.A.OrganizationId), [data.A.OrganizationId], data.A);
        AssertScope(await data.QueryAsync(ct, businessId: data.B.BusinessId),
            [data.A.OrganizationId, data.B.OrganizationId], data.B);

        foreach (var unknown in new[] { Guid.NewGuid(), Guid.Empty, data.Foreign.OrganizationId })
        {
            AssertScope(await data.QueryAsync(ct, organizationId: unknown), []);
            AssertScope(await data.QueryAsync(ct, organizationId: unknown, businessId: data.A.BusinessId), []);
        }

        foreach (var mismatch in new[] { data.B.BusinessId, data.Foreign.BusinessId, Guid.NewGuid(), Guid.Empty })
            AssertScope(await data.QueryAsync(ct, organizationId: data.A.OrganizationId, businessId: mismatch),
                [data.A.OrganizationId]);
    }

    [FundAccountDatabaseFact]
    public async Task InactiveOrganization_AfterDatabaseReload_ActiveOnlyControlsDescendants()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await using var data = await ScopeData.CreateAsync(timeout.Token);
        var ct = timeout.Token;
        AssertScope(await data.QueryAsync(ct, organizationId: data.A.OrganizationId), [data.A.OrganizationId], data.A);
        await AssertBusinessViewsAsync(data, data.A, activeOnly: true, AsOf, visible: true, ct);

        var organization = Assert.IsType<OrganizationSummaryDto>(await data.Store.GetOrganizationAsync(data.A.OrganizationId, ct));
        await data.Store.UpsertOrganizationAsync(organization with { IsActive = false }, ct);
        var reloaded = Assert.IsType<OrganizationSummaryDto>(await data.Store.GetOrganizationAsync(data.A.OrganizationId, ct));
        Assert.False(reloaded.IsActive);
        Assert.Null(reloaded.EffectiveTo);

        AssertScope(await data.QueryAsync(ct, organizationId: data.A.OrganizationId), []);
        await AssertBusinessViewsAsync(data, data.A, activeOnly: true, AsOf, visible: false, ct);
        var historical = await data.QueryAsync(ct, organizationId: data.A.OrganizationId, activeOnly: false);
        AssertScope(historical, [data.A.OrganizationId], data.A);
        Assert.False(Assert.Single(historical.Graph.Organizations).IsActive);
        await AssertBusinessViewsAsync(data, data.A, activeOnly: false, AsOf, visible: true, ct);
        AssertScope(await data.QueryAsync(ct, organizationId: data.B.OrganizationId), [data.B.OrganizationId], data.B);
        await AssertBusinessViewsAsync(data, data.B, activeOnly: true, AsOf, visible: true, ct);
    }

    [FundAccountDatabaseFact]
    public async Task OrganizationEffectiveWindow_AfterDatabaseReload_EndpointsStayInclusive()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await using var data = await ScopeData.CreateAsync(timeout.Token);
        var ct = timeout.Token;
        var end = AsOf.AddDays(1);
        var organization = Assert.IsType<OrganizationSummaryDto>(await data.Store.GetOrganizationAsync(data.A.OrganizationId, ct));
        await data.Store.UpsertOrganizationAsync(organization with { EffectiveFrom = AsOf, EffectiveTo = end }, ct);
        var reloaded = Assert.IsType<OrganizationSummaryDto>(await data.Store.GetOrganizationAsync(data.A.OrganizationId, ct));
        Assert.Equal(AsOf, reloaded.EffectiveFrom);
        Assert.Equal(end, reloaded.EffectiveTo);

        // PostgreSQL timestamps have microsecond precision. Use one stored precision unit
        // outside each endpoint, and keep every descendant and the other root effective.
        foreach (var activeOnly in new[] { true, false })
        {
            foreach (var (at, included) in new[]
                     { (AsOf.AddTicks(-10), false), (AsOf, true), (end, true), (end.AddTicks(10), false) })
            {
                AssertScope(await data.QueryAsync(ct, organizationId: data.B.OrganizationId, asOf: at, activeOnly: activeOnly),
                    [data.B.OrganizationId], data.B);
                var selected = await data.QueryAsync(ct, organizationId: data.A.OrganizationId, asOf: at, activeOnly: activeOnly);
                if (included)
                    AssertScope(selected, [data.A.OrganizationId], data.A);
                else
                    AssertScope(selected, []);
                await AssertBusinessViewsAsync(data, data.A, activeOnly, at, included, ct);
            }
        }
    }

    [FundAccountDatabaseFact]
    public async Task StrictTenant_AfterDatabaseReload_EmptyAuthorityIsDistinctFromMissingAuthority()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await using var data = await ScopeData.CreateAsync(timeout.Token);
        var ct = timeout.Token;
        AssertScope(await data.QueryAsync(ct, tenant: "tenant-b"), [data.Foreign.OrganizationId], data.Foreign);
        AssertScope(await data.QueryAsync(ct, tenant: " TENANT-A "),
            [data.A.OrganizationId, data.B.OrganizationId], data.A, data.B);

        Assert.Empty(await data.Accounts("tenant-empty").QueryAccountsAsync(new AccountStructureQuery(ActiveOnly: false), ct));
        AssertScope(await data.QueryAsync(ct, tenant: "tenant-empty"), []);
        AssertScope(await data.QueryAsync(ct, tenant: "tenant-empty", organizationId: data.A.OrganizationId), []);

        foreach (var unresolved in new string?[] { null, "", "  ", " all " })
        {
            var service = data.Service(unresolved);
            await Assert.ThrowsAsync<FundStructureTenantScopeException>(() =>
                service.GetOrganizationStructureAsync(new OrganizationStructureQuery(AsOf: AsOf), ct));
            await Assert.ThrowsAsync<FundStructureTenantScopeException>(() =>
                service.GetAccountingViewAsync(new AccountingStructureQuery(AsOf: AsOf), ct));
            await Assert.ThrowsAsync<TenantScopeRejectedException>(() =>
                data.Accounts(unresolved).QueryAccountsAsync(new AccountStructureQuery(), ct));
        }
    }

    [FundAccountDatabaseFact]
    public async Task AccountTenantStamp_VisibleForeignReferences_DoNotGrantAccountVisibility()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await using var data = await ScopeData.CreateAsync(timeout.Token);
        var ct = timeout.Token;
        var foreignAccountId = Guid.NewGuid();
        await data.Accounts("tenant-b").CreateAccountAsync(new CreateAccountRequest(
            foreignAccountId, AccountTypeDto.Custody, "FOREIGN-REFERENCES", "Foreign-owned account", "USD",
            EffectiveFrom, "scope-test", FundId: data.A.FundId, EntityId: data.A.AccountEntityId,
            LedgerReference: "FOREIGN-BOOK"), ct);

        // The account's own retained SQL tenant stamp must decide admission. Its fund and entity
        // both belong to tenant-a, so structural parent checks alone would otherwise admit it.
        var ownerRead = Assert.IsType<AccountSummaryDto>(await data.Accounts("tenant-b").GetAccountAsync(foreignAccountId, ct));
        Assert.Equal(data.A.FundId, ownerRead.FundId);
        Assert.Equal(data.A.AccountEntityId, ownerRead.EntityId);
        Assert.Null(await data.Accounts("tenant-a").GetAccountAsync(foreignAccountId, ct));
        AssertIds([data.A.DirectAccountId],
            (await data.Accounts("tenant-a").QueryAccountsAsync(new AccountStructureQuery(FundId: data.A.FundId), ct))
            .Select(account => account.AccountId));
        AssertScope(await data.QueryAsync(ct, organizationId: data.A.OrganizationId), [data.A.OrganizationId], data.A);
        AssertScope(await data.QueryAsync(ct), [data.A.OrganizationId, data.B.OrganizationId], data.A, data.B);
    }

    [FundAccountDatabaseFact]
    public async Task AccountingDescendantSelection_AfterDatabaseReload_RetainsOnlyApplicableAssignments()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await using var data = await ScopeData.CreateAsync(timeout.Token);
        var ct = timeout.Token;
        AssertScope(await data.QueryAsync(ct), [data.A.OrganizationId, data.B.OrganizationId], data.A, data.B);
        var fundAssignmentId = Guid.NewGuid();
        await data.Service("tenant-a").AssignNodeAsync(new AssignFundStructureNodeRequest(
            fundAssignmentId, data.B.FundId, "LedgerGroup", "FUND-BOOK-B", EffectiveFrom, "scope-test", IsPrimary: true), ct);

        Task<AccountingStructureViewDto> ReadAsync(AccountingStructureQuery query) =>
            data.Service("tenant-a").GetAccountingViewAsync(query with { AsOf = AsOf }, ct);

        var fund = await ReadAsync(new AccountingStructureQuery(FundId: data.B.FundId));
        Assert.Null(fund.Organization);
        Assert.Null(fund.Business);
        AssertIds([data.B.PortfolioId], fund.InvestmentPortfolios.Select(item => item.InvestmentPortfolioId));
        AssertIds([data.B.DirectAccountId, data.B.LinkedAccountId], fund.Accounts.Select(item => item.AccountId));
        Assert.NotNull(fund.Assignments);
        AssertIds([data.B.AssignmentId, fundAssignmentId], fund.Assignments.Select(item => item.AssignmentId));
        Assert.Equal("BOOK-B", fund.Accounts.Single(item => item.AccountId == data.B.DirectAccountId).LedgerReference);
        AssertIds([data.B.DirectAccountId], Assert.Single(fund.LedgerGroups, group => group.DisplayName == "FUND-BOOK-B").AccountIds);
        AssertIds([data.B.LinkedAccountId], Assert.Single(fund.LedgerGroups, group => group.DisplayName == "ASSIGNED-B").AccountIds);
        Assert.Equal(2, fund.LedgerGroups.Count);

        var portfolio = await ReadAsync(new AccountingStructureQuery(InvestmentPortfolioId: data.B.PortfolioId));
        Assert.Null(portfolio.Organization);
        Assert.Null(portfolio.Business);
        AssertIds([data.B.PortfolioId], portfolio.InvestmentPortfolios.Select(item => item.InvestmentPortfolioId));
        AssertIds([data.B.LinkedAccountId], portfolio.Accounts.Select(item => item.AccountId));
        Assert.NotNull(portfolio.Assignments);
        AssertIds([data.B.AssignmentId], portfolio.Assignments.Select(item => item.AssignmentId));
        Assert.Equal("ASSIGNED-B", Assert.Single(portfolio.LedgerGroups).DisplayName);

        foreach (var query in new[]
                 {
                     new AccountingStructureQuery(FundId: Guid.NewGuid()),
                     new AccountingStructureQuery(InvestmentPortfolioId: Guid.NewGuid()),
                     new AccountingStructureQuery(FundId: data.A.FundId, InvestmentPortfolioId: data.B.PortfolioId),
                     new AccountingStructureQuery(OrganizationId: data.A.OrganizationId, FundId: data.B.FundId)
                 })
        {
            var empty = await ReadAsync(query);
            Assert.Equal(query.OrganizationId, empty.Organization?.OrganizationId);
            Assert.Null(empty.Business);
            Assert.Empty(empty.InvestmentPortfolios);
            Assert.Empty(empty.Accounts);
            Assert.Empty(empty.LedgerGroups);
            Assert.NotNull(empty.Assignments);
            Assert.Empty(empty.Assignments);
        }
    }

    private static async Task AssertBusinessViewsAsync(ScopeData data, Branch branch, bool activeOnly,
        DateTimeOffset asOf, bool visible, CancellationToken ct)
    {
        var service = data.Service(branch.Tenant);
        var advisory = await service.GetAdvisoryViewAsync(new AdvisoryStructureQuery(
            branch.BusinessId, OrganizationId: branch.OrganizationId, ActiveOnly: activeOnly, AsOf: asOf), ct);
        var operating = await service.GetFundOperatingViewAsync(new FundOperatingStructureQuery(
            branch.BusinessId, OrganizationId: branch.OrganizationId, ActiveOnly: activeOnly, AsOf: asOf), ct);
        if (!visible)
        {
            Assert.Null(advisory);
            Assert.Null(operating);
            return;
        }

        Assert.NotNull(advisory);
        Assert.Equal(branch.OrganizationId, advisory.Organization.OrganizationId);
        Assert.Equal(branch.BusinessId, advisory.Business.BusinessId);
        Assert.Equal(branch.ClientId, Assert.Single(advisory.Clients).Client.ClientId);
        Assert.Empty(advisory.UnassignedInvestmentPortfolios);
        Assert.Empty(advisory.UnassignedAccounts);
        Assert.NotNull(operating);
        Assert.Equal(branch.OrganizationId, operating.Organization.OrganizationId);
        Assert.Equal(branch.BusinessId, operating.Business.BusinessId);
        var fund = Assert.Single(operating.Funds);
        Assert.Equal(branch.FundId, fund.Fund.FundId);
        Assert.Equal(branch.PortfolioId, Assert.Single(fund.InvestmentPortfolios).InvestmentPortfolioId);
        Assert.Equal(branch.SleeveId, Assert.Single(fund.Sleeves).Sleeve.SleeveId);
        var vehicle = Assert.Single(fund.Vehicles);
        Assert.Equal(branch.VehicleId, vehicle.Vehicle.VehicleId);
        Assert.Equal(branch.EntityId, Assert.IsType<LegalEntitySummaryDto>(vehicle.LegalEntity).EntityId);
        AssertIds(branch.AccountIds, fund.Accounts.Concat(vehicle.Accounts)
            .Concat(fund.Sleeves.SelectMany(sleeve => sleeve.Accounts)).Select(account => account.AccountId).Distinct());
        Assert.Empty(operating.UnassignedInvestmentPortfolios);
        Assert.Empty(operating.UnassignedAccounts);
    }

    private static void AssertScope(Views views, IReadOnlyList<Guid> organizationIds, params Branch[] branches)
    {
        var graph = views.Graph;
        var accounting = views.Accounting;
        AssertIds(organizationIds, graph.Organizations.Select(item => item.OrganizationId));
        AssertIds(branches.Select(item => item.BusinessId), graph.Businesses.Select(item => item.BusinessId));
        AssertIds(branches.Select(item => item.ClientId), graph.Clients.Select(item => item.ClientId));
        AssertIds(branches.Select(item => item.FundId), graph.Funds.Select(item => item.FundId));
        AssertIds(branches.Select(item => item.SleeveId), graph.Sleeves.Select(item => item.SleeveId));
        AssertIds(branches.Select(item => item.VehicleId), graph.Vehicles.Select(item => item.VehicleId));
        AssertIds(branches.SelectMany(item => new[] { item.EntityId, item.AccountEntityId }), graph.Entities.Select(item => item.EntityId));
        AssertIds(branches.Select(item => item.PortfolioId), graph.InvestmentPortfolios.Select(item => item.InvestmentPortfolioId));
        var accountIds = branches.SelectMany(item => item.AccountIds).ToList();
        AssertIds(accountIds, graph.Accounts.Select(item => item.AccountId));
        var nodeIds = organizationIds.Concat(branches.SelectMany(item => item.DescendantNodeIds)).ToHashSet();
        AssertIds(nodeIds, graph.Nodes.Select(item => item.NodeId));
        Assert.All(graph.OwnershipLinks, link =>
        {
            Assert.Contains(link.ParentNodeId, nodeIds);
            Assert.Contains(link.ChildNodeId, nodeIds);
        });
        AssertIds(branches.Select(item => item.AssignmentId), graph.Assignments.Select(item => item.AssignmentId));
        if (branches.Length == 0)
            Assert.Empty(graph.OwnershipLinks);
        else
            foreach (var branch in branches)
                Assert.Contains(graph.OwnershipLinks, link => link.OwnershipLinkId == branch.AccountLinkId);

        if (organizationIds.Count == 0)
            Assert.Null(accounting.Organization);
        else
        {
            Assert.NotNull(accounting.Organization);
            Assert.Contains(accounting.Organization.OrganizationId, organizationIds);
        }
        if (branches.Length == 0)
            Assert.Null(accounting.Business);
        else
        {
            Assert.NotNull(accounting.Business);
            Assert.Contains(accounting.Business.BusinessId, branches.Select(item => item.BusinessId));
        }
        AssertIds(branches.Select(item => item.PortfolioId), accounting.InvestmentPortfolios.Select(item => item.InvestmentPortfolioId));
        AssertIds(accountIds, accounting.Accounts.Select(item => item.AccountId));
        AssertIds(accountIds, accounting.LedgerGroups.SelectMany(item => item.AccountIds));
        Assert.Equal(branches.SelectMany(item => new[] { $"BOOK-{item.Tag}", $"ASSIGNED-{item.Tag}" }).Order(),
            accounting.LedgerGroups.Select(item => item.DisplayName).Order());
        Assert.NotNull(accounting.Assignments);
        AssertIds(branches.Select(item => item.AssignmentId), accounting.Assignments.Select(item => item.AssignmentId));
    }

    private static void AssertIds(IEnumerable<Guid> expected, IEnumerable<Guid> actual) =>
        Assert.Equal(expected.Order(), actual.Order());

    private sealed record Views(OrganizationStructureGraphDto Graph, AccountingStructureViewDto Accounting);
}
