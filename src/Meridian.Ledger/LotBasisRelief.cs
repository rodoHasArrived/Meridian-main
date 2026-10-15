namespace Meridian.Ledger;

/// <summary>
/// Exact carrying-basis allocation for the shared decimal consumption walk. Callers retain
/// ownership of lot ordering, scope validation, currency precision and journal consequences.
/// Invoke independently for each retained currency basis; no FX rate is inferred here.
/// </summary>
public static class LotBasisRelief
{
    public static IReadOnlyList<LotBasisSlice<TLot>> Allocate<TLot>(
        IReadOnlyList<LotSlice<TLot>> slices,
        Func<TLot, decimal> openQuantity,
        Func<TLot, decimal> openBasis,
        LotBasisPool? pool = null)
    {
        ArgumentNullException.ThrowIfNull(slices);
        ArgumentNullException.ThrowIfNull(openQuantity);
        ArgumentNullException.ThrowIfNull(openBasis);
        if (pool is { Quantity: <= 0m } or { CostBasis: < 0m })
            throw new ArgumentOutOfRangeException(nameof(pool), "A basis pool requires positive quantity and non-negative basis.");

        var quantity = slices.Sum(static slice => slice.Quantity);
        var target = 0m;
        if (pool is { } total)
        {
            if (quantity > total.Quantity)
                throw new ArgumentException("Relief cannot exceed its basis pool quantity.", nameof(pool));
            target = total.CostBasis * quantity / total.Quantity;
        }

        var result = new LotBasisSlice<TLot>[slices.Count];
        var allocated = 0m;
        for (var index = 0; index < slices.Count; index++)
        {
            var slice = slices[index];
            var available = openQuantity(slice.Lot);
            if (slice.Quantity <= 0m || slice.Quantity > available || slice.ClosesLot != (slice.Quantity == available))
                throw new ArgumentException("Relief slices must agree with the available lot quantity.", nameof(slices));

            decimal basis;
            if (pool is { } pooled)
            {
                basis = pooled.CostBasis * slice.Quantity / pooled.Quantity;
            }
            else
            {
                var currentBasis = openBasis(slice.Lot);
                basis = slice.ClosesLot ? currentBasis : currentBasis * slice.Quantity / available;
            }
            result[index] = new(slice.Lot, slice.Quantity, slice.ClosesLot, basis);
            allocated += basis;
        }

        if (pool is { } retained && result.Length > 0)
        {
            // Preserve the existing evaluation order for valid retained pool plans. Replacing
            // this with target-minus-prior-slices can change their exact decimal parcel values.
            result[^1] = result[^1] with { CostBasis = result[^1].CostBasis + target - allocated };
            if (retained.CostBasis > 0m && result.Any(static slice => slice.CostBasis <= 0m))
            {
                // A positive pool and positive quantities require positive proportional slices.
                // Bounding an exhausted target creates zero-basis parcels that durable posting
                // refuses. Block the unsupported plan instead of certifying altered economics.
                throw new InvalidOperationException(
                    "Pooled basis cannot retain a positive amount for every relief slice at decimal precision.");
            }
        }

        return result;
    }
}

/// <summary>One currency's non-negative pool basis; quantity includes all open lots in that pool.</summary>
public readonly record struct LotBasisPool(decimal Quantity, decimal CostBasis);

/// <summary>A consumption slice valued at its exact current carrying basis.</summary>
public readonly record struct LotBasisSlice<TLot>(TLot Lot, decimal Quantity, bool ClosesLot, decimal CostBasis);
