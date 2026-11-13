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
        var evidence = ConsolidationService.ReadEvidence(draft.ConsolidationEvidenceJson);
        if (evidence?.RuleVersion is "w10-v2" or "w10-v3" or "w10-v4")
        {
            foreach (var line in draft.Lines)
            {
                var binding = evidence.AccountBindings?.SingleOrDefault(x => x.LedgerBookId == draft.LedgerBookId &&
                    x.ChartAccountNodeId == line.Dimensions?.ChartAccountNodeId &&
                    x.RevisionId.ToString("D") == line.Dimensions?.AccountBindingRevisionId);
                var node = binding is null ? null : workspace.ChartOfAccounts.SingleOrDefault(x => x.NodeId == binding.ChartAccountNodeId);
                if (binding is null || binding.IsArchived || node is null || node.IsArchived ||
                    node.AccountName != binding.AccountName || node.Path != line.AccountPath || node.Path != binding.AccountPath ||
                    !string.Equals(node.AccountType, binding.AccountType, StringComparison.OrdinalIgnoreCase) ||
                    node.IntercompanyRole != binding.Role || node.Symbol is not null || node.FinancialAccountId is not null)
                    throw new InvalidOperationException("The current elimination chart differs from its reviewed stable role binding; rerun and renew review.");
            }
            return;
        }
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
