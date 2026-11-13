using Meridian.Contracts.FundStructure;
using Meridian.Storage.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>Bridges accounting configuration to the single native legal registry without another legal workflow.</summary>
public sealed class IntercompanyAccountingArrangementLegalAuthority(IIntercompanyAgreementStore store)
    : IIntercompanyAccountingArrangementLegalAuthority
{
    public async Task<IntercompanyAgreementRevisionDto> GetRevisionAsync(string tenantId,
        IntercompanyCanonicalAgreementReferenceDto reference, CancellationToken ct = default)
        => Exact(await RequiredAsync(tenantId, reference.AgreementId, ct).ConfigureAwait(false), reference);
    public async Task<IntercompanyAgreementRevisionDto> GetEffectiveRevisionAsync(string tenantId,
        IntercompanyCanonicalAgreementReferenceDto reference, DateOnly economicDate, CancellationToken ct = default)
        => Effective(await RequiredAsync(tenantId, reference.AgreementId, ct).ConfigureAwait(false), reference, economicDate);
    public async Task<IReadOnlyList<IntercompanyAgreementRevisionDto>> ListEffectiveRevisionsAsync(string tenantId,
        Guid entityId, DateOnly economicDate, CancellationToken ct = default)
    {
        var results = new List<IntercompanyAgreementRevisionDto>();
        foreach (var state in await store.ListAsync(tenantId, ct).ConfigureAwait(false))
        {
            var selected = IntercompanyAgreementTimeline.Resolve(state.Revisions, economicDate);
            if (selected.Status == IntercompanyAgreementStatusDto.Active && selected.Revision is { } revision
                && (revision.Terms.PartyOneId == entityId || revision.Terms.PartyTwoId == entityId))
                results.Add(revision);
        }
        return results.OrderBy(item => item.Terms.Code, StringComparer.Ordinal).ThenBy(item => item.AgreementId).ToArray();
    }
    public async Task<IIntercompanyAccountingArrangementLegalLease> AcquireLeaseAsync(string tenantId,
        IReadOnlyList<IntercompanyCanonicalAgreementReferenceDto> references,
        LegalEntityReadAuthorityProof? structureAuthority = null, CancellationToken ct = default)
    {
        if (references.Count == 0)
            throw new InvalidOperationException("Canonical agreement references are required.");
        // Without an existing proof this must be the caller's first authority acquisition:
        // the native store then owns graph plus legal authority. Mapping workflows that
        // already hold graph/lifecycle authority pass their live proof instead of relocking.
        var ids = references.Select(item => item.AgreementId).Distinct().Order().ToArray();
        var lease = structureAuthority is null
            ? await store.AcquirePostingLeaseAsync(tenantId, ids, ct).ConfigureAwait(false)
            : await store.AcquirePostingLeaseAsync(tenantId, ids, structureAuthority, ct).ConfigureAwait(false);
        try
        {
            foreach (var reference in references)
                _ = Exact(Required(lease.Agreements, reference.AgreementId), reference);
            return new LegalLease(lease);
        }
        catch { await lease.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private async Task<IntercompanyAgreementStoreState> RequiredAsync(string tenantId, Guid id, CancellationToken ct)
        => await store.GetAsync(tenantId, id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The canonical legal agreement is unavailable in this tenant.");
    private static IntercompanyAgreementStoreState Required(IReadOnlyList<IntercompanyAgreementStoreState> agreements, Guid id)
        => agreements.SingleOrDefault(item => item.AgreementId == id)
            ?? throw new InvalidOperationException("The canonical legal agreement is unavailable under its retained authority lease.");
    private static IntercompanyAgreementRevisionDto Exact(IntercompanyAgreementStoreState state, IntercompanyCanonicalAgreementReferenceDto reference)
    {
        var revision = state.Revisions.SingleOrDefault(item => item.AgreementVersion == reference.AgreementVersion);
        if (reference.AgreementId != state.AgreementId || reference.AgreementVersion < 1 || revision is null
            || revision.Action == IntercompanyAgreementActionDto.Terminate || revision.PayloadHash != reference.PayloadHash)
            throw new InvalidOperationException("Accounting configuration must pin the exact approved canonical legal agreement version and payload hash.");
        return revision;
    }
    private static IntercompanyAgreementRevisionDto Effective(IntercompanyAgreementStoreState state,
        IntercompanyCanonicalAgreementReferenceDto reference, DateOnly date)
    {
        var exact = Exact(state, reference);
        var current = IntercompanyAgreementTimeline.Resolve(state.Revisions, date);
        if (current.Status != IntercompanyAgreementStatusDto.Active || current.Revision?.AgreementVersion != exact.AgreementVersion)
            throw new InvalidOperationException("The pinned canonical legal agreement version is not effective on the economic date.");
        return exact;
    }
    private sealed class LegalLease(IIntercompanyAgreementPostingLease lease) : IIntercompanyAccountingArrangementLegalLease
    {
        private int _disposed;
        public IntercompanyAgreementRevisionDto ResolveExact(IntercompanyCanonicalAgreementReferenceDto reference)
        { ThrowIfDisposed(); return Exact(Required(lease.Agreements, reference.AgreementId), reference); }
        public IntercompanyAgreementRevisionDto ResolveEffective(IntercompanyCanonicalAgreementReferenceDto reference, DateOnly economicDate)
        { ThrowIfDisposed(); return Effective(Required(lease.Agreements, reference.AgreementId), reference, economicDate); }
        public void ValidateWindow(IntercompanyCanonicalAgreementReferenceDto reference, DateTimeOffset from, DateTimeOffset? to)
        {
            ThrowIfDisposed();
            var state = Required(lease.Agreements, reference.AgreementId);
            var revision = Effective(state, reference, DateOnly.FromDateTime(from.UtcDateTime));
            var ends = state.Revisions.Where(item => item.Action == IntercompanyAgreementActionDto.Terminate)
                .Select(item => item.Terms.TerminationEffectiveDate)
                .Concat(state.Revisions.Where(item => item.Action != IntercompanyAgreementActionDto.Terminate
                    && item.Terms.EffectiveFrom > revision.Terms.EffectiveFrom).Select(item => (DateOnly?)item.Terms.EffectiveFrom))
                .Append(revision.Terms.EffectiveTo).Where(item => item.HasValue).Select(item => item!.Value).ToArray();
            if (ends.Length > 0)
            {
                var cutoff = new DateTimeOffset(ends.Min().ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                if (to is null || to > cutoff)
                    throw new InvalidOperationException("Accounting mapping validity must end by the canonical legal revision's retained cutoff.");
            }
        }
        private void ThrowIfDisposed() { if (_disposed != 0) throw new ObjectDisposedException(nameof(LegalLease)); }
        public async ValueTask DisposeAsync()
        { if (Interlocked.Exchange(ref _disposed, 1) == 0) await lease.DisposeAsync().ConfigureAwait(false); }
    }
}
