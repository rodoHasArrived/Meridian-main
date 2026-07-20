using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

public sealed record StatementImportCommitRequest(
    StatementSourceDocument Document,
    string? ConnectorId,
    string SourceKind,
    string SourceInstitution,
    string FundAccountId,
    string ExternalAccountId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? ToleranceProfileId,
    string ImportedBy);

public interface IStatementImportCommitService
{
    Task<StatementImportCommitResultDto> CommitAsync(
        StatementImportCommitRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Orchestrates the statement connector pipeline: preview (per-column mapping confidence,
/// per-kind record breakdown, drift and parse diagnostics, profile suggestions) and commit
/// (deterministic canonical-CSV artifact, retained raw evidence, hand-off to the existing
/// statement-run workflow and reconciliation queue).
/// </summary>
public sealed class StatementImportService(
    StatementConnectorRegistry connectors,
    StatementMappingProfileCatalog catalog,
    IStatementRunWorkflowService workflow,
    string dataRoot) : IStatementImportCommitService
{
    private const int SamplesPerKind = 5;
    private const int MaxProfileSuggestions = 3;
    private const string ReadyStatus = "ReadyToImport";
    private const string NeedsAttentionStatus = "NeedsAttention";

    private static readonly string[] CanonicalArtifactHeader =
    [
        "account", "symbol", "quantity", "price", "cashAmount", "activityType", "tradeDate",
        "settlementDate", "currency", "feesCommission", "externalTransactionId"
    ];

    private readonly string _retainedRoot = Path.Combine(dataRoot, "reconciliation", "statement-connector-imports");

    public async Task<StatementImportPreviewDto> PreviewAsync(
        StatementSourceDocument document,
        string? connectorId,
        CancellationToken ct = default)
    {
        var (connector, resolutionIssue) = ResolveConnector(document, connectorId);
        if (connector is null)
        {
            return BuildPreview(
                connectorId ?? "unknown",
                connectorId ?? "Unknown connector",
                profileId: document.MappingProfileId,
                document,
                parse: null,
                extraIssues: [resolutionIssue!],
                suggestions: []);
        }

        var parse = await connector.ParseAsync(document, ct).ConfigureAwait(false);
        var issues = new List<StatementParseIssue>();
        var profile = await catalog.FindAsync(parse.ProfileId, ct).ConfigureAwait(false);
        if (profile is not null && StatementMappingProfileCatalog.CheckDrift(profile, parse.Fingerprint) is { } drift)
        {
            issues.Add(drift);
        }

        var suggestions = await SuggestProfilesAsync(connector, parse, ct).ConfigureAwait(false);
        return BuildPreview(
            connector.Descriptor.ConnectorId,
            connector.Descriptor.DisplayName,
            parse.ProfileId,
            document,
            parse,
            issues,
            suggestions);
    }

    public async Task<StatementImportCommitResultDto> CommitAsync(
        StatementImportCommitRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceKind = NormalizeSourceKind(request.SourceKind);

        var (connector, resolutionIssue) = ResolveConnector(request.Document, request.ConnectorId);
        if (connector is null)
        {
            throw new InvalidDataException(resolutionIssue!.Message);
        }

        var parse = await connector.ParseAsync(request.Document, ct).ConfigureAwait(false);
        if (parse.HasErrors)
        {
            var errors = parse.Issues
                .Where(static issue => issue.Severity == StatementParseIssue.ErrorSeverity)
                .Select(static issue => issue.RowNumber is { } row ? $"Row {row}: {issue.Message}" : issue.Message);
            throw new InvalidDataException($"Statement cannot be imported: {string.Join(" ", errors)}");
        }

        if (parse.Records.Count == 0)
        {
            throw new InvalidDataException("Statement produced no canonical records; nothing to import.");
        }

        var artifactContent = RenderCanonicalArtifact(parse.Records);
        var artifactBytes = Encoding.UTF8.GetBytes(artifactContent);
        var uploadId = "sc-" + Convert.ToHexString(SHA256.HashData(request.Document.Content.Span))[..16].ToLowerInvariant();
        var retainedDirectory = Path.Combine(_retainedRoot, uploadId);
        Directory.CreateDirectory(retainedDirectory);

        var safeSourceName = SanitizeFileName(request.Document.FileName);
        var rawPath = Path.Combine(retainedDirectory, safeSourceName);
        var canonicalPath = Path.Combine(retainedDirectory, "canonical.csv");
        await AtomicFileWriter.WriteAsync(rawPath, request.Document.Content.ToArray(), ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(canonicalPath, artifactBytes, ct).ConfigureAwait(false);

        var runRequest = await StatementRunCreateRequest.FromFileAsync(
                broker: sourceKind,
                sourceInstitution: request.SourceInstitution,
                fundAccountId: request.FundAccountId,
                externalAccountId: request.ExternalAccountId,
                statementPeriodStart: request.PeriodStart,
                statementPeriodEnd: request.PeriodEnd,
                sourcePath: canonicalPath,
                mappingProfileId: StatementMappingProfileRegistry.CanonicalCsvV1ProfileId,
                toleranceProfileId: string.IsNullOrWhiteSpace(request.ToleranceProfileId)
                    ? StatementToleranceProfile.DefaultProfileId
                    : request.ToleranceProfileId,
                importedBy: request.ImportedBy,
                originalFileName: request.Document.FileName,
                ct: ct)
            .ConfigureAwait(false);

        var kindSummaries = BuildKindSummaries(parse.Records);
        var relativeRaw = ToRelativeRetainedPath(uploadId, safeSourceName);
        var relativeCanonical = ToRelativeRetainedPath(uploadId, "canonical.csv");

        StatementRunWorkflowResult result;
        try
        {
            result = await workflow.CreateAsync(runRequest.ToStatementRunRequest(), ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already imported", StringComparison.OrdinalIgnoreCase))
        {
            // Idempotent re-import: the deterministic artifact hashes to the same duplicate
            // key, so surface the existing run instead of failing the operator.
            return new StatementImportCommitResultDto(
                RunId: runRequest.DuplicateKey,
                Duplicate: true,
                RecordCount: parse.Records.Count,
                KindSummaries: kindSummaries,
                BreakCount: 0,
                CaseCount: 0,
                RetainedSourcePath: relativeRaw,
                RetainedCanonicalPath: relativeCanonical,
                Status: "Duplicate",
                NextAction: "This statement was already imported for the fund account and period; review the existing reconciliation run.");
        }

        await catalog.RecordAcceptedFingerprintAsync(parse.ProfileId, parse.Fingerprint, ct).ConfigureAwait(false);
        var breakIds = result.Breaks
            .Select(static item => item.BreakId)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var caseLinks = BuildReconciliationCaseLinks(result.Import.ImportId, result.Cases);
        var caseIds = caseLinks
            .Select(static item => item.CaseId)
            .ToArray();

        return new StatementImportCommitResultDto(
            RunId: result.Import.ImportId,
            Duplicate: false,
            RecordCount: parse.Records.Count,
            KindSummaries: kindSummaries,
            BreakCount: result.Breaks.Count,
            CaseCount: result.Cases.Count,
            RetainedSourcePath: relativeRaw,
            RetainedCanonicalPath: relativeCanonical,
            Status: "Imported",
            NextAction: result.Cases.Count > 0
                ? $"{result.Cases.Count} reconciliation case(s) entered the queue; review and disposition them in the reconciliation workspace."
                : "All rows matched within tolerance; no reconciliation cases were opened.")
        {
            BreakIds = breakIds,
            CaseIds = caseIds,
            ReconciliationCaseRoutes = caseLinks
                .Select(static item => item.Route)
                .ToArray(),
            ReconciliationCaseLinks = caseLinks
        };
    }

    /// <summary>Fetches a remote statement document through a fetch-capable connector.</summary>
    public async Task<StatementSourceDocument> FetchDocumentAsync(StatementFetchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var connector = connectors.Resolve(request.ConnectorId);
        if (connector is not IFetchingStatementConnector fetching || !connector.Descriptor.SupportsRemoteFetch)
        {
            throw new NotSupportedException($"Statement connector '{request.ConnectorId}' does not support remote fetch.");
        }

        return await fetching.FetchAsync(request, ct).ConfigureAwait(false);
    }

    private (IStatementConnector? Connector, StatementParseIssue? Issue) ResolveConnector(
        StatementSourceDocument document,
        string? connectorId)
    {
        if (!string.IsNullOrWhiteSpace(connectorId))
        {
            try
            {
                return (connectors.Resolve(connectorId), null);
            }
            catch (NotSupportedException ex)
            {
                return (null, StatementParseIssue.Error("CONNECTOR_NOT_FOUND", ex.Message));
            }
        }

        var detected = connectors.Detect(document);
        return detected is null
            ? (null, StatementParseIssue.Error(
                "CONNECTOR_NOT_DETECTED",
                $"No statement connector recognizes '{document.FileName}'. Choose a connector explicitly or check the file format."))
            : (detected, null);
    }

    private async Task<IReadOnlyList<StatementProfileSuggestionDto>> SuggestProfilesAsync(
        IStatementConnector connector,
        StatementParseResult parse,
        CancellationToken ct)
    {
        if (parse.DetectedColumns.Count == 0
            || !string.Equals(connector.Descriptor.ConnectorId, CsvStatementConnector.ConnectorId, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var profiles = await catalog.ListAsync(ct).ConfigureAwait(false);
        return profiles
            .Where(static profile => string.Equals(profile.Format, StatementMappingProfileDocument.CsvFormat, StringComparison.OrdinalIgnoreCase))
            .Select(profile => new StatementProfileSuggestionDto(
                profile.ProfileId,
                profile.DisplayName,
                StatementColumnConfidenceScorer.ScoreProfile(parse.DetectedColumns, profile)))
            .Where(static suggestion => suggestion.Score > 0m)
            .OrderByDescending(static suggestion => suggestion.Score)
            .ThenBy(static suggestion => suggestion.ProfileId, StringComparer.OrdinalIgnoreCase)
            .Take(MaxProfileSuggestions)
            .ToArray();
    }

    private static StatementImportPreviewDto BuildPreview(
        string connectorId,
        string connectorDisplayName,
        string? profileId,
        StatementSourceDocument document,
        StatementParseResult? parse,
        IReadOnlyList<StatementParseIssue> extraIssues,
        IReadOnlyList<StatementProfileSuggestionDto> suggestions)
    {
        var issues = (parse?.Issues ?? []).Concat(extraIssues)
            .Select(static issue => new StatementImportIssueDto(issue.Code, issue.Severity, issue.RowNumber, issue.Field, issue.Message))
            .ToArray();
        var hasErrors = issues.Any(static issue =>
            string.Equals(issue.Severity, StatementParseIssue.ErrorSeverity, StringComparison.OrdinalIgnoreCase));
        var records = parse?.Records ?? [];

        return new StatementImportPreviewDto(
            ConnectorId: connectorId,
            ConnectorDisplayName: connectorDisplayName,
            ProfileId: parse?.ProfileId ?? profileId,
            FileName: document.FileName,
            FileSizeBytes: document.Content.Length,
            DetectedColumns: parse?.DetectedColumns ?? [],
            ColumnMappings: (parse?.ColumnMappings ?? [])
                .Select(static mapping => new StatementColumnMappingDto(
                    mapping.SourceColumn,
                    mapping.CanonicalField?.ToString(),
                    (StatementColumnConfidenceDto)mapping.Confidence,
                    mapping.Score,
                    mapping.Rationale))
                .ToArray(),
            RecordCount: records.Count,
            KindSummaries: BuildKindSummaries(records),
            Issues: issues,
            ProfileSuggestions: suggestions,
            Status: hasErrors ? NeedsAttentionStatus : ReadyStatus,
            NextAction: hasErrors
                ? "Resolve the blocking issues (adjust the mapping profile or repair the source file), then preview again."
                : "Review the per-column mappings and per-kind records, then commit the import into the reconciliation queue.");
    }

    private static IReadOnlyList<StatementKindSummaryDto> BuildKindSummaries(IReadOnlyList<StatementCanonicalRecord> records)
        => records
            .GroupBy(static record => record.Kind)
            .OrderBy(static group => group.Key)
            .Select(static group => new StatementKindSummaryDto(
                group.Key.ToString(),
                group.Count(),
                group.Take(SamplesPerKind)
                    .Select(static record => new StatementRecordPreviewDto(
                        record.Kind.ToString(),
                        record.Account,
                        record.Symbol,
                        record.Quantity,
                        record.Price,
                        record.CashAmount,
                        record.ActivityType,
                        record.TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        record.SettlementDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        record.Currency,
                        record.FeesCommission,
                        record.ExternalTransactionId))
                    .ToArray()))
            .ToArray();

    /// <summary>
    /// Renders records to the canonical CSV artifact deterministically: fixed column order,
    /// invariant formatting, LF endings, and delimiter-safe values, so the same source file
    /// always produces byte-identical bytes and therefore the same duplicate key.
    /// </summary>
    internal static string RenderCanonicalArtifact(IReadOnlyList<StatementCanonicalRecord> records)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', CanonicalArtifactHeader)).Append('\n');
        foreach (var record in records)
        {
            builder
                .Append(SanitizeArtifactValue(record.Account)).Append(',')
                .Append(SanitizeArtifactValue(record.Symbol)).Append(',')
                .Append(record.Quantity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(record.Price.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(record.CashAmount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(SanitizeArtifactValue(record.ActivityType)).Append(',')
                .Append(record.TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(record.SettlementDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(SanitizeArtifactValue(record.Currency)).Append(',')
                .Append(record.FeesCommission?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(SanitizeArtifactValue(record.ExternalTransactionId)).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// The downstream canonical parser splits rows with a plain comma split, so artifact
    /// values must never contain the delimiter, quotes, or line breaks.
    /// </summary>
    private static string SanitizeArtifactValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(character is ',' or '"' or '\r' or '\n' ? ' ' : character);
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeSourceKind(string sourceKind)
    {
        var normalized = sourceKind?.Trim().ToLowerInvariant();
        return normalized is "broker" or "custodian"
            ? normalized
            : throw new InvalidDataException($"Statement source kind '{sourceKind}' is not supported for connector imports. Use 'broker' or 'custodian'.");
    }

    private static string SanitizeFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return "statement.dat";
        }

        foreach (var character in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(character, '-');
        }

        return safeName;
    }

    private static string ToRelativeRetainedPath(string uploadId, string fileName)
        => string.Join('/', "reconciliation", "statement-connector-imports", uploadId, fileName);

    private static IReadOnlyList<StatementImportReconciliationCaseLinkDto> BuildReconciliationCaseLinks(
        string runId,
        IReadOnlyList<ReconciliationCase> cases)
        => cases
            .Where(static item => !string.IsNullOrWhiteSpace(item.CaseId))
            .GroupBy(static item => item.CaseId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static item => item.CaseId, StringComparer.OrdinalIgnoreCase)
            .Select(reconciliationCase =>
            {
                var caseId = reconciliationCase.CaseId.Trim();
                var breakId = ResolveBreakIdFromCaseId(caseId);
                return new StatementImportReconciliationCaseLinkDto(
                    CaseId: caseId,
                    BreakId: breakId,
                    Route: BuildReconciliationCaseRoute(runId, caseId, breakId),
                    Label: breakId is null
                        ? $"Reconciliation case {caseId}"
                        : $"Reconciliation case {caseId} for break {breakId}",
                    Status: NormalizeCaseText(reconciliationCase.Status, "Open"),
                    Priority: NormalizeCaseText(reconciliationCase.Priority, "Normal"),
                    Reason: NormalizeCaseText(reconciliationCase.Reason, "Statement import created a reconciliation case."),
                    SuggestedNextAction: NormalizeCaseText(
                        reconciliationCase.BreakExplanation?.SuggestedNextAction,
                        "Assign the case, compare retained statement evidence to Meridian records, then attach support before disposition."));
            })
            .ToArray();

    private static string BuildReconciliationCaseRoute(string runId, string caseId, string? breakId = null)
    {
        var route = "/accounting/reconciliation/match"
            + $"?runId={Uri.EscapeDataString(runId)}"
            + $"&caseId={Uri.EscapeDataString(caseId)}";

        var resolvedBreakId = string.IsNullOrWhiteSpace(breakId)
            ? ResolveBreakIdFromCaseId(caseId)
            : breakId.Trim();
        if (!string.IsNullOrWhiteSpace(resolvedBreakId))
        {
            route += $"&breakId={Uri.EscapeDataString(resolvedBreakId)}";
        }

        return route;
    }

    private static string? ResolveBreakIdFromCaseId(string caseId)
    {
        const string casePrefix = "case:";
        if (!caseId.StartsWith(casePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var breakId = caseId[casePrefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(breakId) ? null : breakId;
    }

    private static string NormalizeCaseText(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
