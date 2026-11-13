using Meridian.Contracts.FundStructure;
using Meridian.Storage.FundStructure;

namespace Meridian.Application.FundStructure;

public sealed partial class IntercompanyAgreementService
{
    public async Task<IntercompanyAgreementDetailDto?> GetAsync(IntercompanyAgreementCallContext context, Guid agreementId,
        DateOnly? asOf = null, DateTimeOffset? knownAtUtc = null, CancellationToken ct = default)
    {
        ValidateContext(context);
        var state = await store.GetAsync(context.TenantId, agreementId, ct).ConfigureAwait(false);
        if (state is null)
            return null;
        if (knownAtUtc.HasValue && !state.History.Any(h => h.RecordedAtUtc <= knownAtUtc.Value))
            return null;
        if (!await access.CanAccessEntitiesAsync(context, DisclosedParties(state), ct).ConfigureAwait(false))
            return null;
        return Project(state, context, asOf ?? Today(), knownAtUtc);
    }

    public async Task<IntercompanyAgreementListDto> ListAsync(IntercompanyAgreementCallContext context,
        IntercompanyAgreementQueryDto query, CancellationToken ct = default)
    {
        ValidateContext(context);
        if (query.PageSize is < 1 or > 100)
            throw new IntercompanyAgreementValidationException("PageSize must be between 1 and 100.");
        Guid? after = null;
        if (query.Cursor is not null)
        {
            if (!Guid.TryParseExact(query.Cursor, "N", out var cursor))
                throw new IntercompanyAgreementValidationException("Cursor is invalid.");
            after = cursor;
        }
        var results = new List<IntercompanyAgreementDetailDto>();
        foreach (var state in (await store.ListAsync(context.TenantId, ct).ConfigureAwait(false)).OrderBy(s => s.AgreementId))
        {
            if (query.KnownAtUtc.HasValue && !state.History.Any(h => h.RecordedAtUtc <= query.KnownAtUtc.Value))
                continue;
            if (after.HasValue && state.AgreementId.CompareTo(after.Value) <= 0)
                continue;
            var identity = Identity(state);
            if (query.EntityId.HasValue && identity.PartyOneId != query.EntityId && identity.PartyTwoId != query.EntityId)
                continue;
            if (!await access.CanAccessEntitiesAsync(context, DisclosedParties(state), ct).ConfigureAwait(false))
                continue;
            var detail = Project(state, context, query.AsOf ?? Today(), query.KnownAtUtc);
            if (query.AgreementType.HasValue && detail.AgreementType != query.AgreementType)
                continue;
            if (query.Status.HasValue && detail.EffectiveStatus != query.Status)
                continue;
            results.Add(detail);
            if (results.Count > query.PageSize)
                break;
        }
        var hasMore = results.Count > query.PageSize;
        if (hasMore)
            results.RemoveAt(results.Count - 1);
        return new(results, hasMore ? results[^1].AgreementId.ToString("N") : null);
    }

    private static IntercompanyAgreementDraftDto Identity(IntercompanyAgreementStoreState state) =>
        state.Revisions.FirstOrDefault()?.Terms ?? state.Proposals[^1].Draft;

    private static IReadOnlyList<Guid> DisclosedParties(IntercompanyAgreementStoreState state) =>
        PartyIds(state.Proposals.Select(p => p.Draft).Concat(state.Revisions.Select(r => r.Terms))
            .Concat(state.History.Where(h => h.Snapshot is not null).Select(h => h.Snapshot!)));

    private static IReadOnlyList<Guid> DisclosedParties(IntercompanyAgreementDetailDto detail) =>
        PartyIds(detail.Proposals.Select(p => p.Draft).Concat(detail.Revisions.Select(r => r.Terms))
            .Concat(detail.History.Where(h => h.Snapshot is not null).Select(h => h.Snapshot!)))
        .Concat([detail.PartyOneId, detail.PartyTwoId]).Distinct().ToArray();

    private static IReadOnlyList<Guid> PartyIds(IEnumerable<IntercompanyAgreementDraftDto> drafts) =>
        drafts.SelectMany(d => new[] { d.PartyOneId, d.PartyTwoId }).Distinct().ToArray();

    private static IntercompanyAgreementDetailDto Project(IntercompanyAgreementStoreState state, IntercompanyAgreementCallContext context,
        DateOnly asOf, DateTimeOffset? knownAtUtc)
    {
        var revisions = state.Revisions.Where(r => !knownAtUtc.HasValue || r.ApprovedAtUtc <= knownAtUtc.Value).ToArray();
        var history = state.History.Where(h => !knownAtUtc.HasValue || h.RecordedAtUtc <= knownAtUtc.Value).ToArray();
        var identity = revisions.LastOrDefault()?.Terms ?? history.LastOrDefault(h => h.Snapshot is not null)?.Snapshot ?? Identity(state);
        var timeline = IntercompanyAgreementTimeline.Resolve(revisions, asOf);
        var open = state.Proposals.LastOrDefault(p => p.State is IntercompanyAgreementProposalStateDto.Draft or IntercompanyAgreementProposalStateDto.Submitted);
        var independent = open is not null && !IntercompanyAgreementRules.SameActor(open.Submitter, context.Actor)
            && !open.Authors.Any(a => IntercompanyAgreementRules.SameActor(a, context.Actor));
        var historical = knownAtUtc.HasValue;
        var submitted = open?.State == IntercompanyAgreementProposalStateDto.Submitted;
        var draft = open?.State == IntercompanyAgreementProposalStateDto.Draft;
        var canChange = !historical && open is null && revisions.Length > 0;
        var terminated = state.Revisions.Any(r => r.Action == IntercompanyAgreementActionDto.Terminate);
        var caps = new IntercompanyAgreementCapabilitiesDto(!historical && draft, !historical && draft,
            !historical && submitted && independent, !historical && submitted && independent,
            !historical && open is not null && (IntercompanyAgreementRules.SameActor(open.Submitter, context.Actor)
                || open.Authors.Any(a => IntercompanyAgreementRules.SameActor(a, context.Actor))),
            canChange, canChange && !terminated,
            historical ? "Historical knowledge is read-only." : submitted && !independent ? "Independent reviewer required." : null)
        { CanCreateProposal = !historical && open is null };
        var aggregateVersion = historical ? history.Max(h => h.AggregateVersion) : state.AggregateVersion;
        return new(state.AgreementId, aggregateVersion, identity.Code, identity.Name, identity.PartyOneId, identity.PartyOneRole,
            identity.PartyTwoId, identity.PartyTwoRole, identity.AgreementType, identity.Currency, asOf, timeline.Status,
            revisions.Length == 0 ? null : revisions.Max(r => r.AgreementVersion), timeline.Revision,
            historical ? [] : state.Proposals, revisions, history, caps);
    }
}
