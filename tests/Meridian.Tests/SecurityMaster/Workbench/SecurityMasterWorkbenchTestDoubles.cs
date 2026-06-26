// BLUEPRINT: scaffolding for the Security Master Passport Workbench governed-write feature.
// See docs/plans/security-master-passport-workbench.md.
//
// The following types are defined by the blueprint and DO NOT EXIST YET — these test doubles
// (and the tests that use them) are intended to fail to compile until Phase 1–2 lands:
//   - ISecurityMasterWorkbenchCommandService          (Meridian.Application.SecurityMaster)
//   - ISecurityMasterConflictAuthorityPolicy          (Meridian.Application.SecurityMaster)
//   - ISecurityMasterRevisionPublishedHandler         (Meridian.Application.SecurityMaster)
//   - SecurityMasterRevisionPublishedEvent            (Meridian.Contracts.Workstation)
//
// Real, already-existing seams this file builds on:
//   - ISecurityMasterEventStore  (src/Meridian.Storage/SecurityMaster/ISecurityMasterEventStore.cs)
//   - SecurityMasterEventEnvelope, CorporateActionDto (Meridian.Contracts.SecurityMaster)

using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// In-memory, version-aware fake of <see cref="ISecurityMasterEventStore"/>. Models the optimistic
/// concurrency contract: <see cref="AppendAsync"/> throws when the caller's expectedVersion does not
/// match the current stream version. This is the seam the workbench command service relies on for
/// the stale-version → HTTP 409 invariant.
/// </summary>
public sealed class FakeSecurityMasterEventStore : ISecurityMasterEventStore
{
    private readonly Dictionary<Guid, List<SecurityMasterEventEnvelope>> _streams = new();
    private readonly List<CorporateActionDto> _corporateActions = new();
    private long _globalSequence;

    /// <summary>Records every append the service performs, for envelope/concurrency assertions.</summary>
    public List<AppendCall> Appends { get; } = new();

    public sealed record AppendCall(Guid SecurityId, long ExpectedVersion, IReadOnlyList<SecurityMasterEventEnvelope> Events);

    /// <summary>Seeds a stream to a known version so tests can simulate a record loaded at version N.</summary>
    public void SeedVersion(Guid securityId, long version)
    {
        var list = new List<SecurityMasterEventEnvelope>();
        for (var i = 0; i < version; i++)
        {
            list.Add(default!); // placeholder — only the count (version) matters for concurrency
        }

        _streams[securityId] = list;
    }

    private long CurrentVersion(Guid securityId)
        => _streams.TryGetValue(securityId, out var list) ? list.Count : 0;

    public Task AppendAsync(
        Guid securityId,
        long expectedVersion,
        IReadOnlyList<SecurityMasterEventEnvelope> events,
        CancellationToken ct = default)
    {
        Appends.Add(new AppendCall(securityId, expectedVersion, events));

        var current = CurrentVersion(securityId);
        if (expectedVersion != current)
        {
            // The real store surfaces a concurrency failure here; the command service maps it to 409.
            throw new SecurityMasterConcurrencyException(securityId, expectedVersion, current);
        }

        if (!_streams.TryGetValue(securityId, out var list))
        {
            list = new List<SecurityMasterEventEnvelope>();
            _streams[securityId] = list;
        }

        list.AddRange(events);
        _globalSequence += events.Count;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(
            _streams.TryGetValue(securityId, out var list) ? list.ToArray() : Array.Empty<SecurityMasterEventEnvelope>());

    public Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadSinceSequenceAsync(long sequenceExclusive, int take, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(Array.Empty<SecurityMasterEventEnvelope>());

    public Task<long> GetLatestSequenceAsync(CancellationToken ct = default)
        => Task.FromResult(_globalSequence);

    public Task AppendCorporateActionAsync(CorporateActionDto action, CancellationToken ct = default)
    {
        _corporateActions.Add(action);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CorporateActionDto>> LoadCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CorporateActionDto>>(
            _corporateActions.Where(a => a.SecurityId == securityId).ToArray());
}

/// <summary>
/// Stand-in concurrency exception. BLUEPRINT: the real store's concurrency failure type should be
/// used once known; the command service is expected to catch it and surface HTTP 409. Tests assert
/// the service translates a version mismatch into its concurrency result, regardless of the exact
/// exception type the production store throws.
/// </summary>
public sealed class SecurityMasterConcurrencyException : Exception
{
    public SecurityMasterConcurrencyException(Guid securityId, long expectedVersion, long currentVersion)
        : base($"Security {securityId:D} expected version {expectedVersion} but current is {currentVersion}.")
    {
        SecurityId = securityId;
        ExpectedVersion = expectedVersion;
        CurrentVersion = currentVersion;
    }

    public Guid SecurityId { get; }
    public long ExpectedVersion { get; }
    public long CurrentVersion { get; }
}

/// <summary>
/// Records every published-revision event it receives and its invocation order, so tests can assert
/// handler ordering and idempotent fan-out after a durable publish.
/// BLUEPRINT: implements ISecurityMasterRevisionPublishedHandler (not yet implemented).
/// </summary>
public sealed class RecordingRevisionPublishedHandler : ISecurityMasterRevisionPublishedHandler
{
    private readonly Action? _onHandle;

    public RecordingRevisionPublishedHandler(int order, Action? onHandle = null)
    {
        Order = order;
        _onHandle = onHandle;
    }

    public int Order { get; }

    public List<SecurityMasterRevisionPublishedEvent> Received { get; } = new();

    public Task HandleAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default)
    {
        Received.Add(evt);
        _onHandle?.Invoke();
        return Task.CompletedTask;
    }
}
