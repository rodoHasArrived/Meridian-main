using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>Authorizes both retained inputs and binds browser approval to exactly reviewed evidence.</summary>
public sealed class ReportingRunReviewService(IReportingRunStore runs, IReportingArtifactStore artifacts,
    IReportingReviewMaterialityPolicyProvider? materialityPolicies = null)
{
    public async Task<RetainedReportingRunReview> PrepareAsync(
        IReportingGovernanceEndpointCoordinator coordinator, string runId,
        ReportingGovernanceCallerContext caller, CancellationToken ct = default)
    {
        var current = await coordinator.GetAsync(runId, caller, ct).ConfigureAwait(false);
        var currentManifest = Manifest(current, caller);
        var series = await coordinator.GetSeriesHistoryAsync(current.SeriesId, caller, ct).ConfigureAwait(false);
        GovernedReportingRun? baseline = null;
        ReportingOutputManifest? baselineManifest = null;
        foreach (var candidate in series.Runs.OrderByDescending(r => r.Revision))
        {
            if (candidate.RunId == current.RunId || candidate.Revision >= current.Revision
                || candidate.GovernanceState is not (GovernedReportingState.Approved or GovernedReportingState.Released))
                continue;
            var manifest = Manifest(candidate, caller);
            if (!ReportingRunReviewEngine.Comparable(current, currentManifest, candidate, manifest))
                continue;
            baseline = candidate;
            baselineManifest = manifest;
            break;
        }
        var policy = materialityPolicies is null ? null : await materialityPolicies.ResolveAsync(current.Scope,
            current.TemplateId, current.TemplateVersion, currentManifest.ResolvedParameters?.PresentationCurrency ?? "", ct).ConfigureAwait(false);
        return ReportingRunReviewEngine.Build(current, currentManifest, baseline, baselineManifest, policy);
    }

    public async Task<string> VerifyAndRetainAsync(
        IReportingGovernanceEndpointCoordinator coordinator, string runId, long expectedVersion,
        string reviewId, ReportingGovernanceCallerContext caller, CancellationToken ct = default)
    {
        if (!Sha256Digest.IsCanonical(reviewId))
            throw new ArgumentException("A canonical review evidence identity is required.");
        var retained = await PrepareAsync(coordinator, runId, caller, ct).ConfigureAwait(false);
        if (retained.Review.Version != expectedVersion || !Sha256Digest.FixedEquals(reviewId, retained.Review.ReviewId))
            throw new ReportingGovernanceConcurrencyException("The report review basis changed. Refresh the comparison before approving.");
        // The review identity hashes the envelope with an empty identity field (avoids self-reference).
        var bytes = JsonSerializer.SerializeToUtf8Bytes(retained with { Review = retained.Review with { ReviewId = "" } },
            ReportingRunReviewJsonContext.Default.RetainedReportingRunReview);
        var receipt = await artifacts.StoreAsync(new(caller.TenantId, bytes), ct).ConfigureAwait(false);
        if (receipt.Identity.TenantId != caller.TenantId || receipt.ByteSize != bytes.LongLength
            || !Sha256Digest.FixedEquals(receipt.Identity.ContentHashSha256, reviewId))
            throw new IOException("The retained review evidence receipt did not match the reviewed bytes.");
        var read = await artifacts.ReadAsync(receipt.Identity, ct).ConfigureAwait(false);
        if (read.Identity != receipt.Identity || read.ByteSize != bytes.LongLength || !Sha256Digest.FixedEquals(Sha256Digest.Compute(read.Content), reviewId))
            throw new IOException("The retained review evidence failed verification.");
        var policy = retained.Review.MaterialityPolicy;
        var policyReceipt = policy is null ? "NotConfigured" : $"{policy.PolicyId}@{policy.Version}";
        return $"review-evidence:{reviewId};run:{runId};version:{expectedVersion};materiality:{retained.Review.MaterialityOutcome};policy:{policyReceipt}";
    }

    public async Task<ReportingRunReview> ReadAsync(
        IReportingGovernanceEndpointCoordinator coordinator, string runId, string reviewId,
        ReportingGovernanceCallerContext caller, CancellationToken ct = default)
    {
        if (!Sha256Digest.IsCanonical(reviewId))
            throw new ArgumentException("A canonical review evidence identity is required.");
        var current = await coordinator.GetAsync(runId, caller, ct).ConfigureAwait(false);
        var identity = new ReportingArtifactIdentity(caller.TenantId, reviewId);
        var artifact = await artifacts.ReadAsync(identity, ct).ConfigureAwait(false);
        if (artifact.Identity != identity || artifact.ByteSize != artifact.Content.LongLength
            || !Sha256Digest.FixedEquals(Sha256Digest.Compute(artifact.Content), reviewId))
            throw new IOException("The retained review evidence failed verification.");
        RetainedReportingRunReview retained;
        try
        {
            retained = JsonSerializer.Deserialize(artifact.Content, ReportingRunReviewJsonContext.Default.RetainedReportingRunReview)
                ?? throw new JsonException("Empty review evidence.");
        }
        catch (JsonException exception) { throw new IOException("The retained review evidence is invalid.", exception); }
        if (retained.Format != ReportingRunReviewEngine.Format || retained.Review.RunId != runId
            || retained.Review.SnapshotHash != current.Snapshot.SnapshotHash
            || retained.CurrentManifest.OperationalScope != current.Scope)
            throw new ReportingGovernanceNotFoundException("The review evidence does not belong to this run.");
        _ = Authorize(retained.CurrentManifest, current, caller);
        if (retained.Review.BaselineRunId is { } baselineId)
        {
            var baseline = await coordinator.GetAsync(baselineId, caller, ct).ConfigureAwait(false);
            if (retained.BaselineManifest?.OperationalScope != baseline.Scope
                || retained.BaselineManifest?.CertifiedSnapshot?.SnapshotHash != baseline.Snapshot.SnapshotHash)
                throw new ReportingGovernanceNotFoundException("The review baseline evidence is unavailable.");
            _ = Authorize(retained.BaselineManifest, baseline, caller);
        }
        return retained.Review with { ReviewId = reviewId };
    }

    private ReportingOutputManifest Manifest(GovernedReportingRun run, ReportingGovernanceCallerContext caller)
    {
        var manifest = runs.GetManifest(caller.TenantId, run.RunId)
            ?? throw new ReportingGovernanceNotFoundException("The retained report manifest is unavailable.");
        return Authorize(manifest, run, caller);
    }

    private static ReportingOutputManifest Authorize(ReportingOutputManifest manifest,
        GovernedReportingRun run, ReportingGovernanceCallerContext caller)
    {
        if (!StringComparer.Ordinal.Equals(manifest.RunId, run.RunId)
            || !StringComparer.Ordinal.Equals(manifest.TemplateId, run.TemplateId)
            || manifest.CertifiedSnapshot?.SnapshotHash != run.Snapshot.SnapshotHash
            || manifest.OperationalScope != run.Scope)
            throw new ReportingGovernanceException("The retained manifest does not match the governed run identity, template, snapshot and scope.");
        ReportingCertifiedManifestValidation.Validate(manifest);
        var access = new ReportAccessQueryContext(caller.ActorId, GroupPrincipalIds: caller.PrincipalIds.IsDefault ? [] : caller.PrincipalIds, CompanyId: caller.CompanyId,
            HasGlobalOverride: caller.Permissions.HasFlag(Meridian.Identity.Auth.UserPermission.AdminMaintenance),
            TenantId: caller.TenantId, RequireBoundScope: true);
        var evaluation = ReportAccessPolicyEvaluator.Evaluate(manifest, access);
        if (!evaluation.IsAccessible)
            throw new UnauthorizedAccessException(evaluation.Reason);
        return manifest;
    }
}
