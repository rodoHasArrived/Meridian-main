using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Adopted-only financial ownership resolved from retained book/position authority.</summary>
public static class BondIncomeAcquisitionOwnership
{
    public static Guid ResolveAccount(Guid ownerNode, FundStructureNodeKindDto ownerKind, BookPositionDto position)
    {
        var candidates = new List<Guid>();
        if (ownerKind == FundStructureNodeKindDto.Account)
            candidates.Add(ownerNode);
        if (position.BookContext.FundStructureNodeKind == FundStructureNodeKindDto.Account)
            candidates.Add(position.BookContext.FundStructureNodeId);
        if (!string.IsNullOrWhiteSpace(position.BookContext.Dimensions?.AccountId))
        {
            if (!Guid.TryParse(position.BookContext.Dimensions.AccountId, out var account))
                throw new ArgumentException("Adopted acquisition requires an authoritative financial-account identity.");
            candidates.Add(account);
        }
        // PrimaryAccountId identifies a carrying GL mapping. Its text may happen to be a GUID;
        // that does not make it a financial-account ownership assertion.
        var owners = candidates.Distinct().ToArray();
        if (owners.Length != 1 || owners[0] == Guid.Empty)
            throw new ArgumentException("Adopted acquisition requires one unambiguous authoritative financial account.");
        return owners[0];
    }

    public static LedgerDimensionSetDto StampDimensions(LedgerDimensionSetDto? dimensions, Guid owner,
        Guid book, Guid security, Guid position, string lotId)
    {
        var value = dimensions ?? new();
        if (owner == Guid.Empty || book == Guid.Empty || security == Guid.Empty || position == Guid.Empty
            || string.IsNullOrWhiteSpace(lotId)
            || value.InstrumentId is { } instrument && instrument != security
            || value.PositionId is { } held && held != position
            || !string.IsNullOrWhiteSpace(value.BookId) && value.BookId != book.ToString("D")
            || !string.IsNullOrWhiteSpace(value.AccountId) && value.AccountId != owner.ToString("D")
            || !string.IsNullOrWhiteSpace(value.TaxLotId) && value.TaxLotId != lotId)
            throw new ArgumentException("Adopted acquisition line scope conflicts with the authoritative owner, book, security, position or lot.");
        return value with
        {
            AccountId = owner.ToString("D"),
            BookId = book.ToString("D"),
            InstrumentId = security,
            PositionId = position,
            TaxLotId = lotId
        };
    }
}
