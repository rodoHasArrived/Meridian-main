namespace Meridian.Ledger;

/// <summary>
/// Renders a ledger financial report pack into binary delivery formats. The default
/// <see cref="BuiltInLedgerReportBinaryRenderer"/> produces valid, deterministic — if plain —
/// documents with no external dependencies; production hosts inject a client-grade renderer
/// (QuestPDF / ClosedXML) that implements this same seam.
/// </summary>
public interface ILedgerReportBinaryRenderer
{
    /// <summary>Renders the report pack as a PDF document.</summary>
    byte[] RenderPdf(LedgerFinancialReportPack reportPack);

    /// <summary>Renders the report pack as an XLSX workbook.</summary>
    byte[] RenderWorkbook(LedgerFinancialReportPack reportPack);
}
