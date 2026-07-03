using System.Security.Cryptography;
using System.Text;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Declarative, profile-driven CSV connector. Any custodian or broker CSV layout can be
/// onboarded by authoring a mapping-profile document — no code change required. Handles
/// RFC-4180 quoting, delimiter variants, BOMs, header aliases with per-column confidence,
/// and mixed-kind files (positions, transactions, cash balances, fees, dividends in one
/// statement).
/// </summary>
public sealed class CsvStatementConnector(StatementMappingProfileCatalog catalog) : IStatementConnector
{
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

        var quote = profile.Csv?.Quote is { Length: 1 } quoteOption ? quoteOption[0] : '"';
        var content = Encoding.UTF8.GetString(document.Content.Span);
        var lines = CsvLineSplitter.SplitLines(content, quote);
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
            Convert.ToHexString(SHA256.HashData(document.Content.Span)).ToLowerInvariant(),
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
                    records.Add(record);
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
