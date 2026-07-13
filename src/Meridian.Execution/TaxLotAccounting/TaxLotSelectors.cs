using Meridian.Execution.Sdk;
using Meridian.Ledger;

namespace Meridian.Execution.TaxLotAccounting;

/// <summary>
/// Default lot-relief selector implementations for common accounting methods.
/// </summary>
public static class TaxLotSelectors
{
    /// <summary>Returns a FIFO selector (oldest lots relieved first).</summary>
    public static ITaxLotSelector Fifo() => new OrderedLotSelector(
        TaxLotAccountingMethod.Fifo,
        lots => lots.OrderBy(l => l.OpenedAt));

    /// <summary>Returns a LIFO selector (most recent lots relieved first).</summary>
    public static ITaxLotSelector Lifo() => new OrderedLotSelector(
        TaxLotAccountingMethod.Lifo,
        lots => lots.OrderByDescending(l => l.OpenedAt));

    /// <summary>Returns a HIFO selector (highest cost-basis lots relieved first).</summary>
    public static ITaxLotSelector Hifo() => new OrderedLotSelector(
        TaxLotAccountingMethod.Hifo,
        lots => lots.OrderByDescending(l => l.CostBasis));

    /// <summary>
    /// Returns a specific-lot selector that relieves exactly the lots identified by
    /// <paramref name="lotIds"/> in the order provided.
    /// </summary>
    public static ITaxLotSelector SpecificId(IEnumerable<Guid> lotIds) =>
        new SpecificIdLotSelector(lotIds.ToArray());

    // -------------------------------------------------------------------------
    // Implementations
    // -------------------------------------------------------------------------

    private sealed class OrderedLotSelector(
        TaxLotAccountingMethod method,
        Func<IReadOnlyList<TaxLot>, IOrderedEnumerable<TaxLot>> orderFunc)
        : ITaxLotSelector
    {
        public TaxLotAccountingMethod Method => method;

        public TaxLotReliefResult Relieve(
            IReadOnlyList<TaxLot> openLots,
            long quantityToClose,
            decimal closePrice)
        {
            ArgumentNullException.ThrowIfNull(openLots);
            if (quantityToClose <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantityToClose), "Must be positive.");

            var totalOpen = openLots.Sum(l => l.AbsoluteQuantity);
            if (quantityToClose > totalOpen)
                throw new InvalidOperationException(
                    $"Cannot close {quantityToClose} shares; only {totalOpen} open.");

            var ordered = orderFunc(openLots).ToList();
            var consumption = LotConsumption.Consume(
                ordered, quantityToClose, static lot => lot.AbsoluteQuantity);

            var relieved = new List<RelievedLot>();
            var remaining = new List<TaxLot>();
            var sliceIndex = 0;

            foreach (var lot in ordered)
            {
                if (sliceIndex < consumption.Slices.Count
                    && ReferenceEquals(consumption.Slices[sliceIndex].Lot, lot))
                {
                    var slice = consumption.Slices[sliceIndex++];
                    var take = (long)slice.Quantity;
                    relieved.Add(new RelievedLot(lot, take, closePrice));
                    if (!slice.ClosesLot)
                        remaining.Add(lot.WithReducedQuantity(take));
                }
                else
                {
                    remaining.Add(lot);
                }
            }

            return new TaxLotReliefResult(relieved, remaining);
        }
    }

    private sealed class SpecificIdLotSelector(Guid[] lotIds) : ITaxLotSelector
    {
        public TaxLotAccountingMethod Method => TaxLotAccountingMethod.SpecificId;

        public TaxLotReliefResult Relieve(
            IReadOnlyList<TaxLot> openLots,
            long quantityToClose,
            decimal closePrice)
        {
            ArgumentNullException.ThrowIfNull(openLots);
            if (quantityToClose <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantityToClose), "Must be positive.");

            var lotMap = openLots.ToDictionary(l => l.LotId);
            var selectedLots = lotIds.Select(id => lotMap.TryGetValue(id, out var lot)
                ? lot
                : throw new InvalidOperationException($"Lot {id} not found in open positions."));

            var consumption = LotConsumption.Consume(
                selectedLots, quantityToClose, static lot => lot.AbsoluteQuantity);

            if (!consumption.FullyConsumed)
                throw new InvalidOperationException(
                    $"Specified lots did not cover the full close quantity; {consumption.Shortfall} shares remain.");

            var relieved = new List<RelievedLot>();
            var remaining = openLots.ToDictionary(l => l.LotId);
            foreach (var slice in consumption.Slices)
            {
                var take = (long)slice.Quantity;
                relieved.Add(new RelievedLot(slice.Lot, take, closePrice));

                if (slice.ClosesLot)
                    remaining.Remove(slice.Lot.LotId);
                else
                    remaining[slice.Lot.LotId] = slice.Lot.WithReducedQuantity(take);
            }

            return new TaxLotReliefResult(relieved, [.. remaining.Values]);
        }
    }
}
