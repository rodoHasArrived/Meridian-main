using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidateService
{
    private async Task<Guid?> ResolveAdoptedAcquisitionOwnerAsync(PostingRuleJournalCandidateRequestDto request,
        AssetAccountingCandidateAuthorityContext? authority, CancellationToken ct)
    {
        if (request.AssetLotMutation?.Acquisition?.BondIncome is null)
            return null;
        if (authority is null || request.AssetLotMutation.Intent != AssetLotMutationIntentDto.Acquire
            || authority.EventKind != AssetAccountingEventKindDto.Acquisition
            || request.AssetLotMutation.Acquisition.TaxLotRecordId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.AssetLotMutation.Acquisition.LotId)
            || _instrumentPositions is null || _ledgerBookService is null)
            throw new InvalidOperationException("Adopted acquisition requires canonical lot identity and retained position/book authority before review.");
        var position = await _instrumentPositions.GetBookPositionAsync(authority.BookPositionId, ct).ConfigureAwait(false);
        var book = await _ledgerBookService.GetBookAsync(authority.LedgerBookId, ct).ConfigureAwait(false);
        if (position is null || book is null || position.Version != authority.ExpectedBookPositionVersion
            || position.SecurityId != authority.SecurityId || position.BookContext.LedgerBookId != book.LedgerBookId
            || book.LedgerBookId != request.LedgerBookId || position.BookContext.FundProfileId != book.FundProfileId
            || position.BookContext.FundStructureNodeId != book.FundStructureNodeId
            || position.BookContext.FundStructureNodeKind != book.FundStructureNodeKind
            || position.BookContext.AccountingBasis != book.AccountingBasis
            || position.BookContext.BaseCurrency != book.BaseCurrency
            || position.BookContext.AccountingPolicyId != book.AccountingPolicyId
            || position.BookContext.AccountingPolicyVersion != book.AccountingPolicyVersion)
            throw new InvalidOperationException("Adopted acquisition owner must match the reviewed retained position and complete ledger-book authority.");
        return BondIncomeAcquisitionOwnership.ResolveAccount(book.FundStructureNodeId, book.FundStructureNodeKind, position);
    }
}
