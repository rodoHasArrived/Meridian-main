using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidatePostService
{
    private static void ValidateCapitalReturnPosting(AssetLotMutationInstructionDto instruction,
        PostPostingRuleJournalCandidateRequestDto request, LedgerJournalEntryWrite approvedWrite,
        LedgerBookRecord ledgerBook, Guid sourceEventId, AssetPostingAuthoritySnapshot authority)
    {
        var inputs = instruction.CapitalReturn
            ?? throw new InvalidOperationException("Capital-return posting requires retained reviewed inputs.");
        OpenLotCapitalReturn.Project(inputs);
        RequireAssetAssertion(inputs.ExpectedLot.SecurityId == authority.Context.SecurityId
            && inputs.ExpectedLot.BookPositionId == authority.Context.BookPositionId
            && inputs.ExpectedLot.LedgerBookId == ledgerBook.LedgerBookId
            && inputs.ExpectedSecurityVersion == authority.Context.SecurityVersion
            && inputs.Projection.LotMutations!.ExpectedPositionVersion == authority.Context.ExpectedBookPositionVersion
            && inputs.Projection.EconomicEvent!.EventId == sourceEventId
            && inputs.Projection.EconomicEvent.EffectiveDate == request.Candidate.EffectiveDate
            && inputs.Projection.EventAmount == request.Candidate.EventAmount
            && inputs.ExpectedLot.Acquisition.FunctionalCurrency == ledgerBook.BaseCurrency
            && PayloadEquals(inputs, approvedWrite.PostingCommand?.LotCapitalReturn),
            "Capital-return inputs must match the governed event, posting command and exact lot authority.");
    }
}
