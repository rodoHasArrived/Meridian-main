using System.Collections.Immutable;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportingGovernanceCoordinatorService
{
    private static void ValidateProduction(
        ReportingOutputManifest manifest,
        ReportingGovernedArtifactProduction production)
    {
        ArgumentNullException.ThrowIfNull(production);
        ArgumentNullException.ThrowIfNull(production.Certification);
        if (!string.Equals(production.RunId, manifest.RunId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(production.ManifestArtifactId)
            || production.Artifacts.IsDefaultOrEmpty
            || production.Artifacts.Any(artifact =>
                string.IsNullOrWhiteSpace(artifact.ArtifactId)
                || string.IsNullOrWhiteSpace(artifact.FileName)
                || string.IsNullOrWhiteSpace(artifact.ContentType)
                || artifact.Content.IsEmpty)
            || production.Artifacts.Select(static artifact => artifact.ArtifactId).Distinct(StringComparer.Ordinal).Count()
                != production.Artifacts.Length
            || production.Artifacts.Count(artifact =>
                string.Equals(artifact.ArtifactId, production.ManifestArtifactId, StringComparison.Ordinal)) != 1)
        {
            throw new ReportingGovernanceException(
                $"Renderer output for run '{manifest.RunId}' is incomplete, duplicated, or not bound to its manifest.");
        }

        var certification = production.Certification;
        if (!certification.IsAuthoritative
            || certification.AsOfDate != manifest.AsOfDate
            || !Equals(certification.Scope, manifest.OperationalScope)
            || !AccessScopesEqual(certification.Access, manifest.ImmutableAccessScope)
            || !Equals(certification.Snapshot, manifest.CertifiedSnapshot)
            || string.IsNullOrWhiteSpace(certification.SourceCheckpointId)
            || !Sha256Digest.IsWellFormed(certification.SourceCheckpointHash)
            || certification.EvidenceIds.IsDefaultOrEmpty
            || certification.EvidenceIds.Any(string.IsNullOrWhiteSpace)
            || certification.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != certification.EvidenceIds.Length
            || !string.Equals(
                certification.SourceCheckpointId,
                certification.Snapshot.SourceCheckpointId,
                StringComparison.Ordinal)
            || !string.Equals(
                certification.SourceCheckpointHash,
                certification.Snapshot.SourceCheckpointHash,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                certification.SourceCheckpointId,
                certification.Snapshot.ReconciliationCheckpointId,
                StringComparison.Ordinal)
            || !Sha256Digest.IsWellFormed(certification.Snapshot.ReconciliationCheckpointHash)
            || !certification.EvidenceIds.Contains(
                BuildSourceCheckpointEvidence(
                    certification.SourceCheckpointId,
                    certification.SourceCheckpointHash),
                StringComparer.Ordinal)
            || certification.EvidenceIds.Any(evidence =>
                !manifest.Readiness!.Checks.SelectMany(static check => check.EvidenceReferences)
                    .Contains(evidence, StringComparer.Ordinal)))
        {
            throw new ReportingGovernanceException(
                $"Renderer output for run '{manifest.RunId}' has no authoritative point-in-time source checkpoint bound to its immutable certification. Mutable workflow projection rows are not certifiable.");
        }

        var declared = manifest.Artifacts.OrderBy(static artifact => artifact, StringComparer.Ordinal).ToArray();
        var produced = production.Artifacts
            .Select(static artifact => artifact.ArtifactId)
            .OrderBy(static artifact => artifact, StringComparer.Ordinal)
            .ToArray();
        if (!declared.SequenceEqual(produced, StringComparer.Ordinal))
        {
            throw new ReportingGovernanceException(
                $"Renderer output for run '{manifest.RunId}' does not exactly match the declared artifact set.");
        }
    }

    private void ValidateRenderedManifestBinding(
        ReportingOutputManifest manifest,
        GovernedReportingRun run,
        ReportingGovernedArtifactProduction production)
    {
        var renderedManifest = production.Artifacts.Single(artifact =>
            string.Equals(artifact.ArtifactId, production.ManifestArtifactId, StringComparison.Ordinal));
        ReportingRetainedManifestDocument retainedDocument;
        try
        {
            retainedDocument = DeterministicReportingCertifiedArtifactProducer.ParseRetainedManifest(
                renderedManifest.Content.Span);
            ValidateRetainedManifestBinding(run, retainedDocument);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ReportingGovernanceException(
                $"Renderer manifest for run '{run.RunId}' failed pre-retention integrity validation: {exception.Message}",
                exception);
        }

        Dictionary<string, ReportingRetainedManifestArtifactDescriptor> descriptors;
        try
        {
            descriptors = retainedDocument.Artifacts.ToDictionary(
                static descriptor => descriptor.ArtifactId,
                StringComparer.Ordinal);
        }
        catch (ArgumentException exception)
        {
            throw new ReportingGovernanceException(
                $"Renderer manifest for run '{run.RunId}' contains duplicate artifact descriptors.",
                exception);
        }
        if (descriptors.Count != production.Artifacts.Length)
        {
            throw new ReportingGovernanceException(
                $"Renderer manifest for run '{run.RunId}' does not describe the exact produced artifact set.");
        }

        foreach (var artifact in production.Artifacts)
        {
            var isManifest = string.Equals(
                artifact.ArtifactId,
                production.ManifestArtifactId,
                StringComparison.Ordinal);
            var contentHash = ComputeSha256(artifact.Content.Span);
            if (!descriptors.TryGetValue(artifact.ArtifactId, out var descriptor)
                || !string.Equals(descriptor.FileName, artifact.FileName, StringComparison.Ordinal)
                || !string.Equals(descriptor.ContentType, artifact.ContentType, StringComparison.Ordinal)
                || (isManifest
                    ? descriptor.ByteLength is not null || descriptor.ContentHashSha256 is not null
                    : descriptor.ByteLength != artifact.Content.Length
                      || !string.Equals(
                          descriptor.ContentHashSha256,
                          contentHash,
                          StringComparison.OrdinalIgnoreCase)))
            {
                throw new ReportingGovernanceException(
                    $"Renderer manifest descriptor for '{run.RunId}/{artifact.ArtifactId}' does not match the exact filename, content type, byte length, and content hash produced in this attempt.");
            }
        }

        ValidateCanonicalArtifactAndSectionBindings(
            manifest,
            production,
            retainedDocument,
            descriptors);
    }

    private static void ValidateCanonicalArtifactAndSectionBindings(
        ReportingOutputManifest manifest,
        ReportingGovernedArtifactProduction production,
        ReportingRetainedManifestDocument retainedDocument,
        IReadOnlyDictionary<string, ReportingRetainedManifestArtifactDescriptor> renderedDescriptors)
    {
        var canonicalDeclarations = ReportingArtifactDeclaration.Build(manifest);
        var producedById = production.Artifacts.ToDictionary(
            static artifact => artifact.ArtifactId,
            StringComparer.Ordinal);
        if (canonicalDeclarations.Length != producedById.Count
            || canonicalDeclarations.Length != renderedDescriptors.Count)
        {
            throw new ReportingGovernanceException(
                $"Renderer output for run '{manifest.RunId}' does not contain the exact canonical artifact descriptor set.");
        }

        foreach (var declaration in canonicalDeclarations)
        {
            // Artifact kind and GridId are accepted only from this canonical declaration. The
            // renderer may supply bytes and hashes, but cannot rename, retype, or reclassify them.
            if (!producedById.TryGetValue(declaration.ArtifactId, out var produced)
                || !renderedDescriptors.TryGetValue(declaration.ArtifactId, out var rendered)
                || !string.Equals(produced.FileName, declaration.FileName, StringComparison.Ordinal)
                || !string.Equals(produced.ContentType, declaration.ContentType, StringComparison.Ordinal)
                || !string.Equals(rendered.FileName, declaration.FileName, StringComparison.Ordinal)
                || !string.Equals(rendered.ContentType, declaration.ContentType, StringComparison.Ordinal)
                || !CanonicalGridBindingMatches(manifest, declaration))
            {
                throw new ReportingGovernanceException(
                    $"Renderer artifact descriptor for '{manifest.RunId}/{declaration.ArtifactId}' conflicts with its canonical id, filename, content type, artifact kind, or report-writer grid binding.");
            }
        }

        if (!CanonicalSectionsEqual(manifest.Sections, retainedDocument.Sections))
        {
            throw new ReportingGovernanceException(
                $"Renderer manifest for run '{manifest.RunId}' does not contain the exact canonical section identities, hashes, and lineage declared by orchestration.");
        }
    }

    private static bool CanonicalGridBindingMatches(
        ReportingOutputManifest manifest,
        ReportingDeclaredArtifact declaration)
    {
        if (declaration.Kind != ReportingDeclaredArtifactKind.ReportWriterGrid)
        {
            return declaration.GridId is null;
        }

        if (string.IsNullOrWhiteSpace(declaration.GridId))
        {
            return false;
        }

        var gridMetadata = manifest.ReportWriterGrids.IsDefault
            ? []
            : manifest.ReportWriterGrids
                .Where(grid => string.Equals(grid.GridId, declaration.GridId, StringComparison.Ordinal))
                .ToArray();
        var renderedGrids = manifest.RenderedReportWriterGrids.IsDefault
            ? []
            : manifest.RenderedReportWriterGrids
                .Where(grid => string.Equals(grid.GridId, declaration.GridId, StringComparison.Ordinal))
                .ToArray();
        return gridMetadata.Length == 1
            && renderedGrids.Length == 1
            && string.Equals(gridMetadata[0].Artifact, declaration.ArtifactId, StringComparison.Ordinal)
            && string.Equals(gridMetadata[0].Kind, renderedGrids[0].Kind.ToString(), StringComparison.Ordinal);
    }

    private static bool CanonicalSectionsEqual(
        ImmutableArray<ReportingSectionManifest> canonical,
        ImmutableArray<ReportingSectionManifest> rendered)
    {
        if (canonical.IsDefault || rendered.IsDefault || canonical.Length != rendered.Length)
        {
            return false;
        }

        for (var index = 0; index < canonical.Length; index++)
        {
            var expected = canonical[index];
            var actual = rendered[index];
            if (expected.Lineage is null
                || actual.Lineage is null
                || !string.Equals(expected.SectionId, actual.SectionId, StringComparison.Ordinal)
                || !string.Equals(expected.DatasetSnapshotId, actual.DatasetSnapshotId, StringComparison.Ordinal)
                || !string.Equals(expected.ReconciliationCheckpointId, actual.ReconciliationCheckpointId, StringComparison.Ordinal)
                || !string.Equals(expected.Hash, actual.Hash, StringComparison.Ordinal)
                || !string.Equals(expected.Lineage.SectionId, actual.Lineage.SectionId, StringComparison.Ordinal)
                || !string.Equals(expected.Lineage.DatasetSnapshotId, actual.Lineage.DatasetSnapshotId, StringComparison.Ordinal)
                || !string.Equals(expected.Lineage.DatasetSnapshotHash, actual.Lineage.DatasetSnapshotHash, StringComparison.Ordinal)
                || !string.Equals(expected.Lineage.ReconciliationCheckpointId, actual.Lineage.ReconciliationCheckpointId, StringComparison.Ordinal)
                || !expected.Lineage.CapturedAtUtc.EqualsExact(actual.Lineage.CapturedAtUtc))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRequiredForFinality(
        ReportingRunReadinessCheckDto check,
        ReportingFinalityDto finality) =>
        finality == ReportingFinalityDto.Final ? check.BlocksFinal : check.BlocksDraft;

    private async Task<RetainedProduction> RetainAndVerifyProductionAsync(
        GovernedReportingRun run,
        ReportingGovernedArtifactProduction production,
        ReportingAuthorityScope retentionAuthority,
        CancellationToken cancellationToken)
    {
        var manifestArtifact = production.Artifacts.Single(artifact =>
            string.Equals(artifact.ArtifactId, production.ManifestArtifactId, StringComparison.Ordinal));
        var manifestHash = ComputeSha256(manifestArtifact.Content.Span);
        var retentionRequest = new ReportingArtifactPackageRetentionRequest(
            BuildPackageId(run),
            run.RunId,
            run.SeriesId,
            run.Revision,
            run.Scope,
            run.Access,
            run.Snapshot,
            production.ManifestArtifactId,
            manifestHash,
            production.Artifacts);
        var retention = await _artifactVault
            .RetainPackageAsync(retentionRequest, retentionAuthority, cancellationToken)
            .ConfigureAwait(false);
        VerifyRetention(run, production, manifestHash, retention);
        return new RetainedProduction(manifestHash, retention);
    }

    private async Task<VerifiedReleasePackage> ReadAndVerifyRetainedPackageAsync(
        GovernedReportingRun run,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken)
    {
        var packageId = BuildPackageId(run);
        var access = new ReportingArtifactAccessContext(
            caller.ActorId.Trim(),
            run.Scope.TenantId,
            run.Scope.OrganizationId,
            run.Scope.CompanyId,
            run.Scope.FundId,
            run.Scope.BookId,
            run.Scope.PeriodId,
            caller.PrincipalIds.IsDefault
                ? ImmutableArray<string>.Empty
                : caller.PrincipalIds,
            caller.CorrelationId.Trim());
        var package = await _artifactVault
            .GetPackageForReleaseAsync(packageId, access, cancellationToken)
            .ConfigureAwait(false);
        var records = ImmutableArray.CreateBuilder<ReportingRetainedArtifactRecord>(package.Artifacts.Length);
        var auditEventIds = ImmutableArray.CreateBuilder<string>(package.Artifacts.Length);
        byte[]? retainedManifestBytes = null;
        foreach (var catalogRecord in package.Artifacts.OrderBy(static item => item.ArtifactId, StringComparer.Ordinal))
        {
            var download = await _artifactVault
                .ReadForDownloadAsync(packageId, catalogRecord.ArtifactId, access, cancellationToken)
                .ConfigureAwait(false);
            var record = download.Artifact;
            if (!RetainedArtifactRecordsEqual(record, catalogRecord)
                || !string.Equals(record.PackageId, packageId, StringComparison.Ordinal)
                || !string.Equals(record.RunId, run.RunId, StringComparison.Ordinal)
                || !string.Equals(record.SeriesId, run.SeriesId, StringComparison.Ordinal)
                || record.Revision != run.Revision
                || !Equals(record.Scope, run.Scope)
                || !AccessScopesEqual(record.Access, run.Access)
                || !Equals(record.Snapshot, run.Snapshot)
                || record.ByteLength != download.Content.LongLength
                || !string.Equals(
                    record.Identity.ContentHashSha256,
                    ComputeSha256(download.Content),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    $"Retained artifact '{packageId}/{catalogRecord.ArtifactId}' is not bound to the immutable governed run or exact stored bytes.");
            }

            if (string.Equals(record.ArtifactId, record.ManifestId, StringComparison.Ordinal))
            {
                retainedManifestBytes = download.Content;
            }

            records.Add(record);
            auditEventIds.Add(download.AuditEventId);
        }

        var retained = records.MoveToImmutable();
        if (retained.IsDefaultOrEmpty
            || retained.Select(static record => record.ArtifactId).Distinct(StringComparer.Ordinal).Count() != package.Artifacts.Length
            || retained.Select(static record => record.ManifestId).Distinct(StringComparer.Ordinal).Count() != 1
            || retained.Select(static record => record.ManifestHash).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact package '{packageId}' is incomplete or has inconsistent manifest identity.");
        }

        var manifestId = retained[0].ManifestId;
        var manifestHash = retained[0].ManifestHash;
        var retainedManifest = retained.SingleOrDefault(record =>
            string.Equals(record.ArtifactId, manifestId, StringComparison.Ordinal));
        if (retainedManifest is null
            || retainedManifestBytes is null
            || !string.Equals(
                retainedManifest.Identity.ContentHashSha256,
                manifestHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact package '{packageId}' has no verifiable exact manifest bytes.");
        }

        var retainedDocument = DeterministicReportingCertifiedArtifactProducer.ParseRetainedManifest(
            retainedManifestBytes);
        ValidateRetainedManifestBinding(run, retainedDocument);
        var descriptors = retainedDocument.Artifacts.ToDictionary(
            static descriptor => descriptor.ArtifactId,
            StringComparer.Ordinal);
        if (descriptors.Count != retained.Length)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact package '{packageId}' does not exactly match its immutable manifest descriptor set.");
        }
        foreach (var record in retained)
        {
            if (!descriptors.TryGetValue(record.ArtifactId, out var descriptor)
                || !string.Equals(descriptor.FileName, record.FileName, StringComparison.Ordinal)
                || !string.Equals(descriptor.ContentType, record.ContentType, StringComparison.Ordinal)
                || (string.Equals(record.ArtifactId, manifestId, StringComparison.Ordinal)
                    ? descriptor.ByteLength is not null || descriptor.ContentHashSha256 is not null
                    : descriptor.ByteLength != record.ByteLength
                      || !string.Equals(
                          descriptor.ContentHashSha256,
                          record.Identity.ContentHashSha256,
                          StringComparison.OrdinalIgnoreCase)))
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    $"Retained artifact '{packageId}/{record.ArtifactId}' conflicts with its immutable manifest descriptor.");
            }
        }

        return new VerifiedReleasePackage(
            manifestId,
            manifestHash,
            retained
                .Select(static record => new ReportingArtifactReference(
                    record.ArtifactId,
                    record.Identity.ContentHashSha256,
                    record.ByteLength))
                .OrderBy(static artifact => artifact.ArtifactId, StringComparer.Ordinal)
                .ToImmutableArray(),
            auditEventIds.MoveToImmutable(),
            retainedDocument.Sections);
    }
}
