using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Detects and resolves golden record conflicts between providers for the same security.
/// </summary>
public interface ISecurityMasterConflictService
{
    /// <summary>Refreshes conflict detection and returns all open conflicts.</summary>
    Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct);

    /// <summary>Returns a specific conflict by ID, or null if not found.</summary>
    Task<SecurityMasterConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct);

    /// <summary>Resolves or dismisses a conflict. Returns the updated record, or null if not found.</summary>
    Task<SecurityMasterConflict?> ResolveAsync(ResolveConflictRequest request, CancellationToken ct);

    /// <summary>
    /// Checks a freshly written projection for identifier conflicts with existing securities
    /// and records any newly found conflicts in the in-memory store.
    /// Called automatically after projection writes such as create, amend, import, and rebuild replay.
    /// </summary>
    Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct);

    /// <summary>
    /// Compares the pre-write golden copy against an incoming revision of the same security and
    /// records field-level cross-source conflicts (economic and common terms whose values disagree
    /// between two source systems). Called on amend paths where the previous record is in hand;
    /// same-source revisions record nothing. Conflicts an operator already resolved are preserved.
    /// </summary>
    Task RecordFieldConflictsAsync(SecurityProjectionRecord previous, SecurityProjectionRecord incoming, CancellationToken ct);
}

/// <summary>
/// In-memory conflict detection over the Security Master projection store.
/// Detects identifier ambiguities where multiple providers map the same identifier to
/// different SecurityIds.
/// </summary>
public sealed class SecurityMasterConflictService : ISecurityMasterConflictService
{
    private readonly ISecurityMasterStore _store;
    private readonly ILogger<SecurityMasterConflictService> _logger;
    private readonly ConcurrentDictionary<Guid, SecurityMasterConflict> _conflicts = new();

    public SecurityMasterConflictService(
        ISecurityMasterStore store,
        ILogger<SecurityMasterConflictService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct)
    {
        var all = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        var detected = SecurityMasterConflictDetection.DetectAll(all, DateTimeOffset.UtcNow);

        foreach (var conflict in detected)
        {
            // Preserve existing resolution state; only add newly detected conflicts.
            _conflicts.TryAdd(conflict.ConflictId, conflict);
        }

        if (detected.Count > 0)
        {
            _logger.LogInformation("Detected {Count} identifier conflicts in Security Master", detected.Count);
        }

        return _conflicts.Values
            .Where(c => c.Status == "Open")
            .OrderBy(c => c.DetectedAt)
            .ToList();
    }

    public Task<SecurityMasterConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct)
    {
        _conflicts.TryGetValue(conflictId, out var conflict);
        return Task.FromResult(conflict);
    }

    public Task<SecurityMasterConflict?> ResolveAsync(ResolveConflictRequest request, CancellationToken ct)
    {
        if (!_conflicts.TryGetValue(request.ConflictId, out var existing))
            return Task.FromResult<SecurityMasterConflict?>(null);

        // Only an Open conflict can be resolved. Returning null when the conflict was already
        // resolved or dismissed lets a governed caller detect a concurrent/duplicate decision
        // instead of silently overwriting the first operator's winner.
        if (!string.Equals(existing.Status, "Open", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<SecurityMasterConflict?>(null);

        var newStatus = request.Resolution.Equals("Dismiss", StringComparison.OrdinalIgnoreCase)
            ? "Dismissed"
            : "Resolved";

        // Capture the winner, resolver, and reason together with the status so the resolution and
        // its chosen winner are persisted in the SAME atomic write as the close. There is no window
        // in which the conflict is closed but the winner is unrecorded.
        var updated = existing with
        {
            Status = newStatus,
            ResolvedWinnerSource = request.ChosenWinnerSource,
            ResolvedBy = request.ResolvedBy,
            ResolvedReason = request.Reason,
            ResolvedAt = DateTimeOffset.UtcNow,
        };

        // Atomic compare-and-set: only the first resolver whose snapshot still matches the stored
        // (Open) record wins. A concurrent resolver that lost the race observes null and must not
        // re-apply its decision.
        if (!_conflicts.TryUpdate(request.ConflictId, updated, existing))
            return Task.FromResult<SecurityMasterConflict?>(null);

        _logger.LogInformation(
            "Conflict {ConflictId} for security {SecurityId} {Status} by {ResolvedBy}",
            request.ConflictId, existing.SecurityId, newStatus, request.ResolvedBy);

        return Task.FromResult<SecurityMasterConflict?>(updated);
    }

    public async Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct)
    {
        // Load all projections and check the new record's identifiers against existing ones.
        var all = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        var candidates = SecurityMasterConflictDetection.DetectForProjection(projection, all, DateTimeOffset.UtcNow);

        int newConflicts = 0;
        foreach (var conflict in candidates)
        {
            // Only record if not already tracked with a non-Open status.
            if (_conflicts.TryGetValue(conflict.ConflictId, out var existing) && existing.Status != "Open")
                continue;

            _conflicts[conflict.ConflictId] = conflict;
            newConflicts++;

            _logger.LogWarning(
                "Ingest-time conflict detected: {FieldPath} already assigned to security {ExistingId} (new: {NewId})",
                conflict.FieldPath, conflict.ValueB, projection.SecurityId);
        }

        if (newConflicts > 0)
            _logger.LogInformation(
                "Recorded {Count} new identifier conflict(s) for security {SecurityId}",
                newConflicts, projection.SecurityId);
    }

    public Task RecordFieldConflictsAsync(SecurityProjectionRecord previous, SecurityProjectionRecord incoming, CancellationToken ct)
    {
        var candidates = SecurityMasterConflictDetection.DetectFieldConflicts(previous, incoming, DateTimeOffset.UtcNow);

        // A canonical write that replaces BOTH recorded candidate values makes an open field
        // conflict obsolete: it can never resolve to either source (the durable store's resolution
        // guard rejects a winner whose value the record no longer carries), so leaving it Open
        // surfaces an actionable-looking queue row whose resolution flow cannot complete. Close it
        // as Superseded, recording why, without fabricating a winner or field provenance.
        foreach (var (conflictId, existing) in _conflicts)
        {
            if (existing.SecurityId != incoming.SecurityId
                || !string.Equals(existing.Status, "Open", StringComparison.OrdinalIgnoreCase)
                || (!string.Equals(existing.ConflictKind, SecurityMasterConflictKinds.EconomicTermMismatch, StringComparison.Ordinal)
                    && !string.Equals(existing.ConflictKind, SecurityMasterConflictKinds.CommonTermMismatch, StringComparison.Ordinal)))
            {
                continue;
            }

            var persistedValue = SecurityMasterConflictDetection.ReadComparableFieldValue(incoming, existing.FieldPath);
            if (!SecurityMasterConflictDetection.FieldConflictIsObsolete(existing, persistedValue))
            {
                continue;
            }

            var superseded = existing with
            {
                Status = "Superseded",
                ResolvedBy = "system:canonical-write",
                ResolvedReason =
                    $"A later canonical write persisted '{persistedValue}' for '{existing.FieldPath}', which matches " +
                    $"neither recorded candidate ('{existing.ProviderA}'='{existing.ValueA}', '{existing.ProviderB}'='{existing.ValueB}').",
                ResolvedAt = DateTimeOffset.UtcNow,
            };
            if (_conflicts.TryUpdate(conflictId, superseded, existing))
            {
                _logger.LogInformation(
                    "Superseded obsolete field conflict {ConflictId} on {FieldPath} for security {SecurityId}: canonical write replaced both candidates.",
                    conflictId, existing.FieldPath, existing.SecurityId);
            }
        }

        int newConflicts = 0;
        foreach (var conflict in candidates)
        {
            // Only record if not already tracked with a non-Open status (operator resolutions win).
            if (_conflicts.TryGetValue(conflict.ConflictId, out var existing) && existing.Status != "Open")
                continue;

            _conflicts[conflict.ConflictId] = conflict;
            newConflicts++;

            _logger.LogWarning(
                "Cross-source field conflict on {FieldPath} for security {SecurityId}: {SourceA}='{ValueA}' vs {SourceB}='{ValueB}'",
                conflict.FieldPath, conflict.SecurityId, conflict.ProviderA, conflict.ValueA, conflict.ProviderB, conflict.ValueB);
        }

        if (newConflicts > 0)
            _logger.LogInformation(
                "Recorded {Count} new field conflict(s) for security {SecurityId}",
                newConflicts, incoming.SecurityId);

        return Task.CompletedTask;
    }
}
