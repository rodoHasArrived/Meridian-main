using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Integrity;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Creates the delivery authorization envelope from the immutable governed run. The proof is a
/// stable content digest, not a caller credential: authorization still requires a fresh comparison
/// with the authoritative governance repository through
/// <see cref="GovernanceReportingReleaseAuthorizationVerifier"/>.
/// </summary>
public static class ReportingDeliveryReleaseAuthorizationFactory
{
    public static ReportingDeliveryReleaseAuthorization Create(GovernedReportingRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.ExecutionState != GovernedReportingExecutionState.Succeeded
            || run.GovernanceState != GovernedReportingState.Released
            || run.Release is null)
        {
            throw new InvalidOperationException(
                $"Reporting run '{run.RunId}' does not have a governed Released receipt.");
        }

        if (!ReportingGovernanceAuditChain.Verify(run.AuditTrail))
        {
            throw new InvalidDataException(
                $"Reporting run '{run.RunId}' has an invalid governance audit chain.");
        }

        var release = run.Release;
        var packageId = ReportingArtifactPackageIdentity.Create(run);
        var artifacts = release.Artifacts
            .OrderBy(static artifact => artifact.ArtifactId, StringComparer.Ordinal)
            .Select(artifact => new ReportingReleasedArtifactReference(
                artifact.ArtifactId,
                BuildRetainedUri(run.Scope.TenantId, packageId, artifact.ArtifactId),
                artifact.ArtifactHash.ToLowerInvariant(),
                artifact.ByteLength))
            .ToArray();
        var evidence = release.EvidenceIds
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        var receiptId = $"release:{run.RunId}:r{run.Revision}:v{run.Version}";
        var authorization = new ReportingDeliveryReleaseAuthorization(
            receiptId,
            ReportingReleaseState.Released,
            run.Scope.TenantId,
            packageId,
            run.RunId,
            $"{run.Revision.ToString(CultureInfo.InvariantCulture)}.{run.Version.ToString(CultureInfo.InvariantCulture)}",
            release.ManifestHash.ToLowerInvariant(),
            artifacts,
            evidence,
            release.ReleasedAtUtc,
            release.Authority.ActorId,
            AuthorizationProof: string.Empty);

        return authorization with { AuthorizationProof = ComputeProof(authorization) };
    }

    public static bool HasValidSelfProof(ReportingDeliveryReleaseAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return FixedTimeEqualsHex(authorization.AuthorizationProof, ComputeProof(authorization));
    }

    public static bool Matches(
        ReportingDeliveryReleaseAuthorization supplied,
        ReportingDeliveryReleaseAuthorization authoritative)
    {
        ArgumentNullException.ThrowIfNull(supplied);
        ArgumentNullException.ThrowIfNull(authoritative);
        return HasValidSelfProof(supplied)
               && FixedTimeEqualsHex(supplied.AuthorizationProof, authoritative.AuthorizationProof);
    }

    private static string BuildRetainedUri(string tenantId, string packageId, string artifactId) =>
        $"reporting-artifact://{Uri.EscapeDataString(tenantId)}/{Uri.EscapeDataString(packageId)}/{Uri.EscapeDataString(artifactId)}";

    private static string ComputeProof(ReportingDeliveryReleaseAuthorization authorization)
    {
        var canonical = new StringBuilder();
        Append(canonical, authorization.ReceiptId);
        Append(canonical, (int)authorization.State);
        Append(canonical, authorization.TenantId);
        Append(canonical, authorization.PackageId);
        Append(canonical, authorization.RunId);
        Append(canonical, authorization.ReleaseVersion);
        Append(canonical, authorization.ArtifactManifestHashSha256.ToLowerInvariant());
        foreach (var artifact in authorization.Artifacts.OrderBy(static item => item.ArtifactId, StringComparer.Ordinal))
        {
            Append(canonical, artifact.ArtifactId);
            Append(canonical, artifact.RetainedUri);
            Append(canonical, artifact.ContentHashSha256.ToLowerInvariant());
            Append(canonical, artifact.ByteSize);
        }

        foreach (var evidence in authorization.EvidenceReferences
                     .OrderBy(static item => item, StringComparer.Ordinal))
        {
            Append(canonical, evidence);
        }

        Append(canonical, authorization.ReleasedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(canonical, authorization.ReleasedBy);
        return Sha256Digest.ComputeUtf8(canonical.ToString());
    }

    private static void Append(StringBuilder target, object? value)
    {
        var text = value switch
        {
            null => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
        if (text is null)
        {
            target.Append("-1:");
            return;
        }

        target.Append(Encoding.UTF8.GetByteCount(text).ToString(CultureInfo.InvariantCulture));
        target.Append(':');
        target.Append(text);
    }

    private static bool FixedTimeEqualsHex(string left, string right)
    {
        byte[] leftBytes;
        byte[] rightBytes;
        try
        {
            leftBytes = Convert.FromHexString(left);
            rightBytes = Convert.FromHexString(right);
        }
        catch (FormatException)
        {
            leftBytes = [];
            rightBytes = [0];
        }

        return leftBytes.Length == SHA256.HashSizeInBytes
               && rightBytes.Length == SHA256.HashSizeInBytes
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

/// <summary>
/// Verifies a queued delivery authorization against current immutable governed state instead of
/// trusting the serialized authorization retained on the outbox job.
/// </summary>
public sealed class GovernanceReportingReleaseAuthorizationVerifier : IReportingReleaseAuthorizationVerifier
{
    private readonly IReportingGovernanceRepository _repository;
    private readonly ReportingReleasedArtifactIntegrityGate _artifactIntegrityGate;

    public GovernanceReportingReleaseAuthorizationVerifier(
        IReportingGovernanceRepository repository,
        ReportingReleasedArtifactIntegrityGate artifactIntegrityGate)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _artifactIntegrityGate = artifactIntegrityGate
            ?? throw new ArgumentNullException(nameof(artifactIntegrityGate));
    }

    public async Task<ReportingReleaseAuthorizationResult> VerifyAsync(
        ReportingDeliveryReleaseAuthorization authorization,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (authorization.State != ReportingReleaseState.Released)
        {
            return new ReportingReleaseAuthorizationResult(false, "RUN_NOT_RELEASED");
        }

        if (!ReportingDeliveryReleaseAuthorizationFactory.HasValidSelfProof(authorization))
        {
            return new ReportingReleaseAuthorizationResult(false, "AUTHORIZATION_PROOF_INVALID");
        }

        GovernedReportingRun? run;
        try
        {
            run = await _repository.ExecuteTransactionAsync(
                (transaction, cancellationToken) => transaction.GetRunAsync(
                    authorization.TenantId,
                    authorization.RunId,
                    cancellationToken),
                ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ReportingGovernanceException or InvalidDataException)
        {
            return new ReportingReleaseAuthorizationResult(
                false,
                "GOVERNANCE_STATE_INVALID",
                exception.Message);
        }

        if (run is null)
        {
            return new ReportingReleaseAuthorizationResult(false, "RELEASE_NOT_FOUND");
        }

        try
        {
            var authoritative = ReportingDeliveryReleaseAuthorizationFactory.Create(run);
            if (!ReportingDeliveryReleaseAuthorizationFactory.Matches(authorization, authoritative))
            {
                return new ReportingReleaseAuthorizationResult(false, "RELEASE_MISMATCH");
            }

            return await _artifactIntegrityGate
                .VerifyAsync(authoritative, ct)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
        {
            return new ReportingReleaseAuthorizationResult(
                false,
                "GOVERNANCE_STATE_INVALID",
                exception.Message);
        }
    }
}

/// <summary>
/// Re-reads every immutable artifact immediately before queueing and again immediately before a
/// claimed delivery is sent. A governance receipt alone is therefore insufficient when retained
/// bytes or catalog metadata are missing, corrupt, or no longer match the released manifest.
/// </summary>
public sealed class ReportingReleasedArtifactIntegrityGate
{
    private readonly IReportingArtifactCatalog _catalog;
    private readonly IReportingArtifactStore _artifactStore;

    public ReportingReleasedArtifactIntegrityGate(
        IReportingArtifactCatalog catalog,
        IReportingArtifactStore artifactStore)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
    }

    public async Task<ReportingReleaseAuthorizationResult> VerifyAsync(
        ReportingDeliveryReleaseAuthorization authorization,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        try
        {
            var package = await _catalog
                .GetPackageAsync(authorization.TenantId, authorization.PackageId, ct)
                .ConfigureAwait(false);
            if (package is null)
            {
                return Rejected("ARTIFACT_PACKAGE_NOT_FOUND");
            }

            if (!Same(package.PackageId, authorization.PackageId)
                || package.Artifacts.IsDefaultOrEmpty
                || package.Artifacts.Length != authorization.Artifacts.Count)
            {
                return Rejected("ARTIFACT_MANIFEST_MISMATCH");
            }

            var catalogById = package.Artifacts
                .GroupBy(static artifact => artifact.ArtifactId, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
            if (catalogById.Any(static pair => pair.Value.Length != 1))
            {
                return Rejected("ARTIFACT_CATALOG_DUPLICATE");
            }

            var releasedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var released in authorization.Artifacts)
            {
                ct.ThrowIfCancellationRequested();
                if (released is null
                    || string.IsNullOrWhiteSpace(released.ArtifactId)
                    || !releasedIds.Add(released.ArtifactId)
                    || !catalogById.TryGetValue(released.ArtifactId, out var retainedMatches))
                {
                    return Rejected("ARTIFACT_MANIFEST_MISMATCH");
                }

                var retained = retainedMatches[0];
                if (!Same(retained.PackageId, authorization.PackageId)
                    || !Same(retained.RunId, authorization.RunId)
                    || !Same(retained.Scope.TenantId, authorization.TenantId)
                    || !Same(retained.Identity.TenantId, authorization.TenantId)
                    || !string.Equals(
                        retained.ManifestHash,
                        authorization.ArtifactManifestHashSha256,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        retained.Identity.ContentHashSha256,
                        released.ContentHashSha256,
                        StringComparison.OrdinalIgnoreCase)
                    || retained.ByteLength != released.ByteSize
                    || !Same(
                        released.RetainedUri,
                        BuildRetainedUri(
                            authorization.TenantId,
                            authorization.PackageId,
                            released.ArtifactId)))
                {
                    return Rejected("ARTIFACT_MANIFEST_MISMATCH");
                }

                var read = await _artifactStore.ReadAsync(retained.Identity, ct).ConfigureAwait(false);
                var actualHash = Sha256Digest.Compute(read.Content);
                if (!Same(read.Identity.TenantId, retained.Identity.TenantId)
                    || !string.Equals(
                        read.Identity.ContentHashSha256,
                        retained.Identity.ContentHashSha256,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        actualHash,
                        retained.Identity.ContentHashSha256,
                        StringComparison.OrdinalIgnoreCase)
                    || read.ByteSize != retained.ByteLength
                    || read.Content.LongLength != retained.ByteLength)
                {
                    return Rejected("ARTIFACT_BYTES_CORRUPT");
                }
            }

            return new ReportingReleaseAuthorizationResult(true, "RELEASE_AND_ARTIFACTS_VERIFIED");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            ReportingArtifactNotFoundException or
            ReportingArtifactIntegrityException or
            ReportingArtifactCatalogIntegrityException or
            InvalidDataException or
            IOException or
            NotSupportedException or
            ArgumentException)
        {
            // Do not surface provider paths, catalog values, or store diagnostics into a durable
            // delivery failure. The underlying artifact store/audit lane retains detailed proof.
            return Rejected("ARTIFACT_INTEGRITY_UNVERIFIED");
        }
    }

    private static ReportingReleaseAuthorizationResult Rejected(string code) =>
        new(false, code, "Released artifact bytes could not be verified against immutable governance evidence.");

    private static string BuildRetainedUri(string tenantId, string packageId, string artifactId) =>
        $"reporting-artifact://{Uri.EscapeDataString(tenantId)}/{Uri.EscapeDataString(packageId)}/{Uri.EscapeDataString(artifactId)}";

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);
}
