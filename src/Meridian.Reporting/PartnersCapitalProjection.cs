using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

/// <summary>
/// One partner / capital-account row of a certified partners-capital roll-forward. All figures are
/// derived from the same certified ledger scope as the run and are carried on the reporting manifest
/// for the client-grade Capital Account Statement.
/// </summary>
public sealed record CertifiedPartnersCapitalAccount(
    string CapitalAccountName,
    string? InvestorId,
    decimal BeginningCapital,
    decimal Contributions,
    decimal Distributions,
    decimal AllocatedResult,
    decimal OtherMovements,
    decimal EndingCapital,
    decimal ReconciliationVariance);

/// <summary>
/// Certified partners-capital roll-forward projection
/// (opening → contributions → distributions → allocated result → other movements → ending) for the
/// Capital Account Statement template: per-account rows plus the fund total and a reconciliation
/// variance. Produced deterministically from the run's authoritative ledger scope and rendered into
/// the governed report-pack artifacts (whose bytes are hash-retained), so the numbers stay provable.
/// </summary>
public sealed record CertifiedPartnersCapitalProjection(
    DateTimeOffset PeriodStart,
    DateTimeOffset AsOf,
    decimal BeginningCapital,
    decimal Contributions,
    decimal Distributions,
    decimal AllocatedResult,
    decimal OtherMovements,
    decimal EndingCapital,
    decimal ReconciliationVariance,
    bool IsReconciled,
    IReadOnlyList<CertifiedPartnersCapitalAccount> Accounts);

/// <summary>
/// Sources the certified partners-capital roll-forward for a governed reporting run from the same
/// authoritative ledger scope the run certifies. Returns <c>null</c> when the run scope cannot
/// produce a roll-forward (e.g. no ledger book selected, unresolved period, or no partners' capital),
/// in which case the Capital Account Statement falls back to the generic certified-dataset
/// presentation. Implementations must be deterministic for a given scope + as-of.
/// </summary>
public interface IReportingPartnersCapitalSource
{
    Task<CertifiedPartnersCapitalProjection?> CaptureAsync(
        ReportingRunParametersDto parameters,
        CancellationToken cancellationToken = default);
}
