using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidatePostService
{
    private static void ValidateFactorPaydownPosting(AssetLotMutationInstructionDto instruction,
        PostPostingRuleJournalCandidateRequestDto request, LedgerJournalEntryWrite approvedWrite,
        LedgerBookRecord ledgerBook, Guid sourceEventId, AssetPostingAuthoritySnapshot authority)
    {
        var inputs = instruction.FactorPaydown
            ?? throw new InvalidOperationException("Factor-paydown posting requires retained reviewed per-lot inputs.");
        OpenLotFactorPaydown.Project(inputs);
        RequireAssetAssertion(inputs.Scope.SecurityId == authority.Context.SecurityId
            && inputs.Scope.BookPositionId == authority.Context.BookPositionId
            && inputs.Scope.LedgerBookId == ledgerBook.LedgerBookId
            && inputs.Scope.ExpectedSecurityVersion == authority.Context.SecurityVersion
            && inputs.Scope.ExpectedBookPositionVersion == authority.Context.ExpectedBookPositionVersion
            && inputs.EconomicEvent.EventId == sourceEventId
            && inputs.EconomicEvent.EffectiveDate == request.Candidate.EffectiveDate
            && inputs.PrincipalPaydown == request.Candidate.EventAmount
            && string.Equals(inputs.Currency, ledgerBook.BaseCurrency, StringComparison.Ordinal)
            && PayloadEquals(inputs, approvedWrite.PostingCommand?.LotFactorPaydown),
            "Factor-paydown inputs must match the governed event, posting command and exact lot authority.");
    }
}
