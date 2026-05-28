using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

public static class LedgerMappingWorkbenchService
{
    public static LedgerMappingWorkbenchDto Build(
        AccountingStructureViewDto accountingView,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(accountingView);

        var assignments = accountingView.Assignments ?? [];
        var accounts = accountingView.Accounts
            .OrderBy(static account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static account => account.AccountCode, StringComparer.OrdinalIgnoreCase)
            .Select(account => BuildAccount(account, assignments))
            .ToList();
        var mappedCount = accounts.Count(static account => !account.Mapping.RequiresUserMapping);

        return new LedgerMappingWorkbenchDto(
            asOf,
            accountingView.Organization,
            accountingView.Business,
            AccountCount: accounts.Count,
            MappedAccountCount: mappedCount,
            UnmappedAccountCount: accounts.Count - mappedCount,
            accountingView.LedgerGroups,
            accounts,
            accountingView.SharedDataAccess);
    }

    private static LedgerMappingAccountDto BuildAccount(
        AccountSummaryDto account,
        IReadOnlyList<FundStructureAssignmentDto> assignments)
    {
        var mapping = LedgerGroupingRules.ResolveLedgerGroupMapping(account, assignments);
        var recommendedAction = mapping.RequiresUserMapping
            ? "Assign a ledger group before posting, reconciliation, or governed reporting uses this account."
            : "No action required.";

        return new LedgerMappingAccountDto(
            account.AccountId,
            account.AccountCode,
            account.DisplayName,
            account.AccountType,
            account.OperationalStatus,
            account.BaseCurrency,
            account.Institution,
            account.FundId,
            account.SleeveId,
            account.VehicleId,
            account.EntityId,
            account.PortfolioId,
            account.LedgerReference,
            mapping,
            recommendedAction);
    }
}
