using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// File-backed <see cref="IAccountingAuditPendingMarkerStore"/>: a small sidecar written atomically
/// beside the accounting configuration snapshot.
/// </summary>
/// <remarks>
/// <para>Kept out of the snapshot for the same reason the audit chain's head is: the snapshot store
/// replaces the whole document on every write, so a marker living inside it would be cleared by the
/// very write whose completion it is supposed to be uncertain about.</para>
///
/// <para>Written with <see cref="AtomicFileWriter"/>, so a crash mid-write leaves either the previous
/// marker or the new one, never a torn file that reads as a corrupt marker and blocks every
/// subsequent mutation.</para>
/// </remarks>
public sealed class FileAccountingAuditPendingMarkerStore : IAccountingAuditPendingMarkerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileAccountingAuditPendingMarkerStore(string markerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        MarkerPath = markerPath;
    }

    /// <summary>Full path of the marker file.</summary>
    public string MarkerPath { get; }

    /// <summary>The conventional marker path for an accounting configuration snapshot.</summary>
    public static string MarkerPathFor(string snapshotPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        return snapshotPath + ".pending-audit.json";
    }

    public async Task<AccountingAuditPendingMarker?> ReadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadUnlockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(AccountingAuditPendingMarker marker, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(marker);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AtomicFileWriter
                .WriteAsync(MarkerPath, JsonSerializer.Serialize(marker, JsonOptions), ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(Guid auditEventId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await ReadUnlockedAsync(ct).ConfigureAwait(false);

            // A clear for an event that is no longer the outstanding one would erase a newer intent,
            // leaving the next crash undetectable — the precise failure this store exists to catch.
            if (current is null || current.AuditEvent.AuditEventId != auditEventId)
            {
                return;
            }

            File.Delete(MarkerPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AccountingAuditPendingMarker?> ReadUnlockedAsync(CancellationToken ct)
    {
        if (!File.Exists(MarkerPath))
        {
            return null;
        }

        await using var stream = new FileStream(
            MarkerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await JsonSerializer
            .DeserializeAsync<AccountingAuditPendingMarker>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
    }
}
