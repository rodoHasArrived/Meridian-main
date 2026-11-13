using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Consolidation;

/// <summary>Reviews a reporting path change; rebuilds the new target without importing old overlays.</summary>
public sealed class ConsolidationTransitionService(ConsolidationService consolidation, ILedgerJournalStore journals,
    IConsolidationTransitionStore transitions, IAccountingPolicyService policies, IIntercompanyAccountBindingStore bindings,
    IConsolidationFxAuthority fx, ILegacyIntercompanyAttributionStore legacy, IFundProfileTenancyRegistry owners,
    PostgresFundStructureStore? structure = null, TimeProvider? timeProvider = null,
    IIntercompanyAccountingArrangementStore? arrangements = null,
    IIntercompanyAccountingArrangementLegalAuthority? legalAuthority = null)
{
    public async Task<ConsolidationTransitionPreviewDto> PreviewAsync(ConsolidationTransitionRequestDto request,
        string tenantId, string? companyId, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        tenantId = tenantId.Trim().ToLowerInvariant();
        actor = actor.Trim();
        ConsolidationTransitionRules.ValidateRequest(request);
        await using var authority = await AcquireAsync(request, ct).ConfigureAwait(false);
        await using var routing = await transitions.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
        var preview = await ComputeAsync(request, tenantId, companyId, actor, Now, ct).ConfigureAwait(false);
        return await transitions.RetainPreviewAsync(preview, ct).ConfigureAwait(false);
    }

    public async Task<ConsolidationTransitionReceiptDto> ActivateAsync(ApproveConsolidationTransitionRequestDto request,
        string tenantId, string? companyId, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        tenantId = tenantId.Trim().ToLowerInvariant();
        actor = actor.Trim();
        var preview = await transitions.GetPreviewAsync(request.PreviewId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained transition preview is unavailable.");
        await RequireBooksAsync(preview.Request, tenantId, companyId, ct).ConfigureAwait(false);
        ConsolidationTransitionRules.ValidatePreview(preview);
        if (!Sha256Digest.FixedEquals(request.PreviewFingerprint, preview.Fingerprint))
            throw new InvalidOperationException("The approval fingerprint differs from the exact retained transition preview.");
        var existing = await transitions.GetAsync(preview.Request.TransitionId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.PreviewId != preview.PreviewId || existing.PreviewFingerprint != request.PreviewFingerprint || existing.ApprovedBy != actor)
                throw new InvalidOperationException("The transition identity already belongs to a different reviewed approval.");
            return existing;
        }
        if (!preview.CanApprove || string.Equals(preview.PreparedBy, actor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An unblocked retained preview requires an independent approving actor.");
        var receipt = new ConsolidationTransitionReceiptDto(preview.Request.TransitionId, preview.PreviewId,
            preview.RootVersion, preview.PreviousTransitionId, preview.Request, preview.TenantId, preview.CompanyId,
            preview.PreparedBy, actor, Now, preview.Fingerprint, preview.PreviousBookEvidenceDigest,
            preview.PreviousPostedJournalIds, preview.PreviousLatestPostedAsOf, preview.PreviousEntityIds, preview.NewEvidence, "");
        receipt = receipt with { ContentHash = ConsolidationTransitionRules.ReceiptHash(receipt) };
        return await transitions.AppendValidatedWithAuthorityAsync(receipt,
            token => AcquireAsync(preview.Request, token, preview.NewEvidence, tenantId), async token =>
        {
            var current = await ComputeAsync(preview.Request, tenantId, companyId, preview.PreparedBy, preview.PreparedAt, token).ConfigureAwait(false);
            if (!current.CanApprove || !Sha256Digest.FixedEquals(current.Fingerprint, preview.Fingerprint))
                throw new InvalidOperationException("Reporting membership, source books, old overlays, mappings, policy or FX changed. Prepare a new transition preview and renew review.");
        }, ct).ConfigureAwait(false);
    }

    private async Task<ConsolidationTransitionPreviewDto> ComputeAsync(ConsolidationTransitionRequestDto request,
        string tenantId, string? companyId, string preparedBy, DateTimeOffset preparedAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(preparedBy))
            throw new InvalidOperationException("A transition preparer is required.");
        var (oldBook, newBook) = await RequireBooksAsync(request, tenantId, companyId, ct).ConfigureAwait(false);
        var history = await transitions.ListAsync(request.OrganizationId, request.OwnershipRootId, ct).ConfigureAwait(false);
        var head = history.MaxBy(x => x.RootVersion);
        if (head is not null && (head.Request.NewEliminationBookId != oldBook.LedgerBookId || request.CutoverDate <= head.Request.CutoverDate))
            throw new InvalidOperationException("A reporting transition must continue the current root path at a later cutover.");
        if (history.Any(x => x.Request.NewEliminationBookId == newBook.LedgerBookId || x.Request.PreviousEliminationBookId == newBook.LedgerBookId))
            throw new InvalidOperationException("The new reporting book must not reuse an earlier path book.");
        var oldRows = await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: oldBook.LedgerBookId), ct).ConfigureAwait(false);
        if (oldRows.Count == 0)
            throw new InvalidOperationException("The predecessor must have retained posted elimination history.");
        var previousEvidence = new Dictionary<Guid, ConsolidationEvidenceDto>();
        foreach (var record in oldRows)
        {
            var json = record.Entry.Metadata.Tags?.GetValueOrDefault(ConsolidationService.EvidenceTag);
            var evidence = ConsolidationService.ReadEvidence(json);
            if (evidence?.Request is null || evidence.Request.OrganizationId != request.OrganizationId ||
                evidence.Request.OwnershipRootId != request.OwnershipRootId || evidence.Request.EliminationBookId != oldBook.LedgerBookId ||
                !Sha256Digest.FixedEquals(record.Entry.Metadata.Tags?.GetValueOrDefault(ConsolidationService.DigestTag),
                    json is null ? null : Sha256Digest.ComputeUtf8(json)))
                throw new InvalidOperationException("The predecessor book must contain only exact retained governed overlays for this root.");
            previousEvidence.Add(record.Entry.JournalEntryId, evidence);
        }
        var latest = oldRows.OrderByDescending(ConsolidationService.EffectiveDate).ThenByDescending(x => x.GlobalSequence).First();
        var latestDate = ConsolidationService.EffectiveDate(latest);
        if (latestDate >= request.CutoverDate)
            throw new InvalidOperationException("The cutover must be strictly later than every posted predecessor elimination.");
        if ((await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: newBook.LedgerBookId), ct).ConfigureAwait(false)).Count > 0)
            throw new InvalidOperationException("The new dedicated reporting book must be empty, including future-dated journals.");
        foreach (var other in await journals.ListLedgerBooksAsync(oldBook.FundProfileId, request.OwnershipRootId,
            FundStructureNodeKindDto.Fund, ct).ConfigureAwait(false))
        {
            if (other.LedgerBookId == oldBook.LedgerBookId || other.LedgerBookId == newBook.LedgerBookId ||
                history.Any(x => x.Request.PreviousEliminationBookId == other.LedgerBookId && request.CutoverDate >= x.Request.CutoverDate))
                continue;
            if ((await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: other.LedgerBookId), ct).ConfigureAwait(false))
                .Any(x => x.Entry.Metadata.Tags?.ContainsKey(ConsolidationService.EvidenceTag) == true))
                throw new InvalidOperationException("Multiple established current overlay books make the root path ambiguous.");
        }
        var previousMembers = previousEvidence[latest.Entry.JournalEntryId].EntityIds?.Order(StringComparer.Ordinal).ToArray()
            ?? throw new InvalidOperationException("The predecessor evidence lacks its exact authoritative membership.");
        var formerMembers = history.SelectMany(x => x.PreviousEntityIds).Concat(previousMembers).ToHashSet(StringComparer.Ordinal);
        var calculation = await consolidation.CalculateForTransitionAsync(new(request.OrganizationId, request.OwnershipRootId,
            newBook.LedgerBookId, request.NewPeriodId, request.CutoverDate), ct, formerMembers).ConfigureAwait(false);
        if (calculation.Evidence.PriorPostedJournalIds.Count != 0)
            throw new InvalidOperationException("A new reporting target cannot subtract predecessor or other book overlays.");
        var newMembers = calculation.Evidence.EntityIds ?? [];
        var blockers = calculation.Blockers.ToList();
        if (!string.Equals(calculation.Period.Status, "Open", StringComparison.OrdinalIgnoreCase))
            blockers.Add("The new book period must be open.");
        var preview = new ConsolidationTransitionPreviewDto(Guid.Empty, request, (head?.RootVersion ?? 0) + 1, head?.TransitionId,
            tenantId, Company(companyId), preparedBy, preparedAt, ConsolidationService.Hash(new
            { Book = oldBook, Records = oldRows.OrderBy(x => x.GlobalSequence).ToArray() }),
            oldRows.Select(x => x.Entry.JournalEntryId).Order().ToArray(), latestDate, previousMembers, calculation.Evidence,
            newMembers.Except(previousMembers, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            previousMembers.Except(newMembers, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), blockers, "");
        preview = preview with { Fingerprint = ConsolidationTransitionRules.PreviewFingerprint(preview) };
        return preview with { PreviewId = ConsolidationTransitionRules.PreviewId(preview) };
    }

    private async Task<(LedgerBookRecord Old, LedgerBookRecord New)> RequireBooksAsync(ConsolidationTransitionRequestDto request,
        string tenantId, string? companyId, CancellationToken ct)
    {
        var oldBook = await journals.GetLedgerBookAsync(request.PreviousEliminationBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The predecessor reporting book is unavailable.");
        var newBook = await journals.GetLedgerBookAsync(request.NewEliminationBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The new reporting book is unavailable.");
        if (oldBook.FundProfileId != newBook.FundProfileId || oldBook.FundStructureNodeId != request.OwnershipRootId ||
            newBook.FundStructureNodeId != request.OwnershipRootId || !ConsolidationService.IsDedicatedConsolidationBook(oldBook) ||
            !ConsolidationService.IsDedicatedConsolidationBook(newBook) || newBook.AccountingPolicyVersion == "w10-v1")
            throw new InvalidOperationException("The transition requires distinct dedicated Primary books for one fund/root and a governed v2-or-later target.");
        var owner = await owners.ResolveAsync(oldBook.FundProfileId, ct).ConfigureAwait(false);
        if (owner is null || !owner.IsHeldBy(tenantId) || !string.Equals(Company(owner.CompanyId), Company(companyId), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The reporting books are outside the authorized tenant and company.");
        return (oldBook, newBook);
    }

    private async Task<IAsyncDisposable> AcquireAsync(ConsolidationTransitionRequestDto request, CancellationToken ct,
        ConsolidationEvidenceDto? reviewedEvidence = null, string? tenantId = null)
    {
        var held = new List<IAsyncDisposable>();
        try
        {
            held.Add(await policies.AcquireConsolidationAuthorityLeaseAsync(ct).ConfigureAwait(false));
            IAsyncDisposable? graph = null;
            if (structure is not null)
            {
                graph = await structure.AcquireConsolidationAuthorityLeaseAsync(ct).ConfigureAwait(false);
                held.Add(graph);
            }
            var sources = reviewedEvidence?.Sources ?? [];
            var legalSources = sources.Where(source => source.IntercompanyAgreementId.HasValue
                || source.IntercompanyAgreementVersion.HasValue || source.IntercompanyAgreementPayloadHash is not null).ToArray();
            if (legalSources.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(tenantId) || legalSources.Any(source => !source.IntercompanyAgreementId.HasValue
                    || source.IntercompanyAgreementId.Value == Guid.Empty || source.IntercompanyAgreementVersion is not > 0
                    || string.IsNullOrWhiteSpace(source.IntercompanyAgreementPayloadHash)))
                    throw new InvalidOperationException("Reviewed reporting transitions require complete exact canonical legal pins and tenant authority.");
                var pins = legalSources.Select(source => new IntercompanyCanonicalAgreementReferenceDto(source.IntercompanyAgreementId!.Value,
                    source.IntercompanyAgreementVersion!.Value, source.IntercompanyAgreementPayloadHash!)).Distinct().ToArray();
                var legal = await (legalAuthority ?? throw new InvalidOperationException("Canonical reporting transition authority is unavailable."))
                    .AcquireLeaseAsync(tenantId, pins, (graph as ILegalEntityReadAuthorityLease)?.StructureAuthorityProof, ct).ConfigureAwait(false);
                held.Add(legal);
                foreach (var pin in pins)
                    _ = legal.ResolveExact(pin);
            }
            held.Add(await bindings.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false));
            if (arrangements is not null)
                held.Add(await arrangements.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false));
            else if (sources.Any(source => source.AccountingArrangementId is not null))
                throw new InvalidOperationException("Accounting arrangement reporting transition authority is unavailable.");
            // The source calculator selects whether FX is required; this lease also fences a
            // policy-version switch between preview and its final audit-locked revalidation.
            held.Add(await fx.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false));
            held.Add(await legacy.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false));
            return new Leases(held);
        }
        catch { await new Leases(held).DisposeAsync().ConfigureAwait(false); throw; }
    }
    private DateTimeOffset Now => (timeProvider ?? TimeProvider.System).GetUtcNow();
    private static string? Company(string? value) => string.Equals(value, "all", StringComparison.OrdinalIgnoreCase) ? null : value;
    private sealed class Leases(IReadOnlyList<IAsyncDisposable> leases) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }
}
