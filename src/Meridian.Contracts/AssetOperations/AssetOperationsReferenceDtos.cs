namespace Meridian.Contracts.AssetOperations;

/// <summary>
/// The reference view of a DirectLoan security, read from its relational terms projection. The
/// covenant and principal-instalment collections are exposed separately rather than nested here, so
/// a caller that only needs the loan's economics does not pay for its schedules.
/// </summary>
public sealed record DirectLoanReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string Borrower,
    DateOnly? Maturity,
    string? ReferenceIndex,
    decimal? SpreadBps,
    decimal? CurrentCouponRate,
    string? ResetFrequency,
    string? PricingSource,
    string PrimaryIdentifier,
    long Version);

/// <summary>
/// One covenant on a direct loan. <c>Threshold</c> carries the value exactly as contracted
/// ("4.5x", "2.00x fixed charge") — the canonical covenant term is a string, not a number.
/// </summary>
public sealed record DirectLoanCovenantDto(
    Guid SecurityId,
    int Ordinal,
    string CovenantType,
    string Threshold,
    string? Notes);

/// <summary>One contractual principal instalment on a direct loan.</summary>
public sealed record DirectLoanPrincipalPaymentDto(
    Guid SecurityId,
    int Ordinal,
    DateOnly PaymentDate,
    decimal Amount);

/// <summary>
/// The reference view of a StructuredCredit tranche, read from its relational terms projection.
/// <c>FactorScheduleReference</c> is the free-text trustee-report pointer; dated factors come from
/// the factor schedule.
/// </summary>
public sealed record StructuredCreditReferenceDto(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string Tranche,
    string? PoolId,
    string CollateralType,
    decimal OriginalFace,
    decimal? CurrentFactor,
    string CouponOrIndex,
    string? FactorScheduleReference,
    DateOnly? Maturity,
    string PrimaryIdentifier,
    long Version);

/// <summary>One dated pool-factor point: the outstanding factor effective on <c>AsOfDate</c>.</summary>
public sealed record StructuredCreditFactorPointDto(
    Guid SecurityId,
    int Ordinal,
    DateOnly AsOfDate,
    decimal Factor);
