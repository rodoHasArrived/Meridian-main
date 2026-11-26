using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Microsoft.Extensions.Options;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Review P2 / A2: golden-record write admission. Decides whether an incoming source may overwrite a
/// field whose incumbent value another source supplied. Ranking is per source (owner decision
/// 2026-11-25), taken from <see cref="SecurityMasterWorkbenchOptions.SourcePrecedence"/> and
/// <see cref="SecurityMasterWorkbenchOptions.GoldenCopySource"/>.
/// </summary>
public interface ISecurityMasterSourceAdmissionPolicy
{
    /// <summary>
    /// True when <paramref name="incumbentSource"/> outranks <paramref name="challengerSource"/>, so the
    /// challenger's value must be held for review instead of applied. Unranked sources are never held
    /// and never hold others (owner decision: unranked sources keep applying as before).
    /// </summary>
    bool IncumbentOutranks(string? incumbentSource, string? challengerSource);
}

/// <summary>Per-source precedence admission over the workbench source ladder.</summary>
public sealed class SourcePrecedenceAdmissionPolicy : ISecurityMasterSourceAdmissionPolicy
{
    private readonly IOptionsMonitor<SecurityMasterWorkbenchOptions>? _monitor;
    private readonly SecurityMasterWorkbenchOptions _fallback;

    public SourcePrecedenceAdmissionPolicy(IOptionsMonitor<SecurityMasterWorkbenchOptions> monitor)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _fallback = new SecurityMasterWorkbenchOptions();
    }

    /// <summary>Creates the policy over a fixed options snapshot (tests and minimal hosts).</summary>
    public SourcePrecedenceAdmissionPolicy(SecurityMasterWorkbenchOptions options)
    {
        _fallback = options ?? new SecurityMasterWorkbenchOptions();
    }

    private SecurityMasterWorkbenchOptions Options => _monitor?.CurrentValue ?? _fallback;

    public bool IncumbentOutranks(string? incumbentSource, string? challengerSource)
    {
        if (string.IsNullOrWhiteSpace(incumbentSource)
            || string.IsNullOrWhiteSpace(challengerSource)
            || SameSource(incumbentSource, challengerSource)
            // Governed operator publication is maker-checked; it is never held by a vendor rank.
            || SameSource(challengerSource, SecurityMasterWriteAuthority.WorkbenchSourceSystem))
        {
            return false;
        }

        var options = Options;
        if (!string.IsNullOrWhiteSpace(options.GoldenCopySource)
            && SameSource(incumbentSource, options.GoldenCopySource))
        {
            return true;
        }

        var incumbentRank = RankOf(options, incumbentSource);
        var challengerRank = RankOf(options, challengerSource);
        return incumbentRank is { } incumbent && challengerRank is { } challenger && incumbent < challenger;
    }

    private static int? RankOf(SecurityMasterWorkbenchOptions options, string source)
    {
        var ladder = options.SourcePrecedence ?? [];
        for (var i = 0; i < ladder.Count; i++)
        {
            if (SameSource(ladder[i], source))
            {
                return i;
            }
        }

        return null;
    }

    private static bool SameSource(string left, string right)
        => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Lifecycle of a held amendment.</summary>
public static class SecurityMasterHeldAmendmentStatus
{
    public const string Held = "Held";
    public const string Replayed = "Replayed";
    public const string Discarded = "Discarded";
}

/// <summary>
/// An ingest amendment that was not applied because a lower-ranked source tried to overwrite a value a
/// higher-ranked source supplied. The full request is retained so an operator can replay it through a
/// governed draft; <see cref="DominatedFieldPaths"/> names the fields that triggered the hold.
/// </summary>
public sealed record SecurityMasterHeldAmendment(
    Guid HeldAmendmentId,
    Guid SecurityId,
    string SourceSystem,
    string RequestJson,
    IReadOnlyList<string> DominatedFieldPaths,
    IReadOnlyList<Guid> ConflictIds,
    long HeldAtVersion,
    DateTimeOffset HeldAt,
    string Status,
    string? ClosedBy = null,
    DateTimeOffset? ClosedAt = null);

/// <summary>Durable store for held amendments.</summary>
public interface ISecurityMasterHeldAmendmentStore
{
    Task AddAsync(SecurityMasterHeldAmendment amendment, CancellationToken ct = default);

    Task<IReadOnlyList<SecurityMasterHeldAmendment>> ListAsync(Guid? securityId, string? status, CancellationToken ct = default);

    Task<SecurityMasterHeldAmendment?> GetAsync(Guid heldAmendmentId, CancellationToken ct = default);

    /// <summary>Closes a <see cref="SecurityMasterHeldAmendmentStatus.Held"/> row; false when it was not held.</summary>
    Task<bool> CloseAsync(Guid heldAmendmentId, string status, string closedBy, CancellationToken ct = default);
}

/// <summary>Process-local held-amendment store for hosts without PostgreSQL.</summary>
public sealed class InMemorySecurityMasterHeldAmendmentStore : ISecurityMasterHeldAmendmentStore
{
    private readonly ConcurrentDictionary<Guid, SecurityMasterHeldAmendment> _rows = new();

    public Task AddAsync(SecurityMasterHeldAmendment amendment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(amendment);
        _rows[amendment.HeldAmendmentId] = amendment;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SecurityMasterHeldAmendment>> ListAsync(Guid? securityId, string? status, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SecurityMasterHeldAmendment>>(_rows.Values
            .Where(row => securityId is null || row.SecurityId == securityId)
            .Where(row => status is null || string.Equals(row.Status, status, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static row => row.HeldAt)
            .ToArray());

    public Task<SecurityMasterHeldAmendment?> GetAsync(Guid heldAmendmentId, CancellationToken ct = default)
        => Task.FromResult(_rows.TryGetValue(heldAmendmentId, out var row) ? row : null);

    public Task<bool> CloseAsync(Guid heldAmendmentId, string status, string closedBy, CancellationToken ct = default)
    {
        while (_rows.TryGetValue(heldAmendmentId, out var existing))
        {
            if (existing.Status != SecurityMasterHeldAmendmentStatus.Held)
            {
                return Task.FromResult(false);
            }

            if (_rows.TryUpdate(heldAmendmentId, existing with { Status = status, ClosedBy = closedBy, ClosedAt = DateTimeOffset.UtcNow }, existing))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }
}

/// <summary>
/// Raised when an ingest amendment is held rather than applied (review A2). Nothing from the amendment
/// was persisted; a conflict is open for each dominated field and the request is retained as
/// <see cref="HeldAmendmentId"/> for governed replay. Derives from <see cref="InvalidOperationException"/>
/// so existing ingest failure classification still treats it as a refused write.
/// </summary>
public sealed class SecurityMasterAmendmentHeldException : InvalidOperationException
{
    public SecurityMasterAmendmentHeldException(Guid securityId, Guid heldAmendmentId, string sourceSystem, IReadOnlyList<string> dominatedFieldPaths)
        : base($"Amendment of security '{securityId:D}' from '{sourceSystem}' was held for review: a higher-ranked source supplied "
               + $"{string.Join(", ", dominatedFieldPaths)}. Nothing was applied; resolve the open conflicts or replay held amendment '{heldAmendmentId:D}' through a governed draft.")
    {
        SecurityId = securityId;
        HeldAmendmentId = heldAmendmentId;
        SourceSystem = sourceSystem;
        DominatedFieldPaths = dominatedFieldPaths;
    }

    public Guid SecurityId { get; }

    public Guid HeldAmendmentId { get; }

    public string SourceSystem { get; }

    public IReadOnlyList<string> DominatedFieldPaths { get; }
}

/// <summary>Serializes held requests through the Security Master source-generated JSON context.</summary>
internal static class SecurityMasterHeldAmendmentJson
{
    public static string Serialize(AmendSecurityTermsRequest request)
        => JsonSerializer.Serialize(request, SecurityMasterJsonContext.Default.AmendSecurityTermsRequest);

    public static AmendSecurityTermsRequest? Deserialize(string json)
        => JsonSerializer.Deserialize(json, SecurityMasterJsonContext.Default.AmendSecurityTermsRequest);
}
