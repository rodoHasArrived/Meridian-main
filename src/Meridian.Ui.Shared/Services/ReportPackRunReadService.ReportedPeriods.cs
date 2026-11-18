using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Which retained report versions count as reported figures, and the previous reported period
/// the portfolio P&amp;L slices compare with.
/// </summary>
public sealed partial class ReportPackRunReadService
{
    /// <summary>
    /// The report versions a figure may be read from: published or restated only, and only the
    /// highest version for each fund, fund account, template and period. Drafts, reports in review
    /// and superseded versions never contribute to a reported figure.
    /// </summary>
    internal static IReadOnlyList<ReportPackWorkflowRecordDto> SelectReportedRecords(
        IEnumerable<ReportPackWorkflowRecordDto> workflowRecords) =>
        workflowRecords
            .Where(static record => record.State is ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated)
            .Where(static record => record.LineProvenance is { Count: > 0 })
            .GroupBy(static record => (
                Fund: FundKey(record),
                Account: (record.FundAccountId ?? string.Empty).Trim().ToUpperInvariant(),
                Template: (record.TemplateId?.Name ?? string.Empty).Trim().ToUpperInvariant(),
                Period: (record.Period ?? string.Empty).Trim().ToUpperInvariant()))
            .Select(static group => group
                .OrderByDescending(static record => record.Version)
                .ThenByDescending(static record => record.UpdatedAt)
                .First())
            .ToArray();

    private static string FundKey(ReportPackWorkflowRecordDto record) =>
        string.IsNullOrWhiteSpace(record.FundProfileId) ? "unknown-fund" : record.FundProfileId.Trim();

    /// <summary>
    /// For one fund's reported records: the latest reported period's records, and the records of the
    /// period reported before it (the comparison baseline), with both period labels.
    /// </summary>
    internal static (IReadOnlyList<ReportPackWorkflowRecordDto> Current, IReadOnlyList<ReportPackWorkflowRecordDto> Prior, string? CurrentPeriod, string? PriorPeriod) SplitReportedPeriods(
        IReadOnlyList<ReportPackWorkflowRecordDto> fundRecords)
    {
        var periods = fundRecords
            .Select(static record => (record.Period ?? string.Empty).Trim())
            .Where(static period => period.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static period => period, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (periods.Length == 0)
        {
            return (fundRecords, [], null, null);
        }

        bool InPeriod(ReportPackWorkflowRecordDto record, string period) =>
            string.Equals((record.Period ?? string.Empty).Trim(), period, StringComparison.OrdinalIgnoreCase);
        var current = fundRecords.Where(record => InPeriod(record, periods[0])).ToArray();
        var prior = periods.Length > 1 ? fundRecords.Where(record => InPeriod(record, periods[1])).ToArray() : [];
        return (current, prior, periods[0], periods.Length > 1 ? periods[1] : null);
    }

    /// <summary>
    /// The P&amp;L the previous reported period showed, summed across funds, as the baseline for the
    /// P&amp;L slices. Zero sources means no prior published report exists to compare with.
    /// </summary>
    internal static (decimal TotalPnl, int SourceCount, IReadOnlyList<string> Periods) BuildPriorReportedPnl(
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords)
    {
        var total = 0m;
        var count = 0;
        var periods = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in SelectReportedRecords(workflowRecords).GroupBy(FundKey, StringComparer.OrdinalIgnoreCase))
        {
            var (_, prior, _, priorPeriod) = SplitReportedPeriods(group.ToArray());
            var sources = prior
                .SelectMany(static record => record.LineProvenance ?? [])
                .Select(TryClassifyPortfolioLine)
                .Where(static source => source is not null)
                .Select(static source => source!)
                .ToArray();
            if (sources.Length == 0)
            {
                continue;
            }

            var realized = SumSources(sources, PortfolioLineMetric.RealizedPnl);
            var unrealized = SumSources(sources, PortfolioLineMetric.UnrealizedPnl);
            var totalPnl = SumSources(sources, PortfolioLineMetric.TotalPnl);
            total += totalPnl == 0m ? realized + unrealized : totalPnl;
            count += sources.Length;
            if (priorPeriod is not null)
            {
                periods.Add(priorPeriod);
            }
        }

        return (total, count, periods.ToArray());
    }
}
