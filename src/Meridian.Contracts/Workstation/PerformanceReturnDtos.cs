namespace Meridian.Contracts.Workstation;

/// <summary>
/// One labeled return figure produced by the shared return kernel (W10-PERF-001). A figure always
/// names its convention, status, period, currency, and source; <see cref="Value"/> is null whenever
/// no single number may be shown as the return.
/// </summary>
/// <param name="Convention">
/// <c>TimeWeighted</c>, <c>LinkedModifiedDietz</c> (labeled approximation), <c>ModifiedDietz</c>,
/// or <c>MoneyWeighted</c>.
/// </param>
/// <param name="Status">
/// <c>Reported</c>, <c>ProForma</c> (rests on unposted activity), or <c>ReviewRequired</c>
/// (incomplete inputs, mixed currency, or no single defensible rate).
/// </param>
/// <param name="Currency">Currency the figure is measured in.</param>
/// <param name="PeriodStart">First valuation date of the measured period.</param>
/// <param name="PeriodEnd">Last valuation date of the measured period.</param>
/// <param name="Value">Period return as a fraction (0.023 = 2.3%), not annualized.</param>
/// <param name="AnnualizedValue">Annualized rate; money-weighted figures only.</param>
/// <param name="SubPeriodCount">Linked sub-periods for time-weighted figures; 1 otherwise.</param>
/// <param name="CandidateRates">Every money-weighted rate found; more than one blocks the figure.</param>
/// <param name="Source">
/// Where the inputs came from, for example <c>brokerage-balance-snapshots</c>. Figures that are
/// not derived from Meridian's posted ledger say so here.
/// </param>
/// <param name="Reasons">Why the figure carries its status or convention.</param>
public sealed record PerformanceReturnFigureDto(
    string Convention,
    string Status,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal? Value,
    decimal? AnnualizedValue,
    int SubPeriodCount,
    IReadOnlyList<decimal> CandidateRates,
    string Source,
    IReadOnlyList<string> Reasons);
