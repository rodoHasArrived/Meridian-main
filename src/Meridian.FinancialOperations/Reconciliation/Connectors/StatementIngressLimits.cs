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
/// those paths converges. It matches, rather than tightens, what those paths already accept: this is a
/// floor under the uncovered callers, not a new ceiling on the covered ones.
/// </remarks>
public sealed record StatementIngressLimits(
    long MaxDocumentBytes,
    int MaxRecords,
    int MaxLineBytes,
    int MaxNestingDepth)
{
    /// <summary>
    /// Default bounds. <see cref="MaxDocumentBytes"/> is <see cref="StatementConnectorLimits.MaxFileBytes"/>,
    /// the statement-specific 20 MiB cap the workstation statement-connector endpoint and the CLI
    /// import/validate commands already enforce — NOT the general 5 MiB data-upload cap. IB Flex XML
    /// exports routinely exceed 5 MiB, which is why statements carry their own larger bound; anchoring
    /// this default to the smaller cap would refuse every 5–20 MiB statement those paths accept. The
    /// remaining bounds are sized well above any real bank statement — a camt.053 or BAI2 file carrying
    /// more than 250,000 canonical rows, a BAI2 line over 64 KiB, or XML nested deeper than 64 levels is
    /// malformed or hostile, not large.
    /// </summary>
    public static StatementIngressLimits Default { get; } = new(
        MaxDocumentBytes: StatementConnectorLimits.MaxFileBytes,
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
