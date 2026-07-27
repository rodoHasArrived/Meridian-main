using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportPackRunReadService
{
    public async Task<WorkstationReportingPayload> BuildPayloadAsync(
        ReportAccessQueryContext? accessContext,
        int recentRunLimit = DefaultRecentRunLimit,
        CancellationToken ct = default)
    {
        var payload = BuildPayloadCore(
            accessContext,
            recentRunLimit,
            includeCompatibilitySources: _canonicalDeliveryStore is null);
        if (_canonicalDeliveryStore is null)
        {
            return payload;
        }

        EnsureCanonicalReadScope(accessContext);
        if (_runStore is null)
        {
            throw new InvalidOperationException(
                "Canonical reporting workspace reads require the reporting run store.");
        }

        var deliveries = await ListCanonicalDeliveriesAsync(
                accessContext!,
                payload.RecentRuns,
                ct)
            .ConfigureAwait(false);
        var receiptCount = deliveries.Sum(static delivery => delivery.Receipts.Count);
        var distributions = BuildCanonicalDistributionRecords(deliveries);
        return payload with
        {
            CanonicalDeliveries = deliveries,
            DeliveryAttempts = [],
            ReportPackDistributions = distributions,
            Summary =
                $"{payload.ProfileCount} export/reporting profiles are available; " +
                $"{payload.RecentRuns.Count} canonical reporting runs, " +
                $"{deliveries.Count} durable delivery jobs, and " +
                $"{receiptCount} immutable delivery receipts are visible."
        };
    }

    public async Task<WorkstationReportingHistoryPayload> BuildCanonicalHistoryAsync(
        ReportAccessQueryContext? accessContext,
        int limit = 25,
        CancellationToken ct = default)
    {
        if (_runStore is null || _canonicalDeliveryStore is null)
        {
            throw new InvalidOperationException(
                "Canonical reporting history requires both reporting run and delivery stores.");
        }

        EnsureCanonicalReadScope(accessContext);
        var normalizedLimit = Math.Clamp(limit, 1, 200);
        var payload = await BuildPayloadAsync(accessContext, normalizedLimit, ct)
            .ConfigureAwait(false);
        return new WorkstationReportingHistoryPayload(
            payload.RecentRuns,
            payload.CanonicalDeliveries ?? [],
            normalizedLimit,
            DateTimeOffset.UtcNow);
    }

    private async Task<IReadOnlyList<WorkstationReportingDeliveryPayload>>
        ListCanonicalDeliveriesAsync(
            ReportAccessQueryContext accessContext,
            IReadOnlyList<WorkstationReportingRunPayload> visibleRuns,
            CancellationToken ct)
    {
        var tenantId = accessContext.TenantId!.Trim();
        var jobs = new List<ReportingDeliveryJobRecord>();
        foreach (var runId in visibleRuns
                     .Select(static run => run.RunId)
                     .Where(static runId => !string.IsNullOrWhiteSpace(runId))
                     .Distinct(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var runJobs = await _canonicalDeliveryStore!
                .ListByRunAsync(tenantId, runId, ct)
                .ConfigureAwait(false);
            foreach (var job in runJobs)
            {
                ValidateCanonicalDeliveryBinding(job, tenantId, runId);
                jobs.Add(job);
            }
        }

        return jobs
            .GroupBy(static job => job.JobId, StringComparer.Ordinal)
            .Select(static group => group.Single())
            .OrderByDescending(static job => job.UpdatedAtUtc)
            .ThenBy(static job => job.JobId, StringComparer.Ordinal)
            .Select(ProjectCanonicalDelivery)
            .ToArray();
    }

    private static void EnsureCanonicalReadScope(ReportAccessQueryContext? accessContext)
    {
        if (accessContext?.RequireBoundScope != true
            || string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId)
            || string.IsNullOrWhiteSpace(accessContext.TenantId)
            || string.IsNullOrWhiteSpace(accessContext.CompanyId))
        {
            throw new UnauthorizedAccessException(
                "Canonical reporting history requires an authenticated actor and bound tenant/company scope.");
        }
    }

    private static void ValidateCanonicalDeliveryBinding(
        ReportingDeliveryJobRecord job,
        string tenantId,
        string runId)
    {
        if (!string.Equals(job.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(job.ReleaseAuthorization.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(job.ReleaseAuthorization.RunId, runId, StringComparison.Ordinal)
            || !string.Equals(
                job.ReleaseAuthorization.PackageId,
                job.PackageId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Reporting delivery job '{job.JobId}' failed its immutable tenant/run/package binding.");
        }

        if (job.Receipts.Any(receipt =>
                !string.Equals(receipt.TransportId, job.TransportId, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"Reporting delivery job '{job.JobId}' contains a receipt for another transport.");
        }
    }

    private static WorkstationReportingDeliveryPayload ProjectCanonicalDelivery(
        ReportingDeliveryJobRecord job) =>
        new(
            job.JobId,
            job.ReleaseAuthorization.RunId,
            job.PackageId,
            job.ReleaseAuthorization.ReceiptId,
            job.ReleaseAuthorization.ReleaseVersion,
            job.ReleaseAuthorization.ArtifactManifestHashSha256,
            job.DistributionId,
            job.TransportId,
            job.Payload.Recipient,
            job.Payload.RecipientRole,
            job.Payload.Destination,
            job.State.ToString(),
            job.AttemptCount,
            job.MaxAttempts,
            job.CreatedAtUtc,
            job.UpdatedAtUtc,
            job.NextAttemptAtUtc,
            job.RequestedBy,
            job.LastErrorCode,
            job.LastError,
            job.ProviderMessageId,
            job.AccessGrantId,
            job.Receipts
                .OrderBy(static receipt => receipt.OccurredAtUtc)
                .ThenBy(static receipt => receipt.ReceiptId, StringComparer.Ordinal)
                .Select(static receipt => new WorkstationReportingDeliveryReceiptPayload(
                    receipt.ReceiptId,
                    receipt.Kind.ToString(),
                    receipt.OccurredAtUtc,
                    receipt.TransportId,
                    receipt.ProviderReference,
                    receipt.EvidenceReference,
                    receipt.Detail))
                .ToArray());

    private static WorkstationReportPackDistributionPayload[]
        BuildCanonicalDistributionRecords(
            IReadOnlyList<WorkstationReportingDeliveryPayload> deliveries) =>
        deliveries
            .GroupBy(static delivery => delivery.DistributionId, StringComparer.Ordinal)
            .Select(static group =>
            {
                var attempts = group
                    .OrderByDescending(static delivery => delivery.UpdatedAtUtc)
                    .ThenBy(static delivery => delivery.JobId, StringComparer.Ordinal)
                    .ToArray();
                var latest = attempts[0];
                var pending = attempts.Count(static delivery =>
                    delivery.State is nameof(ReportingDeliveryState.Queued)
                        or nameof(ReportingDeliveryState.Dispatching)
                        or nameof(ReportingDeliveryState.RetryScheduled));
                var receiptCount = attempts.Sum(static delivery => delivery.Receipts.Count);
                var lastSentAt = attempts
                    .SelectMany(static delivery => delivery.Receipts)
                    .Where(static receipt =>
                        receipt.Kind is nameof(ReportingDeliveryReceiptKind.Sent)
                            or nameof(ReportingDeliveryReceiptKind.Delivered)
                            or nameof(ReportingDeliveryReceiptKind.Published))
                    .Select(static receipt => (DateTimeOffset?)receipt.OccurredAtUtc)
                    .OrderByDescending(static occurredAt => occurredAt)
                    .FirstOrDefault();
                return new WorkstationReportPackDistributionPayload(
                    latest.DistributionId,
                    latest.Recipient,
                    latest.RecipientRole,
                    latest.TransportId,
                    latest.State,
                    pending,
                    pending > 0
                        ? $"{pending} durable delivery job(s) remain pending."
                        : $"{attempts.Length} durable delivery job(s) retain {receiptCount} immutable receipt(s).",
                    latest.RequestedBy,
                    latest.NextAttemptAtUtc,
                    lastSentAt,
                    $"/api/fund-structure/reporting/distribution/packages/{Uri.EscapeDataString(latest.RunId)}/deliveries");
            })
            .OrderBy(static distribution => distribution.Recipient, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static distribution => distribution.DistributionId, StringComparer.Ordinal)
            .ToArray();
}
