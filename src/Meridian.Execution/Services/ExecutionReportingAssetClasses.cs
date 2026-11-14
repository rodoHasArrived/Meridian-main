namespace Meridian.Execution.Services;

/// <summary>
/// Reporting asset-class tags stamped on <see cref="ExecutionJournalEntry.ReportingAssetClass"/>.
/// The values are persisted in the execution journal, so they must never change.
/// </summary>
public static class ExecutionReportingAssetClasses
{
    /// <summary>A USD-denominated US equity order whose economics the journal reports.</summary>
    public const string UsdUsEquity = "USD_US_EQUITY";
}
