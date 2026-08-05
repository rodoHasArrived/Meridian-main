namespace Meridian.Documents;

/// <summary>
/// A labelled key/value pair shown in a report document's branded header block.
/// </summary>
public sealed record ReportDocumentField(string Label, string Value);

/// <summary>
/// One titled table inside a report document. <see cref="Rows"/> are string cells already formatted
/// by the caller; the renderer performs no numeric parsing so exact source values are preserved.
/// </summary>
public sealed record ReportDocumentTable(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// Presentation-neutral report model rendered to client-grade PDF/XLSX by
/// <see cref="ClientGradeReportRenderer"/>. It intentionally has no dependency on the ledger report
/// pack or the reporting manifest so any producer can populate it. All content is pre-formatted
/// strings so rendering is a pure, deterministic function of this model.
/// </summary>
public sealed record ReportDocumentModel(
    string Title,
    IReadOnlyList<ReportDocumentField> HeaderFields,
    IReadOnlyList<ReportDocumentTable> Tables,
    string? Subtitle = null,
    string? FooterNote = null);
