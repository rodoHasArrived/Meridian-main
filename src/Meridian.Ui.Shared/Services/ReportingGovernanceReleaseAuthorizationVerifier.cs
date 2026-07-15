using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
        var artifacts = release.Artifacts
            .OrderBy(static artifact => artifact.ArtifactId, StringComparer.Ordinal)
            .Select(artifact => new ReportingReleasedArtifactReference(
                artifact.ArtifactId,
                BuildRetainedUri(run.Scope.TenantId, run.RunId, artifact.ArtifactId),
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

    private static string BuildRetainedUri(string tenantId, string runId, string artifactId) =>
        $"reporting-artifact://{Uri.EscapeDataString(tenantId)}/{Uri.EscapeDataString(runId)}/{Uri.EscapeDataString(artifactId)}";

    private static string ComputeProof(ReportingDeliveryReleaseAuthorization authorization)
    {
        var canonical = new StringBuilder();
        Append(canonical, authorization.ReceiptId);
        Append(canonical, (int)authorization.State);
        Append(canonical, authorization.TenantId);
        Append(canonical, authorization.PackageId);
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
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
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

    public GovernanceReportingReleaseAuthorizationVerifier(IReportingGovernanceRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
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
                    authorization.PackageId,
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
            return ReportingDeliveryReleaseAuthorizationFactory.Matches(authorization, authoritative)
                ? new ReportingReleaseAuthorizationResult(true, "RELEASE_VERIFIED")
                : new ReportingReleaseAuthorizationResult(false, "RELEASE_MISMATCH");
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
