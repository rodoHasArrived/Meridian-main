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
        var detected = await DetectConflictsAsync(ct).ConfigureAwait(false);

        foreach (var conflict in detected)
        {
            // Preserve existing resolution state; only add newly detected conflicts.
            _conflicts.TryAdd(conflict.ConflictId, conflict);
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

        var newStatus = request.Resolution.Equals("Dismiss", StringComparison.OrdinalIgnoreCase)
            ? "Dismissed"
            : "Resolved";

        var updated = existing with { Status = newStatus };
        _conflicts[request.ConflictId] = updated;

        _logger.LogInformation(
            "Conflict {ConflictId} for security {SecurityId} {Status} by {ResolvedBy}",
            request.ConflictId, existing.SecurityId, newStatus, request.ResolvedBy);

        return Task.FromResult<SecurityMasterConflict?>(updated);
    }

    private async Task<IReadOnlyList<SecurityMasterConflict>> DetectConflictsAsync(CancellationToken ct)
    {
        var all = await _store.LoadAllAsync(ct).ConfigureAwait(false);

        // Group identifiers by (kind, value) across all securities; flag where multiple
        // distinct SecurityIds reference the same identifier from different providers.
        var byIdentifier = new Dictionary<(string Kind, string Value), List<(Guid SecurityId, string Provider)>>(
            StringComparer.OrdinalIgnoreCase.ToEqualityComparer());

        foreach (var record in all)
        {
            foreach (var id in record.Identifiers)
            {
                var key = (id.Kind.ToString(), id.Value);
                if (!byIdentifier.TryGetValue(key, out var entries))
                {
                    entries = new List<(Guid, string)>();
                    byIdentifier[key] = entries;
                }

                var provider = id.Provider ?? "Unknown";
                if (!entries.Any(e => e.SecurityId == record.SecurityId))
                    entries.Add((record.SecurityId, provider));
            }
        }

        var conflicts = new List<SecurityMasterConflict>();
        foreach (var ((kind, value), entries) in byIdentifier)
        {
            if (entries.Count < 2)
                continue;

            var distinctSecurities = entries.DistinctBy(e => e.SecurityId).ToList();
            if (distinctSecurities.Count < 2)
                continue;

            var a = distinctSecurities[0];
            var b = distinctSecurities[1];

            var conflictId = DeterministicConflictId(kind, value, a.SecurityId, b.SecurityId);

            // Only emit if not already tracked (to avoid re-opening resolved conflicts).
            if (_conflicts.TryGetValue(conflictId, out var existing) && existing.Status != "Open")
                continue;

            conflicts.Add(new SecurityMasterConflict(
                ConflictId: conflictId,
                SecurityId: a.SecurityId,
                ConflictKind: "IdentifierAmbiguity",
                FieldPath: $"Identifiers.{kind}",
                ProviderA: a.Provider,
                ValueA: a.SecurityId.ToString(),
                ProviderB: b.Provider,
                ValueB: b.SecurityId.ToString(),
                DetectedAt: DateTimeOffset.UtcNow,
                Status: "Open"));
        }

        if (conflicts.Count > 0)
            _logger.LogInformation("Detected {Count} identifier conflicts in Security Master", conflicts.Count);

        return conflicts;
    }

    /// <summary>
    /// Generates a stable conflict ID from the identifier tuple so that re-detection
    /// of the same conflict yields the same ID.
    /// </summary>
    private static Guid DeterministicConflictId(string kind, string value, Guid secA, Guid secB)
    {
        var ordered = secA.CompareTo(secB) <= 0
            ? $"{kind}|{value}|{secA}|{secB}"
            : $"{kind}|{value}|{secB}|{secA}";

        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(ordered));
        return new Guid(bytes);
    }
}

/// <summary>
/// Extension to produce an <see cref="IEqualityComparer{T}"/> from a <see cref="StringComparer"/>
/// for use with value-tuple dictionary keys.
/// </summary>
file static class StringComparerExtensions
{
    internal static IEqualityComparer<(string, string)> ToEqualityComparer(this StringComparer comparer)
        => new TupleStringComparer(comparer);

    private sealed class TupleStringComparer : IEqualityComparer<(string, string)>
    {
        private readonly StringComparer _inner;
        internal TupleStringComparer(StringComparer inner) => _inner = inner;
        public bool Equals((string, string) x, (string, string) y)
            => _inner.Equals(x.Item1, y.Item1) && _inner.Equals(x.Item2, y.Item2);
        public int GetHashCode((string, string) obj)
            => HashCode.Combine(_inner.GetHashCode(obj.Item1), _inner.GetHashCode(obj.Item2));
    }
}
