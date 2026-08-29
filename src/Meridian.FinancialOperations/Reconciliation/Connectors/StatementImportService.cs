using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Core.IO;
using Meridian.Domain.Reconciliation;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

public static class StatementConnectorLimits
{
    /// <summary>
    /// Maximum accepted statement file size (20 MiB). IB Flex XML exports routinely exceed the general
    /// 5 MB data-upload cap, so statement imports get their own, larger bound. Shared by the workstation
    /// upload endpoint and the CLI import/validate commands so neither path buffers an unbounded
    /// caller-supplied file into memory.
    /// </summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;
}

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
    string ImportedBy)
{
    /// <summary>
    /// Exact server-verified accounting authority for statement-to-close processing. It remains
    /// optional for legacy reconciliation-report callers that do not enter the governed close lane.
    /// </summary>
    public StatementAccountingScope? AccountingScope { get; init; }
}

/// <summary>
/// The outcome of validating a statement document through the connector pipeline without
/// committing it: whether the document parsed cleanly into canonical records, how many records it
/// produced, and any blocking error messages. Mirrors the connector's parse result so an operator
/// can validate a bank file (camt.053, BAI2, ...) before importing it, not only CSV/IB Flex.
/// </summary>
public sealed record StatementImportValidationResult(
    bool IsValid,
    int RecordCount,
    IReadOnlyList<string> Errors);

public interface IStatementImportCommitService
{
    Task<StatementImportCommitResultDto> CommitAsync(
        StatementImportCommitRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a statement document through the connector pipeline (resolve connector, parse, no
    /// commit), so the newly supported bank formats are validatable from the CLI just as they are
    /// importable. Returns the record count and any blocking parse errors.
    /// </summary>
    Task<StatementImportValidationResult> ValidateAsync(
        StatementSourceDocument document,
        string? connectorId,
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
    string dataRoot,
    StatementIngressLimits? ingressLimits = null) : IStatementImportCommitService
{
    private readonly StatementIngressLimits _ingressLimits = ingressLimits ?? StatementIngressLimits.Default;

    private const int SamplesPerKind = 5;
    private const int MaxProfileSuggestions = 3;
    private const string ReadyStatus = "ReadyToImport";
    private const string NeedsAttentionStatus = "NeedsAttention";

    private static readonly string[] CanonicalArtifactHeader =
    [
        "account", "symbol", "quantity", "price", "cashAmount", "activityType", "tradeDate",
        "settlementDate", "currency", "feesCommission", "externalTransactionId", "activityCategory",
        "activitySubtype", "providerActivityCode", "relatedTransactionId", "orderId", "description"
    ];

    private readonly RootedPathGuard _retainedPathGuard = new(dataRoot);

    public async Task<StatementImportPreviewDto> PreviewAsync(
        StatementSourceDocument document,
        string? connectorId,
        CancellationToken ct = default)
    {
        if (document.Content.Length > _ingressLimits.MaxDocumentBytes)
        {
            return BuildPreview(
                connectorId ?? "unknown",
                connectorId ?? "Unknown connector",
                profileId: document.MappingProfileId,
                document,
                parse: null,
                extraIssues: [_ingressLimits.DocumentTooLarge(document.Content.Length)],
                suggestions: []);
        }

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
        // TotalRetainedRows, not Records.Count. Five evidence-only collections on the parse result -
        // account snapshots, activity events, activity cursors, tax lots, borrow positions - never
        // contribute to Records, so a connector could return one canonical row alongside hundreds of
        // thousands of evidence rows, pass this cap, and have every one of them serialized into the
        // retained artifact. Bounding the total makes the cap a property of this seam rather than of
        // whichever loops each connector happens to guard, and it covers connectors added later.
        if (parse.TotalRetainedRows > _ingressLimits.MaxRecords)
        {
            // Return here rather than falling through. BuildPreview groups every canonical record and
            // activity event and projects every account snapshot into fresh collections, so continuing
            // would allocate in proportion to a payload already known to be refused - the opposite of what
            // this bound is for. Commit and Validate both return at this point; Preview did not.
            return BuildPreview(
                connector.Descriptor.ConnectorId,
                connector.Descriptor.DisplayName,
                parse.ProfileId,
                document,
                parse: null,
                extraIssues: [_ingressLimits.TooManyRecords()],
                suggestions: []);
        }

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

        // The byte cap has to be checked before this copy, not after. Every connector enforces the same
        // bound, but the copy below is what actually doubles an oversize payload in memory, and it happens
        // before the connector is even resolved — so a check that lived only in the connector would run
        // after the allocation it exists to prevent.
        if (request.Document.Content.Length > _ingressLimits.MaxDocumentBytes)
        {
            throw new InvalidDataException(
                $"Statement cannot be imported: {Describe(_ingressLimits.DocumentTooLarge(request.Document.Content.Length))}");
        }

        var capturedSourceBytes = request.Document.Content.ToArray();
        var capturedDocument = request.Document with { Content = capturedSourceBytes };
        var sourceKind = NormalizeSourceKind(request.SourceKind);

        var (connector, resolutionIssue) = ResolveConnector(capturedDocument, request.ConnectorId);
        if (connector is null)
        {
            throw new InvalidDataException(Describe(resolutionIssue!));
        }

        var parse = await connector.ParseAsync(capturedDocument, ct).ConfigureAwait(false);

        // The record cap is enforced here, not only inside the connectors that stream it. camt.053 and
        // BAI2 refuse mid-parse, but every other connector resolves through this service too, so without
        // this check a format that accumulates rows without counting them could commit a document past
        // the configured bound.
        if (parse.TotalRetainedRows > _ingressLimits.MaxRecords)
        {
            throw new InvalidDataException(
                $"Statement cannot be imported: {Describe(_ingressLimits.TooManyRecords())}");
        }

        if (parse.HasErrors)
        {
            var errors = parse.Issues
                .Where(static issue => issue.Severity == StatementParseIssue.ErrorSeverity)
                .Select(Describe);
            throw new InvalidDataException($"Statement cannot be imported: {string.Join(" ", errors)}");
        }

        if (parse.Records.Count == 0)
        {
            throw new InvalidDataException("Statement produced no canonical records; nothing to import.");
        }

        EnsureParsedAccountAuthority(
            capturedDocument,
            parse.Records,
            request.ExternalAccountId);

        var artifactContent = RenderCanonicalArtifact(parse.Records);
        var artifactBytes = Encoding.UTF8.GetBytes(artifactContent);
        // Key the retained evidence on both the raw content and its canonical rendering. Keying on the
        // raw hash alone let a re-import of the same source file under a changed mapping profile — which
        // renders different canonical output — reuse the directory and overwrite the first import's
        // canonical artifact, destroying the normalized evidence that run still references. Combining
        // both hashes gives every distinct rendering its own directory, while a same-profile re-import
        // rewrites identical bytes in place and stays idempotent.
        var rawHash = ComputeSha256Hex(capturedSourceBytes);
        var canonicalHash = ComputeSha256Hex(artifactBytes);
        var uploadId = $"sc-{rawHash}-{canonicalHash}";
        const string reconciliationDirectory = "reconciliation";
        const string retainedImportsDirectory = "statement-connector-imports";
        var retainedDirectory = _retainedPathGuard.ResolvePath(
            reconciliationDirectory,
            retainedImportsDirectory,
            uploadId);

        // Retain the raw source under its own subdirectory so a source file literally named
        // "canonical.csv" cannot overwrite (or be overwritten by) the rendered canonical artifact.
        const string sourceSubdirectory = "source";
        var safeSourceName = SanitizeFileName(request.Document.FileName);
        var retainedSourceDirectory = _retainedPathGuard.ResolvePath(
            reconciliationDirectory,
            retainedImportsDirectory,
            uploadId,
            sourceSubdirectory);
        Directory.CreateDirectory(retainedSourceDirectory);
        _retainedPathGuard.EnsurePath(retainedSourceDirectory);
        var rawPath = _retainedPathGuard.ResolvePath(
            reconciliationDirectory,
            retainedImportsDirectory,
            uploadId,
            sourceSubdirectory,
            safeSourceName);
        var canonicalPath = _retainedPathGuard.ResolvePath(
            reconciliationDirectory,
            retainedImportsDirectory,
            uploadId,
            "canonical.csv");
        var canonicalEvidencePath = _retainedPathGuard.ResolvePath(
            reconciliationDirectory,
            retainedImportsDirectory,
            uploadId,
            "canonical-evidence.json");
        var canonicalEvidence = new StatementCanonicalEvidenceArtifact(
            ConnectorId: parse.ConnectorId,
            ProfileId: parse.ProfileId,
            RetainedAtUtc: DateTimeOffset.UtcNow,
            Fingerprint: parse.Fingerprint,
            Records: parse.Records,
            AccountSnapshots: parse.AccountSnapshots ?? [],
            ActivityEvents: parse.ActivityEvents ?? [],
            ActivityCursors: parse.ActivityCursors ?? [],
            TaxLots: parse.TaxLots ?? [],
            BorrowPositions: parse.BorrowPositions ?? []);
        var canonicalEvidenceBytes = JsonSerializer.SerializeToUtf8Bytes(
            canonicalEvidence,
            StatementCanonicalEvidenceJsonContext.Default.StatementCanonicalEvidenceArtifact);
        _retainedPathGuard.EnsurePath(retainedDirectory);
        await AtomicFileWriter.WriteAsync(rawPath, capturedSourceBytes, ct).ConfigureAwait(false);
        _retainedPathGuard.EnsurePath(canonicalPath);
        await AtomicFileWriter.WriteAsync(canonicalPath, artifactBytes, ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(canonicalEvidencePath, canonicalEvidenceBytes, ct).ConfigureAwait(false);

        var runRequest = new StatementRunCreateRequest(
            Broker: sourceKind,
            SourceInstitution: request.SourceInstitution.Trim(),
            FundAccountId: request.FundAccountId.Trim(),
            ExternalAccountId: request.ExternalAccountId.Trim(),
            StatementPeriodStart: request.PeriodStart,
            StatementPeriodEnd: request.PeriodEnd,
            SourcePath: rawPath,
            OriginalFileName: request.Document.FileName,
            MappingProfileId: StatementMappingProfileRegistry.CanonicalCsvV1ProfileId,
            ToleranceProfileId: string.IsNullOrWhiteSpace(request.ToleranceProfileId)
                ? StatementToleranceProfile.DefaultProfileId
                : request.ToleranceProfileId.Trim(),
            ImportedBy: request.ImportedBy.Trim(),
            SourceFileHash: rawHash)
        {
            CanonicalSourcePath = canonicalPath,
            CanonicalArtifactHash = canonicalHash,
            AccountingScope = request.AccountingScope
        };

        var kindSummaries = BuildKindSummaries(parse.Records);
        var relativeRaw = ToRelativeRetainedPath(uploadId, $"{sourceSubdirectory}/{safeSourceName}");
        var relativeCanonical = ToRelativeRetainedPath(uploadId, "canonical.csv");
        var relativeCanonicalEvidence = ToRelativeRetainedPath(uploadId, "canonical-evidence.json");

        // Statement run creation is idempotent: it resumes an import that is already retained rather
        // than reporting one, so a re-import of the same statement would otherwise be indisting-
        // uishable from a first import. Ask before creating, using the importer's own compatibility
        // rule — the current raw-plus-canonical identity, then the canonical-only identity that runs
        // imported before raw source hashes were retained separately still carry.
        var compatibleDuplicateKeys = request.AccountingScope is null
            ? StatementDuplicateKey.CreateCompatibleKeys(
                runRequest.FundAccountId,
                runRequest.StatementPeriodStart,
                runRequest.StatementPeriodEnd,
                rawHash,
                canonicalHash)
            : StatementDuplicateKey.CreateCompatibleKeys(
                runRequest.FundAccountId,
                runRequest.StatementPeriodStart,
                runRequest.StatementPeriodEnd,
                rawHash,
                canonicalHash,
                request.AccountingScope);
        var retainedImportIds = (await workflow.ListImportsAsync(ct).ConfigureAwait(false))
            .Select(static import => import.ImportId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (compatibleDuplicateKeys.FirstOrDefault(retainedImportIds.Contains) is { } retainedRunId)
        {
            return await DuplicateResultAsync(retainedRunId).ConfigureAwait(false);
        }

        StatementRunWorkflowResult result;
        try
        {
            result = await workflow.CreateAsync(runRequest.ToStatementRunRequest(), ct).ConfigureAwait(false);
        }
        catch (StatementAlreadyImportedException ex)
        {
            // Upgrade compatibility: a pre-hardening run may be keyed only by the canonical
            // artifact hash. Return that retained run identity instead of inventing the new
            // raw-plus-canonical key for a run that was not created.
            return await DuplicateResultAsync(ex.ExistingImportId).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already imported", StringComparison.OrdinalIgnoreCase))
        {
            return await DuplicateResultAsync(runRequest.DuplicateKey).ConfigureAwait(false);
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
            ReconciliationCaseLinks = caseLinks,
            RetainedCanonicalEvidencePath = relativeCanonicalEvidence
        };

        async Task<StatementImportCommitResultDto> DuplicateResultAsync(string existingRunId)
        {
            var retained = await workflow.GetAsync(existingRunId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Statement import '{existingRunId}' was reported as a duplicate, but its retained reconciliation authority could not be loaded.");
            var retainedBreakIds = retained.Breaks
                .Select(static item => item.BreakId)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var retainedCaseLinks = BuildReconciliationCaseLinks(
                retained.Import.ImportId,
                retained.Cases);

            return new StatementImportCommitResultDto(
                RunId: retained.Import.ImportId,
                Duplicate: true,
                RecordCount: parse.Records.Count,
                KindSummaries: kindSummaries,
                BreakCount: retained.Breaks.Count,
                CaseCount: retained.Cases.Count,
                RetainedSourcePath: relativeRaw,
                RetainedCanonicalPath: relativeCanonical,
                Status: "Duplicate",
                NextAction: "This statement was already imported for the fund account and period; review the existing reconciliation run.")
            {
                BreakIds = retainedBreakIds,
                CaseIds = retainedCaseLinks
                    .Select(static item => item.CaseId)
                    .ToArray(),
                ReconciliationCaseRoutes = retainedCaseLinks
                    .Select(static item => item.Route)
                    .ToArray(),
                ReconciliationCaseLinks = retainedCaseLinks
            };
        }
    }

    public async Task<StatementImportValidationResult> ValidateAsync(
        StatementSourceDocument document,
        string? connectorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Content.Length > _ingressLimits.MaxDocumentBytes)
        {
            return new StatementImportValidationResult(
                false,
                0,
                [_ingressLimits.DocumentTooLarge(document.Content.Length).Message]);
        }

        var (connector, resolutionIssue) = ResolveConnector(document, connectorId);
        if (connector is null)
        {
            return new StatementImportValidationResult(false, 0, [resolutionIssue!.Message]);
        }

        var parse = await connector.ParseAsync(document, ct).ConfigureAwait(false);
        if (parse.TotalRetainedRows > _ingressLimits.MaxRecords)
        {
            return new StatementImportValidationResult(
                false,
                parse.Records.Count,
                [_ingressLimits.TooManyRecords().Message]);
        }

        var errors = parse.Issues
            .Where(static issue => string.Equals(issue.Severity, StatementParseIssue.ErrorSeverity, StringComparison.OrdinalIgnoreCase))
            .Select(static issue => issue.RowNumber is { } row ? $"Row {row}: {issue.Message}" : issue.Message)
            .ToArray();

        if (parse.HasErrors)
        {
            return new StatementImportValidationResult(false, parse.Records.Count, errors);
        }

        // A well-formed document that yields no canonical rows cannot be imported (CommitAsync rejects
        // it the same way), so report it as invalid rather than a passing empty validation.
        if (parse.Records.Count == 0)
        {
            return new StatementImportValidationResult(
                false,
                0,
                ["Statement produced no canonical records; nothing to import."]);
        }

        return new StatementImportValidationResult(true, parse.Records.Count, errors);
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

    private static void EnsureParsedAccountAuthority(
        StatementSourceDocument document,
        IReadOnlyList<StatementCanonicalRecord> records,
        string externalAccountId)
    {
        if (string.IsNullOrWhiteSpace(externalAccountId))
        {
            throw new InvalidDataException(
                "Statement import requires an authorized external account before parsed records can be retained.");
        }

        var authorizedAccountId = externalAccountId.Trim();
        if (!string.IsNullOrWhiteSpace(document.ExternalAccountId)
            && !AccountsMatch(document.ExternalAccountId, authorizedAccountId))
        {
            throw new InvalidDataException(
                "The uploaded statement account scope conflicts with the authorized external account.");
        }

        if (records.Any(record =>
                string.IsNullOrWhiteSpace(record.Account)
                || !AccountsMatch(record.Account, authorizedAccountId)))
        {
            throw new InvalidDataException(
                "Statement contains a missing or conflicting parsed account identity; every row must match the authorized external account.");
        }
    }

    private static bool AccountsMatch(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

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
                : "Review the per-column mappings and per-kind records, then commit the import into the reconciliation queue.")
        {
            AccountSnapshots = (parse?.AccountSnapshots ?? [])
                .Select(static snapshot => new StatementAccountSnapshotPreviewDto(
                    snapshot.ProviderId,
                    snapshot.AccountId,
                    snapshot.AsOf,
                    snapshot.Currency,
                    snapshot.Status,
                    snapshot.MarginRegime.ToString(),
                    snapshot.Cash,
                    snapshot.Equity,
                    snapshot.BuyingPower,
                    snapshot.InitialMargin,
                    snapshot.MaintenanceMargin,
                    snapshot.ExcessLiquidity,
                    snapshot.MarginLoan,
                    snapshot.Multiplier,
                    snapshot.TradingBlocked,
                    snapshot.TransfersBlocked,
                    snapshot.AccountBlocked,
                    snapshot.ShortingEnabled,
                    snapshot.OptionsApprovedLevel,
                    snapshot.OptionsTradingLevel,
                    snapshot.Restrictions ?? []))
                .ToArray(),
            ActivitySubtypeSummaries = (parse?.ActivityEvents ?? [])
                .GroupBy(static activity => new { activity.Category, activity.Subtype })
                .OrderBy(static group => group.Key.Category)
                .ThenBy(static group => group.Key.Subtype)
                .Select(static group => new StatementActivitySubtypeSummaryDto(
                    group.Key.Category.ToString(),
                    group.Key.Subtype.ToString(),
                    group.Count()))
                .ToArray(),
            ActivityCompleteness = (parse?.ActivityCursors ?? [])
                .Select(static cursor => new StatementActivityCompletenessDto(
                    cursor.LastEventId,
                    cursor.HighWatermark,
                    cursor.PageCount,
                    cursor.SourceRecordCount,
                    cursor.IsComplete))
                .ToArray(),
            TaxLotCount = parse?.TaxLots?.Count ?? 0,
            BorrowPositionCount = parse?.BorrowPositions?.Count ?? 0
        };
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
                        record.ExternalTransactionId,
                        record.ActivityCategory,
                        record.ActivitySubtype,
                        record.ProviderActivityCode,
                        record.RelatedTransactionId,
                        record.OrderId,
                        record.Description))
                    .ToArray()))
            .ToArray();

    /// <summary>
    /// Renders records to the canonical CSV artifact deterministically: fixed column order,
    /// invariant formatting, LF endings, and reversible CSV quoting, so the same source file
    /// always produces byte-identical bytes and therefore the same duplicate key.
    /// </summary>
    private static string ComputeSha256Hex(ReadOnlySpan<byte> content)
        => Sha256Digest.Compute(content);

    internal static string RenderCanonicalArtifact(IReadOnlyList<StatementCanonicalRecord> records)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', CanonicalArtifactHeader)).Append('\n');
        foreach (var record in records)
        {
            builder
                .Append(EncodeArtifactValue(record.Account)).Append(',')
                .Append(EncodeArtifactValue(record.Symbol)).Append(',')
                .Append(record.Quantity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(record.Price.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(record.CashAmount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(EncodeArtifactValue(record.ActivityType)).Append(',')
                .Append(record.TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(record.SettlementDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(EncodeArtifactValue(record.Currency)).Append(',')
                .Append(EncodeArtifactValue(record.FeesCommission?.ToString(CultureInfo.InvariantCulture))).Append(',')
                .Append(EncodeArtifactValue(record.ExternalTransactionId)).Append(',')
                .Append(EncodeArtifactValue(record.ActivityCategory)).Append(',')
                .Append(EncodeArtifactValue(record.ActivitySubtype)).Append(',')
                .Append(EncodeArtifactValue(record.ProviderActivityCode)).Append(',')
                .Append(EncodeArtifactValue(record.RelatedTransactionId)).Append(',')
                .Append(EncodeArtifactValue(record.OrderId)).Append(',')
                .Append(EncodeArtifactValue(record.Description)).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Encodes one canonical value without discarding source characters. The downstream parser
    /// accepts quoted commas, doubled quotes, and quoted line breaks.
    /// </summary>
    private static string EncodeArtifactValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <summary>
    /// Renders an issue for an exception message with its stable code intact. Commit reports failures by
    /// throwing, while preview and validate return the issue objects, so a caller that routed on
    /// StatementIngressLimits codes could see STATEMENT_DOCUMENT_TOO_LARGE from one path and only prose
    /// from the other for the very same document. The code is the part an operator or a client can act
    /// on programmatically, so it belongs in the text when the text is all that survives.
    /// </summary>
    private static string Describe(StatementParseIssue issue)
        => issue.RowNumber is { } row
            ? $"[{issue.Code}] Row {row}: {issue.Message}"
            : $"[{issue.Code}] {issue.Message}";

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

        try
        {
            RootedPathGuard.ValidatePathSegment(safeName, nameof(fileName));
            return safeName;
        }
        catch (ArgumentException)
        {
            return "statement.dat";
        }
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
