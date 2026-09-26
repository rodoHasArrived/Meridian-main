namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Read access to the DirectLoan relational terms projection. The asset-specific-terms JSONB blob
/// stays the source of truth; this store answers the portfolio-shaped questions the blob cannot —
/// which loans a borrower carries, which mature in a window, and which principal instalments fall
/// due — without parsing every security's document.
/// </summary>
public interface IDirectLoanReferenceProjectionStore
{
    Task<DirectLoanProjectionRow?> GetDirectLoanAsync(Guid securityId, CancellationToken ct = default);

    Task<IReadOnlyList<DirectLoanProjectionRow>> GetByBorrowerAsync(string borrower, CancellationToken ct = default);

    Task<IReadOnlyList<DirectLoanProjectionRow>> GetByReferenceIndexAsync(string referenceIndex, CancellationToken ct = default);

    Task<IReadOnlyList<DirectLoanProjectionRow>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>The loan's covenants in the order the terms document declares them.</summary>
    Task<IReadOnlyList<DirectLoanCovenantRow>> GetCovenantsAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>The loan's contractual principal instalments in declared order.</summary>
    Task<IReadOnlyList<DirectLoanPrincipalPaymentRow>> GetPrincipalScheduleAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Every projected principal instalment falling in <paramref name="from"/>..<paramref name="to"/>
    /// inclusive, across loans, ordered by payment date.
    /// </summary>
    Task<IReadOnlyList<DirectLoanPrincipalPaymentRow>> GetPrincipalPaymentsDueAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed record DirectLoanProjectionRow(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string Borrower,
    DateOnly? MaturityDate,
    string? ReferenceIndex,
    decimal? SpreadBps,
    decimal? CurrentCouponRate,
    string? ResetFrequency,
    string? PricingSource,
    string PrimaryIdentifierValue,
    long Version);

/// <summary>
/// One projected covenant. <c>Threshold</c> carries the value exactly as contracted ("4.5x",
/// "2.00x fixed charge") — a string, because the canonical covenant term is one; coercing it to a
/// number would drop every ratio covenant.
/// </summary>
public sealed record DirectLoanCovenantRow(
    Guid SecurityId,
    int Ordinal,
    string CovenantType,
    string Threshold,
    string? Notes);

public sealed record DirectLoanPrincipalPaymentRow(
    Guid SecurityId,
    int Ordinal,
    DateOnly PaymentDate,
    decimal Amount);
