using Meridian.Contracts.FundStructure;
using Meridian.Entities.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Pure read-model projections shared by local and PostgreSQL fund-structure services.
/// Callers capture structure state and resolve authorized accounts before invoking a projection;
/// this component does not read stores, resolve tenants, mutate state, or consult the clock.
/// </summary>
internal static class FundStructureProjector
{
    internal static OrganizationStructureGraphDto ProjectOrganizationStructure(
        StructureSnapshot snapshot,
        IReadOnlyList<AccountSummaryDto> visibleAccounts,
        OrganizationStructureQuery query,
        DateTimeOffset asOf,
        FundStructureSharedDataAccessDto? sharedDataAccess)
    {
        var filtered = FilterForOrganizationScope(
            snapshot,
            visibleAccounts,
            query.OrganizationId,
            query.BusinessId,
            query.ActiveOnly,
            asOf);
        var enrichedPortfolios = AttachSharedDataAccess(filtered.InvestmentPortfolios, sharedDataAccess);

        var nodes = BuildNodes(filtered)
            .Where(node => query.NodeId is null || node.NodeId == query.NodeId.Value)
            .Where(node => query.NodeKind is null || node.Kind == query.NodeKind.Value)
            .ToList();

        var nodeIds = nodes.Select(static node => node.NodeId).ToHashSet();
        var links = filtered.OwnershipLinks
            .Where(link => nodeIds.Contains(link.ParentNodeId) && nodeIds.Contains(link.ChildNodeId))
            .ToList();
        var assignments = filtered.Assignments
            .Where(assignment => nodeIds.Contains(assignment.NodeId))
            .ToList();

        return new OrganizationStructureGraphDto(
            filtered.Organizations,
            filtered.Businesses,
            filtered.Clients,
            filtered.Funds,
            filtered.Sleeves,
            filtered.Vehicles,
            filtered.Entities,
            enrichedPortfolios,
            filtered.Accounts,
            nodes,
            links,
            assignments,
            sharedDataAccess);
    }

    internal static FundStructureGraphDto ProjectFundStructureGraph(
        StructureSnapshot snapshot,
        IReadOnlyList<AccountSummaryDto> visibleAccounts,
        FundStructureQuery query,
        DateTimeOffset asOf)
    {
        var activeLinks = FilterVisible(snapshot.OwnershipLinks, query.ActiveOnly, asOf, static link => (link.EffectiveFrom, link.EffectiveTo));
        var activeAssignments = FilterVisible(snapshot.Assignments, query.ActiveOnly, asOf, static assignment => (assignment.EffectiveFrom, assignment.EffectiveTo));
        var funds = FilterVisible(snapshot.Funds, query.ActiveOnly, asOf, static fund => (fund.EffectiveFrom, fund.EffectiveTo));

        if (query.FundId.HasValue)
        {
            funds = funds.Where(fund => fund.FundId == query.FundId.Value).ToList();
        }

        var fundIds = funds.Select(static fund => fund.FundId).ToHashSet();
        var sleeves = FilterVisible(snapshot.Sleeves, query.ActiveOnly, asOf, static sleeve => (sleeve.EffectiveFrom, sleeve.EffectiveTo))
            .Where(sleeve => fundIds.Contains(sleeve.FundId))
            .ToList();
        var sleeveIds = sleeves.Select(static sleeve => sleeve.SleeveId).ToHashSet();
        var vehicles = FilterVisible(snapshot.Vehicles, query.ActiveOnly, asOf, static vehicle => (vehicle.EffectiveFrom, vehicle.EffectiveTo))
            .Where(vehicle => fundIds.Contains(vehicle.FundId))
            .ToList();
        var vehicleIds = vehicles.Select(static vehicle => vehicle.VehicleId).ToHashSet();
        var portfolioIds = FilterVisible(snapshot.InvestmentPortfolios, query.ActiveOnly, asOf, static portfolio => (portfolio.EffectiveFrom, portfolio.EffectiveTo))
            .Where(portfolio =>
                (portfolio.FundId.HasValue && fundIds.Contains(portfolio.FundId.Value))
                || (portfolio.SleeveId.HasValue && sleeveIds.Contains(portfolio.SleeveId.Value))
                || (portfolio.VehicleId.HasValue && vehicleIds.Contains(portfolio.VehicleId.Value)))
            .Select(static portfolio => portfolio.InvestmentPortfolioId)
            .ToHashSet();
        var entityIds = vehicles.Select(static vehicle => vehicle.LegalEntityId)
            .Concat(funds.SelectMany(static fund => fund.EntityIds))
            .ToHashSet();

        var accounts = visibleAccounts
            .Where(account =>
                (account.FundId.HasValue && fundIds.Contains(account.FundId.Value))
                || (account.SleeveId.HasValue && sleeveIds.Contains(account.SleeveId.Value))
                || (account.VehicleId.HasValue && vehicleIds.Contains(account.VehicleId.Value))
                || (account.EntityId.HasValue && entityIds.Contains(account.EntityId.Value))
                || IsAccountLinkedToAny(account.AccountId, fundIds, activeLinks)
                || IsAccountLinkedToAny(account.AccountId, sleeveIds, activeLinks)
                || IsAccountLinkedToAny(account.AccountId, vehicleIds, activeLinks)
                || IsAccountLinkedToAny(account.AccountId, portfolioIds, activeLinks))
            .ToList();
        var entities = FilterVisible(snapshot.Entities, query.ActiveOnly, asOf, static entity => (entity.EffectiveFrom, entity.EffectiveTo))
            .Where(entity => entityIds.Contains(entity.EntityId) || IsEntityLinkedToScope(entity.EntityId, fundIds, activeLinks))
            .ToList();

        var nodes = BuildNodes(new StructureScope(
                Organizations: [],
                Businesses: [],
                Clients: [],
                Funds: funds,
                Sleeves: sleeves,
                Vehicles: vehicles,
                Entities: entities,
                InvestmentPortfolios: [],
                Accounts: accounts,
                OwnershipLinks: activeLinks,
                Assignments: activeAssignments))
            .Where(node => node.Kind is FundStructureNodeKindDto.Fund
                or FundStructureNodeKindDto.Sleeve
                or FundStructureNodeKindDto.Vehicle
                or FundStructureNodeKindDto.Entity
                or FundStructureNodeKindDto.Account)
            .Where(node => query.NodeId is null || node.NodeId == query.NodeId.Value)
            .Where(node => query.NodeKind is null || node.Kind == query.NodeKind.Value)
            .ToList();

        var nodeIds = nodes.Select(static node => node.NodeId).ToHashSet();
        var links = activeLinks
            .Where(link => nodeIds.Contains(link.ParentNodeId) && nodeIds.Contains(link.ChildNodeId))
            .ToList();
        var assignments = activeAssignments
            .Where(assignment => nodeIds.Contains(assignment.NodeId))
            .ToList();

        return new FundStructureGraphDto(nodes, links, assignments);
    }

    internal static AdvisoryStructureViewDto? ProjectAdvisoryView(
        StructureSnapshot snapshot,
        IReadOnlyList<AccountSummaryDto> accounts,
        AdvisoryStructureQuery query,
        DateTimeOffset asOf,
        FundStructureSharedDataAccessDto? sharedDataAccess)
    {
        var activeLinks = FilterVisible(snapshot.OwnershipLinks, query.ActiveOnly, asOf, static link => (link.EffectiveFrom, link.EffectiveTo));

        var business = FilterVisible(snapshot.Businesses, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .FirstOrDefault(candidate => candidate.BusinessId == query.BusinessId);
        if (business is null)
        {
            return null;
        }

        if (query.OrganizationId.HasValue && business.OrganizationId != query.OrganizationId.Value)
        {
            return null;
        }

        var organization = FilterVisible(snapshot.Organizations, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => !query.ActiveOnly || candidate.IsActive)
            .FirstOrDefault(candidate => candidate.OrganizationId == business.OrganizationId);
        if (organization is null)
        {
            return null;
        }

        var clients = FilterVisible(snapshot.Clients, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => candidate.BusinessId == business.BusinessId)
            .Where(candidate => query.ClientId is null || candidate.ClientId == query.ClientId.Value)
            .ToList();
        var portfolios = FilterVisible(snapshot.InvestmentPortfolios, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => candidate.BusinessId == business.BusinessId)
            .Where(IsAdvisoryPortfolio)
            .Where(candidate => query.InvestmentPortfolioId is null || candidate.InvestmentPortfolioId == query.InvestmentPortfolioId.Value)
            .ToList();
        var enrichedPortfolios = AttachSharedDataAccess(portfolios, sharedDataAccess);

        var clientViews = clients
            .Select(client =>
            {
                var clientPortfolios = enrichedPortfolios
                    .Where(portfolio => portfolio.ClientId == client.ClientId)
                    .ToList();
                var portfolioIds = clientPortfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
                var clientAccounts = GetScopeAccounts(
                        accounts,
                        activeLinks,
                        entityIds: Array.Empty<Guid>(),
                        fundIds: Array.Empty<Guid>(),
                        sleeveIds: Array.Empty<Guid>(),
                        vehicleIds: Array.Empty<Guid>(),
                        portfolioIds)
                    .ToList();

                return new AdvisoryClientViewDto(client, clientPortfolios, clientAccounts);
            })
            .ToList();

        var unassignedPortfolios = enrichedPortfolios
            .Where(portfolio => portfolio.ClientId is null)
            .ToList();
        var assignedAccountIds = clientViews
            .SelectMany(static view => view.Accounts)
            .Select(static account => account.AccountId)
            .ToHashSet();
        var unassignedAccounts = GetScopeAccounts(
                accounts,
                activeLinks,
                entityIds: Array.Empty<Guid>(),
                fundIds: Array.Empty<Guid>(),
                sleeveIds: Array.Empty<Guid>(),
                vehicleIds: Array.Empty<Guid>(),
                unassignedPortfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet())
            .Where(account => !assignedAccountIds.Contains(account.AccountId))
            .ToList();

        return new AdvisoryStructureViewDto(
            organization,
            business,
            clientViews,
            unassignedPortfolios,
            unassignedAccounts,
            sharedDataAccess);
    }

    internal static FundOperatingViewDto? ProjectFundOperatingView(
        StructureSnapshot snapshot,
        IReadOnlyList<AccountSummaryDto> accounts,
        FundOperatingStructureQuery query,
        DateTimeOffset asOf,
        FundStructureSharedDataAccessDto? sharedDataAccess)
    {
        var activeLinks = FilterVisible(snapshot.OwnershipLinks, query.ActiveOnly, asOf, static link => (link.EffectiveFrom, link.EffectiveTo));

        var business = FilterVisible(snapshot.Businesses, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .FirstOrDefault(candidate => candidate.BusinessId == query.BusinessId);
        if (business is null)
        {
            return null;
        }

        if (query.OrganizationId.HasValue && business.OrganizationId != query.OrganizationId.Value)
        {
            return null;
        }

        var organization = FilterVisible(snapshot.Organizations, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => !query.ActiveOnly || candidate.IsActive)
            .FirstOrDefault(candidate => candidate.OrganizationId == business.OrganizationId);
        if (organization is null)
        {
            return null;
        }

        var funds = FilterVisible(snapshot.Funds, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => candidate.BusinessId == business.BusinessId)
            .Where(candidate => query.FundId is null || candidate.FundId == query.FundId.Value)
            .ToList();
        var fundIds = funds.Select(static fund => fund.FundId).ToHashSet();
        var allSleeves = FilterVisible(snapshot.Sleeves, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => fundIds.Contains(candidate.FundId))
            .Where(candidate => query.SleeveId is null || candidate.SleeveId == query.SleeveId.Value)
            .ToList();
        var allVehicles = FilterVisible(snapshot.Vehicles, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => fundIds.Contains(candidate.FundId))
            .Where(candidate => query.VehicleId is null || candidate.VehicleId == query.VehicleId.Value)
            .ToList();
        var allPortfolios = FilterVisible(snapshot.InvestmentPortfolios, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => candidate.BusinessId == business.BusinessId)
            .Where(IsFundPortfolio)
            .Where(candidate => query.InvestmentPortfolioId is null || candidate.InvestmentPortfolioId == query.InvestmentPortfolioId.Value)
            .ToList();
        var enrichedPortfolios = AttachSharedDataAccess(allPortfolios, sharedDataAccess);
        var entities = FilterVisible(snapshot.Entities, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .ToDictionary(static entity => entity.EntityId);

        var fundSlices = new List<FundOperatingSliceDto>();
        foreach (var fund in funds)
        {
            var sleeves = allSleeves.Where(sleeve => sleeve.FundId == fund.FundId).ToList();
            var vehicles = allVehicles.Where(vehicle => vehicle.FundId == fund.FundId).ToList();
            var directPortfolios = enrichedPortfolios
                .Where(portfolio => portfolio.FundId == fund.FundId && portfolio.SleeveId is null && portfolio.VehicleId is null)
                .ToList();
            var directAccounts = GetScopeAccounts(
                    accounts,
                    activeLinks,
                    fund.EntityIds.ToHashSet(),
                    fundIds: new[] { fund.FundId },
                    sleeveIds: Array.Empty<Guid>(),
                    vehicleIds: Array.Empty<Guid>(),
                    directPortfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet())
                .Where(account => account.SleeveId is null && account.VehicleId is null)
                .ToList();

            var sleeveSlices = sleeves
                .Select(sleeve =>
                {
                    var portfolios = enrichedPortfolios.Where(portfolio => portfolio.SleeveId == sleeve.SleeveId).ToList();
                    var sleeveAccounts = GetScopeAccounts(
                        accounts,
                        activeLinks,
                        entityIds: Array.Empty<Guid>(),
                        fundIds: Array.Empty<Guid>(),
                        sleeveIds: new[] { sleeve.SleeveId },
                        vehicleIds: Array.Empty<Guid>(),
                        portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet()).ToList();

                    return new FundSleeveOperatingViewDto(sleeve, portfolios, sleeveAccounts);
                })
                .ToList();

            var vehicleSlices = vehicles
                .Select(vehicle =>
                {
                    var portfolios = enrichedPortfolios.Where(portfolio => portfolio.VehicleId == vehicle.VehicleId).ToList();
                    var vehicleAccounts = GetScopeAccounts(
                        accounts,
                        activeLinks,
                        entityIds: new[] { vehicle.LegalEntityId },
                        fundIds: Array.Empty<Guid>(),
                        sleeveIds: Array.Empty<Guid>(),
                        vehicleIds: new[] { vehicle.VehicleId },
                        portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet()).ToList();

                    entities.TryGetValue(vehicle.LegalEntityId, out var entity);
                    return new VehicleOperatingViewDto(vehicle, entity, portfolios, vehicleAccounts);
                })
                .ToList();

            fundSlices.Add(new FundOperatingSliceDto(
                fund,
                directPortfolios,
                directAccounts,
                sleeveSlices,
                vehicleSlices));
        }

        var assignedPortfolioIds = fundSlices
            .SelectMany(static slice => slice.InvestmentPortfolios)
            .Concat(fundSlices.SelectMany(static slice => slice.Sleeves).SelectMany(static sleeve => sleeve.InvestmentPortfolios))
            .Concat(fundSlices.SelectMany(static slice => slice.Vehicles).SelectMany(static vehicle => vehicle.InvestmentPortfolios))
            .Select(static portfolio => portfolio.InvestmentPortfolioId)
            .ToHashSet();
        var unassignedPortfolios = enrichedPortfolios
            .Where(portfolio => !assignedPortfolioIds.Contains(portfolio.InvestmentPortfolioId))
            .ToList();
        var assignedAccountIds = fundSlices
            .SelectMany(static slice => slice.Accounts)
            .Concat(fundSlices.SelectMany(static slice => slice.Sleeves).SelectMany(static sleeve => sleeve.Accounts))
            .Concat(fundSlices.SelectMany(static slice => slice.Vehicles).SelectMany(static vehicle => vehicle.Accounts))
            .Select(static account => account.AccountId)
            .ToHashSet();
        var unassignedAccounts = GetScopeAccounts(
                accounts,
                activeLinks,
                entityIds: Array.Empty<Guid>(),
                fundIds: Array.Empty<Guid>(),
                sleeveIds: Array.Empty<Guid>(),
                vehicleIds: Array.Empty<Guid>(),
                unassignedPortfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet())
            .Where(account => !assignedAccountIds.Contains(account.AccountId))
            .ToList();

        return new FundOperatingViewDto(
            organization,
            business,
            fundSlices,
            unassignedPortfolios,
            unassignedAccounts,
            sharedDataAccess);
    }

    internal static AccountingStructureViewDto ProjectAccountingView(
        StructureSnapshot snapshot,
        IReadOnlyList<AccountSummaryDto> visibleAccounts,
        AccountingStructureQuery query,
        DateTimeOffset asOf,
        FundStructureSharedDataAccessDto? sharedDataAccess)
    {
        var scoped = FilterForOrganizationScope(
            snapshot,
            visibleAccounts,
            query.OrganizationId,
            query.BusinessId,
            query.ActiveOnly,
            asOf);

        var portfolios = scoped.InvestmentPortfolios
            .Where(portfolio => query.ClientId is null || portfolio.ClientId == query.ClientId.Value)
            .Where(portfolio => query.FundId is null || portfolio.FundId == query.FundId.Value)
            .Where(portfolio => query.SleeveId is null || portfolio.SleeveId == query.SleeveId.Value)
            .Where(portfolio => query.VehicleId is null || portfolio.VehicleId == query.VehicleId.Value)
            .Where(portfolio => query.InvestmentPortfolioId is null || portfolio.InvestmentPortfolioId == query.InvestmentPortfolioId.Value)
            .ToList();
        portfolios = AttachSharedDataAccess(portfolios, sharedDataAccess).ToList();
        var accountScope = BuildAccountingAccountScope(scoped, portfolios);
        var accounts = scoped.Accounts
            .Where(account => query.LedgerReference is null || string.Equals(account.LedgerReference, query.LedgerReference, StringComparison.OrdinalIgnoreCase))
            .Where(account => IsAccountInAccountingScope(account, query, accountScope, scoped.OwnershipLinks))
            .ToList();

        var hasNarrowedAccountSelection = HasAccountingAccountScopeFilter(query) || query.LedgerReference is not null;
        var organization = query.OrganizationId.HasValue
            ? scoped.Organizations.FirstOrDefault(candidate => candidate.OrganizationId == query.OrganizationId.Value)
            : !hasNarrowedAccountSelection ? scoped.Organizations.FirstOrDefault() : null;
        var business = query.BusinessId.HasValue
            ? scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == query.BusinessId.Value)
            : !hasNarrowedAccountSelection ? scoped.Businesses.FirstOrDefault() : null;
        var assignments = hasNarrowedAccountSelection
            ? FilterAccountingAssignments(scoped, accounts, portfolios, query.LedgerReference is not null,
                organization?.OrganizationId, business?.BusinessId)
            : scoped.Assignments;
        var portfolioById = portfolios.ToDictionary(static portfolio => portfolio.InvestmentPortfolioId);
        var ledgerAssignments = LedgerGroupingRules.BuildLedgerAssignments(assignments);

        var ledgerGroups = accounts
            .GroupBy(account => LedgerGroupingRules.ResolveLedgerGroupId(account, ledgerAssignments))
            .Select(group =>
            {
                var accountIds = group.Select(static account => account.AccountId).ToList();
                var relatedPortfolioIds = group
                    .Select(account => TryParseGuid(account.PortfolioId, out var portfolioId) ? portfolioId : Guid.Empty)
                    .Where(static portfolioId => portfolioId != Guid.Empty)
                    .Concat(scoped.OwnershipLinks
                        .Where(link => accountIds.Contains(link.ChildNodeId) && portfolioById.ContainsKey(link.ParentNodeId))
                        .Select(static link => link.ParentNodeId))
                    .Distinct()
                    .ToList();
                var relatedPortfolios = relatedPortfolioIds
                    .Where(portfolioById.ContainsKey)
                    .Select(portfolioId => portfolioById[portfolioId])
                    .ToList();

                return new LedgerGroupSummaryDto(
                    group.Key,
                    DisplayName: group.Key.Value,
                    accountIds,
                    relatedPortfolioIds,
                    relatedPortfolios.Where(static portfolio => portfolio.ClientId.HasValue).Select(static portfolio => portfolio.ClientId!.Value).Distinct().ToList(),
                    group.Where(static account => account.FundId.HasValue).Select(static account => account.FundId!.Value)
                        .Concat(relatedPortfolios.Where(static portfolio => portfolio.FundId.HasValue).Select(static portfolio => portfolio.FundId!.Value))
                        .Distinct()
                        .ToList(),
                    group.Where(static account => account.SleeveId.HasValue).Select(static account => account.SleeveId!.Value)
                        .Concat(relatedPortfolios.Where(static portfolio => portfolio.SleeveId.HasValue).Select(static portfolio => portfolio.SleeveId!.Value))
                        .Distinct()
                        .ToList(),
                    group.Where(static account => account.VehicleId.HasValue).Select(static account => account.VehicleId!.Value)
                        .Concat(relatedPortfolios.Where(static portfolio => portfolio.VehicleId.HasValue).Select(static portfolio => portfolio.VehicleId!.Value))
                        .Distinct()
                        .ToList(),
                    SharedDataAccess: sharedDataAccess);
            })
            .OrderBy(static group => group.DisplayName)
            .ToList();

        return new AccountingStructureViewDto(
            organization,
            business,
            portfolios,
            accounts,
            ledgerGroups,
            sharedDataAccess,
            assignments);
    }

    internal static IReadOnlyList<AccountSummaryDto> AttachSharedDataAccess(
        IReadOnlyList<AccountSummaryDto> accounts,
        FundStructureSharedDataAccessDto? sharedDataAccess)
    {
        if (sharedDataAccess is null || accounts.Count == 0)
        {
            return accounts;
        }

        return accounts
            .Select(account => account with { SharedDataAccess = sharedDataAccess })
            .ToList();
    }

    internal static IReadOnlyList<InvestmentPortfolioSummaryDto> AttachSharedDataAccess(
        IReadOnlyList<InvestmentPortfolioSummaryDto> portfolios,
        FundStructureSharedDataAccessDto? sharedDataAccess)
    {
        if (sharedDataAccess is null || portfolios.Count == 0)
        {
            return portfolios;
        }

        return portfolios
            .Select(portfolio => portfolio with { SharedDataAccess = sharedDataAccess })
            .ToList();
    }

    internal static StructureScope FilterForOrganizationScope(
        StructureSnapshot snapshot,
        IReadOnlyList<AccountSummaryDto> accounts,
        Guid? organizationId,
        Guid? businessId,
        bool activeOnly,
        DateTimeOffset asOf)
    {
        var organizations = FilterVisible(snapshot.Organizations, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        // Preserve partial-input compatibility only when the window-filtered input has no roots.
        // Status or requested-id filtering must not turn an empty selection into a wildcard.
        var allowUnscopedBusinesses = !organizationId.HasValue && organizations.Count == 0;
        organizations = organizations
            .Where(candidate => !activeOnly || candidate.IsActive)
            .ToList();
        var businesses = FilterVisible(snapshot.Businesses, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var clients = FilterVisible(snapshot.Clients, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var funds = FilterVisible(snapshot.Funds, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var sleeves = FilterVisible(snapshot.Sleeves, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var vehicles = FilterVisible(snapshot.Vehicles, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var entities = FilterVisible(snapshot.Entities, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var portfolios = FilterVisible(snapshot.InvestmentPortfolios, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var activeLinks = FilterVisible(snapshot.OwnershipLinks, activeOnly, asOf, static link => (link.EffectiveFrom, link.EffectiveTo));
        var activeAssignments = FilterVisible(snapshot.Assignments, activeOnly, asOf, static assignment => (assignment.EffectiveFrom, assignment.EffectiveTo));

        if (businessId.HasValue)
        {
            businesses = businesses.Where(candidate => candidate.BusinessId == businessId.Value).ToList();
        }

        if (organizationId.HasValue)
        {
            organizations = organizations.Where(candidate => candidate.OrganizationId == organizationId.Value).ToList();
        }

        var organizationIds = organizations.Select(static organization => organization.OrganizationId).ToHashSet();
        businesses = businesses
            .Where(candidate => allowUnscopedBusinesses || organizationIds.Contains(candidate.OrganizationId))
            .ToList();
        var businessIds = businesses.Select(static business => business.BusinessId).ToHashSet();

        clients = clients.Where(candidate => businessIds.Contains(candidate.BusinessId)).ToList();
        funds = funds.Where(candidate => candidate.BusinessId.HasValue && businessIds.Contains(candidate.BusinessId.Value)).ToList();
        var fundIds = funds.Select(static fund => fund.FundId).ToHashSet();
        sleeves = sleeves.Where(candidate => fundIds.Contains(candidate.FundId)).ToList();
        var sleeveIds = sleeves.Select(static sleeve => sleeve.SleeveId).ToHashSet();
        vehicles = vehicles.Where(candidate => fundIds.Contains(candidate.FundId)).ToList();
        var vehicleIds = vehicles.Select(static vehicle => vehicle.VehicleId).ToHashSet();
        portfolios = portfolios.Where(candidate => businessIds.Contains(candidate.BusinessId)).ToList();
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        // Account-derived entities may extend an already selected branch, but must not recreate
        // an explicit empty organization/business scope from unrelated account references.
        var accountsForEntityScope = organizationId.HasValue || businessId.HasValue
            ? GetScopeAccounts(accounts, activeLinks, entityIds: Array.Empty<Guid>(),
                fundIds, sleeveIds, vehicleIds, portfolioIds)
            : accounts;
        var entityIds = vehicles.Select(static vehicle => vehicle.LegalEntityId)
            .Concat(funds.SelectMany(static fund => fund.EntityIds))
            .Concat(portfolios.Where(static portfolio => portfolio.EntityId.HasValue).Select(static portfolio => portfolio.EntityId!.Value))
            .Concat(accountsForEntityScope.Where(static account => account.EntityId.HasValue).Select(static account => account.EntityId!.Value))
            .ToHashSet();
        entities = entities
            .Where(candidate =>
                entityIds.Contains(candidate.EntityId)
                || activeLinks.Any(link => link.ChildNodeId == candidate.EntityId
                    && (businessIds.Contains(link.ParentNodeId)
                        || fundIds.Contains(link.ParentNodeId)
                        || vehicleIds.Contains(link.ParentNodeId)
                        || portfolioIds.Contains(link.ParentNodeId))))
            .ToList();

        var scopedAccounts = GetScopeAccounts(
            accounts,
            activeLinks,
            entities.Select(static entity => entity.EntityId).ToHashSet(),
            fundIds,
            sleeveIds,
            vehicleIds,
            portfolioIds)
            .ToList();
        var accountIds = scopedAccounts.Select(static account => account.AccountId).ToHashSet();
        var nodeIds = organizationIds
            .Concat(businessIds)
            .Concat(clients.Select(static client => client.ClientId))
            .Concat(fundIds)
            .Concat(sleeveIds)
            .Concat(vehicleIds)
            .Concat(entities.Select(static entity => entity.EntityId))
            .Concat(portfolioIds)
            .Concat(accountIds)
            .ToHashSet();
        activeLinks = activeLinks
            .Where(link => nodeIds.Contains(link.ParentNodeId) && nodeIds.Contains(link.ChildNodeId))
            .ToList();
        activeAssignments = activeAssignments
            .Where(assignment => nodeIds.Contains(assignment.NodeId))
            .ToList();

        return new StructureScope(
            organizations,
            businesses,
            clients,
            funds,
            sleeves,
            vehicles,
            entities,
            portfolios,
            scopedAccounts,
            activeLinks,
            activeAssignments);
    }

    private static IReadOnlyList<FundStructureNodeDto> BuildNodes(StructureScope scope)
    {
        return scope.Organizations
            .Select(static organization => new FundStructureNodeDto(
                organization.OrganizationId,
                FundStructureNodeKindDto.Organization,
                organization.Code,
                organization.Name,
                organization.Description,
                organization.IsActive,
                organization.EffectiveFrom,
                organization.EffectiveTo))
            .Concat(scope.Businesses.Select(static business => new FundStructureNodeDto(
                business.BusinessId,
                FundStructureNodeKindDto.Business,
                business.Code,
                business.Name,
                business.Description,
                business.IsActive,
                business.EffectiveFrom,
                business.EffectiveTo)))
            .Concat(scope.Clients.Select(static client => new FundStructureNodeDto(
                client.ClientId,
                FundStructureNodeKindDto.Client,
                client.Code,
                client.Name,
                client.Description,
                client.IsActive,
                client.EffectiveFrom,
                client.EffectiveTo)))
            .Concat(scope.Funds.Select(static fund => new FundStructureNodeDto(
                fund.FundId,
                FundStructureNodeKindDto.Fund,
                fund.Code,
                fund.Name,
                fund.Description,
                fund.IsActive,
                fund.EffectiveFrom,
                fund.EffectiveTo)))
            .Concat(scope.Sleeves.Select(static sleeve => new FundStructureNodeDto(
                sleeve.SleeveId,
                FundStructureNodeKindDto.Sleeve,
                sleeve.Code,
                sleeve.Name,
                sleeve.Mandate,
                sleeve.IsActive,
                sleeve.EffectiveFrom,
                sleeve.EffectiveTo)))
            .Concat(scope.Vehicles.Select(static vehicle => new FundStructureNodeDto(
                vehicle.VehicleId,
                FundStructureNodeKindDto.Vehicle,
                vehicle.Code,
                vehicle.Name,
                vehicle.Description,
                vehicle.IsActive,
                vehicle.EffectiveFrom,
                vehicle.EffectiveTo)))
            .Concat(scope.Entities.Select(static entity => new FundStructureNodeDto(
                entity.EntityId,
                FundStructureNodeKindDto.Entity,
                entity.Code,
                entity.Name,
                entity.Description,
                entity.IsActive,
                entity.EffectiveFrom,
                entity.EffectiveTo)))
            .Concat(scope.InvestmentPortfolios.Select(static portfolio => new FundStructureNodeDto(
                portfolio.InvestmentPortfolioId,
                FundStructureNodeKindDto.InvestmentPortfolio,
                portfolio.Code,
                portfolio.Name,
                portfolio.Description,
                portfolio.IsActive,
                portfolio.EffectiveFrom,
                portfolio.EffectiveTo)))
            .Concat(scope.Accounts.Select(static account => new FundStructureNodeDto(
                account.AccountId,
                FundStructureNodeKindDto.Account,
                account.AccountCode,
                account.DisplayName,
                account.Institution,
                account.IsActive,
                account.EffectiveFrom,
                account.EffectiveTo)))
            .ToList();
    }

    private static IReadOnlyList<T> FilterVisible<T>(
        IReadOnlyList<T> source,
        bool activeOnly,
        DateTimeOffset asOf,
        Func<T, (DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo)> selector)
        where T : notnull
    {
        return source
            .Where(item =>
            {
                var window = selector(item);
                return IsVisible(isActive: true, window.EffectiveFrom, window.EffectiveTo, activeOnly, asOf);
            })
            .ToList();
    }

    internal static bool IsVisible(
        bool isActive,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        bool activeOnly,
        DateTimeOffset asOf)
    {
        if (activeOnly && !isActive)
        {
            return false;
        }

        if (effectiveFrom > asOf)
        {
            return false;
        }

        if (effectiveTo.HasValue && effectiveTo.Value < asOf)
        {
            return false;
        }

        return true;
    }

    private static bool IsAdvisoryPortfolio(InvestmentPortfolioSummaryDto portfolio) =>
        portfolio.ClientId.HasValue || (!portfolio.FundId.HasValue && !portfolio.SleeveId.HasValue && !portfolio.VehicleId.HasValue);

    private static bool IsFundPortfolio(InvestmentPortfolioSummaryDto portfolio) =>
        portfolio.FundId.HasValue || portfolio.SleeveId.HasValue || portfolio.VehicleId.HasValue;

    private static bool HasAccountingAccountScopeFilter(AccountingStructureQuery query) =>
        query.ClientId.HasValue
        || query.FundId.HasValue
        || query.SleeveId.HasValue
        || query.VehicleId.HasValue
        || query.InvestmentPortfolioId.HasValue;

    private static bool IsAccountInAccountingScope(
        AccountSummaryDto account,
        AccountingStructureQuery query,
        AccountingAccountScope accountScope,
        IReadOnlyList<OwnershipLinkDto> ownershipLinks)
    {
        if (IsAccountLinkedToAccountingPortfolios(account, accountScope.PortfolioIds, ownershipLinks))
        {
            return true;
        }

        if (HasAccountingAccountScopeFilter(query))
        {
            return MatchesDirectAccountingAccountFilters(account, query);
        }

        return IsAccountInAccountingBusinessScope(account, accountScope, ownershipLinks);
    }

    private static IReadOnlyList<FundStructureAssignmentDto> FilterAccountingAssignments(
        StructureScope scoped,
        IReadOnlyList<AccountSummaryDto> accounts,
        IReadOnlyList<InvestmentPortfolioSummaryDto> portfolios,
        bool ledgerReferenceFiltered,
        Guid? organizationId,
        Guid? businessId)
    {
        var accountIds = accounts.Select(static account => account.AccountId).ToHashSet();
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        var nodeIds = accountIds.ToHashSet();

        // Retain every actual account reference used by LedgerGroupingRules, so narrowing the
        // response does not discard an inherited portfolio, sleeve, vehicle, fund or entity map.
        foreach (var account in accounts)
        {
            if (TryParseGuid(account.PortfolioId, out var portfolioId))
                nodeIds.Add(portfolioId);
            if (account.SleeveId is Guid sleeveId)
                nodeIds.Add(sleeveId);
            if (account.VehicleId is Guid vehicleId)
                nodeIds.Add(vehicleId);
            if (account.FundId is Guid fundId)
                nodeIds.Add(fundId);
            if (account.EntityId is Guid entityId)
                nodeIds.Add(entityId);
        }

        if (!ledgerReferenceFiltered)
            nodeIds.UnionWith(portfolioIds);
        else
            nodeIds.UnionWith(scoped.OwnershipLinks
                .Where(link => portfolioIds.Contains(link.ParentNodeId) && accountIds.Contains(link.ChildNodeId))
                .Select(static link => link.ParentNodeId));

        // LedgerReference only filters accounts. The existing broader portfolio list must not
        // reintroduce unrelated assignments when no account matches that ledger reference.
        if (organizationId.HasValue)
            nodeIds.Add(organizationId.Value);
        if (businessId.HasValue)
            nodeIds.Add(businessId.Value);
        return scoped.Assignments.Where(assignment => nodeIds.Contains(assignment.NodeId)).ToList();
    }

    private static AccountingAccountScope BuildAccountingAccountScope(
        StructureScope scoped,
        IReadOnlyList<InvestmentPortfolioSummaryDto> portfolios)
    {
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        var fundIds = scoped.Funds.Select(static fund => fund.FundId).ToHashSet();
        var sleeveIds = scoped.Sleeves.Select(static sleeve => sleeve.SleeveId).ToHashSet();
        var vehicleIds = scoped.Vehicles.Select(static vehicle => vehicle.VehicleId).ToHashSet();
        var scopedEntityIds = scoped.Entities.Select(static entity => entity.EntityId).ToHashSet();
        var entityParentIds = scoped.Businesses.Select(static business => business.BusinessId)
            .Concat(fundIds)
            .Concat(sleeveIds)
            .Concat(vehicleIds)
            .Concat(portfolioIds)
            .ToHashSet();
        var entityIds = scoped.Funds.SelectMany(static fund => fund.EntityIds)
            .Concat(scoped.Vehicles.Select(static vehicle => vehicle.LegalEntityId))
            .Concat(portfolios.Where(static portfolio => portfolio.EntityId.HasValue).Select(static portfolio => portfolio.EntityId!.Value))
            .Concat(scoped.OwnershipLinks
                .Where(link => scopedEntityIds.Contains(link.ChildNodeId) && entityParentIds.Contains(link.ParentNodeId))
                .Select(static link => link.ChildNodeId))
            .ToHashSet();

        return new AccountingAccountScope(entityIds, fundIds, sleeveIds, vehicleIds, portfolioIds);
    }

    private static bool IsAccountLinkedToAccountingPortfolios(
        AccountSummaryDto account,
        IReadOnlyCollection<Guid> portfolioIds,
        IReadOnlyList<OwnershipLinkDto> ownershipLinks) =>
        (TryParseGuid(account.PortfolioId, out var portfolioId) && portfolioIds.Contains(portfolioId))
        || IsAccountLinkedToAny(account.AccountId, portfolioIds, ownershipLinks);

    private static bool MatchesDirectAccountingAccountFilters(
        AccountSummaryDto account,
        AccountingStructureQuery query)
    {
        if (query.ClientId.HasValue || query.InvestmentPortfolioId.HasValue)
        {
            return false;
        }

        var hasDirectScopeFilter = query.FundId.HasValue || query.SleeveId.HasValue || query.VehicleId.HasValue;
        return hasDirectScopeFilter
            && MatchesOptionalScope(account.FundId, query.FundId)
            && MatchesOptionalScope(account.SleeveId, query.SleeveId)
            && MatchesOptionalScope(account.VehicleId, query.VehicleId);
    }

    private static bool MatchesOptionalScope(Guid? value, Guid? filter) =>
        !filter.HasValue || value == filter.Value;

    private static bool IsAccountInAccountingBusinessScope(
        AccountSummaryDto account,
        AccountingAccountScope accountScope,
        IReadOnlyList<OwnershipLinkDto> ownershipLinks) =>
        (account.EntityId.HasValue && accountScope.EntityIds.Contains(account.EntityId.Value))
        || (account.FundId.HasValue && accountScope.FundIds.Contains(account.FundId.Value))
        || (account.SleeveId.HasValue && accountScope.SleeveIds.Contains(account.SleeveId.Value))
        || (account.VehicleId.HasValue && accountScope.VehicleIds.Contains(account.VehicleId.Value))
        || IsAccountLinkedToAccountingPortfolios(account, accountScope.PortfolioIds, ownershipLinks)
        || IsAccountLinkedToAny(account.AccountId, accountScope.EntityIds, ownershipLinks)
        || IsAccountLinkedToAny(account.AccountId, accountScope.FundIds, ownershipLinks)
        || IsAccountLinkedToAny(account.AccountId, accountScope.SleeveIds, ownershipLinks)
        || IsAccountLinkedToAny(account.AccountId, accountScope.VehicleIds, ownershipLinks);

    internal static IEnumerable<AccountSummaryDto> GetScopeAccounts(
        IEnumerable<AccountSummaryDto> accounts,
        IReadOnlyList<OwnershipLinkDto> links,
        IReadOnlyCollection<Guid> entityIds,
        IReadOnlyCollection<Guid> fundIds,
        IReadOnlyCollection<Guid> sleeveIds,
        IReadOnlyCollection<Guid> vehicleIds,
        IReadOnlyCollection<Guid> portfolioIds)
    {
        var entitySet = entityIds as ISet<Guid> ?? entityIds.ToHashSet();
        var fundSet = fundIds as ISet<Guid> ?? fundIds.ToHashSet();
        var sleeveSet = sleeveIds as ISet<Guid> ?? sleeveIds.ToHashSet();
        var vehicleSet = vehicleIds as ISet<Guid> ?? vehicleIds.ToHashSet();
        var portfolioSet = portfolioIds as ISet<Guid> ?? portfolioIds.ToHashSet();
        var scopeNodeIds = entitySet
            .Concat(fundSet)
            .Concat(sleeveSet)
            .Concat(vehicleSet)
            .Concat(portfolioSet)
            .ToHashSet();

        return accounts
            .Where(account =>
                (account.EntityId.HasValue && entitySet.Contains(account.EntityId.Value))
                || (account.FundId.HasValue && fundSet.Contains(account.FundId.Value))
                || (account.SleeveId.HasValue && sleeveSet.Contains(account.SleeveId.Value))
                || (account.VehicleId.HasValue && vehicleSet.Contains(account.VehicleId.Value))
                || (TryParseGuid(account.PortfolioId, out var portfolioId) && portfolioSet.Contains(portfolioId))
                || links.Any(link => link.ChildNodeId == account.AccountId && scopeNodeIds.Contains(link.ParentNodeId)))
            .DistinctBy(static account => account.AccountId);
    }

    internal static bool IsAccountLinkedToAny(
        Guid accountId,
        IReadOnlyCollection<Guid> parentIds,
        IReadOnlyList<OwnershipLinkDto> links) =>
        links.Any(link => link.ChildNodeId == accountId && parentIds.Contains(link.ParentNodeId));

    private static bool IsEntityLinkedToScope(
        Guid entityId,
        IReadOnlyCollection<Guid> parentIds,
        IReadOnlyList<OwnershipLinkDto> links) =>
        links.Any(link => link.ChildNodeId == entityId && parentIds.Contains(link.ParentNodeId));

    internal static IReadOnlyList<Guid> AppendUnique(IReadOnlyList<Guid> source, Guid value)
    {
        if (source.Contains(value))
        {
            return source;
        }

        var updated = source.ToList();
        updated.Add(value);
        return updated;
    }

    internal static bool TryParseGuid(string? value, out Guid guid) =>
        Guid.TryParse(value, out guid);

    private sealed record AccountingAccountScope(
        IReadOnlySet<Guid> EntityIds,
        IReadOnlySet<Guid> FundIds,
        IReadOnlySet<Guid> SleeveIds,
        IReadOnlySet<Guid> VehicleIds,
        IReadOnlySet<Guid> PortfolioIds);

    // Services construct these collections from their captured working set. The projector only
    // reads the DTOs and uses record copies for enrichment, so later service writes cannot alter
    // the captured projection input.
    internal sealed record StructureSnapshot(
        IReadOnlyList<OrganizationSummaryDto> Organizations,
        IReadOnlyList<BusinessSummaryDto> Businesses,
        IReadOnlyList<ClientSummaryDto> Clients,
        IReadOnlyList<FundSummaryDto> Funds,
        IReadOnlyList<SleeveSummaryDto> Sleeves,
        IReadOnlyList<VehicleSummaryDto> Vehicles,
        IReadOnlyList<LegalEntitySummaryDto> Entities,
        IReadOnlyList<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
        IReadOnlyList<OwnershipLinkDto> OwnershipLinks,
        IReadOnlyList<FundStructureAssignmentDto> Assignments);

    internal sealed record StructureScope(
        IReadOnlyList<OrganizationSummaryDto> Organizations,
        IReadOnlyList<BusinessSummaryDto> Businesses,
        IReadOnlyList<ClientSummaryDto> Clients,
        IReadOnlyList<FundSummaryDto> Funds,
        IReadOnlyList<SleeveSummaryDto> Sleeves,
        IReadOnlyList<VehicleSummaryDto> Vehicles,
        IReadOnlyList<LegalEntitySummaryDto> Entities,
        IReadOnlyList<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
        IReadOnlyList<AccountSummaryDto> Accounts,
        IReadOnlyList<OwnershipLinkDto> OwnershipLinks,
        IReadOnlyList<FundStructureAssignmentDto> Assignments);
}
