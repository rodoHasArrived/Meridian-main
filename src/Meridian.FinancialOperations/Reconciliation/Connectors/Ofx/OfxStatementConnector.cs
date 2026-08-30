using System.Text;
using Meridian.Contracts.Integrity;

namespace Meridian.FinancialOperations.Reconciliation.Connectors.Ofx;

/// <summary>
/// OFX statement connector covering bank statements (cash transactions + ledger balance)
/// and investment statements (buys/sells + positions), OFX 1.x SGML and 2.x XML alike.
/// Entries are flattened to tag pseudo-columns and mapped through the same declarative
/// profiles as CSV, so operators can remap or reclassify OFX activity without a release.
/// </summary>
public sealed class OfxStatementConnector(
    StatementMappingProfileCatalog catalog,
    StatementIngressLimits? ingressLimits = null) : IStatementConnector
{
    private readonly StatementIngressLimits _limits = ingressLimits ?? StatementIngressLimits.Default;

    public const string ConnectorId = "ofx";

    public StatementConnectorDescriptor Descriptor { get; } = new(
        ConnectorId,
        "OFX / QFX statement",
        [".ofx", ".qfx"],
        SupportsFileImport: true,
        SupportsRemoteFetch: false,
        RequiresMappingProfile: true,
        DefaultProfileId: StatementBuiltInProfiles.OfxBankV1ProfileId);

    public bool CanHandle(StatementSourceDocument document)
    {
        var extension = Path.GetExtension(document.FileName);
        if (Descriptor.FileExtensions.Any(candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return OfxDocumentParser.LooksLikeOfx(Encoding.UTF8.GetString(SniffSpan(document)));
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
        if (document.Content.Length > _limits.MaxDocumentBytes)
        {
            issues.Add(_limits.DocumentTooLarge(document.Content.Length));
            return EmptyResult(profileId, issues);
        }

        var profile = await catalog.FindAsync(profileId, ct).ConfigureAwait(false);
        if (profile is null)
        {
            issues.Add(StatementParseIssue.Error("PROFILE_NOT_FOUND", $"Mapping profile '{profileId}' is not registered."));
            return EmptyResult(profileId, issues);
        }

        if (!string.Equals(profile.Format, StatementMappingProfileDocument.OfxFormat, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(StatementParseIssue.Error(
                "PROFILE_FORMAT_MISMATCH",
                $"Mapping profile '{profile.ProfileId}' targets format '{profile.Format}', not OFX."));
            return EmptyResult(profileId, issues);
        }

        var content = Encoding.UTF8.GetString(document.Content.Span);
        if (!OfxDocumentParser.LooksLikeOfx(content))
        {
            issues.Add(StatementParseIssue.Error("NOT_OFX", "The file does not look like an OFX 1.x or 2.x statement."));
            return EmptyResult(profileId, issues);
        }

        // Bounded here rather than after the parse returns: the node tree and the flattened entry
        // dictionaries are both built by Parse, so a check on the result would run after the allocation
        // it exists to prevent. An entry-heavy OFX file fits well inside the 20 MiB document cap.
        //
        // The aggregate budget is MaxDocumentEntries, not MaxRecords. Parse flattens one dictionary per
        // aggregate and StatementRecordMapper then rejects some of them, so aggregates and retained
        // records are different counts - passing MaxRecords here refused a document whose canonical rows
        // sat inside the operator's allowance because its rejected aggregates did not. The record cap is
        // charged where a record is appended, below.
        var ofx = OfxDocumentParser.Parse(
            content,
            _limits.MaxDocumentEntries,
            _limits.MaxNestingDepth,
            _limits.MaxParseNodes,
            out var bound);
        if (bound != OfxParseBound.None)
        {
            issues.Add(bound switch
            {
                OfxParseBound.NestingTooDeep => _limits.NestingTooDeep(),
                OfxParseBound.TooManyNodes => _limits.TooManyNodes(),
                _ => _limits.TooManyEntries(),
            });
            return EmptyResult(profileId, issues);
        }

        if (ofx.Entries.Count == 0)
        {
            issues.Add(StatementParseIssue.Warning(
                "NO_RECORDS",
                "The OFX file contains no bank transactions, balances, investment activity, or positions."));
        }

        // The "columns" of an OFX statement are the union of tags across flattened entries.
        var detectedColumns = ofx.Entries
            .SelectMany(static entry => entry.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var columnMappings = StatementColumnConfidenceScorer.MapColumns(detectedColumns, profile);
        var fingerprint = new StatementFormatFingerprint(
            Sha256Digest.Compute(document.Content.Span),
            detectedColumns.Select(static tag => tag.ToLowerInvariant()).ToArray(),
            "ofx");

        var fieldByColumn = new Dictionary<string, StatementCanonicalField>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in columnMappings)
        {
            if (mapping.CanonicalField is { } canonical)
            {
                fieldByColumn[mapping.SourceColumn] = canonical;
            }
        }

        var activityCodeMap = StatementRecordMapper.BuildActivityCodeMap(profile);
        var reportedUnknownCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var records = new List<StatementCanonicalRecord>();
        for (var index = 0; index < ofx.Entries.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = ofx.Entries[index];
            var mappedValues = new Dictionary<StatementCanonicalField, string>();
            foreach (var (tag, value) in entry)
            {
                if (fieldByColumn.TryGetValue(tag, out var canonical))
                {
                    mappedValues.TryAdd(canonical, value);
                }
            }

            var record = StatementRecordMapper.MapRecord(
                mappedValues, profile, activityCodeMap, index + 1, issues, reportedUnknownCodes);
            if (record is not null)
            {
                // Charged on the append, on what the entry actually produced. MapRecord returns null for
                // an entry it rejects, so an aggregate count could only ever over-charge.
                if (records.Count >= _limits.MaxRecords)
                {
                    issues.Add(_limits.TooManyRecords());
                    return EmptyResult(profileId, issues);
                }

                records.Add(record);
            }

            // Entry count is bounded by MaxDocumentEntries inside OfxDocumentParser.Parse, so this loop
            // runs a bounded number of times - but each pass can retain up to two diagnostics for a row
            // that produces no record, so the issue list is bounded at a multiple of that allowance
            // rather than by it. Diagnostics are retained evidence and get their own ceiling.
            if (issues.Count > _limits.MaxDiagnostics)
            {
                issues.Add(_limits.TooManyDiagnostics());
                break;
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

    private static ReadOnlySpan<byte> SniffSpan(StatementSourceDocument document)
    {
        var span = document.Content.Span;
        return span.Length > 512 ? span[..512] : span;
    }

    private static StatementParseResult EmptyResult(string? profileId, IReadOnlyList<StatementParseIssue> issues)
        => new(ConnectorId, profileId, [], [], [], issues, new StatementFormatFingerprint(string.Empty, [], "ofx"));
}
