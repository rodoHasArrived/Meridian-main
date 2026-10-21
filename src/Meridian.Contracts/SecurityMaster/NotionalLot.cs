namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Canonical open lot for notional-denominated OTC derivatives (interest rate swaps, credit default
/// swaps, swaptions, caps and floors), whose <see cref="SecurityAssetClassDescriptor.LotUnit"/> is
/// <see cref="SecurityLotUnitKind.Notional"/>. The lot's size is the notional the contract's
/// payments are computed on, not a count of anything owned, so it has no shares, face or units and
/// amortizes by replacing the notional rather than by relieving quantity. Any upfront payment
/// exchanged at inception is the only cost the lot carries.
/// </summary>
public sealed record NotionalLot : IUnitizedLot
{
    public NotionalLot(
        string lotId,
        Guid securityId,
        DateOnly effectiveDate,
        decimal originalNotional,
        decimal? currentNotional = null,
        decimal upfrontPayment = 0m)
    {
        if (string.IsNullOrWhiteSpace(lotId))
            throw new ArgumentException("Notional lot identifier must not be null or whitespace.", nameof(lotId));
        if (securityId == Guid.Empty)
            throw new ArgumentException("Notional lots must be linked to a Security Master identity.", nameof(securityId));
        if (originalNotional <= 0m)
            throw new ArgumentOutOfRangeException(nameof(originalNotional), originalNotional, "Original notional must be positive.");
        if (currentNotional is < 0m)
            throw new ArgumentOutOfRangeException(nameof(currentNotional), currentNotional, "Current notional cannot be negative.");
        if (currentNotional > originalNotional)
            throw new ArgumentOutOfRangeException(nameof(currentNotional), currentNotional, "Current notional cannot exceed the original notional.");
        if (upfrontPayment < 0m)
            throw new ArgumentOutOfRangeException(nameof(upfrontPayment), upfrontPayment, "Upfront payment cannot be negative.");

        LotId = lotId.Trim();
        SecurityId = securityId;
        EffectiveDate = effectiveDate;
        OriginalNotional = originalNotional;
        CurrentNotional = currentNotional ?? originalNotional;
        UpfrontPayment = upfrontPayment;
    }

    public string LotId { get; }

    /// <summary>A notional lot is the notional-denominated case of the shared lot shape: its size is the notional outstanding.</summary>
    decimal IUnitizedLot.Quantity => CurrentNotional;

    SecurityLotUnitKind IUnitizedLot.UnitKind => SecurityLotUnitKind.Notional;

    decimal IUnitizedLot.Multiplier => 1m;

    /// <summary>Security Master identity the contract's terms (legs, index, fixed rate, maturity) resolve from.</summary>
    public Guid SecurityId { get; }

    /// <summary>Date the contract became effective and the notional started accruing.</summary>
    public DateOnly EffectiveDate { get; }

    /// <summary>Notional at inception, in the contract currency.</summary>
    public decimal OriginalNotional { get; }

    /// <summary>Notional outstanding after any amortization, in the contract currency.</summary>
    public decimal CurrentNotional { get; }

    /// <summary>Upfront payment made at inception, in the contract currency; zero for a par swap.</summary>
    public decimal UpfrontPayment { get; }

    /// <summary>The share of the original notional still outstanding, the notional analogue of a pool factor.</summary>
    public decimal AmortizationFactor => CurrentNotional / OriginalNotional;

    /// <summary>Notional the lot has amortized away since inception.</summary>
    public decimal AmortizedNotional => OriginalNotional - CurrentNotional;

    /// <summary>The lot's cost basis: a notional lot's only cost is the upfront payment.</summary>
    public decimal CostBasis => UpfrontPayment;

    /// <summary>True once the notional has fully amortized and the contract no longer accrues.</summary>
    public bool IsFullyAmortized => CurrentNotional == 0m;

    /// <summary>The same lot with its notional replaced by <paramref name="currentNotional"/>, the way an amortization schedule steps it.</summary>
    public NotionalLot WithNotional(decimal currentNotional)
        => new(LotId, SecurityId, EffectiveDate, OriginalNotional, currentNotional, UpfrontPayment);

    /// <summary>
    /// Reduces the notional outstanding by <paramref name="amount"/>. A reduction larger than the
    /// notional outstanding is refused rather than silently floored.
    /// </summary>
    public NotionalLot Amortize(decimal amount)
    {
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amortization amount must be positive.");
        if (amount > CurrentNotional)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, $"Amortization amount exceeds the notional outstanding {CurrentNotional}.");

        return WithNotional(CurrentNotional - amount);
    }
}
