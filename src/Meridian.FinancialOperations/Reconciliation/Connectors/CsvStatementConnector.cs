using System.Text;
using Meridian.Contracts.Integrity;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Declarative, profile-driven CSV connector. Any custodian or broker CSV layout can be
/// onboarded by authoring a mapping-profile document — no code change required. Handles
/// RFC-4180 quoting, delimiter variants, BOMs, header aliases with per-column confidence,
/// and mixed-kind files (positions, transactions, cash balances, fees, dividends in one
/// statement).
/// </summary>
public sealed class CsvStatementConnector(
    StatementMappingProfileCatalog catalog,
    StatementIngressLimits? ingressLimits = null) : IStatementConnector
{
    private readonly StatementIngressLimits _ingressLimits = ingressLimits ?? StatementIngressLimits.Default;

    public const string ConnectorId = "csv-mapped";

    private static readonly char[] CandidateDelimiters = [',', ';', '\t', '|'];

    public StatementConnectorDescriptor Descriptor { get; } = new(
        ConnectorId,
        "Custodian/Broker CSV (mapping profile)",
        [".csv", ".txt"],
        SupportsFileImport: true,
        SupportsRemoteFetch: false,
        RequiresMappingProfile: true,
        DefaultProfileId: StatementMappingProfileRegistry.CanonicalCsvV1ProfileId);

    private static StatementParseResult CsvIngressRefusal(StatementParseIssue issue)
        => new(
            ConnectorId,
            ProfileId: null,
            [],
            ColumnMappings: [],
            [],
            [issue],
            new StatementFormatFingerprint(string.Empty, [], "csv-mapped"));

    public bool CanHandle(StatementSourceDocument document)
    {
        var extension = Path.GetExtension(document.FileName);
        return Descriptor.FileExtensions.Any(candidate =>
            string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<StatementParseResult> ParseAsync(StatementSourceDocument document, CancellationToken ct = default)
    {
        var issues = new List<StatementParseIssue>();
        var profileId = string.IsNullOrWhiteSpace(document.MappingProfileId)
            ? Descriptor.DefaultProfileId!
            : document.MappingProfileId.Trim();
        // Refuse before decoding, as camt.053, BAI2 and IB Flex already do. StatementImportService
        // checks this cap too, but ParseAsync is public connector API reached directly by in-process
        // callers and by these tests, and the decode below materializes the whole payload as a UTF-16
        // string: a document whose single leaf is enormous stays under the node, entry and depth bounds
        // while allocating twice its byte size. The bound has to be checked where the allocation is.
        if (document.Content.Length > _ingressLimits.MaxDocumentBytes)
        {
            issues.Add(_ingressLimits.DocumentTooLarge(document.Content.Length));
            return EmptyResult(profileId, issues, []);
        }

        var profile = await catalog.FindAsync(profileId, ct).ConfigureAwait(false);
        if (profile is null)
        {
            issues.Add(StatementParseIssue.Error("PROFILE_NOT_FOUND", $"Mapping profile '{profileId}' is not registered."));
            return EmptyResult(profileId, issues, []);
        }

        if (!string.Equals(profile.Format, StatementMappingProfileDocument.CsvFormat, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(StatementParseIssue.Error(
                "PROFILE_FORMAT_MISMATCH",
                $"Mapping profile '{profile.ProfileId}' targets format '{profile.Format}', not CSV."));
            return EmptyResult(profileId, issues, []);
        }

        var content = Encoding.UTF8.GetString(document.Content.Span);
        // Its own bound, not a MaxRecords derivation. This was MaxRecords * 2 + 4, and the allowance was
        // built to cover a header, the terminal empty segment a trailing newline leaves, and an equal
        // number of blank lines - but it still scaled with the record cap, so rejected rows were charged
        // to the record allowance one step removed. MapRecord rejects rows: a header, one valid row and
        // five unparseable ones is seven lines against a cap of six at MaxRecords = 1, refused, though the
        // parse retains one record and five diagnostics and both sit well inside their bounds.
        //
        // No multiplier fixes that, for the reason the BAI2 path already records: lines-per-record has no
        // upper bound, because a legal file may hold any number of rows that map to nothing. Deriving from
        // MaxRecords + MaxDiagnostics fails too, since a blank line is neither. So this is MaxDocumentLines,
        // the budget that owns raw lines, set far above any real statement and biting only on abuse - and
        // it reports STATEMENT_TOO_MANY_LINES, a different claim about the file than record overflow.
        var hardLineCap = _ingressLimits.MaxDocumentLines;
        // One more than the bound, so a truncation *at* the bound cannot be mistaken for the synthetic
        // trailing segment below. Asking for exactly hardLineCap makes the two indistinguishable at
        // hardLineCap + 1 returned lines.
        var lines = CsvLineSplitter.SplitLines(
            content, hardLineCap + 1, _ingressLimits.MaxLineBytes, out var lineTooLong);

        if (lineTooLong)
        {
            return CsvIngressRefusal(_ingressLimits.LineTooLong(lines.Count + 1));
        }

        // There is deliberately no "nonblank lines cannot exceed MaxRecords + 1" precheck here. It read as
        // a cheap early refusal, but it predicted one canonical record per nonblank line, and MapRecord
        // rejects rows: a file of one valid row and three malformed ones retains a single record and three
        // diagnostics, both well inside their bounds, yet that precheck refused it as record overflow.
        // Allocation is bounded without predicting anything - MaxDocumentLines above bounds line discovery,
        // the mapping loop bounds records as it appends them, and MaxDiagnostics bounds what rejected rows
        // retain - so the bound that fires is the one whose message is true of the file.

        // Reported separately from the record bound, because they are not the same statement about the
        // file. Blank lines produce no canonical row but still cost a list entry, so a document can breach
        // the allocation bound while carrying only a handful of records - and calling that "too many
        // records" told the operator something untrue about what they had submitted. Row numbers are
        // physical line indices, so blank lines cannot simply be dropped to dodge the bound.
        // An empty final segment can only mean the content ended with a newline, so it is synthetic and
        // must not be billed. Without this a file of exactly MaxDocumentLines lines was refused when it
        // ended with a newline and accepted when it did not - acceptance turning on newline convention,
        // which is the defect the BAI2 path exempts the same segment to avoid. The old MaxRecords * 2 + 4
        // cap carried enough slack to absorb it; replacing that derivation with the real line budget
        // removed the slack and left the segment charged.
        var discoveredLines = lines.Count > 0 && lines[^1].Length == 0 ? lines.Count - 1 : lines.Count;
        if (discoveredLines > hardLineCap)
        {
            return CsvIngressRefusal(_ingressLimits.TooManyLines(hardLineCap));
        }

        var firstContentLine = lines.FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line));
        if (firstContentLine is null)
        {
            issues.Add(StatementParseIssue.Error("EMPTY_FILE", "The statement file contains no rows."));
            return EmptyResult(profileId, issues, []);
        }

        var (delimiter, delimiterIssue) = ResolveDelimiter(profile, firstContentLine);
        if (delimiterIssue is not null)
        {
            issues.Add(delimiterIssue);
        }

        var quote = profile.Csv?.Quote is { Length: 1 } quoteOption ? quoteOption[0] : '"';
        var hasHeader = profile.Csv?.HasHeader ?? true;

        string[] detectedColumns;
        var dataStartIndex = 0;
        if (hasHeader)
        {
            var headerIndex = 0;
            while (string.IsNullOrWhiteSpace(lines[headerIndex]))
            {
                headerIndex++;
            }

            detectedColumns = CsvLineSplitter.Split(lines[headerIndex], delimiter, quote)
                .Select(static column => column.Trim())
                .ToArray();
            dataStartIndex = headerIndex + 1;
        }
        else
        {
            // Headerless files expose synthesized positional names so profiles can map
            // "column1", "column2", ... explicitly.
            var width = CsvLineSplitter.Split(firstContentLine, delimiter, quote).Count;
            detectedColumns = Enumerable.Range(1, width).Select(static index => $"column{index}").ToArray();
        }

        var columnMappings = StatementColumnConfidenceScorer.MapColumns(detectedColumns, profile);
        var fingerprint = new StatementFormatFingerprint(
            Sha256Digest.Compute(document.Content.Span),
            detectedColumns.Select(static column => column.Trim().ToLowerInvariant()).ToArray(),
            delimiter.ToString());

        foreach (var field in profile.Fields.Where(static field => field.Required))
        {
            if (!Enum.TryParse<StatementCanonicalField>(field.CanonicalField, ignoreCase: true, out var canonical))
            {
                continue;
            }

            if (columnMappings.All(mapping => mapping.CanonicalField != canonical))
            {
                issues.Add(StatementParseIssue.Error(
                    "MISSING_REQUIRED_COLUMN",
                    $"No column maps to required field '{canonical}' (expected '{field.SourceColumn}').",
                    rowNumber: hasHeader ? 1 : null,
                    field.CanonicalField));
            }
        }

        var records = new List<StatementCanonicalRecord>();
        if (!issues.Any(static issue => issue.Severity == StatementParseIssue.ErrorSeverity))
        {
            var fieldByColumnIndex = new Dictionary<int, StatementCanonicalField>();
            for (var index = 0; index < columnMappings.Count; index++)
            {
                if (columnMappings[index].CanonicalField is { } canonical)
                {
                    fieldByColumnIndex[index] = canonical;
                }
            }

            var activityCodeMap = StatementRecordMapper.BuildActivityCodeMap(profile);
            var reportedUnknownCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var lineIndex = dataStartIndex; lineIndex < lines.Count; lineIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var line = lines[lineIndex];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var rowNumber = lineIndex + 1;
                var values = CsvLineSplitter.Split(line, delimiter, quote);
                if (values.Count != detectedColumns.Length)
                {
                    issues.Add(StatementParseIssue.Warning(
                        "COLUMN_COUNT_MISMATCH",
                        $"Row has {values.Count} values for {detectedColumns.Length} detected columns.",
                        rowNumber));
                }

                var mappedValues = new Dictionary<StatementCanonicalField, string>();
                foreach (var (columnIndex, canonical) in fieldByColumnIndex)
                {
                    if (columnIndex < values.Count)
                    {
                        mappedValues[canonical] = values[columnIndex].Trim();
                    }
                }

                var record = StatementRecordMapper.MapRecord(
                    mappedValues, profile, activityCodeMap, rowNumber, issues, reportedUnknownCodes);
                if (record is not null)
                {
                    // Stop accumulating at the bound rather than building the whole set and having the
                    // import service reject it afterwards: a compact CSV inside the byte cap can still
                    // carry millions of rows, and the peak allocation is what the cap exists to avoid.
                    if (records.Count >= _ingressLimits.MaxRecords)
                    {
                        issues.Add(_ingressLimits.TooManyRecords());
                        break;
                    }

                    records.Add(record);
                }

                // The record cap above bounds what a row produces; it does not bound what a row that
                // produces nothing still retains. MapRecord returns null for a rejected row and keeps its
                // error, so a file of unparseable rows accumulates diagnostics while records.Count stays
                // put and this cap never fires. The line pre-check bounds the iteration count, so the
                // growth is bounded rather than unbounded - but at roughly two issues per row it is
                // bounded well above the record allowance, and diagnostics are retained and projected
                // into the preview exactly like records.
                if (issues.Count > _ingressLimits.MaxDiagnostics)
                {
                    issues.Add(_ingressLimits.TooManyDiagnostics());
                    break;
                }
            }

            if (records.Count == 0)
            {
                issues.Add(StatementParseIssue.Warning("NO_RECORDS", "The statement produced no canonical records."));
            }
        }

        return new StatementParseResult(
            ConnectorId,
            profile.ProfileId,
            detectedColumns,
            columnMappings,
            records,
            issues,
            fingerprint);
    }

    private static (char Delimiter, StatementParseIssue? Issue) ResolveDelimiter(
        StatementMappingProfileDocument profile,
        string headerLine)
    {
        var configured = profile.Csv?.Delimiter is { Length: 1 } option ? option[0] : (char?)null;
        if (configured is { } configuredDelimiter && headerLine.Contains(configuredDelimiter, StringComparison.Ordinal))
        {
            return (configuredDelimiter, null);
        }

        // No usable configured delimiter: sniff the most frequent candidate in the first
        // content line so semicolon/tab exports import without a profile edit.
        var sniffed = CandidateDelimiters
            .OrderByDescending(candidate => headerLine.Count(character => character == candidate))
            .First();
        var issue = configured is { } expected && expected != sniffed
            ? StatementParseIssue.Info(
                "DELIMITER_AUTODETECTED",
                $"The profile's '{expected}' delimiter does not appear in the header; '{sniffed}' was auto-detected instead.")
            : null;
        return (sniffed, issue);
    }

    private static StatementParseResult EmptyResult(
        string? profileId,
        IReadOnlyList<StatementParseIssue> issues,
        IReadOnlyList<string> detectedColumns)
        => new(
            ConnectorId,
            profileId,
            detectedColumns,
            [],
            [],
            issues,
            new StatementFormatFingerprint(string.Empty, detectedColumns, ","));
}
