using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.Rebuild;

/// <summary>
/// Regenerates retained Security Master reconstruction snapshots (ADR-024 amendment 2026-11-13,
/// step 4). A snapshot is never patched in place: each one is decoded through the replay decoder
/// (<see cref="SecurityMasterMapping.FromEconomicPayload"/>, which declares the terms family on the
/// retained legacy terms in memory) and re-serialized through
/// <see cref="SecurityMasterMapping.ToSnapshot"/> at its own version and timestamp. Migration 068
/// rewrote current state only; this brings the snapshots written before every writer declared its
/// family into the same form. Events are never touched.
/// </summary>
public interface ISecurityMasterSnapshotRegenerationService
{
    /// <summary>
    /// Regenerates every retained snapshot. Idempotent: a snapshot whose regenerated payload
    /// equals the stored one is left unchanged, and one a concurrent write moved to a newer
    /// version is skipped rather than overwritten.
    /// </summary>
    Task<SecurityMasterSnapshotRegenerationResult> RegenerateAsync(CancellationToken ct = default);
}

/// <summary>Counts reported by one <see cref="ISecurityMasterSnapshotRegenerationService.RegenerateAsync"/> run.</summary>
/// <param name="Scanned">Snapshots read.</param>
/// <param name="Regenerated">Snapshots whose payload was replaced by the re-serialized record.</param>
/// <param name="Unchanged">Snapshots already in regenerated form.</param>
/// <param name="Skipped">Snapshots removed or moved to another version while the run read them.</param>
public sealed record SecurityMasterSnapshotRegenerationResult(int Scanned, int Regenerated, int Unchanged, int Skipped);

/// <inheritdoc />
public sealed class SecurityMasterSnapshotRegenerationService : ISecurityMasterSnapshotRegenerationService
{
    private readonly ISecurityMasterSnapshotStore _snapshotStore;
    private readonly ILogger<SecurityMasterSnapshotRegenerationService> _logger;

    public SecurityMasterSnapshotRegenerationService(
        ISecurityMasterSnapshotStore snapshotStore,
        ILogger<SecurityMasterSnapshotRegenerationService> logger)
    {
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SecurityMasterSnapshotRegenerationResult> RegenerateAsync(CancellationToken ct = default)
    {
        var securityIds = await _snapshotStore.ListSecurityIdsAsync(ct).ConfigureAwait(false);
        int scanned = 0, regenerated = 0, unchanged = 0, skipped = 0;
        foreach (var securityId in securityIds)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = await _snapshotStore.LoadAsync(securityId, ct).ConfigureAwait(false);
            if (snapshot is null)
            {
                skipped++;
                continue;
            }

            scanned++;
            var replayed = SecurityMasterMapping.FromEconomicPayload(snapshot.Payload);
            if (replayed.SecurityId != snapshot.SecurityId || replayed.Version != snapshot.Version)
            {
                throw new InvalidOperationException(
                    $"Security Master snapshot for '{snapshot.SecurityId}' at version {snapshot.Version} decodes to " +
                    $"security '{replayed.SecurityId}' at version {replayed.Version}; it was not regenerated.");
            }

            var outcome = await _snapshotStore
                .ReplaceAsync(SecurityMasterMapping.ToSnapshot(replayed, snapshot.SnapshotTimestamp), ct)
                .ConfigureAwait(false);
            switch (outcome)
            {
                case SecuritySnapshotReplaceOutcome.Replaced:
                    regenerated++;
                    break;
                case SecuritySnapshotReplaceOutcome.Unchanged:
                    unchanged++;
                    break;
                default:
                    skipped++;
                    break;
            }
        }

        _logger.LogInformation(
            "Security master snapshot regeneration scanned {Scanned} snapshot(s): {Regenerated} regenerated, {Unchanged} unchanged, {Skipped} skipped",
            scanned,
            regenerated,
            unchanged,
            skipped);
        return new SecurityMasterSnapshotRegenerationResult(scanned, regenerated, unchanged, skipped);
    }
}

/// <summary>No-op regeneration used when Security Master backing storage is not configured.</summary>
public sealed class NullSecurityMasterSnapshotRegenerationService : ISecurityMasterSnapshotRegenerationService
{
    private readonly ILogger<NullSecurityMasterSnapshotRegenerationService> _logger;

    public NullSecurityMasterSnapshotRegenerationService(ILogger<NullSecurityMasterSnapshotRegenerationService> logger)
    {
        _logger = logger;
    }

    public Task<SecurityMasterSnapshotRegenerationResult> RegenerateAsync(CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Ignoring Security Master snapshot regeneration because Security Master storage is not configured.");
        return Task.FromResult(new SecurityMasterSnapshotRegenerationResult(0, 0, 0, 0));
    }
}
