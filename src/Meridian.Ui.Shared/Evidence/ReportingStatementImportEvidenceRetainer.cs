using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Core.IO;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>
/// Production statement-evidence adapter. It copies the import service's retained source bytes
/// into the statement workflow's content-addressed authority and records deterministic linkage.
/// The generic Evidence Workbench store remains a separate product surface.
/// </summary>
public sealed class ReportingStatementImportEvidenceRetainer(
    IStatementReconciliationReportAuthorityStore authorityStore,
    string dataRoot) : IStatementImportEvidenceRetainer
{
    private const string StatementRunSubjectKind = "statement-run";
    private const int EvidenceSchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _dataRoot = Path.GetFullPath(
        string.IsNullOrWhiteSpace(dataRoot)
            ? throw new ArgumentException("Statement evidence data root is required.", nameof(dataRoot))
            : dataRoot);
    private readonly RootedPathGuard _retainedPathGuard = new(dataRoot);

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
        if (result.EvidenceVaultIdentity is { } retainedIdentity
            && await HasVerifiedCanonicalRunEvidenceAsync(
                    scope,
                    result,
                    request,
                    statusRoute,
                    retainedIdentity,
                    ct)
                .ConfigureAwait(false))
        {
            return result;
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
            Artifacts = artifacts
        };

        return result with
        {
            EvidenceVaultIdentity = identity,
            EvidenceWorkbenchRoute = statusRoute,
            ReconciliationRoute =
                $"/accounting/reconciliation/match?runId={Uri.EscapeDataString(result.RunId)}",
            NextActions =
            [
                "Review the authority-retained source, canonical statement, and run evidence from the statement reconciliation workflow.",
                result.CaseCount > 0 || result.BreakCount > 0
                    ? "Review reconciliation cases linked to the retained statement and canonical run evidence."
                    : "Review the statement run before using it as close support."
            ]
        };
    }

    internal Task<bool> HasVerifiedCanonicalRunEvidenceAsync(
        StatementImportCommitResultDto result,
        StatementImportEvidenceBridgeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(request);
        if (!authorityStore.IsDurableAuthority
            || result.EvidenceVaultIdentity is not { } identity)
        {
            return Task.FromResult(false);
        }

        var scope = new StatementReconciliationReportAuthorityScope(
            RequireIdentity(request.TenantId, "tenant"),
            RequireIdentity(request.CompanyId, "company"),
            RequireIdentity(request.WorkflowId, "workflow"));
        return HasVerifiedCanonicalRunEvidenceAsync(
            scope,
            result,
            request,
            BuildStatusRoute(scope.WorkflowId),
            identity,
            ct);
    }

    private async Task<bool> HasVerifiedCanonicalRunEvidenceAsync(
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
            || !IsSha256(identity.ContentHashSha256)
            || identity.Artifacts.Count != 3)
        {
            return false;
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
                return false;
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
                return false;
            }

            var expectedRunEvidenceBytes = JsonSerializer.SerializeToUtf8Bytes(
                BuildRunEvidence(
                    result,
                    source.Document.Identity.ContentHashSha256,
                    canonical.Document.Identity.ContentHashSha256),
                JsonOptions);
            if (!runEvidence.Content.AsSpan().SequenceEqual(expectedRunEvidenceBytes))
            {
                return false;
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
                return false;
            }

            var expectedArtifacts = BuildArtifacts(
                result,
                statusRoute,
                source.Document,
                canonical.Document,
                runEvidence.Document);
            return identity.Artifacts.SequenceEqual(expectedArtifacts);
        }
        catch (ReportingArtifactIntegrityException)
        {
            return false;
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
            || !IsSha256(document.Identity.ContentHashSha256)
            || !string.Equals(
                ComputeHash(content),
                document.Identity.ContentHashSha256,
                StringComparison.Ordinal))
        {
            return null;
        }

        return new VerifiedAuthorityDocument(document, content);
    }

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
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
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

    private static string ComputeHash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

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
