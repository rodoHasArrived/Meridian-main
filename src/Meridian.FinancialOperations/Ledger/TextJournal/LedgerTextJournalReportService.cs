namespace Meridian.FinancialOperations.LedgerTextJournal;

/// <summary>
/// Parses ledger text journals and renders reports with supplied report options.
/// </summary>
public sealed class LedgerTextJournalReportService
{
    public LedgerTextJournalDocument Parse(IReadOnlyList<string> lines)
        => LedgerTextJournalParser.Parse(lines);

    public string RenderReport(IReadOnlyList<string> lines, LedgerTextReportOptions options)
        => RenderReport(Parse(lines), options);

    public string RenderReport(LedgerTextJournalDocument document, LedgerTextReportOptions options)
        => LedgerTextReportRenderer.Render(document, options);
}
