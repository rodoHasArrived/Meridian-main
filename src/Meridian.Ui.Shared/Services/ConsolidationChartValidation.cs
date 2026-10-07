using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;

namespace Meridian.Ui.Shared.Services;

/// <summary>Rechecks the current elimination chart before intake and governed lifecycle mutations.</summary>
internal static class ConsolidationChartValidation
{
    public static async Task ValidateAsync(IAccountingConfigurationService configuration,
        ManualJournalEntryDraftDto draft, CancellationToken ct)
    {
        var workspace = await configuration.GetWorkspaceAsync(draft.FundProfileId, draft.LedgerBookId,
            ct, draft.TenantId, draft.CompanyId).ConfigureAwait(false);
        foreach (var line in draft.Lines)
        {
            var expectedType = line.AccountPath switch
            {
                ConsolidationService.ReceivableAccount => "Asset",
                ConsolidationService.PayableAccount => "Liability",
                _ => null
            };
            var account = workspace.ChartOfAccounts.SingleOrDefault(x => x.Path == line.AccountPath);
            if (expectedType is null || account is null || account.IsArchived || account.AccountName != line.AccountPath ||
                account.Symbol is not null || account.FinancialAccountId is not null ||
                !string.Equals(account.AccountType, expectedType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The elimination chart must contain the supported unscoped intercompany account paths with identical account names; Symbol and FinancialAccountId must be absent.");
        }
    }
}
