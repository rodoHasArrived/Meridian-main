namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Ingress bounds for statement parsing (PRD-010). <see cref="StatementImportService"/> enforces
/// <see cref="MaxDocumentBytes"/> and <see cref="MaxRecords"/> for every connector it resolves, so no
/// format can exceed them; the camt.053 and BAI2 connectors additionally enforce them while streaming,
/// which refuses a hostile payload partway through instead of after it is already built.
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
/// <param name="MaxDocumentLines">
/// Raw lines a line-oriented parser may walk. Deliberately its own bound rather than a multiple of
/// MaxRecords: envelope lines are unbounded relative to records, because a legal BAI2 file may carry any
/// number of 03/49 account sections and 02/98 groups that produce no record at all, so no multiplier of
/// the record cap can avoid refusing some legal document. This budget exists to stop unbounded allocation
/// from unrecognized record types, not to enforce the record cap - the record cap already does that - so
/// it is set far above any real statement and only bites on abuse.
/// </param>
/// <param name="MaxDiagnostics">
/// Parse issues a document may retain. Its own bound rather than part of MaxRecords for the same reason
/// MaxDocumentLines is: a diagnostic is not a row. A rejected row produces an issue and no record, so a
/// file can retain far more diagnostics than records, and charging them to the record allowance would
/// refuse a diagnostic-heavy file with a message about a row limit it never approached. Issues are
/// retained evidence - they are held in the parse result and projected into the preview exactly like
/// records are - so they need a ceiling; this one sits far above any statement worth importing, since
/// every diagnostic past the first few thousand describes a row an operator is not going to reconcile
/// by hand.
/// </param>
public sealed record StatementIngressLimits(
    long MaxDocumentBytes,
    int MaxRecords,
    int MaxLineBytes,
    int MaxNestingDepth,
    int MaxSubtreeNodes = 50_000,
    int MaxParseNodes = 500_000,
    int MaxDocumentLines = 2_000_000,
    int MaxDiagnostics = 25_000)
{
    /// <summary>
    /// Default bounds. <see cref="MaxDocumentBytes"/> is <see cref="StatementConnectorLimits.MaxFileBytes"/>,
    /// the statement-specific 20 MiB cap the workstation statement-connector endpoint and the CLI
    /// import/validate commands already enforce — NOT the general 5 MiB data-upload cap. IB Flex XML
    /// exports routinely exceed 5 MiB, which is why statements carry their own larger bound; anchoring
    /// this default to the smaller cap would refuse every 5–20 MiB statement those paths accept. The
    /// remaining bounds are sized well above any real bank statement — a camt.053 or BAI2 file carrying
    /// more than 250,000 canonical rows, a BAI2 line over 64 KiB, or XML nested deeper than 64 levels is
    /// malformed or hostile, not large. <see cref="MaxSubtreeNodes"/> bounds one materialized XML subtree:
    /// depth alone does not, because a shallow element with millions of siblings stays inside the nesting
    /// bound while still expanding far beyond its own byte size once it becomes an object graph.
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

    /// <summary>
    /// Issue code for a document carrying more raw lines than the allocation bound allows. Distinct from
    /// <see cref="TooManyRecordsCode"/> on purpose: blank lines map to no canonical row but still cost a
    /// list entry, so a file can breach the line bound while being nowhere near the record bound.
    /// Reporting that as record overflow told the operator something untrue about their file.
    /// </summary>
    public const string TooManyLinesCode = "STATEMENT_TOO_MANY_LINES";

    /// <summary>
    /// Issue code for a document that retained more parse issues than the allocation bound allows.
    /// Distinct from <see cref="TooManyRecordsCode"/> for the same reason <see cref="TooManyLinesCode"/>
    /// is: a row rejected for an unparseable date retains an issue and no record, so a file can carry
    /// tens of thousands of diagnostics while producing almost no canonical rows. Reporting that as
    /// record overflow would tell the operator something untrue about their file.
    /// </summary>
    public const string TooManyDiagnosticsCode = "STATEMENT_TOO_MANY_DIAGNOSTICS";

    /// <summary>Issue code for a payload nested deeper than the cap allows.</summary>
    public const string NestingTooDeepCode = "STATEMENT_NESTING_TOO_DEEP";

    /// <summary>Issue code for a single XML element that expands past the per-subtree node cap.</summary>
    public const string SubtreeTooLargeCode = "STATEMENT_SUBTREE_TOO_LARGE";

    /// <summary>
    /// Whole-document retained-node cap, deliberately <b>not</b> derived from <see cref="MaxRecords"/>.
    /// Deriving it made the bound vary with an unrelated knob and, at the default record allowance, put it
    /// above what <see cref="MaxDocumentBytes"/> can even produce - a bound that cannot be reached is not
    /// a bound.
    ///
    /// The value is a deliberate trade, and worth stating plainly. Node count alone cannot separate a
    /// hostile payload from a very large legitimate one: a single entry carrying a million uniquely named
    /// compact leaves and a hundred thousand ordinary entries of ten leaves each reach a similar total.
    /// Each retained leaf costs a name string, a value string, a slot in <c>OfxNode.Leaves</c>, another
    /// slot once <c>FlattenLeaves</c> copies it into the entry dictionary, and an entry in the
    /// detected-column set - on the order of a couple of hundred bytes for a few bytes of source. A
    /// ceiling set above the legitimate maximum therefore never fires, so this one is set below it: a
    /// statement above roughly fifty thousand rich entries is refused rather than expanded, with a message
    /// that names the two remedies. Refusing an enormous-but-honest file, actionably, is the better error
    /// than expanding a hostile one silently.
    /// </summary>
    public const string TooManyNodesCode = "STATEMENT_TOO_MANY_NODES";

    public StatementParseIssue SubtreeTooLarge() => StatementParseIssue.Error(
        SubtreeTooLargeCode,
        $"A single statement element expands to more than {MaxSubtreeNodes} nodes, above the ingress limit; " +
        "the file is malformed or not the declared format.");

    public StatementParseIssue TooManyDiagnostics() => StatementParseIssue.Error(
        TooManyDiagnosticsCode,
        $"The statement produced more than {MaxDiagnostics} parse issues. A file that fails this many rows " +
        "is malformed at a scale no operator can reconcile row by row; correct the export or the mapping " +
        "profile and re-import rather than reviewing the diagnostics individually.");

    public StatementParseIssue TooManyNodes() => StatementParseIssue.Error(
        TooManyNodesCode,
        $"The statement document retains more than {MaxParseNodes} parsed nodes, above the ingress allocation " +
        "limit; a document this tag-dense expands far beyond its own byte size once parsed. Split the " +
        "statement into smaller files, or raise the configured limit deliberately before importing.");

    public StatementParseIssue DocumentTooLarge(long actualBytes) => StatementParseIssue.Error(
        DocumentTooLargeCode,
        $"The statement document is {actualBytes} bytes, above the {MaxDocumentBytes}-byte ingress limit. " +
        "Split the statement into smaller files, or raise the configured limit deliberately before importing.");

    public StatementParseIssue TooManyRecords() => StatementParseIssue.Error(
        TooManyRecordsCode,
        $"The statement produced more than {MaxRecords} records, above the ingress limit. " +
        "Split the statement into smaller files, or raise the configured limit deliberately before importing.");

    public StatementParseIssue TooManyLines(int lineBound) => StatementParseIssue.Error(
        TooManyLinesCode,
        $"The statement document contains more than {lineBound} lines, above the ingress allocation limit. " +
        "Blank lines count toward this bound because each one still costs memory to discover, even though " +
        "none of them produces a record. Split the statement into smaller files, or raise the configured " +
        "limit deliberately before importing.");

    public StatementParseIssue LineTooLong(int rowNumber) => StatementParseIssue.Error(
        LineTooLongCode,
        $"A statement line exceeds the {MaxLineBytes}-byte ingress limit; the file is malformed or not the declared format.",
        rowNumber);

    public StatementParseIssue NestingTooDeep() => StatementParseIssue.Error(
        NestingTooDeepCode,
        $"The statement document nests deeper than the {MaxNestingDepth}-level ingress limit; " +
        "the file is malformed or not the declared format.");
}
