using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidatePostService
{
    private readonly IInstrumentPositionProjectionStore? _instrumentPositions;

    private async Task RequireAdoptedAcquisitionOwnerAsync(AssetAcquisitionLotDto acquisition,
        AssetAccountingCandidateAuthorityContext context, LedgerBookRecord ledgerBook,
        LedgerJournalEntryWrite approvedWrite, CancellationToken ct)
    {
        BondIncomeAcquisitionJournalPrecision.RequireRepresentable(approvedWrite.Entry);
        var position = _instrumentPositions is null ? null
            : await _instrumentPositions.GetBookPositionAsync(context.BookPositionId, ct).ConfigureAwait(false);
        RequireAssetAssertion(position is not null && position.Version == context.ExpectedBookPositionVersion
            && position.SecurityId == context.SecurityId && position.BookContext.LedgerBookId == ledgerBook.LedgerBookId
            && position.BookContext.FundStructureNodeId == ledgerBook.FundStructureNodeId
            && position.BookContext.FundStructureNodeKind == ledgerBook.FundStructureNodeKind,
            "Adopted acquisition requires its reviewed retained owner position before posting.");
        var owner = BondIncomeAcquisitionOwnership.ResolveAccount(ledgerBook.FundStructureNodeId,
            ledgerBook.FundStructureNodeKind, position!).ToString("D");
        RequireAssetAssertion(approvedWrite.Entry.Lines.All(line => line.Account.FinancialAccountId == owner
            && line.Dimensions?.AccountId == owner && line.Dimensions.BookId == ledgerBook.LedgerBookId.ToString("D")
            && line.Dimensions.InstrumentId == context.SecurityId
            && line.Dimensions.PositionId == context.BookPositionId
            && line.Dimensions.TaxLotId == acquisition.LotId),
            "Adopted acquisition birth ownership and actual lot dimensions must already match the reviewed candidate.");
    }
}
