using Meridian.Execution.Sdk;

namespace Meridian.Risk;

/// <summary>
/// Exposure taken by orders that are being validated or have been approved but not yet settled by
/// the caller.
/// </summary>
/// <remarks>
/// <para>
/// Exposure snapshots describe filled positions, so an order under evaluation is invisible to any
/// order evaluated beside it. Nothing serialises pre-trade validation — the composite validator
/// holds no lock and the order manager validates each submission independently — so concurrent
/// submissions each observe the same headroom below the ceiling and all pass, jointly breaching a
/// limit none of them breached alone. Routed orders cannot be recalled, so that breach is only
/// unwound at whatever the market charges. This ledger makes the check and the consumption atomic,
/// which is the guarantee <see cref="IReservingRiskRule"/> exists to provide.
/// </para>
/// <para>
/// <b>Scope of the guarantee.</b> A reservation is held from evaluation until the caller settles
/// it, which happens once the order has been routed or provably has not been. Both outcomes
/// release it: a rolled-back order never reached a venue and carries no exposure, and a committed
/// order's exposure becomes the portfolio's to account for. The window between routing and the
/// fill appearing in the snapshot is therefore still uncovered — closing it needs a release driven
/// by order-terminal state rather than by settlement, which no seam currently provides. That
/// window is unchanged by this type; what it removes is concurrent evaluations spending the same
/// headroom, which is the part that scales with submission rate.
/// </para>
/// <para>
/// <b>Account qualification.</b> The portfolio-wide gross ceiling counts every account's in-flight
/// notional, because gross is a property of the whole book. Direction-aware projection consults
/// the order's <em>own</em> account only: with a long book in one account and a short book in
/// another, another account's pending order says nothing about whether this one increases or
/// decreases risk. Netting across accounts here would repeat the mistake
/// <see cref="SymbolExposure.ResolveSignedExposureFor"/> already refuses to make on filled
/// positions — letting a flat fund sell "against" another fund's long and project a near-zero book.
/// Orders with no fund account share one unattributed pool: they count toward the portfolio total
/// like any other, and never toward an attributed account's signed exposure.
/// </para>
/// </remarks>
public sealed class ExposureReservationLedger
{
    private static readonly Guid UnattributedAccount = Guid.Empty;

    private readonly object _sync = new();
    private readonly Dictionary<Guid, AccountInFlight> _byAccount = [];

    private decimal _totalGrossInFlight;

    /// <summary>Total in-flight gross notional across every account.</summary>
    public decimal TotalGrossInFlight
    {
        get { lock (_sync) { return _totalGrossInFlight; } }
    }

    /// <summary>Reservations currently held, for diagnostics and tests.</summary>
    public int HeldReservationCount
    {
        get { lock (_sync) { return _byAccount.Values.Sum(static entry => entry.Count); } }
    }

    /// <summary>
    /// Gross in-flight notional for one account, or the unattributed pool when
    /// <paramref name="fundAccountId"/> is <see langword="null"/>.
    /// </summary>
    public decimal GrossInFlightFor(Guid? fundAccountId)
    {
        lock (_sync)
        {
            return _byAccount.TryGetValue(fundAccountId ?? UnattributedAccount, out var entry)
                ? entry.Gross
                : 0m;
        }
    }

    /// <summary>
    /// Signed in-flight notional for one account. Only the order's own account may be consulted
    /// for direction-aware projection; see the type remarks.
    /// </summary>
    public decimal SignedInFlightFor(Guid? fundAccountId)
    {
        lock (_sync)
        {
            return _byAccount.TryGetValue(fundAccountId ?? UnattributedAccount, out var entry)
                ? entry.Signed
                : 0m;
        }
    }

    /// <summary>
    /// Evaluates a projection against the ceiling and, when it fits, takes the order's exposure in
    /// the same atomic step.
    /// </summary>
    /// <param name="fundAccountId">Owning account; <see langword="null"/> uses the unattributed pool.</param>
    /// <param name="grossNotional">Absolute notional the order adds to the book.</param>
    /// <param name="signedNotional">Signed notional, negative when the order reduces exposure.</param>
    /// <param name="fits">
    /// Given the account's in-flight signed notional and the portfolio-wide in-flight gross, decides
    /// whether this order still fits. Invoked under the ledger lock, so it must not block or call
    /// back into the ledger.
    /// </param>
    /// <returns>
    /// The reservation when the order fit and its exposure was taken; <see langword="null"/> when it
    /// did not, in which case nothing was taken.
    /// </returns>
    public IRiskReservation? TryReserve(
        Guid? fundAccountId,
        decimal grossNotional,
        decimal signedNotional,
        Func<decimal, decimal, bool> fits)
    {
        ArgumentNullException.ThrowIfNull(fits);

        var key = fundAccountId ?? UnattributedAccount;
        lock (_sync)
        {
            var entry = _byAccount.TryGetValue(key, out var existing) ? existing : default;

            // Evaluated inside the lock: releasing it between the decision and the take is exactly
            // the split that lets two orders spend one order's worth of headroom.
            if (!fits(entry.Signed, _totalGrossInFlight))
                return null;

            _byAccount[key] = new AccountInFlight(
                entry.Gross + grossNotional,
                entry.Signed + signedNotional,
                entry.Count + 1);
            _totalGrossInFlight += grossNotional;
        }

        return new ExposureReservation(this, key, grossNotional, signedNotional);
    }

    /// <summary>
    /// Returns capacity taken by <see cref="TryReserve"/>. Called only by the reservation handle,
    /// which guarantees exactly one release.
    /// </summary>
    private void Release(Guid key, decimal grossNotional, decimal signedNotional)
    {
        lock (_sync)
        {
            if (!_byAccount.TryGetValue(key, out var entry))
                return;

            var remaining = new AccountInFlight(
                entry.Gross - grossNotional,
                entry.Signed - signedNotional,
                entry.Count - 1);

            // Drop the account with its last reservation so an idle book cannot accumulate
            // entries whose residual arithmetic drifts away from zero.
            if (remaining.Count <= 0)
                _byAccount.Remove(key);
            else
                _byAccount[key] = remaining;

            _totalGrossInFlight -= grossNotional;
        }
    }

    private readonly record struct AccountInFlight(decimal Gross, decimal Signed, int Count);

    /// <summary>
    /// Handle for one order's in-flight exposure. Settling releases it under either outcome — see
    /// the scope note in the type remarks — and both settlements are idempotent, so a cleanup path
    /// may settle unconditionally.
    /// </summary>
    private sealed class ExposureReservation(
        ExposureReservationLedger owner,
        Guid accountKey,
        decimal grossNotional,
        decimal signedNotional) : IRiskReservation
    {
        private int _settled;

        /// <inheritdoc />
        public void Commit() => Settle();

        /// <inheritdoc />
        public void Rollback() => Settle();

        private void Settle()
        {
            if (Interlocked.Exchange(ref _settled, 1) != 0)
                return;

            owner.Release(accountKey, grossNotional, signedNotional);
        }
    }
}
