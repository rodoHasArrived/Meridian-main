namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Ingress bounds every statement connector enforces before it materializes a payload (PRD-010).
/// A connector that decodes a whole document and builds a full parse tree lets a caller-supplied
/// <see cref="StatementSourceDocument"/> size the parse rather than the operator, so the limits are
/// checked while parsing rather than after: an oversize document is refused before its bytes are
/// copied, and a document within the byte cap can still be refused once it exceeds the record,
/// line, or nesting bound.
/// </summary>
/// <remarks>
/// The transport-level upload and CLI caps do not cover this seam. A <see cref="StatementSourceDocument"/>
/// reaches <see cref="StatementImportService"/> from the workstation upload endpoint, the CLI, a
/// scheduled fetch, and directly from in-process callers, so the bound has to live where every one of
/// those paths converges.
/// </remarks>
public sealed record StatementIngressLimits(
    long MaxDocumentBytes,
    int MaxRecords,
    int MaxLineBytes,
    int MaxNestingDepth)
{
    /// <summary>
    /// Default bounds. <see cref="MaxDocumentBytes"/> matches the 5 MiB workstation upload cap so a
    /// file accepted by the endpoint is not then refused by the connector, and so the connector cap
    /// is not the looser of the two. The remaining bounds are sized well above any real bank
    /// statement — a camt.053 or BAI2 file carrying more than 250,000 canonical rows, a BAI2 line
    /// over 64 KiB, or XML nested deeper than 64 levels is malformed or hostile, not large.
    /// </summary>
    public static StatementIngressLimits Default { get; } = new(
        MaxDocumentBytes: 5L * 1024 * 1024,
        MaxRecords: 250_000,
        MaxLineBytes: 64 * 1024,
        MaxNestingDepth: 64);

    /// <summary>Issue code for a document refused before parsing because it exceeds the byte cap.</summary>
    public const string DocumentTooLargeCode = "STATEMENT_DOCUMENT_TOO_LARGE";

    /// <summary>Issue code for a document that produced more canonical records than the cap allows.</summary>
    public const string TooManyRecordsCode = "STATEMENT_TOO_MANY_RECORDS";

    /// <summary>Issue code for a line longer than the cap allows.</summary>
    public const string LineTooLongCode = "STATEMENT_LINE_TOO_LONG";

    /// <summary>Issue code for a payload nested deeper than the cap allows.</summary>
    public const string NestingTooDeepCode = "STATEMENT_NESTING_TOO_DEEP";

    public StatementParseIssue DocumentTooLarge(long actualBytes) => StatementParseIssue.Error(
        DocumentTooLargeCode,
        $"The statement document is {actualBytes} bytes, above the {MaxDocumentBytes}-byte ingress limit. " +
        "Split the statement into smaller files, or raise the configured limit deliberately before importing.");

    public StatementParseIssue TooManyRecords() => StatementParseIssue.Error(
        TooManyRecordsCode,
        $"The statement produced more than {MaxRecords} records, above the ingress limit. " +
        "Split the statement into smaller files, or raise the configured limit deliberately before importing.");

    public StatementParseIssue LineTooLong(int rowNumber) => StatementParseIssue.Error(
        LineTooLongCode,
        $"A statement line exceeds the {MaxLineBytes}-byte ingress limit; the file is malformed or not the declared format.",
        rowNumber);

    public StatementParseIssue NestingTooDeep() => StatementParseIssue.Error(
        NestingTooDeepCode,
        $"The statement document nests deeper than the {MaxNestingDepth}-level ingress limit; " +
        "the file is malformed or not the declared format.");
}
