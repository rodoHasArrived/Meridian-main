using Meridian.Contracts.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Integrations;

/// <summary>
/// Flags provider-integration records whose stored actor does not name a person (a session desk label,
/// a Settings placeholder, or blank). Flags are appended beside the records; nothing is rewritten, so
/// immutable manifest versions keep their content digests and pinned references.
/// </summary>
public sealed class ProviderIntegrationActorFlagService(
    IProviderIntegrationManifestStore store,
    TimeProvider? timeProvider = null,
    ILogger<ProviderIntegrationActorFlagService>? logger = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<ProviderIntegrationActorFlagService>.Instance;

    /// <summary>
    /// Scans the tenant's integration store and appends a flag for each unverified actor not already
    /// flagged. <paramref name="flaggedBy"/> is the authenticated operator running the scan.
    /// </summary>
    public async Task<ProviderIntegrationActorFlagScanResultDto> ScanAsync(
        string tenantId,
        string flaggedBy,
        IReadOnlyCollection<string>? extraLabels = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flaggedBy);
        var flags = ResolveFlagStore(tenantId);
        var values = await flags.ScanStoredActorValuesAsync(ct).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var appended = new List<ProviderIntegrationActorFlagDto>();
        var alreadyFlagged = 0;

        foreach (var value in values)
        {
            var reason = ProviderIntegrationActorLabelClassifier.Classify(value.Value, extraLabels);
            if (reason is null)
            {
                continue;
            }

            var flag = new ProviderIntegrationActorFlagDto(
                ProviderIntegrationActorFlagDto.ComputeFlagId(value.RecordPath, value.FieldPath, value.Value),
                value.RecordPath,
                value.RecordKind,
                value.SubjectId,
                value.FieldPath,
                value.Value,
                reason,
                value.RecordedAt,
                flaggedBy.Trim(),
                now);
            if (await flags.TryAppendActorFlagAsync(flag, ct).ConfigureAwait(false))
            {
                appended.Add(flag);
            }
            else
            {
                alreadyFlagged++;
            }
        }

        _logger.LogInformation(
            "Provider-integration actor scan by {FlaggedBy} checked {ActorFieldCount} actor fields: {Appended} flags appended, {AlreadyFlagged} already flagged",
            flaggedBy, values.Count, appended.Count, alreadyFlagged);
        return new ProviderIntegrationActorFlagScanResultDto(values.Count, appended.Count, alreadyFlagged, appended);
    }

    public Task<IReadOnlyList<ProviderIntegrationActorFlagDto>> ListAsync(string tenantId, CancellationToken ct = default)
        => ResolveFlagStore(tenantId).ListActorFlagsAsync(ct);

    private IProviderIntegrationActorFlagStore ResolveFlagStore(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var tenantStore = store is IProviderIntegrationTenantManifestStoreFactory factory ? factory.ForTenant(tenantId) : store;
        return tenantStore as IProviderIntegrationActorFlagStore
            ?? throw new NotSupportedException("This integration store does not support actor flags.");
    }
}
