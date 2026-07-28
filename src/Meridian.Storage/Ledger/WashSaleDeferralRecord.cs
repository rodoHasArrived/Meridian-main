using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>
/// A retained wash-sale deferral: one loss-generating disposal capitalized
/// <see cref="DisallowedAmount"/> into one replacement lot. Retention is what makes the deferral
/// survive past the period it was computed in — a later sale of the replacement lot replays these
/// rows as basis adjustments, so the deferred loss is finally recognized at the right time and with
/// the carried holding period, instead of disappearing.
/// </summary>
/// <param name="DisposalMutationBatchId">The atomic tax-lot batch that recorded the disposal.</param>
/// <param name="DisposalAccount">The account that sold at a loss (not necessarily the account holding the replacement).</param>
/// <param name="ReplacementTaxLotRecordId">Durable identity of the lot whose basis absorbed the loss.</param>
/// <param name="MatchedReplacementQuantity">
/// Replacement shares this disposal matched against, retained so a rebuilt projection reports the
/// deferral that was actually booked rather than one re-derived from later lot history.
/// </param>
/// <param name="HoldingPeriodCarryDate">Holding-period start carried onto the replacement under IRC §1223(3).</param>
/// <param name="PolicyId">The tax-lot policy revision in force when the deferral was computed.</param>
/// <param name="WindowDays">Replacement window the deferral was matched under, retained for audit.</param>
/// <param name="Scope">Account scope the replacement was matched under, retained for audit.</param>
public sealed record WashSaleDeferralRecord(
    Guid DeferralId,
    Guid LedgerBookId,
    Guid DisposalMutationBatchId,
    Guid SecurityId,
    DateOnly SaleDate,
    LedgerAccount DisposalAccount,
    Guid ReplacementTaxLotRecordId,
    string ReplacementLotId,
    decimal DisallowedAmount,
    decimal MatchedReplacementQuantity,
    DateOnly HoldingPeriodCarryDate,
    string PolicyId,
    int WindowDays,
    WashSaleReplacementScope Scope,
    DateTimeOffset RecordedAt);

/// <summary>
/// Durable retention for wash-sale deferrals.
/// </summary>
public interface IWashSaleDeferralStore
{
    /// <summary>
    /// Retains the deferrals produced by one disposal. Idempotent per
    /// (<see cref="WashSaleDeferralRecord.DisposalMutationBatchId"/>,
    /// <see cref="WashSaleDeferralRecord.ReplacementTaxLotRecordId"/>) so a replayed batch cannot
    /// capitalize the same loss twice.
    /// </summary>
    Task SaveWashSaleDeferralsAsync(
        IReadOnlyList<WashSaleDeferralRecord> deferrals,
        CancellationToken ct = default);

    /// <summary>Lists deferrals recorded against disposals in a book, ordered by sale date.</summary>
    Task<IReadOnlyList<WashSaleDeferralRecord>> ListWashSaleDeferralsAsync(
        Guid ledgerBookId,
        DateOnly fromSaleDate,
        DateOnly toSaleDate,
        CancellationToken ct = default);
}
