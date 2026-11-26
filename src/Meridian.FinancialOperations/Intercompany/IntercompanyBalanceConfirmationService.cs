using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>
/// Reciprocal intercompany balance confirmation. Each side's balance is summed from its own book's posted balance
/// schedule for the approved accounting profile; nothing is entered by an operator. Each side is confirmed or
/// disputed only by a caller whose scope holds that side's book. The two books may belong to different companies of
/// the tenant: the approved, tenant-scoped accounting profile that maps both books is the authority linking them, the
/// confirmation is visible to both companies, and each company acts only on its own side (DEC-IC-CONFIRM-003).
/// Advisory unless the book adopts a reviewed confirmation close policy (ADR-032), which
/// <see cref="IntercompanyConfirmationCloseGate"/> evaluates.
/// </summary>
public sealed class IntercompanyBalanceConfirmationService(IIntercompanyBalanceConfirmationStore store,
    IIntercompanyBalanceScheduleSource schedules, IntercompanyAccountingArrangementService profiles,
    IIntercompanyAccountingArrangementStore profileStore, ILedgerJournalStore journals, IFundProfileTenancyRegistry owners,
    TimeProvider? clock = null) : IIntercompanyBalanceConfirmationSummarySource
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<IntercompanyBalanceConfirmationDto?> GetAsync(string tenant, string? company, Guid confirmationId, CancellationToken ct = default)
        => GetScopedAsync(tenant, company, confirmationId, ct);

    /// <summary>Pins both sides' current posted balances for the profile at the date.</summary>
    public async Task<IntercompanyBalanceConfirmationDto> ProposeAsync(string tenant, string? company, string actor,
        ProposeIntercompanyBalanceConfirmationRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (request.RequestId == Guid.Empty)
            throw new ArgumentException("A proposal request identity is required.");
        var current = await store.FindAsync(request.AccountingArrangementId, request.AsOf, company, ct).ConfigureAwait(false);
        if (current?.Events.FirstOrDefault(item => item.RequestId == request.RequestId) is { } replay)
            return replay.Action == IntercompanyBalanceConfirmationActionDto.Propose ? current
                : throw new InvalidOperationException("This request identity already recorded a different confirmation step.");
        var pair = await ResolvePairAsync(tenant, company, request.AccountingArrangementId, request.AsOf, ct).ConfigureAwait(false);
        var next = IntercompanyBalanceConfirmationRules.Propose(current, Guid.NewGuid(), request.RequestId, tenant, company,
            request.AccountingArrangementId, pair.AgreementId, request.AsOf, pair.One, pair.Two, actor, _clock.GetUtcNow(),
            pair.CrossCompany ? pair.OneCompany : null, pair.CrossCompany ? pair.TwoCompany : null);
        return await store.AppendAsync(next, current?.Version ?? 0, ct).ConfigureAwait(false);
    }

    /// <summary>Confirms, disputes or resolves the exact retained version the caller saw.</summary>
    public async Task<IntercompanyBalanceConfirmationDto> ActAsync(string tenant, string? company, string actor, Guid confirmationId,
        IntercompanyBalanceConfirmationActionRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (request.RequestId == Guid.Empty)
            throw new ArgumentException("An action request identity is required.");
        var current = await GetScopedAsync(tenant, company, confirmationId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The balance confirmation is unavailable in this scope.");
        if (current.Events.FirstOrDefault(item => item.RequestId == request.RequestId) is { } replay)
            return replay.Action == request.Action && replay.Party == IntercompanyBalanceConfirmationRules.RecordedParty(current, request)
                ? current : throw new InvalidOperationException("This request identity already recorded a different confirmation step.");
        // A side acts only through its own book. A same-company resolution needs authority over both books; across
        // companies each side records its own resolution through its own book.
        var bothBooks = request.Party is null
            || (request.Action == IntercompanyBalanceConfirmationActionDto.Resolve && !current.IsCrossCompany);
        var books = bothBooks
            ? new[] { current.PartyOne.LedgerBookId, current.PartyTwo.LedgerBookId }
            : [request.Party == IntercompanyBalanceConfirmationPartyDto.PartyOne ? current.PartyOne.LedgerBookId : current.PartyTwo.LedgerBookId];
        foreach (var book in books)
            await RequireBookAsync(tenant, company, book, ct).ConfigureAwait(false);
        var next = IntercompanyBalanceConfirmationRules.Apply(current, request, actor, _clock.GetUtcNow());
        return await store.AppendAsync(next, current.Version, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The confirmation position for every approved profile that maps the book at the date. Advisory findings only;
    /// a profile whose balances cannot be read reports <c>Unavailable</c> rather than being treated as confirmed.
    /// </summary>
    public async Task<IntercompanyBalanceConfirmationSummaryDto> SummaryAsync(string tenant, string? company, Guid ledgerBookId,
        DateOnly asOf, CancellationToken ct = default)
    {
        await RequireBookAsync(tenant, company, ledgerBookId, ct).ConfigureAwait(false);
        var confirmations = new List<IntercompanyBalanceConfirmationDto>();
        var findings = new List<IntercompanyBalanceConfirmationFindingDto>();
        foreach (var arrangement in (await profileStore.ListAsync(tenant, ct: ct).ConfigureAwait(false)).OrderBy(item => item.ArrangementId))
        {
            var profile = await EffectiveProfileAsync(tenant, arrangement.ArrangementId, asOf, ct).ConfigureAwait(false);
            if (profile is null || profile.PartyMappings.All(map => map.LedgerBookId != ledgerBookId))
                continue;
            var current = await store.FindAsync(arrangement.ArrangementId, asOf, company, ct).ConfigureAwait(false);
            if (current is not null)
                confirmations.Add(current);
            string? digest = null;
            try
            {
                var pair = await PairAsync(tenant, company, profile, asOf, ct).ConfigureAwait(false);
                digest = IntercompanyBalanceConfirmationRules.Digest(arrangement.ArrangementId, asOf, pair.One, pair.Two);
            }
            catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
            {
                findings.Add(new("Unavailable", $"Current posted balances cannot be read: {error.Message}", arrangement.ArrangementId, current?.ConfirmationId));
                continue;
            }
            if (IntercompanyBalanceConfirmationRules.Finding(arrangement.ArrangementId, current, digest) is { } finding)
                findings.Add(finding);
        }
        return new(ledgerBookId, asOf, confirmations, findings);
    }

    private async Task<IntercompanyBalanceConfirmationDto?> GetScopedAsync(string tenant, string? company, Guid confirmationId, CancellationToken ct)
    {
        var current = await store.GetAsync(confirmationId, company, ct).ConfigureAwait(false);
        return current is null || current.TenantId != tenant || !current.IsVisibleTo(company) ? null : current;
    }

    private async Task<Pair> ResolvePairAsync(string tenant, string? company, Guid arrangementId, DateOnly asOf, CancellationToken ct)
    {
        var profile = await EffectiveProfileAsync(tenant, arrangementId, asOf, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No approved accounting profile revision is in effect on {asOf:yyyy-MM-dd}.");
        return await PairAsync(tenant, company, profile, asOf, ct).ConfigureAwait(false);
    }

    private async Task<Pair> PairAsync(string tenant, string? company, ResolvedIntercompanyAccountingArrangementDto profile, DateOnly asOf, CancellationToken ct)
    {
        var (one, oneCompany) = await SideAsync(tenant, company, profile, profile.PartyOneEntityId, profile.PartyTwoEntityId, asOf, ct).ConfigureAwait(false);
        var (two, twoCompany) = await SideAsync(tenant, company, profile, profile.PartyTwoEntityId, profile.PartyOneEntityId, asOf, ct).ConfigureAwait(false);
        // The caller must hold at least one side; the approved profile is what authorizes reading the other.
        if (!SameCompany(oneCompany, company) && !SameCompany(twoCompany, company))
            throw new UnauthorizedAccessException("Neither ledger book of this agreement is in the current company.");
        return new(profile.CanonicalAgreementId, one, two, oneCompany, twoCompany);
    }

    // One side's posted due-to/due-from for this profile and counterparty, summed from its own book's schedule, read in
    // the scope of the company that owns that book. Only the totals, journal identities and schedule hash are retained.
    private async Task<(IntercompanyBalanceConfirmationSideDto Side, string? Company)> SideAsync(string tenant, string? company,
        ResolvedIntercompanyAccountingArrangementDto profile, Guid entity, Guid counterparty, DateOnly asOf, CancellationToken ct)
    {
        var book = profile.PartyMappings.SingleOrDefault(map => map.EntityId == entity)?.LedgerBookId
            ?? throw new InvalidOperationException("The approved profile does not map a ledger book for each party.");
        var (record, owningCompany) = await TenantBookAsync(tenant, company, book, ct).ConfigureAwait(false);
        var schedule = await schedules.GetAsync(tenant, owningCompany, null, book, asOf, ct).ConfigureAwait(false);
        if (!schedule.Available)
            throw new InvalidOperationException($"The balance schedule for book {book:D} is unavailable.");
        var rows = schedule.Rows.Where(row => row.LedgerBookId == book && row.AccountingArrangementId == profile.ArrangementId
            && row.EntityId == entity && row.CounterpartyEntityId == counterparty).ToArray();
        var currencies = rows.Select(row => row.TransactionCurrency).Distinct(StringComparer.Ordinal).ToArray();
        if (currencies.Length > 1 || (currencies.Length == 1 && currencies[0] != profile.Currency))
            throw new InvalidOperationException($"Book {book:D} holds this agreement's balance in a currency other than {profile.Currency}.");
        var functional = rows.Select(row => row.FunctionalCurrency).Distinct(StringComparer.Ordinal).SingleOrDefault() ?? record.BaseCurrency;
        return (new(entity, book, profile.Currency, rows.Sum(row => row.TransactionSignedBalance), functional,
            rows.Sum(row => row.FunctionalSignedBalance), schedule.ContentHash,
            rows.SelectMany(row => row.JournalEntryIds).Distinct().Order().ToArray()), owningCompany);
    }

    /// <summary>
    /// The approved profile revision in effect at the end of <paramref name="asOf"/>, if any; superseded or inactive
    /// revisions are skipped.
    /// </summary>
    public async Task<ResolvedIntercompanyAccountingArrangementDto?> EffectiveProfileAsync(string tenant, Guid arrangementId, DateOnly asOf, CancellationToken ct)
    {
        var history = await profiles.GetHistoryAsync(tenant, arrangementId, ct).ConfigureAwait(false);
        if (history is null)
            return null;
        ResolvedIntercompanyAccountingArrangementDto? effective = null;
        foreach (var revision in history.Events.Where(item => item.Action == IntercompanyAccountingArrangementActionDto.Approve)
            .Select(item => item.AffectedRevision.RevisionId).Distinct())
        {
            try
            {
                effective = await profiles.ResolveApprovedRevisionAsync(tenant, arrangementId, revision,
                    new DateTimeOffset(asOf.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException) { /* Not in effect on this date. */ }
        }
        return effective;
    }

    /// <summary>The book, when the caller's tenant and company hold it; otherwise refused.</summary>
    public async Task<LedgerBookRecord> RequireBookAsync(string tenant, string? company, Guid bookId, CancellationToken ct = default)
    {
        var book = await journals.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false);
        var owner = book is null ? null : await owners.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
        if (book is null || owner is null || !owner.IsHeldBy(tenant)
            || (owner.CompanyId is not null && !string.Equals(owner.CompanyId, company, StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("A ledger book of this agreement is outside the current tenant or company.");
        return book;
    }

    // A book of the caller's tenant and the company that owns it. A book with no owning company is read in the caller's.
    private async Task<(LedgerBookRecord Book, string? Company)> TenantBookAsync(string tenant, string? company, Guid bookId, CancellationToken ct)
    {
        var book = await journals.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false);
        var owner = book is null ? null : await owners.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
        if (book is null || owner is null || !owner.IsHeldBy(tenant))
            throw new UnauthorizedAccessException("A ledger book of this agreement is outside the current tenant or company.");
        return (book, owner.CompanyId ?? company);
    }

    private static bool SameCompany(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed record Pair(Guid AgreementId, IntercompanyBalanceConfirmationSideDto One, IntercompanyBalanceConfirmationSideDto Two,
        string? OneCompany, string? TwoCompany)
    {
        public bool CrossCompany => !SameCompany(OneCompany, TwoCompany);
    }
}
