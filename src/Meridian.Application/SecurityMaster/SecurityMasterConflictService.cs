using System.Collections.Concurrent;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Reconciles OPEN field conflicts against a projection that has just been DURABLY persisted:
    /// a conflict whose both candidate values the persisted record no longer matches is closed as
    /// Superseded (third-party author) or has its candidate refreshed (a candidate revising its
    /// own value). This runs strictly AFTER the canonical write commits — retiring or refreshing
    /// conflicts from a value the event store might still reject (a stale ExpectedVersion) would
    /// mutate the governed conflict queue for an amendment that never happened.
    /// </summary>
    Task ReconcileOpenFieldConflictsAsync(SecurityProjectionRecord persisted, CancellationToken ct);
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
        ILogger<SecurityMasterConflictService> logger,
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null)
    {
        _store = store;
        _logger = logger;
        _assetProfileCatalog = assetProfileCatalog;
    }

    private readonly Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? _assetProfileCatalog;

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
            request.ConflictId, existing.SecurityId, newStatus,
            Regex.Replace(request.ResolvedBy ?? string.Empty, @"[\r\n\p{Cc}\u2028\u2029]+", " "));

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
        var candidates = SecurityMasterConflictDetection.DetectFieldConflicts(
            previous, incoming, DateTimeOffset.UtcNow, assetProfileCatalog: _assetProfileCatalog);

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

    public Task ReconcileOpenFieldConflictsAsync(SecurityProjectionRecord persisted, CancellationToken ct)
    {
        // A DURABLY persisted write that replaces BOTH recorded candidate values makes an open
        // field conflict obsolete: it can never resolve to either source (the durable store's
        // resolution guard rejects a winner whose value the record no longer carries), so leaving
        // it Open surfaces an actionable-looking queue row whose resolution flow cannot complete.
        // WHO authored the write decides the outcome: a CANDIDATE author is revising its own
        // value — the disagreement is still live, so its recorded candidate refreshes and the
        // conflict stays open — while a third-party author replaced both candidates and the
        // conflict closes as Superseded, recording why, without fabricating a winner or field
        // provenance. This runs only AFTER the canonical write commits, never against a value the
        // event store might still reject.
        var persistedSource = SecurityMasterProvenanceReader.Read(persisted.Provenance).SourceSystem;
        foreach (var (conflictId, existing) in _conflicts)
        {
            if (existing.SecurityId != persisted.SecurityId
                || !string.Equals(existing.Status, "Open", StringComparison.OrdinalIgnoreCase)
                || (!string.Equals(existing.ConflictKind, SecurityMasterConflictKinds.EconomicTermMismatch, StringComparison.Ordinal)
                    && !string.Equals(existing.ConflictKind, SecurityMasterConflictKinds.CommonTermMismatch, StringComparison.Ordinal)))
            {
                continue;
            }

            var persistedValue = SecurityMasterConflictDetection.ReadComparableFieldValue(persisted, existing.FieldPath, _assetProfileCatalog);
            var declaredFieldType = SecurityMasterConflictDetection.ResolveDeclaredFieldTypeForPath(persisted, existing.FieldPath, _assetProfileCatalog);
            if (!SecurityMasterConflictDetection.FieldConflictIsObsolete(existing, persistedValue, declaredFieldType))
            {
                continue;
            }

            if (SecurityMasterConflictDetection.TryMatchCandidateProvider(existing, persistedSource, out var revisesProviderA))
            {
                // COALESCE before refreshing: pre-persist detection may already have opened a
                // newer conflict for this field and provider pair carrying the live values.
                // Refreshing this row too would surface TWO independently resolvable queue
                // entries for one disagreement — the older row closes into the newer one.
                var newerDuplicate = _conflicts.Values.FirstOrDefault(other =>
                    other.ConflictId != existing.ConflictId
                    && other.SecurityId == existing.SecurityId
                    && string.Equals(other.Status, "Open", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(other.FieldPath, existing.FieldPath, StringComparison.Ordinal)
                    && SecurityMasterConflictDetection.SameProviderPair(other, existing));
                if (newerDuplicate is not null)
                {
                    var coalesced = existing with
                    {
                        Status = "Superseded",
                        ResolvedBy = "system:canonical-write",
                        ResolvedReason =
                            $"Coalesced into conflict '{newerDuplicate.ConflictId:D}': the same providers dispute " +
                            $"'{existing.FieldPath}' with refreshed candidate values recorded there.",
                        ResolvedAt = DateTimeOffset.UtcNow,
                    };
                    if (_conflicts.TryUpdate(conflictId, coalesced, existing))
                    {
                        _logger.LogInformation(
                            "Coalesced open field conflict {ConflictId} into {DuplicateId} ({FieldPath}) for security {SecurityId}.",
                            conflictId, newerDuplicate.ConflictId, existing.FieldPath, existing.SecurityId);
                    }

                    continue;
                }

                var refreshed = revisesProviderA
                    ? existing with { ValueA = persistedValue! }
                    : existing with { ValueB = persistedValue! };
                if (_conflicts.TryUpdate(conflictId, refreshed, existing))
                {
                    _logger.LogInformation(
                        "Refreshed candidate {Provider} on open field conflict {ConflictId} ({FieldPath}) for security {SecurityId}: the candidate revised its own value.",
                        revisesProviderA ? existing.ProviderA : existing.ProviderB,
                        conflictId, existing.FieldPath, existing.SecurityId);
                }

                continue;
            }

            // An UNKNOWN author must never retire a real disagreement on guesswork.
            if (string.Equals(persistedSource, SecurityMasterProvenanceReader.UnknownSource, StringComparison.OrdinalIgnoreCase))
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

        return Task.CompletedTask;
    }
}
