using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidateService
{
    private static JournalEntry WithCanonicalCashFlow(JournalEntry entry,
        PostingRuleJournalCandidateRequestDto request, AssetAccountingCandidateAuthorityContext? authority,
        IReadOnlyList<GeneratedPostingLineDto> generated)
    {
        if (request.AssetLotMutation is { Intent: AssetLotMutationIntentDto.FactorPaydown })
            return LedgerCashFlowAttribution.WithClassifiedActivity(entry,
                LedgerCashFlowCategory.Investing, "Factor principal repayment");

        // The spine has already resolved current canonical lot relief and bound these selections
        // to its retained projection. An arbitrary Dispose label or a balanced compound journal
        // cannot establish that every line belongs to the disposal's investing cash activity.
        if (authority?.EventKind != AssetAccountingEventKindDto.Disposal
            || request.AssetLotMutation is not { Intent: AssetLotMutationIntentDto.Dispose } disposal
            || disposal.DisposalSelections.Count == 0 || request.EventAmount <= 0m
            || entry.Lines.Count is < 2 or > 3 || generated.Count != entry.Lines.Count
            || disposal.DisposalSelections.Any(selection => selection.ExpectedCostBasis <= 0m
                || selection.ExpectedVersion <= 0 || selection.Quantity <= 0m
                || selection.Quantity > selection.ExpectedOpenQuantity))
            return entry;

        var basis = disposal.DisposalSelections.Sum(selection => selection.ExpectedCostBasis);
        var cash = entry.Lines.Where(line => IsCash(line.Account)).ToArray();
        var carryingIndexes = generated.Select((line, index) => (line, index))
            .Where(item => string.Equals(item.line.AccountPath, disposal.AssetAccountId, StringComparison.Ordinal))
            .Select(item => item.index).ToArray();
        if (cash.Length != 1 || cash[0].Debit != request.EventAmount || cash[0].Credit != 0m
            || carryingIndexes.Length != 1)
            return entry;
        var carrying = entry.Lines[carryingIndexes[0]];
        if (IsCash(carrying.Account) || carrying.Account.AccountType != LedgerAccountType.Asset
            || carrying.Credit != basis || carrying.Debit != 0m
            || entry.Lines.Any(line => !string.Equals(line.Account.FinancialAccountId,
                carrying.Account.FinancialAccountId, StringComparison.OrdinalIgnoreCase)))
            return entry;

        var result = request.EventAmount - basis;
        var realized = entry.Lines.Where(line => line.EntryId != cash[0].EntryId
            && line.EntryId != carrying.EntryId).ToArray();
        var exactResult = result == 0m ? realized.Length == 0
            : realized.Length == 1 && (result > 0m
                ? realized[0].Account.AccountType == LedgerAccountType.Revenue
                    && realized[0].Credit == result && realized[0].Debit == 0m
                : realized[0].Account.AccountType == LedgerAccountType.Expense
                    && realized[0].Debit == -result && realized[0].Credit == 0m);
        return exactResult
            ? LedgerCashFlowAttribution.WithClassifiedActivity(entry, LedgerCashFlowCategory.Investing, "Security disposal")
            : entry;
    }

    private static bool IsCash(LedgerAccount account) => account.AccountType == LedgerAccountType.Asset
        && account.Name.StartsWith("Cash", StringComparison.OrdinalIgnoreCase);
}
