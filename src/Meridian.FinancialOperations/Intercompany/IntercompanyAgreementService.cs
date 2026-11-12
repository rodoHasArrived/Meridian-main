using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Entities.FundStructure;
using Meridian.Storage.FundStructure;

namespace Meridian.FinancialOperations.Intercompany;

public interface IIntercompanyAgreementScopeAuthority
{
    Task<IAsyncDisposable> AcquireLeaseAsync(string tenantId, IntercompanyAgreementDto agreement, CancellationToken ct = default);
    Task<IAsyncDisposable> AcquireLeasesAsync(string tenantId, IReadOnlyList<IntercompanyAgreementDto> agreements, CancellationToken ct = default)
        => agreements.Count == 1 ? AcquireLeaseAsync(tenantId, agreements[0], ct)
            : Task.FromException<IAsyncDisposable>(new NotSupportedException("This scope authority cannot lease all novation parties."));
    Task ValidateAsync(string tenantId, IntercompanyAgreementDto agreement, bool requireActive, CancellationToken ct = default);
    Task<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>> GetLifecycleEvidenceAsync(string tenantId, IntercompanyAgreementDto agreement, CancellationToken ct = default);
}

public interface IIntercompanyAgreementEvidenceAuthority
{
    Task VerifyAsync(string tenantId, Guid agreementId, IntercompanySignedDocumentDto document, CancellationToken ct = default);
}

/// <summary>Coordinates exact agreement revisions; approvals confer no journal posting or cash authority.</summary>
public sealed class IntercompanyAgreementService(
    IIntercompanyAgreementStore store,
    IIntercompanyAgreementScopeAuthority scopeAuthority,
    IIntercompanyAgreementEvidenceAuthority evidenceAuthority,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<IntercompanyAgreementDto>> ListAsync(string tenantId, Guid? organizationId = null, CancellationToken ct = default)
        => store.ListAsync(Require(tenantId, "tenant"), organizationId, ct);

    public async Task<IntercompanyAgreementDto?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
        => (await GetHistoryAsync(tenantId, id, ct).ConfigureAwait(false))?.Current;

    public Task<IntercompanyAgreementHistoryDto?> GetHistoryAsync(string tenantId, Guid id, CancellationToken ct = default)
        => store.GetAsync(Require(tenantId, "tenant"), id, ct);

    public async Task<IntercompanyAgreementDto> CreateAsync(string tenantId, string actor, CreateIntercompanyAgreementRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request.RequestId, actor, tenantId);
        var payload = JsonSerializer.Serialize(request, IntercompanyContractsJsonContext.Default.CreateIntercompanyAgreementRequest);
        var digest = Digest(actor, IntercompanyAgreementActionDto.Create, payload);
        request = JsonSerializer.Deserialize(payload,
            IntercompanyContractsJsonContext.Default.CreateIntercompanyAgreementRequest)!;
        if (await RetryAsync(tenantId, request.AgreementId, request.RequestId, digest, ct).ConfigureAwait(false) is { } retry) return retry;
        var now = _clock.GetUtcNow();
        var agreement = new IntercompanyAgreementDto(request.AgreementId, request.OrganizationId,
            Require(request.FacilityReference, "facility reference"), request.Kind, request.PartyOneEntityId, request.PartyTwoEntityId,
            Require(request.PartyOneRole, "first party role"), Require(request.PartyTwoRole, "second party role"),
            Require(request.Currency, "currency").ToUpperInvariant(), Require(request.SettlementCadence, "settlement cadence"),
            request.EffectiveFrom.ToUniversalTime(), request.EffectiveTo?.ToUniversalTime(), Guid.NewGuid(), 1, 1,
            IntercompanyAgreementStatusDto.Draft, request.PartyMappings, request.SignedDocument, actor, now, actor, now)
            { SettlementDueDays = request.SettlementDueDays };
        IntercompanyAgreementPolicy.ValidateTerms(agreement);
        await using var scopeLease = await scopeAuthority.AcquireLeaseAsync(tenantId, agreement, ct).ConfigureAwait(false);
        return await store.MutateAsync(tenantId, request.AgreementId, request.RequestId, digest, async (history, token) =>
        {
            if (history is not null) throw new InvalidOperationException("The agreement identity already exists.");
            var recorded = _clock.GetUtcNow();
            var retained = agreement with { CreatedAt = recorded, UpdatedAt = recorded };
            await scopeAuthority.ValidateAsync(tenantId, retained, requireActive: false, token).ConfigureAwait(false);
            return new(request.RequestId, digest, IntercompanyAgreementActionDto.Create, actor, recorded, null, retained);
        }, ct).ConfigureAwait(false);
    }

    public async Task<IntercompanyAgreementDto> ReviseAsync(string tenantId, string actor, Guid agreementId, ReviseIntercompanyAgreementRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request.RequestId, actor, tenantId);
        var payload = JsonSerializer.Serialize(request, IntercompanyContractsJsonContext.Default.ReviseIntercompanyAgreementRequest);
        var digest = Digest(actor, IntercompanyAgreementActionDto.Revise, payload);
        request = JsonSerializer.Deserialize(payload,
            IntercompanyContractsJsonContext.Default.ReviseIntercompanyAgreementRequest)!;
        if (await RetryAsync(tenantId, agreementId, request.RequestId, digest, ct).ConfigureAwait(false) is { } retry) return retry;
        var history = await RequiredHistoryAsync(tenantId, agreementId, ct).ConfigureAwait(false);
        var old = history.Current;
        if (old.Version != request.ExpectedVersion) throw new InvalidOperationException("The agreement version changed; reload before revising.");
        if (old.Status is IntercompanyAgreementStatusDto.Submitted or IntercompanyAgreementStatusDto.Novated)
            throw new InvalidOperationException("Submitted or novated agreement terms cannot be revised.");
        var now = _clock.GetUtcNow();
        var revised = old with
        {
            FacilityReference = Require(request.FacilityReference, "facility reference"),
            PartyOneRole = Require(request.PartyOneRole, "first party role"), PartyTwoRole = Require(request.PartyTwoRole, "second party role"),
            Currency = Require(request.Currency, "currency").ToUpperInvariant(), SettlementCadence = Require(request.SettlementCadence, "settlement cadence"),
            SettlementDueDays = request.SettlementDueDays,
            EffectiveFrom = request.EffectiveFrom.ToUniversalTime(), EffectiveTo = request.EffectiveTo?.ToUniversalTime(),
            PartyMappings = request.PartyMappings, SignedDocument = request.SignedDocument, RevisionId = Guid.NewGuid(),
            RevisionNumber = old.RevisionNumber + 1, Version = old.Version + 1, Status = IntercompanyAgreementStatusDto.Draft,
            UpdatedBy = actor, UpdatedAt = now, ApprovedBy = null, ApprovedAt = null, TerminatedAt = null,
            PredecessorRevisionId = old.RevisionId, SuccessorAgreementId = null, LifecycleEvidence = null,
            NovationSuccessorRevisionId = null, NovationSuccessorVersion = null, NovationSuccessorLifecycleEvidence = null,
            SupersedesRevisionId = history.Events.LastOrDefault(x => x.Action == IntercompanyAgreementActionDto.Approve)?.AffectedRevision.RevisionId
        };
        IntercompanyAgreementPolicy.ValidateTerms(revised);
        await using var scopeLease = await scopeAuthority.AcquireLeaseAsync(tenantId, revised, ct).ConfigureAwait(false);
        return await store.MutateAsync(tenantId, agreementId, request.RequestId, digest, async (lockedHistory, token) =>
        {
            if (lockedHistory is null) throw new KeyNotFoundException("Agreement was not found in the current tenant.");
            if (lockedHistory.Current.Version != request.ExpectedVersion)
                throw new InvalidOperationException("The agreement version changed; reload before revising.");
            if (lockedHistory.Events.Any(x => x.Action == IntercompanyAgreementActionDto.Novate))
                throw new InvalidOperationException("A novated facility cannot create further revisions; use its approved successor.");
            if (lockedHistory.Current.Status is IntercompanyAgreementStatusDto.Submitted or IntercompanyAgreementStatusDto.Novated)
                throw new InvalidOperationException("Submitted or novated agreement terms cannot be revised.");
            var recorded = _clock.GetUtcNow();
            var retained = revised with { UpdatedAt = recorded, Version = lockedHistory.Current.Version + 1,
                SupersedesRevisionId = lockedHistory.Events.LastOrDefault(x => x.Action == IntercompanyAgreementActionDto.Approve)?.AffectedRevision.RevisionId };
            await scopeAuthority.ValidateAsync(tenantId, retained, requireActive: false, token).ConfigureAwait(false);
            return new(request.RequestId, digest, IntercompanyAgreementActionDto.Revise, actor, recorded,
                Require(request.Reason, "revision reason"), retained);
        }, ct).ConfigureAwait(false);
    }

    public async Task<IntercompanyAgreementDto> ApplyActionAsync(string tenantId, string actor, Guid agreementId,
        IntercompanyAgreementActionDto action, IntercompanyAgreementActionRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request.RequestId, actor, tenantId);
        var digest = Digest(actor, action, JsonSerializer.Serialize(request, IntercompanyContractsJsonContext.Default.IntercompanyAgreementActionRequest));
        if (await RetryAsync(tenantId, agreementId, request.RequestId, digest, ct).ConfigureAwait(false) is { } retry) return retry;
        var probe = await RequiredHistoryAsync(tenantId, agreementId, ct).ConfigureAwait(false);
        var terminal = action is IntercompanyAgreementActionDto.Terminate or IntercompanyAgreementActionDto.Novate;
        var sourceProbe = SelectRevision(probe, action, request.RevisionId);
        var leaseAgreements = new List<IntercompanyAgreementDto> { sourceProbe };
        long? expectedSuccessorVersion = null;
        if (action == IntercompanyAgreementActionDto.Novate)
        {
            if (request.SuccessorAgreementId is not { } successorId || successorId == agreementId || request.EffectiveAt is null)
                throw new ArgumentException("Novation requires a distinct separately approved successor and an explicit effective date.");
            var successorProbe = await RequiredHistoryAsync(tenantId, successorId, ct).ConfigureAwait(false);
            expectedSuccessorVersion = request.ExpectedSuccessorVersion ?? successorProbe.Current.Version;
            // Legal party identities cannot change inside one agreement. Lock every probed party before
            // acquiring mutation authority, then reload both agreements under that authority below.
            leaseAgreements.Add(successorProbe.Current);
        }
        await using var scopeLease = await scopeAuthority.AcquireLeasesAsync(tenantId, leaseAgreements, ct).ConfigureAwait(false);
        return await store.MutateAsync(tenantId, agreementId, request.RequestId, digest, async (history, token) =>
        {
            if (history is null) throw new KeyNotFoundException("Agreement was not found in the current tenant.");
            var current = history.Current;
            if (current.Version != request.ExpectedVersion)
                throw new InvalidOperationException("The agreement version changed; reload before applying the action.");
            var old = SelectRevision(history, action, request.RevisionId);
            if (!terminal && action != IntercompanyAgreementActionDto.Reject && history.Events.Any(x => x.Action == IntercompanyAgreementActionDto.Novate))
                throw new InvalidOperationException("A novated facility cannot activate or submit further commitments.");
            var status = IntercompanyAgreementPolicy.Transition(old.Status, action);
            var recorded = _clock.GetUtcNow();
            var effective = (request.EffectiveAt ?? recorded).ToUniversalTime();
            if (terminal)
            {
                if (effective < old.EffectiveFrom || effective > recorded)
                    throw new ArgumentException("Termination/novation must take effect within the elapsed agreement window.");
                ResolveApprovedRevision(history, old.RevisionId, effective > old.EffectiveFrom ? effective.AddTicks(-1) : effective);
                // Closing legal authority must remain possible after old account mappings are archived.
                await scopeAuthority.ValidateAsync(tenantId, old with { EffectiveTo = effective, PartyMappings = [] }, false, token).ConfigureAwait(false);
            }
            else await scopeAuthority.ValidateAsync(tenantId, old, action == IntercompanyAgreementActionDto.Approve, token).ConfigureAwait(false);

            IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>? lifecycleEvidence = old.LifecycleEvidence;
            IntercompanyAgreementDto? successor = null;
            IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>? successorLifecycle = null;
            if (action == IntercompanyAgreementActionDto.Approve)
            {
                if (old.SignedDocument is null) throw new InvalidOperationException("Approval requires retained signed agreement evidence.");
                await evidenceAuthority.VerifyAsync(tenantId, agreementId, old.SignedDocument, token).ConfigureAwait(false);
                lifecycleEvidence = await scopeAuthority.GetLifecycleEvidenceAsync(tenantId, old, token).ConfigureAwait(false);
                if (lifecycleEvidence.Count != 2) throw new InvalidOperationException("Approval requires exact lifecycle evidence for both parties.");
                ValidateNoApprovedOverlap(history, old);
            }
            if (action == IntercompanyAgreementActionDto.Novate)
            {
                var successorHistory = await RequiredHistoryAsync(tenantId, request.SuccessorAgreementId!.Value, token).ConfigureAwait(false);
                if (successorHistory.Current.Version != expectedSuccessorVersion)
                    throw new InvalidOperationException("The novation successor changed; reload its exact approved revision and retry.");
                successor = request.SuccessorRevisionId is { } revision
                    ? ResolveApprovedRevision(successorHistory, revision, effective)
                    : ResolveApprovedAt(successorHistory, effective);
                if (successor.OrganizationId != old.OrganizationId || successor.Kind != old.Kind ||
                    successor.Currency != old.Currency || successor.EffectiveFrom != effective)
                    throw new InvalidOperationException("Novation successor must match organization, kind, currency and the explicit effective date.");
                await scopeAuthority.ValidateAsync(tenantId, successor, true, token).ConfigureAwait(false);
                successorLifecycle = await scopeAuthority.GetLifecycleEvidenceAsync(tenantId, successor, token).ConfigureAwait(false);
                if (successorLifecycle.Count != 2) throw new InvalidOperationException("Novation requires exact successor-party lifecycle evidence.");
                if (successor.SignedDocument is null) throw new InvalidOperationException("The approved novation successor lacks signed evidence.");
                await evidenceAuthority.VerifyAsync(tenantId, successor.AgreementId, successor.SignedDocument, token).ConfigureAwait(false);
            }
            var affected = old with
            {
                Status = status, Version = current.Version + 1, UpdatedAt = recorded, UpdatedBy = actor,
                ApprovedBy = action == IntercompanyAgreementActionDto.Approve ? actor : old.ApprovedBy,
                ApprovedAt = action == IntercompanyAgreementActionDto.Approve ? recorded : old.ApprovedAt,
                TerminatedAt = terminal ? effective : old.TerminatedAt,
                SuccessorAgreementId = successor?.AgreementId ?? old.SuccessorAgreementId,
                NovationSuccessorRevisionId = successor?.RevisionId ?? old.NovationSuccessorRevisionId,
                NovationSuccessorVersion = successor is not null ? expectedSuccessorVersion : old.NovationSuccessorVersion,
                NovationSuccessorLifecycleEvidence = successorLifecycle ?? old.NovationSuccessorLifecycleEvidence,
                LifecycleEvidence = lifecycleEvidence
            };
            var targeted = old.RevisionId != current.RevisionId;
            var projection = targeted ? current with { Version = current.Version + 1, UpdatedBy = actor, UpdatedAt = recorded } : affected;
            return new IntercompanyAgreementEventDto(request.RequestId, digest, action, actor, recorded,
                action is IntercompanyAgreementActionDto.Reject or IntercompanyAgreementActionDto.Terminate or IntercompanyAgreementActionDto.Novate
                    ? Require(request.Reason, "action reason") : request.Reason, projection,
                TargetRevisionId: targeted ? old.RevisionId : null, TargetRevisionSnapshot: targeted ? affected : null);
        }, ct).ConfigureAwait(false);
    }

    public async Task<IntercompanyAgreementDto> ResolveApprovedRevisionAsync(string tenantId, Guid agreementId, Guid revisionId,
        DateTimeOffset economicDate, CancellationToken ct = default)
        => ResolveApprovedRevision(await RequiredHistoryAsync(tenantId, agreementId, ct).ConfigureAwait(false), revisionId, economicDate);

    private static IntercompanyAgreementDto SelectRevision(IntercompanyAgreementHistoryDto history,
        IntercompanyAgreementActionDto action, Guid? revisionId)
    {
        var id = revisionId ?? history.Current.RevisionId;
        if (action is not (IntercompanyAgreementActionDto.Terminate or IntercompanyAgreementActionDto.Novate) && id != history.Current.RevisionId)
            throw new InvalidOperationException("Review actions must target the current authoring revision.");
        return history.Events.LastOrDefault(x => x.AffectedRevision.RevisionId == id)?.AffectedRevision
            ?? throw new KeyNotFoundException("The requested agreement revision was not found.");
    }

    private static IntercompanyAgreementDto ResolveApprovedRevision(IntercompanyAgreementHistoryDto history, Guid revisionId,
        DateTimeOffset economicDate)
        => IntercompanyAgreementRevisionResolver.ResolveApprovedRevision(history, revisionId, economicDate);

    private static IntercompanyAgreementDto ResolveApprovedAt(IntercompanyAgreementHistoryDto history, DateTimeOffset economicDate)
    {
        var candidates = new List<IntercompanyAgreementDto>();
        foreach (var id in history.Events.Where(x => x.Action == IntercompanyAgreementActionDto.Approve)
            .Select(x => x.AffectedRevision.RevisionId).Distinct())
        {
            try { candidates.Add(ResolveApprovedRevision(history, id, economicDate)); }
            catch (InvalidOperationException) { }
        }
        return candidates.Count == 1 ? candidates[0]
            : throw new InvalidOperationException("The novation date requires exactly one effective approved successor revision.");
    }

    private static void ValidateNoApprovedOverlap(IntercompanyAgreementHistoryDto history, IntercompanyAgreementDto candidate)
    {
        foreach (var group in history.Events.Where(x => x.AffectedRevision.RevisionId != candidate.RevisionId).GroupBy(x => x.AffectedRevision.RevisionId))
        {
            var approval = group.FirstOrDefault(x => x.Action == IntercompanyAgreementActionDto.Approve)?.AffectedRevision;
            if (approval is null) continue;
            var termination = group.LastOrDefault(x => x.Action is IntercompanyAgreementActionDto.Terminate or IntercompanyAgreementActionDto.Novate)?.AffectedRevision.TerminatedAt;
            var end = termination is { } stopped && (approval.EffectiveTo is null || stopped < approval.EffectiveTo) ? stopped : approval.EffectiveTo ?? DateTimeOffset.MaxValue;
            var priorSupersession = history.Events.Where(x => x.Action == IntercompanyAgreementActionDto.Approve &&
                    x.AffectedRevision.SupersedesRevisionId == approval.RevisionId).Select(x => (DateTimeOffset?)x.AffectedRevision.EffectiveFrom).Min();
            if (priorSupersession is { } priorCutoff && priorCutoff < end) end = priorCutoff;
            if (candidate.SupersedesRevisionId == approval.RevisionId)
            {
                if (candidate.EffectiveFrom <= approval.EffectiveFrom)
                    throw new InvalidOperationException("An approved amendment must start after its approved predecessor; historical terms are immutable.");
                if (candidate.EffectiveFrom < end) end = candidate.EffectiveFrom;
            }
            if (candidate.EffectiveFrom < end && approval.EffectiveFrom < (candidate.EffectiveTo ?? DateTimeOffset.MaxValue))
                throw new InvalidOperationException("Approved revisions of one facility cannot have conflicting effective windows.");
        }
    }

    private async Task<IntercompanyAgreementDto?> RetryAsync(string tenant, Guid id, Guid requestId, string digest, CancellationToken ct)
    {
        var history = await store.GetAsync(tenant, id, ct).ConfigureAwait(false);
        var retry = history?.Events.SingleOrDefault(x => x.RequestId == requestId);
        if (retry is null) return null;
        if (retry.RequestDigest != digest) throw new InvalidOperationException("The agreement request ID is retained with different content.");
        return retry.Snapshot;
    }

    private async Task<IntercompanyAgreementHistoryDto> RequiredHistoryAsync(string tenant, Guid id, CancellationToken ct)
        => await store.GetAsync(tenant, id, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException("Agreement was not found in the current tenant.");

    private static string Require(string? value, string field) => IntercompanyAgreementPolicy.RequireText(value, field);
    private static void ValidateRequest(Guid requestId, string actor, string tenant)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("A request ID is required.");
        Require(actor, "authenticated actor"); Require(tenant, "tenant");
    }
    private static string Digest(string actor, IntercompanyAgreementActionDto action, string json)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor + "\n" + action + "\n" + json))).ToLowerInvariant();
}
