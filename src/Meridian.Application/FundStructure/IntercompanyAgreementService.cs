using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Entities.FundStructure;
using Meridian.Storage.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>Agreement commands execute under the store's durable authority lock, including approval rechecks.</summary>
public sealed partial class IntercompanyAgreementService(
    IIntercompanyAgreementStore store, IIntercompanyAgreementAccessAuthority access,
    IIntercompanyAgreementEvidenceAuthority evidence, TimeProvider? timeProvider = null,
    IEntityLifecycleStore? lifecycleStore = null, IFundStructureStore? structureStore = null) : IIntercompanyAgreementService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public Task<IntercompanyAgreementCommandResultDto> CreateAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default)
        => ExecuteAsync(context, command, "Create", ct);
    public Task<IntercompanyAgreementCommandResultDto> CreateProposalAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default)
        => ExecuteAsync(context, command, "CreateProposal", ct);
    public Task<IntercompanyAgreementCommandResultDto> SaveDraftAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default)
        => ExecuteAsync(context, command, "SaveDraft", ct);
    public Task<IntercompanyAgreementCommandResultDto> SubmitAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default)
        => ExecuteAsync(context, command, "Submit", ct);
    public Task<IntercompanyAgreementCommandResultDto> ApproveAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default)
        => ExecuteAsync(context, command, "Approve", ct);
    public Task<IntercompanyAgreementCommandResultDto> RejectAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default)
        => ExecuteAsync(context, command, "Reject", ct);
    public Task<IntercompanyAgreementCommandResultDto> WithdrawAsync(IntercompanyAgreementCallContext context, IntercompanyAgreementCommandDto command, CancellationToken ct = default)
        => ExecuteAsync(context, command, "Withdraw", ct);

    private async Task<IntercompanyAgreementCommandResultDto> ExecuteAsync(IntercompanyAgreementCallContext context,
        IntercompanyAgreementCommandDto command, string action, CancellationToken ct)
    {
        ValidateContext(context);
        if (command.CommandId == Guid.Empty || command.AgreementId == Guid.Empty || command.ProposalId == Guid.Empty)
            throw new IntercompanyAgreementValidationException("Command, agreement and proposal identities are required.");
        var hash = IntercompanyAgreementRules.CommandHash(command);
        var probe = await store.GetAsync(context.TenantId, command.AgreementId, ct).ConfigureAwait(false);
        var proposed = action is "Create" or "CreateProposal" or "SaveDraft" ? command.Draft
            : probe?.Proposals.SingleOrDefault(item => item.ProposalId == command.ProposalId)?.Draft;
        var parties = proposed is null ? Array.Empty<Guid>() : new[] { proposed.PartyOneId, proposed.PartyTwoId }.Distinct().Order().ToArray();
        // Keep graph -> sorted lifecycle -> legal aggregate order. Eligibility is evaluated only
        // inside the CAS callback; an exact retained command retry remains valid after later status changes.
        await using var graph = structureStore is PostgresFundStructureStore postgres
            ? await postgres.AcquireConsolidationAuthorityLeaseAsync(ct).ConfigureAwait(false) : null;
        if (store is PostgresIntercompanyAgreementStore && lifecycleStore is not null && graph is not ILegalEntityReadAuthorityLease)
            throw new IntercompanyAgreementValidationException("Native agreement lifecycle validation requires retained legal-entity graph authority.");
        await using var lifecycle = lifecycleStore is not null && parties.Length > 0
            ? await lifecycleStore.AcquireReadLeaseAsync(context.TenantId, parties, ct).ConfigureAwait(false) : null;
        async Task<IntercompanyAgreementCommandResultDto> Apply(IIntercompanyAgreementStoreTransaction tx, CancellationToken token)
        {
            var current = tx.State;
            if (current is not null)
            {
                var retained = current.Receipts.SingleOrDefault(r => r.CommandId == command.CommandId);
                if (retained is not null)
                {
                    await RequireAccessAsync(context, DisclosedParties(retained.Detail), token).ConfigureAwait(false);
                    if (!IntercompanyAgreementRules.SameActor(retained.Actor, context.Actor) || retained.Action != action || retained.PayloadHash != hash)
                        throw new IntercompanyAgreementConflictException("Command identity was already used for different content, action or actor.");
                    return new IntercompanyAgreementCommandResultDto(retained.Detail, retained, Replayed: true);
                }
                await RequireAccessAsync(context, DisclosedParties(current), token).ConfigureAwait(false);
            }
            else if (action != "Create")
                throw new IntercompanyAgreementNotFoundException();
            if (command.ExpectedAggregateVersion != (current?.AggregateVersion ?? 0))
                throw new IntercompanyAgreementConflictException("Agreement changed; compare the current version and submit again.");
            if (action == "Create" && current is not null)
                throw new IntercompanyAgreementConflictException("Agreement already exists.");
            if (action is "Submit" or "Approve")
            {
                var pending = current?.Proposals.SingleOrDefault(item => item.ProposalId == command.ProposalId)?.Draft;
                if (proposed is null || pending is null || pending.PartyOneId != proposed.PartyOneId || pending.PartyTwoId != proposed.PartyTwoId)
                    throw new IntercompanyAgreementConflictException("The pending agreement parties changed after lifecycle authority was acquired.");
            }
            var state = current ?? new IntercompanyAgreementStoreState(command.AgreementId, 0, [], [], [], []);
            var now = _clock.GetUtcNow();
            var migrationWarnings = new List<string>();
            var updated = await ApplyAsync(tx, context, state, command, action, now, migrationWarnings, token).ConfigureAwait(false);
            var version = checked(state.AggregateVersion + 1);
            updated = updated with
            {
                AggregateVersion = version,
                History = [.. state.History, new IntercompanyAgreementAuditDto(Guid.NewGuid(), command.ProposalId, action,
                    context.Actor, now, version, command.Rationale, updated.Proposals.Single(p => p.ProposalId == command.ProposalId).Draft)
                    { MigrationWarnings = migrationWarnings.Count == 0 ? null : migrationWarnings.Distinct().ToArray() }]
            };
            var detail = Project(updated, context, Today(), null);
            var receipt = new IntercompanyAgreementCommandReceiptDto(command.CommandId, action, hash, context.Actor, now, version, detail);
            updated = updated with { Receipts = [.. state.Receipts, receipt] };
            tx.Save(updated);
            return new IntercompanyAgreementCommandResultDto(detail, receipt);
        }
        return graph is ILegalEntityReadAuthorityLease retainedGraph
            ? await store.ExecuteAsync(context.TenantId, command.AgreementId, retainedGraph.StructureAuthorityProof, Apply, ct).ConfigureAwait(false)
            : await store.ExecuteAsync(context.TenantId, command.AgreementId, Apply, ct).ConfigureAwait(false);
    }

    private async Task<IntercompanyAgreementStoreState> ApplyAsync(IIntercompanyAgreementStoreTransaction tx,
        IntercompanyAgreementCallContext context, IntercompanyAgreementStoreState state,
        IntercompanyAgreementCommandDto command, string action, DateTimeOffset now, List<string> migrationWarnings, CancellationToken ct)
    {
        var initial = state.Revisions.FirstOrDefault(r => r.Action == IntercompanyAgreementActionDto.Activate);
        if (action is "Create" or "CreateProposal")
        {
            if (state.Proposals.Any(p => p.State is IntercompanyAgreementProposalStateDto.Draft or IntercompanyAgreementProposalStateDto.Submitted))
                throw new IntercompanyAgreementConflictException("Only one open proposal is permitted.");
            if (state.Proposals.Any(p => p.ProposalId == command.ProposalId))
                throw new IntercompanyAgreementConflictException("Proposal identity already exists.");
            var draft = IntercompanyAgreementRules.Normalize(command.Draft ?? throw new IntercompanyAgreementValidationException("Draft is required."));
            if ((initial is null) != (draft.Action == IntercompanyAgreementActionDto.Activate))
                throw new IntercompanyAgreementValidationException("Unapproved agreements require activation; approved agreements accept amendment or termination proposals.");
            if (initial is not null)
                IntercompanyAgreementRules.ValidateImmutableParties(initial.Terms, draft);
            IntercompanyAgreementRules.ValidateShape(draft);
            await RequirePartiesAsync(tx, context, draft, ct).ConfigureAwait(false);
            var proposal = new IntercompanyAgreementProposalDto(command.ProposalId, draft.Action, IntercompanyAgreementProposalStateDto.Draft,
                draft, IntercompanyAgreementTimeline.SemanticBase(state.Revisions, draft), [context.Actor], null, null, null, now, now, command.Rationale);
            return state with { Proposals = [.. state.Proposals, proposal] };
        }
        var existing = state.Proposals.SingleOrDefault(p => p.ProposalId == command.ProposalId)
            ?? throw new IntercompanyAgreementNotFoundException();
        var proposalUpdate = existing;
        if (action == "SaveDraft")
        {
            RequireState(existing, IntercompanyAgreementProposalStateDto.Draft);
            var draft = IntercompanyAgreementRules.Normalize(command.Draft ?? throw new IntercompanyAgreementValidationException("Draft is required."));
            if (draft.Action != existing.Action)
                throw new IntercompanyAgreementValidationException("Proposal action cannot change.");
            if (initial is not null)
                IntercompanyAgreementRules.ValidateImmutableParties(initial.Terms, draft);
            IntercompanyAgreementRules.ValidateShape(draft);
            await RequirePartiesAsync(tx, context, draft, ct).ConfigureAwait(false);
            proposalUpdate = existing with
            {
                Draft = draft,
                BaseAgreementVersion = IntercompanyAgreementTimeline.SemanticBase(state.Revisions, draft),
                Authors = existing.Authors.Append(context.Actor).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                UpdatedAtUtc = now,
                Rationale = command.Rationale
            };
        }
        else if (action == "Submit")
        {
            RequireState(existing, IntercompanyAgreementProposalStateDto.Draft);
            var canonical = await ValidateReviewAsync(tx, context, state, existing, command.Rationale ?? existing.Rationale, migrationWarnings, ct).ConfigureAwait(false);
            proposalUpdate = existing with
            {
                Draft = canonical,
                State = IntercompanyAgreementProposalStateDto.Submitted,
                Submitter = context.Actor,
                SubmittedAtUtc = now,
                UpdatedAtUtc = now,
                Rationale = command.Rationale ?? existing.Rationale
            };
            proposalUpdate = proposalUpdate with { SubmittedHash = ReviewHash(state, proposalUpdate) };
        }
        else if (action == "Approve")
        {
            RequireState(existing, IntercompanyAgreementProposalStateDto.Submitted);
            RequireIndependent(existing, context.Actor);
            if (string.IsNullOrWhiteSpace(command.Rationale))
                throw new IntercompanyAgreementValidationException("Approval rationale is required.");
            if (command.SubmittedHash != existing.SubmittedHash || ReviewHash(state, existing) != existing.SubmittedHash)
                throw new IntercompanyAgreementConflictException("Approval must bind the exact submitted hash.");
            var canonical = await ValidateReviewAsync(tx, context, state, existing, existing.Rationale, migrationWarnings, ct).ConfigureAwait(false);
            if (!IntercompanyAgreementRules.SameEvidence(canonical.Evidence ?? [], existing.Draft.Evidence ?? []))
                throw new IntercompanyAgreementConflictException("Evidence changed after submission; replace the proposal and obtain fresh review.");
            var revision = new IntercompanyAgreementRevisionDto(state.AgreementId, state.Revisions.Count + 1,
                state.Revisions.Count == 0 ? null : state.Revisions.Max(r => r.AgreementVersion), existing.BaseAgreementVersion,
                existing.Action, existing.Draft, existing.ProposalId, existing.SubmittedHash!, existing.Authors,
                existing.Submitter!, context.Actor, now, command.Rationale.Trim());
            state = state with { Revisions = [.. state.Revisions, revision] };
            proposalUpdate = existing with { State = IntercompanyAgreementProposalStateDto.Approved, UpdatedAtUtc = now };
        }
        else if (action == "Reject")
        {
            RequireState(existing, IntercompanyAgreementProposalStateDto.Submitted);
            RequireIndependent(existing, context.Actor);
            if (string.IsNullOrWhiteSpace(command.Rationale))
                throw new IntercompanyAgreementValidationException("Rejection rationale is required.");
            proposalUpdate = existing with { State = IntercompanyAgreementProposalStateDto.Rejected, UpdatedAtUtc = now };
        }
        else if (action == "Withdraw")
        {
            if (existing.State is not (IntercompanyAgreementProposalStateDto.Draft or IntercompanyAgreementProposalStateDto.Submitted))
                throw new IntercompanyAgreementConflictException("Only an open proposal can be withdrawn.");
            if (!existing.Authors.Any(a => IntercompanyAgreementRules.SameActor(a, context.Actor))
                && !IntercompanyAgreementRules.SameActor(existing.Submitter, context.Actor))
                throw new IntercompanyAgreementForbiddenException("An authorized proposal author or submitter must withdraw it.");
            if (string.IsNullOrWhiteSpace(command.Rationale))
                throw new IntercompanyAgreementValidationException("Withdrawal rationale is required.");
            proposalUpdate = existing with { State = IntercompanyAgreementProposalStateDto.Withdrawn, UpdatedAtUtc = now };
        }
        else
            throw new IntercompanyAgreementValidationException("Unknown agreement command.");
        return state with { Proposals = state.Proposals.Select(p => p.ProposalId == existing.ProposalId ? proposalUpdate : p).ToArray() };
    }

    private async Task<IntercompanyAgreementDraftDto> ValidateReviewAsync(IIntercompanyAgreementStoreTransaction tx,
        IntercompanyAgreementCallContext context, IntercompanyAgreementStoreState state, IntercompanyAgreementProposalDto proposal,
        string? rationale, List<string> migrationWarnings, CancellationToken ct)
    {
        var draft = proposal.Draft;
        IntercompanyAgreementRules.ValidateComplete(draft);
        var (first, second) = await RequirePartiesAsync(tx, context, draft, ct).ConfigureAwait(false);
        if (draft.Action != IntercompanyAgreementActionDto.Terminate)
            foreach (var entity in new[] { first, second })
                await ValidateNewCommitmentLifecycleAsync(context, entity, draft.EffectiveFrom, migrationWarnings, ct).ConfigureAwait(false);
        if (proposal.BaseAgreementVersion != IntercompanyAgreementTimeline.SemanticBase(state.Revisions, draft))
            throw new IntercompanyAgreementConflictException("The effective-date base revision changed; replace this proposal.");
        IntercompanyAgreementRules.ValidateExistence(draft, state.Revisions, first, second);
        var date = draft.Action == IntercompanyAgreementActionDto.Terminate ? draft.TerminationEffectiveDate!.Value : draft.EffectiveFrom;
        if (draft.Action != IntercompanyAgreementActionDto.Activate && date < Today() && string.IsNullOrWhiteSpace(rationale))
            throw new IntercompanyAgreementValidationException("Retrospective changes require a rationale.");
        var retained = await evidence.ResolveAsync(context, state.AgreementId, proposal.ProposalId, draft.Evidence ?? [], ct).ConfigureAwait(false);
        if (retained.Count == 0 || retained.Any(e => string.IsNullOrWhiteSpace(e.Reference) || string.IsNullOrWhiteSpace(e.ArtifactId) || string.IsNullOrWhiteSpace(e.ContentHash)))
            throw new IntercompanyAgreementValidationException("At least one resolved retained agreement artifact is required.");
        return draft with { Evidence = retained.OrderBy(e => e.Reference, StringComparer.Ordinal).ToArray() };
    }

    private async Task ValidateNewCommitmentLifecycleAsync(IntercompanyAgreementCallContext context,
        LegalEntitySummaryDto entity, DateOnly effectiveFrom, List<string> migrationWarnings, CancellationToken ct)
    {
        var revision = lifecycleStore is null ? null
            : await lifecycleStore.GetLatestAsync(context.TenantId, entity.EntityId, ct).ConfigureAwait(false);
        if (revision is null)
        {
            // Native registry migration remains available for retained active entity snapshots.
            // New governed accounting profiles and enforced books still require a real baseline.
            if (!entity.IsActive || entity.LifecycleStatus is LegalEntityLifecycleStatusDto.Draft or LegalEntityLifecycleStatusDto.Forming
                or LegalEntityLifecycleStatusDto.WindingDown or LegalEntityLifecycleStatusDto.Dissolved or LegalEntityLifecycleStatusDto.Merged)
                throw new IntercompanyAgreementValidationException("The retained entity status does not permit a new legal commitment.");
            migrationWarnings.Add($"Entity {entity.EntityId:D} has no retained effective lifecycle baseline; native legal authoring used its active legacy snapshot. Governed accounting activation still requires migration.");
            return;
        }
        foreach (var date in new[] { new DateTimeOffset(effectiveFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), _clock.GetUtcNow() })
        {
            var eligibility = EntityLifecyclePolicy.Evaluate(revision, date, EntityLifecycleOperationDto.NewAgreement, false);
            if (!eligibility.Allowed)
                throw new IntercompanyAgreementValidationException(eligibility.Message);
        }
    }

    private async Task<(LegalEntitySummaryDto First, LegalEntitySummaryDto Second)> RequirePartiesAsync(
        IIntercompanyAgreementStoreTransaction tx, IntercompanyAgreementCallContext context, IntercompanyAgreementDraftDto draft, CancellationToken ct)
    {
        await RequireAccessAsync(context, draft.PartyOneId, draft.PartyTwoId, ct).ConfigureAwait(false);
        var first = await tx.GetLegalEntityAsync(draft.PartyOneId, ct).ConfigureAwait(false);
        var second = await tx.GetLegalEntityAsync(draft.PartyTwoId, ct).ConfigureAwait(false);
        if (first is null || second is null)
            throw new IntercompanyAgreementNotFoundException();
        return (first, second);
    }
    private async Task RequireAccessAsync(IntercompanyAgreementCallContext context, Guid first, Guid second, CancellationToken ct)
        => await RequireAccessAsync(context, [first, second], ct).ConfigureAwait(false);
    private async Task RequireAccessAsync(IntercompanyAgreementCallContext context, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (!await access.CanAccessEntitiesAsync(context, ids, ct).ConfigureAwait(false))
            throw new IntercompanyAgreementNotFoundException();
    }
    private static void RequireState(IntercompanyAgreementProposalDto proposal, IntercompanyAgreementProposalStateDto expected)
    {
        if (proposal.State != expected)
            throw new IntercompanyAgreementConflictException($"Proposal must be {expected}.");
    }
    private static void RequireIndependent(IntercompanyAgreementProposalDto proposal, string actor)
    {
        if (IntercompanyAgreementRules.SameActor(proposal.Submitter, actor) || proposal.Authors.Any(a => IntercompanyAgreementRules.SameActor(a, actor)))
            throw new IntercompanyAgreementForbiddenException("Reviewer must differ from the submitter and every proposal author.");
    }
    private static string ReviewHash(IntercompanyAgreementStoreState state, IntercompanyAgreementProposalDto proposal) =>
        IntercompanyAgreementRules.ReviewedHash(new(proposal.Draft, proposal.BaseAgreementVersion,
            state.Revisions.Count == 0 ? null : state.Revisions.Max(r => r.AgreementVersion), proposal.Rationale)
        { AgreementId = state.AgreementId, ProposalId = proposal.ProposalId });
    private static void ValidateContext(IntercompanyAgreementCallContext context)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId) || string.IsNullOrWhiteSpace(context.Actor)
            || context.TenantId.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            throw new IntercompanyAgreementForbiddenException("An authenticated actor and explicit tenant scope are required.");
    }
    private DateOnly Today() => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
}
