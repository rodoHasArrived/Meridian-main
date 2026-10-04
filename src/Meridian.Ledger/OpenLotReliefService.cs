using Meridian.Contracts.Accounting.Lots;

namespace Meridian.Ledger;

/// <summary>Decimal relief over canonical lots, retaining both acquisition and functional basis.</summary>
public sealed class OpenLotReliefService : IOpenLotReliefService
{
    public OpenLotReliefResultDto Select(IReadOnlyList<OpenLotDto> lots, decimal quantity,
        OpenLotReliefMethod method, IReadOnlyList<Guid>? specificLotIds = null)
    {
        ArgumentNullException.ThrowIfNull(lots);
        if (quantity <= 0 || lots.Count == 0)
            throw new ArgumentException("Positive relief and at least one open lot are required.");
        foreach (var lot in lots)
            OpenLotValidation.Validate(lot);
        var first = lots[0];
        if (lots.Any(l => l.SecurityId != first.SecurityId || l.LedgerBookId != first.LedgerBookId
            || l.BookPositionId != first.BookPositionId || l.Acquisition.QuantityBasis != first.Acquisition.QuantityBasis
            || l.Acquisition.AcquisitionCurrency != first.Acquisition.AcquisitionCurrency
            || l.Acquisition.FunctionalCurrency != first.Acquisition.FunctionalCurrency))
            throw new ArgumentException("Relief cannot pool different security, position, book, quantity-basis, or currency scopes.");
        if (lots.Select(l => l.TaxLotRecordId).Distinct().Count() != lots.Count)
            throw new ArgumentException("Duplicate durable lot identity.");
        var open = lots.Where(l => l.OpenQuantity > 0).ToArray();
        IEnumerable<OpenLotDto> ordered = method switch
        {
            OpenLotReliefMethod.Fifo or OpenLotReliefMethod.AverageCost => open.OrderBy(l => l.AcquiredDate).ThenBy(l => l.TaxLotRecordId),
            OpenLotReliefMethod.Lifo => open.OrderByDescending(l => l.AcquiredDate).ThenBy(l => l.TaxLotRecordId),
            OpenLotReliefMethod.Hifo => open.OrderByDescending(l => l.OpenFunctionalCostBasis / l.OpenQuantity)
                .ThenBy(l => l.AcquiredDate).ThenBy(l => l.TaxLotRecordId),
            OpenLotReliefMethod.SpecificId => SelectSpecific(open, specificLotIds),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
        var consumed = LotConsumption.Consume(ordered, quantity, l => l.OpenQuantity);
        if (!consumed.FullyConsumed)
            throw new InvalidOperationException("Insufficient selected open-lot quantity.");
        var totalQuantity = open.Sum(l => l.OpenQuantity);
        var pooledTransaction = open.Sum(l => l.OpenTransactionCostBasis);
        var pooledFunctional = open.Sum(l => l.OpenFunctionalCostBasis);
        var selections = consumed.Slices.Select(s => new OpenLotReliefSelectionDto(s.Lot.TaxLotRecordId, s.Lot.LotId,
            s.Lot.Version, s.Quantity,
            method == OpenLotReliefMethod.AverageCost ? pooledTransaction * s.Quantity / totalQuantity
                : s.ClosesLot ? s.Lot.OpenTransactionCostBasis : s.Lot.OpenTransactionCostBasis * s.Quantity / s.Lot.OpenQuantity,
            method == OpenLotReliefMethod.AverageCost ? pooledFunctional * s.Quantity / totalQuantity
                : s.ClosesLot ? s.Lot.OpenFunctionalCostBasis : s.Lot.OpenFunctionalCostBasis * s.Quantity / s.Lot.OpenQuantity)).ToArray();
        // Allocate any decimal division residual to the last slice so pooled totals conserve exactly.
        if (method == OpenLotReliefMethod.AverageCost)
        {
            selections[^1] = selections[^1] with
            {
                TransactionCostBasis = selections[^1].TransactionCostBasis + pooledTransaction * quantity / totalQuantity - selections.Sum(s => s.TransactionCostBasis),
                FunctionalCostBasis = selections[^1].FunctionalCostBasis + pooledFunctional * quantity / totalQuantity - selections.Sum(s => s.FunctionalCostBasis)
            };
        }
        return new(selections, quantity, selections.Sum(s => s.TransactionCostBasis), selections.Sum(s => s.FunctionalCostBasis));
    }

    private static IReadOnlyList<OpenLotDto> SelectSpecific(OpenLotDto[] lots, IReadOnlyList<Guid>? ids)
    {
        if (ids is null || ids.Count == 0 || ids.Distinct().Count() != ids.Count)
            throw new ArgumentException("Specific relief requires unique durable lot identifiers.");
        var byId = lots.ToDictionary(l => l.TaxLotRecordId);
        return ids.Select(id => byId.TryGetValue(id, out var lot) ? lot
            : throw new ArgumentException("A selected durable lot is not open in this scope.")).ToArray();
    }
}
