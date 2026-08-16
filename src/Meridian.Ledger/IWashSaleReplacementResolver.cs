namespace Meridian.Ledger;

/// <summary>
/// The question a wash-sale replacement search answers: for this loss-generating disposal, which
/// acquisitions of the same security fall inside the policy window, and which prior deferrals have
/// already been capitalized into the lots being relieved?
/// </summary>
/// <param name="LedgerBookId">The disposing ledger book; also the search boundary for <see cref="WashSaleReplacementScope.LedgerBook"/>.</param>
/// <param name="DisposingAccount">The account the disposal relieves lots from.</param>
/// <param name="SecurityId">Security Master identity of the sold security. Replacements must be substantially identical.</param>
/// <param name="SaleDate">Sale date the ±<see cref="WashSalePolicy.WindowDays"/> window centres on.</param>
/// <param name="Policy">The policy in force for this sale; supplies the window and the account scope.</param>
/// <param name="RelievedLotIds">
/// Lots being relieved by this disposal. They are excluded from the replacement candidates — a lot
/// cannot be its own replacement, and matching one would disallow a loss against itself.
/// </param>
public sealed record WashSaleReplacementQuery(
    Guid LedgerBookId,
    LedgerAccount DisposingAccount,
    Guid SecurityId,
    DateOnly SaleDate,
    WashSalePolicy Policy,
    IReadOnlyList<string> RelievedLotIds);

/// <summary>
/// What a <see cref="IWashSaleReplacementResolver"/> found for one disposal.
/// </summary>
/// <param name="Replacements">
/// Candidate replacement acquisitions inside the policy window. The relief engine still filters
/// these by window and security, so a resolver may return a slightly wider set without changing the
/// computed deferral.
/// </param>
/// <param name="PriorDeferrals">
/// Wash-sale deferrals already capitalized into the lots this disposal is relieving, expressed as
/// <see cref="LedgerTaxLotBasisAdjustmentKind.WashSale"/> basis adjustments. Feeding these back in
/// is what makes a deferral survive to the sale that finally recognizes it: the replacement lot is
/// relieved at its increased basis and with the carried holding period, rather than at the raw
/// price it was bought for.
/// </param>
public sealed record WashSaleReplacementLookup(
    IReadOnlyList<WashSaleReplacementAcquisition> Replacements,
    IReadOnlyList<LedgerTaxLotBasisAdjustment> PriorDeferrals)
{
    /// <summary>Nothing found; relief proceeds exactly as if wash-sale matching were disabled.</summary>
    public static WashSaleReplacementLookup Empty { get; } = new([], []);

    /// <summary>True when neither a replacement nor a prior deferral affects this disposal.</summary>
    public bool IsEmpty => Replacements.Count == 0 && PriorDeferrals.Count == 0;
}

/// <summary>
/// Resolves the replacement acquisitions a wash-sale evaluation needs. This is the seam that was
/// missing between the relief engine and durable tax-lot history: <see cref="LedgerTaxLotReliefProjector"/>
/// has always been able to compute a deferral, but only if someone handed it the replacements to
/// match against.
/// <para>
/// Implementations run one indexed query per loss-generating disposal, so the search is scoped by
/// <see cref="WashSaleReplacementQuery.SecurityId"/> and the policy window rather than scanning
/// lot history.
/// </para>
/// </summary>
public interface IWashSaleReplacementResolver
{
    /// <summary>
    /// Finds replacement acquisitions and prior deferrals for <paramref name="query"/>. Returns
    /// <see cref="WashSaleReplacementLookup.Empty"/> when the policy does not govern the sale date.
    /// </summary>
    Task<WashSaleReplacementLookup> ResolveAsync(WashSaleReplacementQuery query, CancellationToken ct = default);
}
