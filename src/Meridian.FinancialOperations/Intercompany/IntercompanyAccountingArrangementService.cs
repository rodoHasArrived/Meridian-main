using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Entities.FundStructure;
using Meridian.Storage.FundStructure;

namespace Meridian.FinancialOperations.Intercompany;

public interface IIntercompanyAccountingArrangementScopeLease : IAsyncDisposable
{
    LegalEntityReadAuthorityProof? StructureAuthorityProof { get; }
}
public interface IIntercompanyAccountingArrangementScopeAuthority
{
    Task<IIntercompanyAccountingArrangementScopeLease> AcquireLeaseAsync(string tenantId,
        ResolvedIntercompanyAccountingArrangementDto arrangement, CancellationToken ct = default);
    Task ValidateAsync(string tenantId, ResolvedIntercompanyAccountingArrangementDto arrangement, bool requireActive, CancellationToken ct = default);
    Task<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>> GetLifecycleEvidenceAsync(string tenantId,
        ResolvedIntercompanyAccountingArrangementDto arrangement, CancellationToken ct = default);
}

/// <summary>Approves accounting configuration only; native legal terms, review and history are never authored here.</summary>
public sealed class IntercompanyAccountingArrangementService(IIntercompanyAccountingArrangementStore store,
    IIntercompanyAccountingArrangementScopeAuthority scopeAuthority,
    IIntercompanyAccountingArrangementLegalAuthority legalAuthority, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    public async Task<IReadOnlyList<ResolvedIntercompanyAccountingArrangementDto>> ListAsync(string tenantId,
        Guid? organizationId = null, CancellationToken ct = default)
    {
        var results = new List<ResolvedIntercompanyAccountingArrangementDto>();
        foreach (var item in await store.ListAsync(Require(tenantId, "tenant"), organizationId, ct).ConfigureAwait(false))
            results.Add(await ResolveExactAsync(tenantId, item, ct).ConfigureAwait(false));
        return results;
    }
    public async Task<ResolvedIntercompanyAccountingArrangementDto?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var history = await GetHistoryAsync(tenantId, id, ct).ConfigureAwait(false);
        return history is null ? null : await ResolveExactAsync(tenantId, history.Current, ct).ConfigureAwait(false);
    }
    public Task<IntercompanyAccountingArrangementHistoryDto?> GetHistoryAsync(string tenantId, Guid id, CancellationToken ct = default)
        => store.GetAsync(Require(tenantId, "tenant"), id, ct);
    public async Task<ResolvedIntercompanyAccountingArrangementDto> ResolveApprovedRevisionAsync(string tenantId, Guid arrangementId,
        Guid revisionId, DateTimeOffset economicDate, CancellationToken ct = default)
    {
        var history = await RequiredHistoryAsync(tenantId, arrangementId, ct).ConfigureAwait(false);
        var approved = IntercompanyAccountingArrangementRevisionResolver.ResolveApprovedRevision(history, revisionId, economicDate);
        var legal = await legalAuthority.GetEffectiveRevisionAsync(tenantId, approved.CanonicalReference,
            DateOnly.FromDateTime(economicDate.UtcDateTime), ct).ConfigureAwait(false);
        return new(approved, legal);
    }
    /// <summary>Exact immutable configuration for proven source inverses/history; does not grant new posting eligibility.</summary>
    public async Task<ResolvedIntercompanyAccountingArrangementDto> GetApprovedRevisionAsync(string tenantId, Guid arrangementId,
        Guid revisionId, CancellationToken ct = default)
    {
        var history = await RequiredHistoryAsync(tenantId, arrangementId, ct).ConfigureAwait(false);
        var approved = history.Events.FirstOrDefault(item => item.Action == IntercompanyAccountingArrangementActionDto.Approve
            && item.AffectedRevision.RevisionId == revisionId)?.AffectedRevision
            ?? throw new InvalidOperationException("The exact accounting arrangement revision has not been approved.");
        return await ResolveExactAsync(tenantId, approved, ct).ConfigureAwait(false);
    }
    public async Task<IntercompanyAccountingArrangementDto> CreateAsync(string tenantId, string actor,
        CreateIntercompanyAccountingArrangementRequest request, CancellationToken ct = default)
    {
        ValidateRequest(tenantId, actor, request.RequestId);
        var payload = JsonSerializer.Serialize(request, IntercompanyAccountingArrangementsJsonContext.Default.CreateIntercompanyAccountingArrangementRequest);
        var digest = Digest(actor, IntercompanyAccountingArrangementActionDto.Create, payload);
        request = JsonSerializer.Deserialize(payload, IntercompanyAccountingArrangementsJsonContext.Default.CreateIntercompanyAccountingArrangementRequest)!;
        if (await RetryAsync(tenantId, request.ArrangementId, request.RequestId, digest, ct).ConfigureAwait(false) is { } retry)
            return retry;
        var now = _clock.GetUtcNow();
        var arrangement = new IntercompanyAccountingArrangementDto(request.ArrangementId, request.OrganizationId,
            Require(request.ArrangementReference, "reference"), request.TemplateKind, request.CanonicalAgreementId,
            request.CanonicalAgreementVersion, request.CanonicalPayloadHash, request.EffectiveFrom.ToUniversalTime(),
            request.EffectiveTo?.ToUniversalTime(), Guid.NewGuid(), 1, 1, IntercompanyAccountingArrangementStatusDto.Draft,
            request.PartyMappings, actor, now, actor, now);
        var probe = await ResolveExactAsync(tenantId, arrangement, ct).ConfigureAwait(false);
        IntercompanyAccountingArrangementPolicy.Validate(arrangement, probe.LegalRevision);
        await using var scope = await scopeAuthority.AcquireLeaseAsync(tenantId, probe, ct).ConfigureAwait(false);
        await using var legal = await legalAuthority.AcquireLeaseAsync(tenantId, [arrangement.CanonicalReference],
            scope.StructureAuthorityProof, ct).ConfigureAwait(false);
        return await store.MutateAsync(tenantId, arrangement.ArrangementId, request.RequestId, digest, async (history, token) =>
        {
            if (history is not null)
                throw new InvalidOperationException("The accounting arrangement identity already exists.");
            var retained = arrangement with { CreatedAt = _clock.GetUtcNow(), UpdatedAt = _clock.GetUtcNow() };
            var resolved = new ResolvedIntercompanyAccountingArrangementDto(retained, legal.ResolveExact(retained.CanonicalReference));
            IntercompanyAccountingArrangementPolicy.Validate(retained, resolved.LegalRevision);
            await scopeAuthority.ValidateAsync(tenantId, resolved, requireActive: false, token).ConfigureAwait(false);
            return new(request.RequestId, digest, IntercompanyAccountingArrangementActionDto.Create, actor, retained.UpdatedAt, null, retained);
        }, ct).ConfigureAwait(false);
    }
    public async Task<IntercompanyAccountingArrangementDto> ReviseAsync(string tenantId, string actor, Guid arrangementId,
        ReviseIntercompanyAccountingArrangementRequest request, CancellationToken ct = default)
    {
        ValidateRequest(tenantId, actor, request.RequestId);
        var payload = JsonSerializer.Serialize(request, IntercompanyAccountingArrangementsJsonContext.Default.ReviseIntercompanyAccountingArrangementRequest);
        var digest = Digest(actor, IntercompanyAccountingArrangementActionDto.Revise, payload);
        request = JsonSerializer.Deserialize(payload, IntercompanyAccountingArrangementsJsonContext.Default.ReviseIntercompanyAccountingArrangementRequest)!;
        if (await RetryAsync(tenantId, arrangementId, request.RequestId, digest, ct).ConfigureAwait(false) is { } retry)
            return retry;
        var history = await RequiredHistoryAsync(tenantId, arrangementId, ct).ConfigureAwait(false);
        var old = history.Current;
        if (old.Version != request.ExpectedVersion || old.Status == IntercompanyAccountingArrangementStatusDto.Submitted)
            throw new InvalidOperationException("The accounting arrangement changed or its mapping revision is submitted.");
        var revised = old with
        {
            ArrangementReference = Require(request.ArrangementReference, "reference"),
            TemplateKind = request.TemplateKind,
            CanonicalAgreementVersion = request.CanonicalAgreementVersion,
            CanonicalPayloadHash = request.CanonicalPayloadHash,
            EffectiveFrom = request.EffectiveFrom.ToUniversalTime(),
            EffectiveTo = request.EffectiveTo?.ToUniversalTime(),
            PartyMappings = request.PartyMappings,
            RevisionId = Guid.NewGuid(),
            RevisionNumber = old.RevisionNumber + 1,
            Version = old.Version + 1,
            Status = IntercompanyAccountingArrangementStatusDto.Draft,
            UpdatedBy = actor,
            UpdatedAt = _clock.GetUtcNow(),
            ApprovedBy = null,
            ApprovedAt = null,
            RetiredAt = null,
            LifecycleEvidence = null,
            PredecessorRevisionId = old.RevisionId,
            SupersedesRevisionId = history.Events.LastOrDefault(item => item.Action == IntercompanyAccountingArrangementActionDto.Approve)?.AffectedRevision.RevisionId
        };
        var probe = await ResolveExactAsync(tenantId, revised, ct).ConfigureAwait(false);
        IntercompanyAccountingArrangementPolicy.Validate(revised, probe.LegalRevision);
        await using var scope = await scopeAuthority.AcquireLeaseAsync(tenantId, probe, ct).ConfigureAwait(false);
        await using var legal = await legalAuthority.AcquireLeaseAsync(tenantId, [revised.CanonicalReference], scope.StructureAuthorityProof, ct).ConfigureAwait(false);
        return await store.MutateAsync(tenantId, arrangementId, request.RequestId, digest, async (locked, token) =>
        {
            if (locked is null || locked.Current.Version != request.ExpectedVersion || locked.Current.Status == IntercompanyAccountingArrangementStatusDto.Submitted)
                throw new InvalidOperationException("The accounting arrangement version changed; reload before revising.");
            var retained = revised with
            {
                Version = locked.Current.Version + 1,
                UpdatedAt = _clock.GetUtcNow(),
                SupersedesRevisionId = locked.Events.LastOrDefault(item => item.Action == IntercompanyAccountingArrangementActionDto.Approve)?.AffectedRevision.RevisionId
            };
            var resolved = new ResolvedIntercompanyAccountingArrangementDto(retained, legal.ResolveExact(retained.CanonicalReference));
            IntercompanyAccountingArrangementPolicy.Validate(retained, resolved.LegalRevision);
            await scopeAuthority.ValidateAsync(tenantId, resolved, requireActive: false, token).ConfigureAwait(false);
            return new(request.RequestId, digest, IntercompanyAccountingArrangementActionDto.Revise, actor, retained.UpdatedAt,
                Require(request.Reason, "revision reason"), retained);
        }, ct).ConfigureAwait(false);
    }
    public async Task<IntercompanyAccountingArrangementDto> ApplyActionAsync(string tenantId, string actor, Guid arrangementId,
        IntercompanyAccountingArrangementActionDto action, IntercompanyAccountingArrangementActionRequest request, CancellationToken ct = default)
    {
        ValidateRequest(tenantId, actor, request.RequestId);
        var digest = Digest(actor, action, JsonSerializer.Serialize(request, IntercompanyAccountingArrangementsJsonContext.Default.IntercompanyAccountingArrangementActionRequest));
        if (await RetryAsync(tenantId, arrangementId, request.RequestId, digest, ct).ConfigureAwait(false) is { } retry)
            return retry;
        var history = await RequiredHistoryAsync(tenantId, arrangementId, ct).ConfigureAwait(false);
        var source = SelectRevision(history, action, request.RevisionId);
        var probe = await ResolveExactAsync(tenantId, source, ct).ConfigureAwait(false);
        await using var scope = await scopeAuthority.AcquireLeaseAsync(tenantId, probe, ct).ConfigureAwait(false);
        await using var legal = await legalAuthority.AcquireLeaseAsync(tenantId, [source.CanonicalReference], scope.StructureAuthorityProof, ct).ConfigureAwait(false);
        return await store.MutateAsync(tenantId, arrangementId, request.RequestId, digest, async (locked, token) =>
        {
            if (locked is null || locked.Current.Version != request.ExpectedVersion)
                throw new InvalidOperationException("The accounting arrangement version changed; reload before acting.");
            var selected = SelectRevision(locked, action, request.RevisionId);
            if (selected.RevisionId != source.RevisionId || selected.CanonicalReference != source.CanonicalReference)
                throw new InvalidOperationException("The accounting configuration changed after authority was acquired.");
            var status = IntercompanyAccountingArrangementPolicy.Transition(selected.Status, action);
            var resolved = new ResolvedIntercompanyAccountingArrangementDto(selected, legal.ResolveExact(selected.CanonicalReference));
            IntercompanyAccountingArrangementPolicy.Validate(selected, resolved.LegalRevision);
            if (action is IntercompanyAccountingArrangementActionDto.Submit or IntercompanyAccountingArrangementActionDto.Approve)
            {
                legal.ValidateWindow(selected.CanonicalReference, selected.EffectiveFrom, selected.EffectiveTo);
                await scopeAuthority.ValidateAsync(tenantId, resolved, requireActive: true, token).ConfigureAwait(false);
            }
            var recorded = _clock.GetUtcNow();
            var next = selected with { Version = locked.Current.Version + 1, Status = status, UpdatedBy = actor, UpdatedAt = recorded };
            if (action == IntercompanyAccountingArrangementActionDto.Approve)
                next = next with
                {
                    ApprovedBy = actor,
                    ApprovedAt = recorded,
                    LifecycleEvidence = await scopeAuthority.GetLifecycleEvidenceAsync(tenantId, resolved, token).ConfigureAwait(false)
                };
            if (action == IntercompanyAccountingArrangementActionDto.Retire)
            {
                var effective = request.EffectiveAt?.ToUniversalTime() ?? recorded;
                if (effective < selected.EffectiveFrom || selected.EffectiveTo is { } end && effective > end)
                    throw new ArgumentException("Mapping retirement must fall within its operational validity window.");
                Require(request.Reason, "retirement reason");
                next = next with { RetiredAt = effective };
            }
            var targeted = selected.RevisionId != locked.Current.RevisionId;
            var snapshot = targeted ? locked.Current with { Version = next.Version } : next;
            return new(request.RequestId, digest, action, actor, recorded, request.Reason, snapshot,
                TargetRevisionId: targeted ? selected.RevisionId : null, TargetRevisionSnapshot: targeted ? next : null);
        }, ct).ConfigureAwait(false);
    }
    private static IntercompanyAccountingArrangementDto SelectRevision(IntercompanyAccountingArrangementHistoryDto history,
        IntercompanyAccountingArrangementActionDto action, Guid? revisionId)
    {
        if (revisionId is null || revisionId == history.Current.RevisionId)
            return history.Current;
        if (action != IntercompanyAccountingArrangementActionDto.Retire)
            throw new InvalidOperationException("Only retirement may target a prior mapping revision.");
        return history.Events.Select(item => item.AffectedRevision).LastOrDefault(item => item.RevisionId == revisionId)
            ?? throw new InvalidOperationException("The requested accounting mapping revision does not exist.");
    }
    private async Task<ResolvedIntercompanyAccountingArrangementDto> ResolveExactAsync(string tenantId,
        IntercompanyAccountingArrangementDto arrangement, CancellationToken ct)
        => new(arrangement, await legalAuthority.GetRevisionAsync(Require(tenantId, "tenant"), arrangement.CanonicalReference, ct).ConfigureAwait(false));
    private async Task<IntercompanyAccountingArrangementHistoryDto> RequiredHistoryAsync(string tenantId, Guid id, CancellationToken ct)
        => await GetHistoryAsync(tenantId, id, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Accounting arrangement was not found in this tenant.");
    private async Task<IntercompanyAccountingArrangementDto?> RetryAsync(string tenantId, Guid id, Guid requestId, string digest, CancellationToken ct)
    {
        var history = await GetHistoryAsync(tenantId, id, ct).ConfigureAwait(false);
        var retained = history?.Events.SingleOrDefault(item => item.RequestId == requestId);
        if (retained is not null && retained.RequestDigest != digest)
            throw new InvalidOperationException("The accounting arrangement request ID is retained with different content.");
        return retained?.Snapshot;
    }
    private static string Digest(string actor, IntercompanyAccountingArrangementActionDto action, string payload)
        => Sha256Digest.ComputeUtf8(actor + "\n" + action + "\n" + payload);
    private static void ValidateRequest(string tenant, string actor, Guid requestId)
    { Require(tenant, "tenant"); Require(actor, "actor"); if (requestId == Guid.Empty) throw new ArgumentException("Request ID is required."); }
    private static string Require(string? value, string field) => IntercompanyAccountingArrangementPolicy.RequireArrangementText(value, field);
}
