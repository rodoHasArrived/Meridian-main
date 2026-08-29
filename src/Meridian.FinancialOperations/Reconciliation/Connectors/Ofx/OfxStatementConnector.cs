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
        // it exists to prevent. A 250,001-entry OFX file fits well inside the 20 MiB document cap.
        var ofx = OfxDocumentParser.Parse(content, _limits.MaxRecords, _limits.MaxNestingDepth, out var bound);
        if (bound != OfxParseBound.None)
        {
            issues.Add(bound == OfxParseBound.NestingTooDeep
                ? _limits.NestingTooDeep()
                : _limits.TooManyRecords());
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
                records.Add(record);
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
