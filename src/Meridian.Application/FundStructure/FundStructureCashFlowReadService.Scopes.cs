using Meridian.Contracts.FundStructure;
using Meridian.Entities.FundStructure;
using static Meridian.Application.FundStructure.FundStructureProjector;

namespace Meridian.Application.FundStructure;

internal sealed partial class FundStructureCashFlowReadService
{
    private static IReadOnlyDictionary<Guid, LedgerGroupId> BuildLedgerAssignments(
        IReadOnlyList<FundStructureAssignmentDto> assignments) =>
        LedgerGroupingRules.BuildLedgerAssignments(assignments);

    private static ResolvedCashFlowScope? ResolveCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped,
        IReadOnlyDictionary<Guid, LedgerGroupId> ledgerAssignments)
    {
        return query.ScopeKind switch
        {
            GovernanceCashFlowScopeKindDto.Organization => ResolveOrganizationCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.Business => ResolveBusinessCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.Client => ResolveClientCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.Fund => ResolveFundCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.Sleeve => ResolveSleeveCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.Vehicle => ResolveVehicleCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.InvestmentPortfolio => ResolveInvestmentPortfolioCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.Account => ResolveAccountCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.LedgerGroup => ResolveLedgerGroupCashFlowScope(query, scoped, ledgerAssignments),
            _ => throw new InvalidOperationException($"Unsupported governance cash-flow scope '{query.ScopeKind}'.")
        };
    }

    private static ResolvedCashFlowScope? ResolveOrganizationCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var organization = ResolveSelectedScope(
            query.OrganizationId,
            scoped.Organizations,
            static candidate => candidate.OrganizationId,
            "organization");
        if (organization is null)
        {
            return null;
        }

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Organization,
                organization.Name,
                organization.OrganizationId,
                query.BusinessId,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                scoped.Accounts.Select(static account => account.AccountId).ToList(),
                scoped.InvestmentPortfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToList()),
            scoped.Accounts,
            organization.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveBusinessCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var business = ResolveSelectedScope(
            query.BusinessId,
            scoped.Businesses,
            static candidate => candidate.BusinessId,
            "business");
        if (business is null)
        {
            return null;
        }

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Business,
                business.Name,
                business.OrganizationId,
                business.BusinessId,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                scoped.Accounts.Select(static account => account.AccountId).ToList(),
                scoped.InvestmentPortfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToList()),
            scoped.Accounts,
            business.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveClientCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var client = ResolveSelectedScope(
            query.ClientId,
            scoped.Clients,
            static candidate => candidate.ClientId,
            "client");
        if (client is null)
        {
            return null;
        }

        var portfolios = scoped.InvestmentPortfolios
            .Where(portfolio => portfolio.ClientId == client.ClientId)
            .ToList();
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        var accounts = GetScopeAccounts(
                scoped.Accounts,
                scoped.OwnershipLinks,
                entityIds: Array.Empty<Guid>(),
                fundIds: Array.Empty<Guid>(),
                sleeveIds: Array.Empty<Guid>(),
                vehicleIds: Array.Empty<Guid>(),
                portfolioIds)
            .ToList();
        var business = scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == client.BusinessId);

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Client,
                client.Name,
                business?.OrganizationId,
                client.BusinessId,
                client.ClientId,
                null,
                null,
                null,
                null,
                null,
                null,
                accounts.Select(static account => account.AccountId).ToList(),
                portfolioIds.ToList()),
            accounts,
            client.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveFundCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var fund = ResolveSelectedScope(
            query.FundId,
            scoped.Funds,
            static candidate => candidate.FundId,
            "fund");
        if (fund is null)
        {
            return null;
        }

        var sleeves = scoped.Sleeves.Where(candidate => candidate.FundId == fund.FundId).ToList();
        var sleeveIds = sleeves.Select(static sleeve => sleeve.SleeveId).ToHashSet();
        var vehicles = scoped.Vehicles.Where(candidate => candidate.FundId == fund.FundId).ToList();
        var vehicleIds = vehicles.Select(static vehicle => vehicle.VehicleId).ToHashSet();
        var portfolios = scoped.InvestmentPortfolios
            .Where(portfolio =>
                portfolio.FundId == fund.FundId
                || (portfolio.SleeveId.HasValue && sleeveIds.Contains(portfolio.SleeveId.Value))
                || (portfolio.VehicleId.HasValue && vehicleIds.Contains(portfolio.VehicleId.Value)))
            .ToList();
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        var entityIds = fund.EntityIds
            .Concat(vehicles.Select(static vehicle => vehicle.LegalEntityId))
            .Concat(portfolios.Where(static portfolio => portfolio.EntityId.HasValue).Select(static portfolio => portfolio.EntityId!.Value))
            .ToHashSet();
        var accounts = GetScopeAccounts(
                scoped.Accounts,
                scoped.OwnershipLinks,
                entityIds,
                [fund.FundId],
                sleeveIds,
                vehicleIds,
                portfolioIds)
            .ToList();

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Fund,
                fund.Name,
                query.OrganizationId,
                fund.BusinessId,
                null,
                fund.FundId,
                null,
                null,
                null,
                null,
                null,
                accounts.Select(static account => account.AccountId).ToList(),
                portfolioIds.ToList()),
            accounts,
            fund.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveSleeveCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var sleeve = ResolveSelectedScope(
            query.SleeveId,
            scoped.Sleeves,
            static candidate => candidate.SleeveId,
            "sleeve");
        if (sleeve is null)
        {
            return null;
        }

        var fund = scoped.Funds.FirstOrDefault(candidate => candidate.FundId == sleeve.FundId);
        var portfolios = scoped.InvestmentPortfolios
            .Where(portfolio => portfolio.SleeveId == sleeve.SleeveId)
            .ToList();
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        var entityIds = portfolios
            .Where(static portfolio => portfolio.EntityId.HasValue)
            .Select(static portfolio => portfolio.EntityId!.Value)
            .ToHashSet();
        var accounts = GetScopeAccounts(
                scoped.Accounts,
                scoped.OwnershipLinks,
                entityIds,
                fundIds: Array.Empty<Guid>(),
                [sleeve.SleeveId],
                vehicleIds: Array.Empty<Guid>(),
                portfolioIds)
            .ToList();

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Sleeve,
                sleeve.Name,
                query.OrganizationId,
                fund?.BusinessId,
                null,
                fund?.FundId,
                sleeve.SleeveId,
                null,
                null,
                null,
                null,
                accounts.Select(static account => account.AccountId).ToList(),
                portfolioIds.ToList()),
            accounts,
            fund?.BaseCurrency ?? accounts.FirstOrDefault()?.BaseCurrency ?? DefaultCashFlowCurrency);
    }

    private static ResolvedCashFlowScope? ResolveVehicleCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var vehicle = ResolveSelectedScope(
            query.VehicleId,
            scoped.Vehicles,
            static candidate => candidate.VehicleId,
            "vehicle");
        if (vehicle is null)
        {
            return null;
        }

        var fund = scoped.Funds.FirstOrDefault(candidate => candidate.FundId == vehicle.FundId);
        var portfolios = scoped.InvestmentPortfolios
            .Where(portfolio => portfolio.VehicleId == vehicle.VehicleId)
            .ToList();
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        var entityIds = portfolios
            .Where(static portfolio => portfolio.EntityId.HasValue)
            .Select(static portfolio => portfolio.EntityId!.Value)
            .Append(vehicle.LegalEntityId)
            .ToHashSet();
        var accounts = GetScopeAccounts(
                scoped.Accounts,
                scoped.OwnershipLinks,
                entityIds,
                fundIds: Array.Empty<Guid>(),
                sleeveIds: Array.Empty<Guid>(),
                [vehicle.VehicleId],
                portfolioIds)
            .ToList();

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Vehicle,
                vehicle.Name,
                query.OrganizationId,
                fund?.BusinessId,
                null,
                fund?.FundId,
                null,
                vehicle.VehicleId,
                null,
                null,
                null,
                accounts.Select(static account => account.AccountId).ToList(),
                portfolioIds.ToList()),
            accounts,
            vehicle.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveInvestmentPortfolioCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var portfolio = ResolveSelectedScope(
            query.InvestmentPortfolioId,
            scoped.InvestmentPortfolios,
            static candidate => candidate.InvestmentPortfolioId,
            "investment portfolio");
        if (portfolio is null)
        {
            return null;
        }

        var accounts = scoped.Accounts
            .Where(account =>
                (TryParseGuid(account.PortfolioId, out var portfolioId) && portfolioId == portfolio.InvestmentPortfolioId)
                || IsAccountLinkedToAny(account.AccountId, [portfolio.InvestmentPortfolioId], scoped.OwnershipLinks))
            .DistinctBy(static account => account.AccountId)
            .ToList();
        var business = scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == portfolio.BusinessId);

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.InvestmentPortfolio,
                portfolio.Name,
                business?.OrganizationId,
                portfolio.BusinessId,
                portfolio.ClientId,
                portfolio.FundId,
                portfolio.SleeveId,
                portfolio.VehicleId,
                portfolio.InvestmentPortfolioId,
                null,
                null,
                accounts.Select(static account => account.AccountId).ToList(),
                [portfolio.InvestmentPortfolioId]),
            accounts,
            portfolio.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveAccountCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var account = ResolveSelectedScope(
            query.AccountId,
            scoped.Accounts,
            static candidate => candidate.AccountId,
            "account");
        if (account is null)
        {
            return null;
        }

        var portfolioIds = new List<Guid>();
        if (TryParseGuid(account.PortfolioId, out var portfolioId))
        {
            portfolioIds.Add(portfolioId);
        }

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Account,
                account.DisplayName,
                query.OrganizationId,
                query.BusinessId,
                null,
                account.FundId,
                account.SleeveId,
                account.VehicleId,
                portfolioIds.FirstOrDefault() == Guid.Empty ? null : portfolioIds.FirstOrDefault(),
                account.AccountId,
                null,
                [account.AccountId],
                portfolioIds),
            [account],
            account.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveLedgerGroupCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped,
        IReadOnlyDictionary<Guid, LedgerGroupId> ledgerAssignments)
    {
        if (!query.LedgerGroupId.HasValue)
        {
            throw new InvalidOperationException("LedgerGroup scope requires LedgerGroupId.");
        }

        var accounts = scoped.Accounts
            .Where(account => LedgerGroupingRules.ResolveLedgerGroupId(account, ledgerAssignments) == query.LedgerGroupId.Value)
            .ToList();
        var portfolioIds = GetRelatedPortfolioIds(accounts, scoped.OwnershipLinks, scoped.InvestmentPortfolios);

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.LedgerGroup,
                query.LedgerGroupId.Value.Value,
                query.OrganizationId,
                query.BusinessId,
                null,
                null,
                null,
                null,
                null,
                null,
                query.LedgerGroupId,
                accounts.Select(static account => account.AccountId).ToList(),
                portfolioIds),
            accounts,
            accounts.FirstOrDefault()?.BaseCurrency ?? DefaultCashFlowCurrency);
    }

    private static T? ResolveSelectedScope<T>(
        Guid? requestedId,
        IReadOnlyList<T> candidates,
        Func<T, Guid> idSelector,
        string scopeLabel)
    {
        if (requestedId.HasValue)
        {
            return candidates.FirstOrDefault(candidate => idSelector(candidate) == requestedId.Value);
        }

        return candidates.Count switch
        {
            0 => default,
            1 => candidates[0],
            _ => throw new InvalidOperationException(
                $"{scopeLabel} scope requires an explicit identifier when multiple {scopeLabel}s are available.")
        };
    }

    private static IReadOnlyList<Guid> GetRelatedPortfolioIds(
        IReadOnlyList<AccountSummaryDto> accounts,
        IReadOnlyList<OwnershipLinkDto> links,
        IReadOnlyList<InvestmentPortfolioSummaryDto> portfolios)
    {
        if (accounts.Count == 0 || portfolios.Count == 0)
        {
            return [];
        }

        var portfolioById = portfolios.ToDictionary(static portfolio => portfolio.InvestmentPortfolioId);
        var accountIds = accounts.Select(static account => account.AccountId).ToHashSet();

        return accounts
            .Select(account => TryParseGuid(account.PortfolioId, out var portfolioId) ? portfolioId : Guid.Empty)
            .Where(static portfolioId => portfolioId != Guid.Empty)
            .Concat(links
                .Where(link => accountIds.Contains(link.ChildNodeId) && portfolioById.ContainsKey(link.ParentNodeId))
                .Select(static link => link.ParentNodeId))
            .Distinct()
            .ToList();
    }

    private static string ResolveCashFlowCurrency(
        string? requestedCurrency,
        ResolvedCashFlowScope scope)
    {
        if (!string.IsNullOrWhiteSpace(requestedCurrency))
        {
            return requestedCurrency.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(scope.BaseCurrency))
        {
            return scope.BaseCurrency.Trim().ToUpperInvariant();
        }

        var accountCurrency = scope.Accounts
            .Select(static account => account.BaseCurrency)
            .FirstOrDefault(static currency => !string.IsNullOrWhiteSpace(currency));

        return string.IsNullOrWhiteSpace(accountCurrency)
            ? DefaultCashFlowCurrency
            : accountCurrency.Trim().ToUpperInvariant();
    }

}
