using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Core.IO;
using Meridian.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>
/// Production statement-evidence adapter. It copies the import service's retained source bytes
/// into the statement workflow's content-addressed authority and projects the verified source into
/// the shared Evidence Workbench without moving reconciliation or reporting authority.
/// </summary>
public sealed class ReportingStatementImportEvidenceRetainer : IStatementImportEvidenceRetainer
{
    private const string StatementRunSubjectKind = "statement-run";
    private const int EvidenceSchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IStatementReconciliationReportAuthorityStore authorityStore;
    private readonly IEvidenceArtifactStore evidenceStore;
    private readonly string _dataRoot;
    private readonly RootedPathGuard _retainedPathGuard;
    private readonly SemaphoreSlim _evidenceWorkbenchProjectionGate = new(1, 1);

    public ReportingStatementImportEvidenceRetainer(
        IStatementReconciliationReportAuthorityStore authorityStore,
        string dataRoot)
        : this(
            authorityStore,
            dataRoot,
            new FileEvidenceArtifactStore(
                dataRoot,
                NullLogger<FileEvidenceArtifactStore>.Instance))
    {
    }

    public ReportingStatementImportEvidenceRetainer(
        IStatementReconciliationReportAuthorityStore authorityStore,
        string dataRoot,
        IEvidenceArtifactStore evidenceStore)
    {
        this.authorityStore = authorityStore
            ?? throw new ArgumentNullException(nameof(authorityStore));
        this.evidenceStore = evidenceStore
            ?? throw new ArgumentNullException(nameof(evidenceStore));
        _dataRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(dataRoot)
                ? throw new ArgumentException(
                    "Statement evidence data root is required.",
                    nameof(dataRoot))
                : dataRoot);
        _retainedPathGuard = new RootedPathGuard(_dataRoot);
    }

    /// <summary>
    /// Durable authority used for raw, canonical, and run-evidence retention. Exposed so
    /// production composition can prove the workflow and evidence adapter share one authority.
    /// </summary>
    public IStatementReconciliationReportAuthorityStore AuthorityStore => authorityStore;

    /// <summary>True because this adapter retains hash-verified canonical run evidence.</summary>
    public bool RetainsCanonicalRunEvidence => true;

    public async Task<StatementImportCommitResultDto> RetainAsync(
        StatementImportCommitResultDto result,
        StatementImportEvidenceBridgeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(request);

        if (!authorityStore.IsDurableAuthority)
        {
            throw new InvalidOperationException(
                "Production statement evidence retention requires a durable statement authority.");
        }

        var scope = new StatementReconciliationReportAuthorityScope(
            RequireIdentity(request.TenantId, "tenant"),
            RequireIdentity(request.CompanyId, "company"),
            RequireIdentity(request.WorkflowId, "workflow"));
        var statusRoute = BuildStatusRoute(scope.WorkflowId);
        var evidenceWorkbenchRoute = BuildEvidenceWorkbenchRoute(result.RunId);
        var reconciliationRoute = StatementImportEvidenceBridge.BuildReconciliationRoute(result.RunId);
        if (result.EvidenceVaultIdentity is { } retainedIdentity)
        {
            var verifiedEvidence = await TryGetVerifiedCanonicalRunEvidenceAsync(
                    scope,
                    result,
                    request,
                    statusRoute,
                    retainedIdentity,
                    ct)
                .ConfigureAwait(false);
            if (verifiedEvidence is not null)
            {
                var scopedRetainedIdentity = retainedIdentity with
                {
                    TenantId = request.TenantId,
                    Scope = request.CompanyId
                };
                var sourceArtifact = scopedRetainedIdentity.Artifacts.Single(static artifact =>
                    string.Equals(artifact.Kind, "statement-source", StringComparison.Ordinal));
                await EnsureEvidenceWorkbenchProjectionAsync(
                        result,
                        request,
                        reconciliationRoute,
                        sourceArtifact.ContentHashSha256,
                        retainedSourcePath: null,
                        verifiedEvidence.SourceContent,
                        ct)
                    .ConfigureAwait(false);
                return BuildRetainedResult(
                    result,
                    scopedRetainedIdentity,
                    evidenceWorkbenchRoute,
                    reconciliationRoute);
            }
        }

        var sourcePath = ResolveRetainedPath(result.RetainedSourcePath, "source");
        var canonicalPath = ResolveRetainedPath(result.RetainedCanonicalPath, "canonical");
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        var canonicalBytes = await File.ReadAllBytesAsync(canonicalPath, ct).ConfigureAwait(false);
        if (sourceBytes.Length == 0)
        {
            throw new InvalidDataException(
                "Statement import retained an empty source document; evidence authority was not written.");
        }
        if (canonicalBytes.Length == 0)
        {
            throw new InvalidDataException(
                "Statement import retained an empty canonical document; evidence authority was not written.");
        }

        var vaultId = BuildVaultId(scope, result.RunId);
        var sourceKey = BuildSourceKey(vaultId, result.RetainedSourcePath);
        var canonicalKey = BuildCanonicalKey(vaultId, result.RetainedCanonicalPath);
        var runEvidenceKey = BuildRunEvidenceKey(vaultId);
        var sourceDocument = await authorityStore
            .WriteDocumentAsync(scope, sourceKey, sourceBytes, isImmutable: true, ct)
            .ConfigureAwait(false);
        var canonicalDocument = await authorityStore
            .WriteDocumentAsync(scope, canonicalKey, canonicalBytes, isImmutable: true, ct)
            .ConfigureAwait(false);
        var runEvidencePayload = BuildRunEvidence(
            result,
            sourceDocument.Identity.ContentHashSha256,
            canonicalDocument.Identity.ContentHashSha256);
        var runEvidenceBytes = JsonSerializer.SerializeToUtf8Bytes(
            runEvidencePayload,
            JsonOptions);
        var runEvidenceDocument = await authorityStore
            .WriteDocumentAsync(
                scope,
                runEvidenceKey,
                runEvidenceBytes,
                isImmutable: true,
                ct)
            .ConfigureAwait(false);
        var manifestPayload = BuildManifest(
            scope,
            vaultId,
            result,
            request,
            sourceDocument,
            canonicalDocument,
            runEvidenceDocument);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifestPayload, JsonOptions);
        var manifestKey = $"evidence/{vaultId}/manifest.json";
        var manifestDocument = await authorityStore
            .WriteDocumentAsync(scope, manifestKey, manifestBytes, isImmutable: true, ct)
            .ConfigureAwait(false);

        var artifacts = BuildArtifacts(
            result,
            statusRoute,
            sourceDocument,
            canonicalDocument,
            runEvidenceDocument);
        var identity = new EvidenceVaultIdentityDto(
            VaultId: vaultId,
            SubjectKind: StatementRunSubjectKind,
            SubjectId: result.RunId,
            ManifestPath: manifestDocument.DocumentKey,
            ManifestRoute: statusRoute,
            RetainedAt: manifestDocument.StoredAtUtc,
            ContentHashSha256: manifestDocument.Identity.ContentHashSha256,
            SchemaVersion: EvidenceSchemaVersion,
            StorageKind: authorityStore.StorageKind)
        {
            TenantId = request.TenantId,
            Scope = request.CompanyId,
            Artifacts = artifacts
        };

        await EnsureEvidenceWorkbenchProjectionAsync(
                result,
                request,
                reconciliationRoute,
                sourceDocument.Identity.ContentHashSha256,
                sourcePath,
                retainedSourceContent: null,
                ct)
            .ConfigureAwait(false);

        return BuildRetainedResult(
            result,
            identity,
            evidenceWorkbenchRoute,
            reconciliationRoute);
    }

    private async Task EnsureEvidenceWorkbenchProjectionAsync(
        StatementImportCommitResultDto result,
        StatementImportEvidenceBridgeRequest request,
        string reconciliationRoute,
        string sourceContentHashSha256,
        string? retainedSourcePath,
        byte[]? retainedSourceContent,
        CancellationToken ct)
    {
        await _evidenceWorkbenchProjectionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureEvidenceWorkbenchProjectionCoreAsync(
                    result,
                    request,
                    reconciliationRoute,
                    sourceContentHashSha256,
                    retainedSourcePath,
                    retainedSourceContent,
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _evidenceWorkbenchProjectionGate.Release();
        }
    }

    private async Task EnsureEvidenceWorkbenchProjectionCoreAsync(
        StatementImportCommitResultDto result,
        StatementImportEvidenceBridgeRequest request,
        string reconciliationRoute,
        string sourceContentHashSha256,
        string? retainedSourcePath,
        byte[]? retainedSourceContent,
        CancellationToken ct)
    {
        var expectedIntake = StatementImportEvidenceBridge.BuildIntakeRequest(
            result,
            request,
            result.RetainedSourcePath,
            reconciliationRoute,
            sourceContentHashSha256);
        var query = new EvidenceVaultDocumentQueryDto(
            Classification: EvidenceDocumentClassificationDto.Statement,
            ChannelKind: EvidenceDocumentIntakeChannelDto.ImportedFileReference,
            SubjectKind: StatementRunSubjectKind,
            SubjectId: result.RunId,
            TenantId: request.TenantId,
            Scope: request.CompanyId,
            MaxResults: 100);
        var existing = await evidenceStore
            .ListDocumentsAsync(query, ct)
            .ConfigureAwait(false);
        if (existing.Any(entry =>
                MatchesEvidenceWorkbenchProjection(
                    entry.Document,
                    expectedIntake,
                    sourceContentHashSha256)))
        {
            return;
        }

        retainedSourcePath ??= retainedSourceContent is null
            ? ResolveRetainedPath(result.RetainedSourcePath, "source")
            : result.RetainedSourcePath;
        var intakeRequest = StatementImportEvidenceBridge.BuildIntakeRequest(
            result,
            request,
            retainedSourcePath,
            reconciliationRoute,
            sourceContentHashSha256);
        if (retainedSourceContent is not null)
        {
            intakeRequest = intakeRequest with
            {
                ContentBase64 = Convert.ToBase64String(retainedSourceContent),
                IntakeSource = new EvidenceDocumentIntakeSourceDto(
                    EvidenceDocumentIntakeSourceKindDto.UploadedContent,
                    DisplayName: result.RetainedSourcePath,
                    ExpectedContentHashSha256: sourceContentHashSha256)
            };
        }

        var retained = await evidenceStore
            .WriteIntakeArtifactAsync(intakeRequest, ct)
            .ConfigureAwait(false);
        if (retained.Document is not { } retainedDocument
            || !MatchesEvidenceWorkbenchProjection(
                retainedDocument,
                expectedIntake,
                sourceContentHashSha256))
        {
            throw new InvalidDataException(
                "Evidence Workbench did not retain the authority-verified Statement document projection.");
        }

        var queryProof = await evidenceStore
            .ListDocumentsAsync(query, ct)
            .ConfigureAwait(false);
        if (!queryProof.Any(entry =>
                string.Equals(entry.VaultId, retained.VaultIdentity.VaultId, StringComparison.Ordinal)
                && MatchesEvidenceWorkbenchProjection(
                    entry.Document,
                    expectedIntake,
                    sourceContentHashSha256)))
        {
            throw new InvalidDataException(
                "Evidence Workbench retained the Statement document but did not return it from the canonical query seam.");
        }
    }

    private static bool MatchesEvidenceWorkbenchProjection(
        EvidenceDocumentDto document,
        EvidenceVaultIntakeRequestDto expected,
        string sourceContentHashSha256)
    {
        var sourceRecord = document.SourceRecord;
        if (document.Classification != EvidenceDocumentClassificationDto.Statement
            || !string.Equals(document.FileName, expected.FileName, StringComparison.Ordinal)
            || !string.Equals(
                document.SourceHashSha256,
                sourceContentHashSha256,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(document.SourceChannel, expected.IntakeChannel, StringComparison.Ordinal)
            || !string.Equals(document.Actor, expected.Actor, StringComparison.Ordinal)
            || !string.Equals(document.TenantId, expected.TenantId, StringComparison.Ordinal)
            || !string.Equals(document.Scope, expected.Scope, StringComparison.Ordinal)
            || document.ChannelKind != EvidenceDocumentIntakeChannelDto.ImportedFileReference
            || !string.Equals(document.ExtractorId, expected.ExtractorId, StringComparison.Ordinal)
            || !string.Equals(document.ContentType, expected.ContentType, StringComparison.Ordinal)
            || !string.Equals(document.SourceSystem, expected.SourceSystem, StringComparison.Ordinal)
            || !string.Equals(document.SourceReference, expected.SourceReference, StringComparison.Ordinal)
            || sourceRecord is null
            || !string.Equals(
                sourceRecord.SourceHashSha256,
                sourceContentHashSha256,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                sourceRecord.ReceiptHash,
                sourceContentHashSha256,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(sourceRecord.SourceChannel, expected.IntakeChannel, StringComparison.Ordinal)
            || sourceRecord.ChannelKind != EvidenceDocumentIntakeChannelDto.ImportedFileReference
            || !string.Equals(sourceRecord.Actor, expected.Actor, StringComparison.Ordinal)
            || !string.Equals(sourceRecord.TenantId, expected.TenantId, StringComparison.Ordinal)
            || !string.Equals(sourceRecord.Scope, expected.Scope, StringComparison.Ordinal)
            || !string.Equals(sourceRecord.SourceSystem, expected.SourceSystem, StringComparison.Ordinal)
            || !string.Equals(sourceRecord.SourceReference, expected.SourceReference, StringComparison.Ordinal))
        {
            return false;
        }

        if (!expected.ObjectLinks.All(expectedLink =>
                document.ObjectLinks.Any(actualLink =>
                    actualLink.LinkKind == expectedLink.LinkKind
                    && string.Equals(actualLink.ObjectId, expectedLink.ObjectId, StringComparison.Ordinal)
                    && string.Equals(actualLink.Relationship, expectedLink.Relationship, StringComparison.Ordinal))))
        {
            return false;
        }

        if (!(expected.ExtractedFields ?? []).All(expectedField =>
                document.ExtractedFields.Any(actualField =>
                    string.Equals(actualField.FieldName, expectedField.FieldName, StringComparison.Ordinal)
                    && string.Equals(actualField.ExtractedValue, expectedField.ExtractedValue, StringComparison.Ordinal)
                    && actualField.ValidationStatus == expectedField.ValidationStatus)))
        {
            return false;
        }

        return document.AuditTrail.Any(auditEvent =>
            string.Equals(auditEvent.Action, "DocumentIntakeRetained", StringComparison.Ordinal)
            && string.Equals(auditEvent.Actor, expected.Actor, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(auditEvent.CorrelationId));
    }

    private static StatementImportCommitResultDto BuildRetainedResult(
        StatementImportCommitResultDto result,
        EvidenceVaultIdentityDto identity,
        string evidenceWorkbenchRoute,
        string reconciliationRoute) =>
        result with
        {
            EvidenceVaultIdentity = identity,
            EvidenceWorkbenchRoute = evidenceWorkbenchRoute,
            ReconciliationRoute = reconciliationRoute,
            NextActions =
            [
                "Review the authority-retained source, canonical statement, and run evidence from the statement reconciliation workflow.",
                .. StatementImportEvidenceBridge.BuildNextActions(result)
            ]
        };

    internal async Task<bool> HasVerifiedCanonicalRunEvidenceAsync(
        StatementImportCommitResultDto result,
        StatementImportEvidenceBridgeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(request);
        if (!authorityStore.IsDurableAuthority
            || result.EvidenceVaultIdentity is not { } identity)
        {
            return false;
        }

        var scope = new StatementReconciliationReportAuthorityScope(
            RequireIdentity(request.TenantId, "tenant"),
            RequireIdentity(request.CompanyId, "company"),
            RequireIdentity(request.WorkflowId, "workflow"));
        return await TryGetVerifiedCanonicalRunEvidenceAsync(
                scope,
                result,
                request,
                BuildStatusRoute(scope.WorkflowId),
                identity,
                ct)
            .ConfigureAwait(false) is not null;
    }

    private async Task<VerifiedCanonicalRunEvidence?> TryGetVerifiedCanonicalRunEvidenceAsync(
        StatementReconciliationReportAuthorityScope scope,
        StatementImportCommitResultDto result,
        StatementImportEvidenceBridgeRequest request,
        string statusRoute,
        EvidenceVaultIdentityDto identity,
        CancellationToken ct)
    {
        var expectedVaultId = BuildVaultId(scope, result.RunId);
        var expectedManifestKey = $"evidence/{expectedVaultId}/manifest.json";
        if (!string.Equals(identity.StorageKind, authorityStore.StorageKind, StringComparison.Ordinal)
            || identity.SchemaVersion != EvidenceSchemaVersion
            || !string.Equals(identity.SubjectKind, StatementRunSubjectKind, StringComparison.Ordinal)
            || !string.Equals(identity.SubjectId, result.RunId, StringComparison.Ordinal)
            || !string.Equals(identity.VaultId, expectedVaultId, StringComparison.Ordinal)
            || !string.Equals(identity.ManifestPath, expectedManifestKey, StringComparison.Ordinal)
            || !string.Equals(identity.ManifestRoute, statusRoute, StringComparison.Ordinal)
            || !Sha256Digest.IsCanonical(identity.ContentHashSha256)
            || identity.Artifacts.Count != 3)
        {
            return null;
        }

        try
        {
            var manifest = await TryReadVerifiedDocumentAsync(
                    scope,
                    expectedManifestKey,
                    ct)
                .ConfigureAwait(false);
            if (manifest is null
                || identity.RetainedAt != manifest.Document.StoredAtUtc
                || !string.Equals(
                    manifest.Document.Identity.ContentHashSha256,
                    identity.ContentHashSha256,
                    StringComparison.Ordinal))
            {
                return null;
            }

            var source = await TryReadVerifiedDocumentAsync(
                    scope,
                    BuildSourceKey(expectedVaultId, result.RetainedSourcePath),
                    ct)
                .ConfigureAwait(false);
            var canonical = await TryReadVerifiedDocumentAsync(
                    scope,
                    BuildCanonicalKey(expectedVaultId, result.RetainedCanonicalPath),
                    ct)
                .ConfigureAwait(false);
            var runEvidence = await TryReadVerifiedDocumentAsync(
                    scope,
                    BuildRunEvidenceKey(expectedVaultId),
                    ct)
                .ConfigureAwait(false);
            if (source is null || canonical is null || runEvidence is null)
            {
                return null;
            }

            var expectedRunEvidenceBytes = JsonSerializer.SerializeToUtf8Bytes(
                BuildRunEvidence(
                    result,
                    source.Document.Identity.ContentHashSha256,
                    canonical.Document.Identity.ContentHashSha256),
                JsonOptions);
            if (!runEvidence.Content.AsSpan().SequenceEqual(expectedRunEvidenceBytes))
            {
                return null;
            }

            var expectedManifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                BuildManifest(
                    scope,
                    expectedVaultId,
                    result,
                    request,
                    source.Document,
                    canonical.Document,
                    runEvidence.Document),
                JsonOptions);
            if (!manifest.Content.AsSpan().SequenceEqual(expectedManifestBytes))
            {
                return null;
            }

            var expectedArtifacts = BuildArtifacts(
                result,
                statusRoute,
                source.Document,
                canonical.Document,
                runEvidence.Document);
            return ArtifactsMatch(identity.Artifacts, expectedArtifacts)
                ? new VerifiedCanonicalRunEvidence(source.Content)
                : null;
        }
        catch (ReportingArtifactIntegrityException)
        {
            return null;
        }
    }

    private async Task<VerifiedAuthorityDocument?> TryReadVerifiedDocumentAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken ct)
    {
        var document = await authorityStore
            .GetDocumentAsync(scope, documentKey, ct)
            .ConfigureAwait(false);
        var content = await authorityStore
            .TryReadDocumentAsync(scope, documentKey, ct)
            .ConfigureAwait(false);
        if (document is null
            || content is null
            || !document.IsImmutable
            || document.Scope != scope
            || !string.Equals(document.DocumentKey, documentKey, StringComparison.Ordinal)
            || document.ByteSize != content.LongLength
            || !Sha256Digest.IsCanonical(document.Identity.ContentHashSha256)
            || !string.Equals(
                ComputeHash(content),
                document.Identity.ContentHashSha256,
                StringComparison.Ordinal))
        {
            return null;
        }

        return new VerifiedAuthorityDocument(document, content);
    }

    private sealed record VerifiedCanonicalRunEvidence(byte[] SourceContent);

    private static bool ArtifactsMatch(
        IReadOnlyList<EvidenceVaultArtifactDto> actual,
        IReadOnlyList<EvidenceVaultArtifactDto> expected) =>
        actual.Count == expected.Count
        && actual.Zip(expected).All(static pair =>
            JsonSerializer.Serialize(pair.First, JsonOptions)
                .Equals(
                    JsonSerializer.Serialize(pair.Second, JsonOptions),
                    StringComparison.Ordinal));

    private string ResolveRetainedPath(string retainedPath, string kind)
    {
        if (string.IsNullOrWhiteSpace(retainedPath))
        {
            throw new InvalidOperationException(
                $"Statement import did not return a retained {kind} path.");
        }

        var candidate = retainedPath.Trim();
        var fullPath = Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(Path.Combine(
                _dataRoot,
                candidate.Replace('/', Path.DirectorySeparatorChar)));
        var root = _dataRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, PathComparison))
        {
            throw new InvalidOperationException(
                $"Statement import retained {kind} path is outside the configured data root.");
        }

        _retainedPathGuard.EnsurePath(fullPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Statement import retained {kind} file was not found.",
                fullPath);
        }

        return fullPath;
    }

    private static StatementEvidenceManifest BuildManifest(
        StatementReconciliationReportAuthorityScope scope,
        string vaultId,
        StatementImportCommitResultDto result,
        StatementImportEvidenceBridgeRequest request,
        StatementReconciliationReportAuthorityDocument sourceDocument,
        StatementReconciliationReportAuthorityDocument canonicalDocument,
        StatementReconciliationReportAuthorityDocument runEvidenceDocument) =>
        new(
            SchemaVersion: EvidenceSchemaVersion,
            VaultId: vaultId,
            TenantId: scope.TenantId,
            CompanyId: scope.CompanyId,
            WorkflowId: scope.WorkflowId,
            SubjectKind: StatementRunSubjectKind,
            SubjectId: result.RunId,
            SourceKind: request.SourceKind,
            SourceInstitution: request.SourceInstitution,
            FundAccountId: request.FundAccountId,
            ExternalAccountId: request.ExternalAccountId,
            PeriodStart: request.PeriodStart,
            PeriodEnd: request.PeriodEnd,
            ImportedBy: request.ImportedBy,
            SourceDocumentKey: sourceDocument.DocumentKey,
            SourceContentHashSha256: sourceDocument.Identity.ContentHashSha256,
            SourceByteSize: sourceDocument.ByteSize,
            SourceRetainedAtUtc: sourceDocument.StoredAtUtc,
            SourceReference: result.RetainedSourcePath,
            CanonicalDocumentKey: canonicalDocument.DocumentKey,
            CanonicalContentHashSha256: canonicalDocument.Identity.ContentHashSha256,
            CanonicalByteSize: canonicalDocument.ByteSize,
            CanonicalRetainedAtUtc: canonicalDocument.StoredAtUtc,
            CanonicalReference: result.RetainedCanonicalPath,
            RunEvidenceDocumentKey: runEvidenceDocument.DocumentKey,
            RunEvidenceContentHashSha256: runEvidenceDocument.Identity.ContentHashSha256,
            RunEvidenceByteSize: runEvidenceDocument.ByteSize,
            RunEvidenceRetainedAtUtc: runEvidenceDocument.StoredAtUtc,
            ReconciliationRunId: result.RunId);

    private static StatementCanonicalRunEvidence BuildRunEvidence(
        StatementImportCommitResultDto result,
        string sourceContentHashSha256,
        string canonicalContentHashSha256) =>
        new(
            SchemaVersion: EvidenceSchemaVersion,
            RunId: result.RunId,
            RecordCount: result.RecordCount,
            BreakCount: result.BreakCount,
            CaseCount: result.CaseCount,
            KindSummaries: result.KindSummaries
                .Select(static item => new StatementRunKindEvidence(
                    item.Kind,
                    item.RecordCount))
                .OrderBy(static item => item.Kind, StringComparer.Ordinal)
                .ThenBy(static item => item.RecordCount)
                .ToArray(),
            BreakIds: NormalizeIdentities(result.BreakIds),
            CaseIds: NormalizeIdentities(result.CaseIds),
            CaseLinks: result.ReconciliationCaseLinks
                .Where(static item => !string.IsNullOrWhiteSpace(item.CaseId))
                .Select(static item => new StatementRunCaseLinkEvidence(
                    item.CaseId.Trim(),
                    string.IsNullOrWhiteSpace(item.BreakId) ? null : item.BreakId.Trim()))
                .Distinct()
                .OrderBy(static item => item.CaseId, StringComparer.Ordinal)
                .ThenBy(static item => item.BreakId, StringComparer.Ordinal)
                .ToArray(),
            SourceContentHashSha256: sourceContentHashSha256,
            CanonicalContentHashSha256: canonicalContentHashSha256);

    private static IReadOnlyList<EvidenceVaultArtifactDto> BuildArtifacts(
        StatementImportCommitResultDto result,
        string statusRoute,
        StatementReconciliationReportAuthorityDocument sourceDocument,
        StatementReconciliationReportAuthorityDocument canonicalDocument,
        StatementReconciliationReportAuthorityDocument runEvidenceDocument) =>
        [
            BuildArtifact(
                "statement-source",
                result.RetainedSourcePath,
                statusRoute,
                result.RunId,
                sourceDocument),
            BuildArtifact(
                "statement-canonical",
                result.RetainedCanonicalPath,
                statusRoute,
                result.RunId,
                canonicalDocument),
            BuildArtifact(
                "statement-run-evidence",
                sourcePath: null,
                statusRoute,
                result.RunId,
                runEvidenceDocument)
        ];

    private static EvidenceVaultArtifactDto BuildArtifact(
        string kind,
        string? sourcePath,
        string statusRoute,
        string runId,
        StatementReconciliationReportAuthorityDocument document) =>
        new(
            ArtifactId: $"{kind}-{document.Identity.ContentHashSha256[..16]}",
            Kind: kind,
            RelativePath: document.DocumentKey,
            ContentHashSha256: document.Identity.ContentHashSha256,
            SizeBytes: document.ByteSize,
            RetainedAt: document.StoredAtUtc,
            SourcePath: sourcePath,
            SourceRoute: statusRoute,
            CanonicalSubjectKind: StatementRunSubjectKind,
            CanonicalSubjectId: runId);

    private static IReadOnlyList<string> NormalizeIdentities(IEnumerable<string> identities) =>
        identities
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string BuildVaultId(
        StatementReconciliationReportAuthorityScope scope,
        string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var canonical = string.Join(
            '\n',
            scope.TenantId,
            scope.CompanyId,
            scope.WorkflowId,
            runId.Trim());
        var hash = Sha256Digest.ComputeUtf8(canonical);
        return $"statement-evidence-{hash[..32]}";
    }

    private static string BuildSourceKey(string vaultId, string sourceReference) =>
        $"evidence/{vaultId}/source/{SanitizeFileName(sourceReference)}";

    private static string BuildCanonicalKey(string vaultId, string canonicalReference) =>
        $"evidence/{vaultId}/canonical/{SanitizeFileName(canonicalReference)}";

    private static string BuildRunEvidenceKey(string vaultId) =>
        $"evidence/{vaultId}/run-evidence.json";

    private static string BuildStatusRoute(string workflowId) =>
        UiApiRoutes.WithParam(
            UiApiRoutes.ReconciliationStatementReconciliationReportById,
            "workflowId",
            workflowId);

    private static string BuildEvidenceWorkbenchRoute(string runId) =>
        "/reporting/evidence"
        + $"?subjectKind={Uri.EscapeDataString(StatementRunSubjectKind)}"
        + $"&subjectId={Uri.EscapeDataString(runId)}"
        + "&documentClassification=Statement";

    private static string ComputeHash(ReadOnlySpan<byte> content) =>
        Sha256Digest.Compute(content);

    private static string RequireIdentity(string? value, string kind) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Statement evidence retention requires an exact {kind} identity.")
            : value.Trim();

    private static string SanitizeFileName(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "statement.bin";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return fileName;
    }

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed record StatementEvidenceManifest(
        int SchemaVersion,
        string VaultId,
        string TenantId,
        string CompanyId,
        string WorkflowId,
        string SubjectKind,
        string SubjectId,
        string SourceKind,
        string SourceInstitution,
        string FundAccountId,
        string ExternalAccountId,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string ImportedBy,
        string SourceDocumentKey,
        string SourceContentHashSha256,
        long SourceByteSize,
        DateTimeOffset SourceRetainedAtUtc,
        string SourceReference,
        string CanonicalDocumentKey,
        string CanonicalContentHashSha256,
        long CanonicalByteSize,
        DateTimeOffset CanonicalRetainedAtUtc,
        string CanonicalReference,
        string RunEvidenceDocumentKey,
        string RunEvidenceContentHashSha256,
        long RunEvidenceByteSize,
        DateTimeOffset RunEvidenceRetainedAtUtc,
        string ReconciliationRunId);

    private sealed record StatementCanonicalRunEvidence(
        int SchemaVersion,
        string RunId,
        int RecordCount,
        int BreakCount,
        int CaseCount,
        IReadOnlyList<StatementRunKindEvidence> KindSummaries,
        IReadOnlyList<string> BreakIds,
        IReadOnlyList<string> CaseIds,
        IReadOnlyList<StatementRunCaseLinkEvidence> CaseLinks,
        string SourceContentHashSha256,
        string CanonicalContentHashSha256);

    private sealed record StatementRunKindEvidence(
        string Kind,
        int RecordCount);

    private sealed record StatementRunCaseLinkEvidence(
        string CaseId,
        string? BreakId);

    private sealed record VerifiedAuthorityDocument(
        StatementReconciliationReportAuthorityDocument Document,
        byte[] Content);
}
