using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Consolidation;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.Ledger;
using Meridian.Ledger;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>Delivers retained sources into the destination's existing review queue.</summary>
public sealed class IntercompanyProposalDeliveryService(IIntercompanyProposalStore store,
    IntercompanyAgreementService agreements, ILedgerJournalStore journals,
    IIntercompanyAccountBindingStore bindings, IAccountingConfigurationService configuration,
    IManualJournalEntryDraftStore drafts, ManualJournalEntryWorkbenchService workbench,
    IFundProfileTenancyRegistry owners, IConsolidationFxAuthority fx, TimeProvider? time = null)
{
    private readonly TimeProvider _clock = time ?? TimeProvider.System;

    public async Task<int> RunOnceAsync(string workerId, CancellationToken ct = default)
    {
        var claimed = await store.ClaimAsync(workerId, 20, TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
        var completed = 0;
        foreach (var proposal in claimed)
        {
            ct.ThrowIfCancellationRequested();
            using var tenantScope = FundScopeTenantAuthority.Enter(proposal.Source.TenantId, "intercompany-counterpart-delivery");
            try
            {
                var source = proposal.Source;
                var reversal = source.ReversesJournalId is not null
                    ? await RetainedReversalAsync(source, ct).ConfigureAwait(false) : null;
                var authorityDate = reversal?.OriginalDate ?? source.EffectiveDate;
                var agreement = await agreements.ResolveApprovedRevisionAsync(source.TenantId, source.AgreementId,
                    source.AgreementRevisionId, new DateTimeOffset(authorityDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), ct).ConfigureAwait(false);
                var maps = agreement.PartyMappings.Where(x => x.EntityId == source.CounterpartyEntityId
                    && (reversal is null || x.LedgerBookId == reversal.Prior.ResolvedTargetBookId)).ToArray();
                if (maps.Length != 1) throw new InvalidOperationException("Select one approved counterpart book for this agreement revision.");
                var target = await journals.GetLedgerBookAsync(maps[0].LedgerBookId, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The destination book is unavailable.");
                var owner = await owners.ResolveAsync(target.FundProfileId, ct).ConfigureAwait(false);
                if (owner is null || !owner.IsHeldBy(source.TenantId)
                    || (source.CompanyId is { } company && !string.Equals(company, owner.CompanyId, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The destination book's retained owner differs from the source authority.");
                var existing = await drafts.GetAsync(target.FundProfileId, source.ProposalId, ct, source.TenantId, owner.CompanyId).ConfigureAwait(false);
                if (existing is not null)
                {
                    if (existing.IntercompanyProposalId != source.ProposalId || existing.IntercompanyProposalSourceDigest != proposal.SourceDigest
                        || existing.LedgerBookId != target.LedgerBookId)
                        throw new InvalidOperationException("The deterministic destination identity already retains different source authority.");
                    // Recover delivery without overwriting any reviewer edits, rejection, approval or posting.
                    await store.CompleteAsync(source.TenantId, source.ProposalId, proposal.ClaimToken!,
                        target.LedgerBookId, existing.JournalEntryId, ct).ConfigureAwait(false);
                    completed++;
                    continue;
                }
                var periodCandidates = (await journals.ListPeriodsAsync(target.LedgerBookId, ct: ct).ConfigureAwait(false))
                    .Where(x => x.Status == "Open" && x.StartDate <= source.EffectiveDate && source.EffectiveDate <= x.EndDate).ToArray();
                if (periodCandidates.Length != 1)
                    throw new InvalidOperationException("Exactly one open destination period must contain the source effective date; no date is moved automatically.");
                var manuallyBooked = await journals.QueryAsync(new(LedgerBookId: target.LedgerBookId,
                    LineDimensions: new() { AgreementId = agreement.AgreementId.ToString("D"), IntercompanyTransactionId = source.TransactionId }), ct).ConfigureAwait(false);
                if (manuallyBooked.Count > 0 && source.ReversesJournalId is null)
                    throw new InvalidOperationException("A possible manually booked counterpart exists. Review and explicitly link it before creating another entry.");
                var targetAccounts = await bindings.ListAsync(target.LedgerBookId, source.EffectiveDate, ct).ConfigureAwait(false);
                var workspace = await configuration.GetWorkspaceAsync(target.FundProfileId, target.LedgerBookId, ct,
                    source.TenantId, owner.CompanyId).ConfigureAwait(false);
                ManualJournalEntryDraftDto draft;
                if (reversal is not null)
                {
                    var original = reversal.Prior.ResolvedTargetJournalId
                        ?? throw new InvalidOperationException("The original counterpart must be linked before preparing its reversal proposal.");
                    var originals = await journals.QueryAsync(new(LedgerBookId: target.LedgerBookId, JournalEntryId: original), ct).ConfigureAwait(false);
                    if (originals.Count != 1 || originals[0].Entry.JournalEntryId != original)
                        throw new InvalidOperationException("The original counterpart must be posted before an exact inverse can be proposed.");
                    var targetPopulation = await journals.QueryAsync(new(LedgerBookId: target.LedgerBookId), ct).ConfigureAwait(false);
                    if (new IntercompanyJournalFamily(targetPopulation, DateOnly.MaxValue).Resolve(original).Count != 1)
                        throw new InvalidOperationException("The original counterpart already has posted corrections. Review and explicitly link the actual correction instead of creating another inverse.");
                    draft = IntercompanyCounterpartReversalBuilder.Build(proposal, reversal.Prior, agreement, originals[0],
                        target, periodCandidates[0], targetAccounts, workspace.ChartOfAccounts, _clock.GetUtcNow());
                }
                else
                {
                    var sourceBook = await journals.GetLedgerBookAsync(source.SourceBookId, ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("The immutable source book is unavailable.");
                    var sourceWorkspace = await configuration.GetWorkspaceAsync(sourceBook.FundProfileId,
                        source.SourceBookId, ct, source.TenantId, owner.CompanyId).ConfigureAwait(false);
                    var sourceAccounts = await bindings.ListAsync(source.SourceBookId, source.EffectiveDate, ct).ConfigureAwait(false);
                    ConsolidationFxAmountDto? conversion = null;
                    if (agreement.Currency != target.BaseCurrency)
                    {
                        var sourceMap = agreement.PartyMappings.Single(x => x.EntityId == source.SourceEntityId && x.LedgerBookId == source.SourceBookId);
                        var monetary = source.Lines.Where(x => x.ChartAccountNodeId == sourceMap.ReceivableAccountNodeId
                            || x.ChartAccountNodeId == sourceMap.PayableAccountNodeId).ToArray();
                        var context = await fx.ResolveAsync(target.LedgerBookId, source.EffectiveDate, ct).ConfigureAwait(false);
                        if (context.Configuration.Policy.ReportingCurrency != target.BaseCurrency)
                            throw new InvalidOperationException("The destination FX policy must convert into the destination functional currency.");
                        conversion = ConsolidationFxTranslator.ConvertAmount(Math.Abs(monetary.Sum(x => x.TransactionDebit - x.TransactionCredit)),
                            agreement.Currency, context, source.EffectiveDate);
                    }
                    draft = IntercompanyProposalDraftBuilder.Build(proposal, agreement, target, periodCandidates[0],
                        sourceAccounts, targetAccounts, workspace.ChartOfAccounts, conversion, _clock.GetUtcNow(), sourceWorkspace.ChartOfAccounts);
                }
                draft = draft with { CompanyId = owner.CompanyId };
                var saved = await workbench.SaveAutomatedDraftAsync(new(draft, "intercompany-proposal-service",
                    $"intercompany-proposal:{source.ProposalId:D}", LedgerBookId: target.LedgerBookId,
                    TenantId: source.TenantId, CompanyId: owner.CompanyId), ct).ConfigureAwait(false);
                await store.CompleteAsync(source.TenantId, source.ProposalId, proposal.ClaimToken!, target.LedgerBookId,
                    saved.JournalEntryId, ct).ConfigureAwait(false);
                completed++;
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or UnauthorizedAccessException)
            {
                await store.FailAsync(proposal.Source.TenantId, proposal.Source.ProposalId, proposal.ClaimToken!,
                    error.Message, ct).ConfigureAwait(false);
            }
        }
        return completed;
    }

    private async Task<RetainedReversal> RetainedReversalAsync(IntercompanyProposalSourceDto source, CancellationToken ct)
    {
        var originalId = source.ReversesJournalId!.Value;
        var originals = await journals.QueryAsync(new(LedgerBookId: source.SourceBookId, JournalEntryId: originalId), ct).ConfigureAwait(false);
        var inverses = await journals.QueryAsync(new(LedgerBookId: source.SourceBookId, JournalEntryId: source.SourceJournalId), ct).ConfigureAwait(false);
        if (originals.Count != 1 || inverses.Count != 1)
            throw new InvalidOperationException("A retained same-book original and source reversal are required.");
        var originalDate = IntercompanyCounterpartReversalBuilder.VerifySource(source, originals[0], inverses[0]);
        var matches = (await store.ListAsync(source.TenantId, source.SourceBookId, ct).ConfigureAwait(false))
            .Where(x => x.Source.SourceJournalId == originalId && x.Source.SourceBookId == source.SourceBookId
                && x.Source.SourceEntityId == source.SourceEntityId && x.Source.CounterpartyEntityId == source.CounterpartyEntityId
                && x.Source.AgreementId == source.AgreementId && x.Source.AgreementRevisionId == source.AgreementRevisionId
                && string.Equals(x.Source.TransactionId, source.TransactionId, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1 || matches[0].ResolvedTargetBookId is null || matches[0].ResolvedTargetJournalId is null)
            throw new InvalidOperationException("One exact original event and its reviewed posted counterpart are required before a reversal can be proposed.");
        return new(matches[0], originalDate);
    }

    private sealed record RetainedReversal(IntercompanyProposalDto Prior, DateOnly OriginalDate);

    public async Task<IntercompanyRefreshPreviewDto> RefreshPreviewAsync(string tenant, Guid proposalId, CancellationToken ct = default)
    {
        var proposal = await store.GetAsync(tenant, proposalId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained counterpart source is unavailable.");
        if (proposal.LatestLink is not null)
            throw new InvalidOperationException("A reviewed manual counterpart already resolves this delivery. The rejected generated draft cannot be repriced or reactivated.");
        if (proposal.TargetBookId is not { } bookId || proposal.TargetJournalId is not { } journalId)
            throw new InvalidOperationException("Retry undelivered proposals after resolving their blockers; repricing requires an existing draft.");
        var book = await journals.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The destination book is unavailable.");
        var owner = await owners.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
        if (owner is null || !owner.IsHeldBy(tenant)) throw new UnauthorizedAccessException("The destination is outside the current tenant.");
        var draft = await drafts.GetAsync(book.FundProfileId, journalId, ct, tenant, owner.CompanyId).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained destination draft is unavailable.");
        if (draft.Status is not (ManualJournalEntryStatusDto.Draft or ManualJournalEntryStatusDto.NeedsFix or ManualJournalEntryStatusDto.Rejected)
            || draft.IntercompanyProposalId != proposalId || draft.IntercompanyProposalSourceDigest != proposal.SourceDigest
            || draft.EntryType == ManualJournalEntryTypeDto.Reversal)
            throw new InvalidOperationException("Only an editable source-bound counterpart can be explicitly repriced; retained reversals use original economics.");
        var source = proposal.Source;
        var terms = await agreements.ResolveApprovedRevisionAsync(tenant, source.AgreementId, source.AgreementRevisionId,
            new DateTimeOffset(source.EffectiveDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), ct).ConfigureAwait(false);
        if (terms.Currency == book.BaseCurrency) throw new InvalidOperationException("This counterpart requires no foreign-currency repricing.");
        var sourceMap = terms.PartyMappings.Single(x => x.EntityId == source.SourceEntityId && x.LedgerBookId == source.SourceBookId);
        var amount = Math.Abs(source.Lines.Where(x => x.ChartAccountNodeId == sourceMap.ReceivableAccountNodeId
            || x.ChartAccountNodeId == sourceMap.PayableAccountNodeId).Sum(x => x.TransactionDebit - x.TransactionCredit));
        var context = await fx.ResolveAsync(bookId, draft.AccountingDate, ct).ConfigureAwait(false);
        if (context.Configuration.Policy.ReportingCurrency != book.BaseCurrency)
            throw new InvalidOperationException("The destination FX policy must translate into its functional currency.");
        var converted = ConsolidationFxTranslator.ConvertAmount(amount, terms.Currency, context, draft.AccountingDate);
        if (draft.Lines.Count != 2) throw new InvalidOperationException("An edited multi-leg counterpart needs explicit manual repair; repricing never reconstructs reviewer changes silently.");
        var candidate = draft with { IntercompanyFx = converted,
            Lines = draft.Lines.Select(line => line with { Amount = converted.Amount,
                TransactionCurrency = new(terms.Currency, amount, converted.Rate) }).ToArray() };
        return new(candidate, Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new {
            candidate.JournalEntryId, candidate.Version, candidate.LedgerBookId, candidate.AccountingDate,
            candidate.Lines, candidate.IntercompanyFx, candidate.IntercompanyProposalSourceDigest })), draft);
    }

    public async Task<ManualJournalEntryDraftDto> RefreshAsync(string tenant, Guid proposalId,
        RefreshIntercompanyProposalRequest request, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);
        if (request.RequestId == Guid.Empty) throw new ArgumentException("A refresh request identity is required.");
        var digest = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(request));
        var proposal = await store.GetAsync(tenant, proposalId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The proposal is unavailable.");
        var book = proposal.TargetBookId is { } bookId ? await journals.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false) : null;
        var owner = book is null ? null : await owners.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
        if (book is null || owner is null || !owner.IsHeldBy(tenant)) throw new UnauthorizedAccessException("The destination book is unavailable.");
        var current = proposal.TargetJournalId is { } journalId
            ? await drafts.GetAsync(book.FundProfileId, journalId, ct, tenant, owner.CompanyId).ConfigureAwait(false) : null;
        if (current?.IntercompanyRefreshRequestId == request.RequestId)
        {
            if (current.IntercompanyRefreshRequestDigest != digest) throw new InvalidOperationException("Refresh identity already retains a different request.");
            return current;
        }
        await using var fxLease = await fx.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
        var preview = await RefreshPreviewAsync(tenant, proposalId, ct).ConfigureAwait(false);
        if (preview.Draft.Version != request.ExpectedDraftVersion || !Sha256Digest.FixedEquals(preview.Fingerprint, request.PreviewFingerprint))
            throw new InvalidOperationException("The draft or FX authority changed; inspect a fresh repricing preview.");
        var updated = preview.Draft with { Status = ManualJournalEntryStatusDto.Draft,
            ApprovalId = null, ApprovedAtUtc = null, ApprovedBy = null, SubmittedAtUtc = null, SubmittedBy = null,
            Memo = preview.Draft.Memo + "\nRepriced: " + request.Reason.Trim(),
            IntercompanyRefreshRequestId = request.RequestId, IntercompanyRefreshRequestDigest = digest };
        return await workbench.SaveAutomatedDraftAsync(new(updated, actor, "intercompany-refresh:" + request.RequestId.ToString("D"),
            LedgerBookId: book.LedgerBookId, TenantId: tenant, CompanyId: owner.CompanyId), ct).ConfigureAwait(false);
    }

    public async Task LinkAsync(string tenant, Guid proposalId, LinkIntercompanyCounterpartRequest request,
        string actor, CancellationToken ct = default)
    {
        var proposal = await store.GetAsync(tenant, proposalId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The source proposal is unavailable.");
        // An exact request replay retains its original reviewed decision even after later corrections.
        if (proposal.LatestLink?.RequestId == request.RequestId)
        {
            await store.LinkAsync(tenant, proposalId, request, actor, ct).ConfigureAwait(false);
            return;
        }
        if (proposal.LatestLink is not null || proposal.Status == IntercompanyProposalStatusDto.Linked)
            throw new InvalidOperationException("A reviewed manual linkage already exists. Replacing a linked target requires a separate correction workflow.");
        var source = proposal.Source;
        var agreement = await agreements.ResolveApprovedRevisionAsync(tenant, source.AgreementId, source.AgreementRevisionId,
            new DateTimeOffset(source.EffectiveDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), ct).ConfigureAwait(false);
        var destinations = agreement.PartyMappings.Where(x => x.EntityId == source.CounterpartyEntityId).ToArray();
        if (!destinations.Any(x => x.LedgerBookId == request.TargetBookId))
            throw new InvalidOperationException("The selected journal is outside the approved destination mapping.");
        var generated = new List<(LedgerBookRecord Book, string? Company)>();
        foreach (var destination in destinations)
        {
            var book = await journals.GetLedgerBookAsync(destination.LedgerBookId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The approved destination book is unavailable.");
            var owner = await owners.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
            if (owner is null || !owner.IsHeldBy(tenant) || (source.CompanyId is not null && !string.Equals(source.CompanyId, owner.CompanyId, StringComparison.OrdinalIgnoreCase)))
                throw new UnauthorizedAccessException("The destination belongs to another tenant or company.");
            generated.Add((book, owner.CompanyId));
        }
        // The workbench uses one recovery authority for these draft snapshots. Hold it even when
        // no target has been recorded yet: a worker may have saved its deterministic draft before crashing.
        var expected = proposal.TargetBookId is { } deliveredBook
            ? generated.Single(x => x.Book.LedgerBookId == deliveredBook) : generated.SingleOrDefault(x => x.Book.LedgerBookId == request.TargetBookId);
        if (expected.Book is null) throw new InvalidOperationException("The generated counterpart book is ambiguous.");
        await workbench.WithIntercompanyDraftLinkAuthorityAsync(expected.Book.FundProfileId, tenant, expected.Company,
            expected.Book.LedgerBookId, proposal.TargetJournalId ?? proposalId, proposalId, proposal.SourceDigest,
            proposal.Status == IntercompanyProposalStatusDto.Drafted, async rejected =>
            {
                foreach (var other in generated.Where(x => x.Book.LedgerBookId != expected.Book.LedgerBookId))
                    if (await drafts.GetAsync(other.Book.FundProfileId, proposalId, ct, tenant, other.Company).ConfigureAwait(false) is not null)
                        throw new InvalidOperationException("Another approved destination already retains a generated draft. Resolve that delivery before manual linking.");
                var mismatch = await CounterpartMismatchAsync(proposal, request.TargetBookId, request.TargetJournalId, ct).ConfigureAwait(false);
                if (mismatch is not null) throw new InvalidOperationException(mismatch);
                if (rejected is null) await store.LinkAsync(tenant, proposalId, request, actor, ct).ConfigureAwait(false);
                else await store.LinkRejectedDraftAsync(tenant, proposalId, request, actor, rejected, ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
    }

    private async Task<string?> CounterpartMismatchAsync(IntercompanyProposalDto proposal, Guid targetBookId,
        Guid targetJournalId, CancellationToken ct, CounterpartEvaluation? evaluation = null)
    {
        var source = proposal.Source;
        evaluation ??= new(journals, agreements, owners, source.TenantId, source.CompanyId,
            DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime));
        IntercompanyAgreementDto agreement;
        try { agreement = await evaluation.TermsAsync(source.AgreementId, source.AgreementRevisionId, ct).ConfigureAwait(false); }
        catch (InvalidOperationException error) { return error.Message; }
        var sourceMap = agreement.PartyMappings.SingleOrDefault(x => x.EntityId == source.SourceEntityId && x.LedgerBookId == source.SourceBookId);
        var targetMap = agreement.PartyMappings.SingleOrDefault(x => x.EntityId == source.CounterpartyEntityId && x.LedgerBookId == targetBookId);
        if (sourceMap is null || targetMap is null) return "The source or counterpart book differs from its approved mapping.";
        var sourceBook = await evaluation.BookAsync(source.SourceBookId, ct).ConfigureAwait(false);
        var targetBook = await evaluation.BookAsync(targetBookId, ct).ConfigureAwait(false);
        if (sourceBook?.FundStructureNodeId != source.SourceEntityId || targetBook?.FundStructureNodeId != source.CounterpartyEntityId)
            return "The source or counterpart book owner differs from the approved parties.";
        try
        {
            var sourcePopulation = await evaluation.PopulationAsync(source.SourceBookId, ct).ConfigureAwait(false);
            var targetPopulation = await evaluation.PopulationAsync(targetBookId, ct).ConfigureAwait(false);
            if (!sourcePopulation.Contains(source.SourceJournalId)) return "The immutable source journal is unavailable on the selected accounting date.";
            if (!targetPopulation.Contains(targetJournalId)) return "An immutable posted counterpart journal is required on the selected accounting date.";
            var sourceBalance = MonetaryFamilyBalance(sourcePopulation.Resolve(source.SourceJournalId), source.SourceJournalId,
                sourceMap, source.SourceEntityId, source.CounterpartyEntityId, source, agreement.Currency, sourceBook.BaseCurrency, sourceAnchor: true);
            if (sourceBalance.Error is not null) return sourceBalance.Error;
            var targetBalance = MonetaryFamilyBalance(targetPopulation.Resolve(targetJournalId), targetJournalId,
                targetMap, source.CounterpartyEntityId, source.SourceEntityId, source, agreement.Currency, targetBook.BaseCurrency);
            if (targetBalance.Error is not null) return targetBalance.Error;
            return targetBalance.Amount == -sourceBalance.Amount ? null
                : "The actual source and counterpart correction families do not reciprocate on the selected accounting date.";
        }
        catch (InvalidOperationException error) { return error.Message; }
    }

    private static (decimal Amount, string? Error) MonetaryFamilyBalance(IReadOnlyList<LedgerJournalEntryRecord> family,
        Guid anchorId, IntercompanyPartyMappingDto mapping, Guid entity, Guid counterparty,
        IntercompanyProposalSourceDto source, string currency, string functionalCurrency, bool sourceAnchor = false)
    {
        bool Node(LedgerEntry line) => line.Dimensions?.ChartAccountNodeId == mapping.ReceivableAccountNodeId
            || line.Dimensions?.ChartAccountNodeId == mapping.PayableAccountNodeId;
        var anchor = family.Single(record => record.Entry.JournalEntryId == anchorId);
        bool Event(LedgerEntry line) => Guid.TryParse(line.Dimensions?.AgreementId, out var agreementId) && agreementId == source.AgreementId
            && Guid.TryParse(line.Dimensions?.AgreementRevisionId, out var revisionId) && revisionId == source.AgreementRevisionId
            && string.Equals(line.Dimensions?.IntercompanyTransactionId, source.TransactionId, StringComparison.Ordinal);
        var originals = anchor.Entry.Lines.Where(line => Node(line) && (!sourceAnchor || Event(line))).ToArray();
        if (originals.Length == 0) return (0m, "The source or counterpart has no authoritative monetary account identity.");
        var names = originals.Select(line => line.Account.Name).ToHashSet(StringComparer.Ordinal);
        decimal amount = 0m;
        foreach (var record in family)
        foreach (var line in record.Entry.Lines.Where(line => Node(line) || (line.Dimensions?.ChartAccountNodeId is null && names.Contains(line.Account.Name))))
        {
            var dimensions = line.Dimensions;
            var sameTransaction = Guid.TryParse(dimensions?.AgreementId, out var agreementId) && agreementId == source.AgreementId
                && Guid.TryParse(dimensions?.AgreementRevisionId, out var revisionId) && revisionId == source.AgreementRevisionId
                && dimensions?.IntercompanyTransactionId == source.TransactionId;
            if (!sameTransaction)
            {
                if (record.Entry.JournalEntryId == anchorId && !sourceAnchor)
                    return (0m, "The counterpart agreement revision or transaction identity differs from the retained source.");
                continue; // A separately attributed rebook has its own source proposal; shared labels alone never join families.
            }
            if (!Guid.TryParse(dimensions?.EntityId, out var party) || party != entity
                || !Guid.TryParse(dimensions?.CounterpartyId, out var other) || other != counterparty
                || line.Account.Symbol is not null || line.Account.FinancialAccountId is not null)
                return (0m, "The source or counterpart monetary parties/account scope differ from the approved transaction.");
            if (!string.Equals(line.Currency?.TransactionCurrency ?? functionalCurrency, currency, StringComparison.OrdinalIgnoreCase))
                return (0m, "The source or counterpart original currency differs from the agreement currency.");
            amount += (line.Currency?.TransactionDebit ?? line.Debit) - (line.Currency?.TransactionCredit ?? line.Credit);
        }
        return (amount, null);
    }

    private sealed class CounterpartEvaluation(ILedgerJournalStore journals, IntercompanyAgreementService agreements,
        IFundProfileTenancyRegistry owners, string tenant, string? company, DateOnly asOf)
    {
        private readonly Dictionary<Guid, Task<LedgerBookRecord?>> _books = new();
        private readonly Dictionary<Guid, Task<IntercompanyJournalFamily>> _populations = new();
        private readonly Dictionary<(Guid, Guid), Task<IntercompanyAgreementDto>> _terms = new();
        public Task<LedgerBookRecord?> BookAsync(Guid id, CancellationToken ct)
        {
            if (!_books.TryGetValue(id, out var task)) _books[id] = task = ReadBookAsync(id, ct);
            return task;
        }
        private async Task<LedgerBookRecord?> ReadBookAsync(Guid id, CancellationToken ct)
        {
            var book = await journals.GetLedgerBookAsync(id, ct).ConfigureAwait(false);
            var owner = book is null ? null : await owners.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
            return owner is not null && owner.IsHeldBy(tenant)
                && (company is null || string.Equals(company, owner.CompanyId, StringComparison.OrdinalIgnoreCase)) ? book : null;
        }
        public Task<IntercompanyJournalFamily> PopulationAsync(Guid book, CancellationToken ct)
        {
            if (!_populations.TryGetValue(book, out var task)) _populations[book] = task = ReadPopulationAsync(book, ct);
            return task;
        }
        private async Task<IntercompanyJournalFamily> ReadPopulationAsync(Guid book, CancellationToken ct)
            => new(await journals.QueryAsync(new(LedgerBookId: book, EffectiveTo: asOf), ct).ConfigureAwait(false), asOf);
        public Task<IntercompanyAgreementDto> TermsAsync(Guid agreementId, Guid revision, CancellationToken ct)
        {
            var key = (agreementId, revision);
            if (!_terms.TryGetValue(key, out var task)) _terms[key] = task = ReadTermsAsync(agreementId, revision, ct);
            return task;
        }
        private async Task<IntercompanyAgreementDto> ReadTermsAsync(Guid agreementId, Guid revision, CancellationToken ct)
        {
            var history = await agreements.GetHistoryAsync(tenant, agreementId, ct).ConfigureAwait(false);
            return history?.Events.FirstOrDefault(item => item.Action == IntercompanyAgreementActionDto.Approve && item.AffectedRevision.RevisionId == revision)?.AffectedRevision
                ?? throw new InvalidOperationException("The approved source agreement revision is unavailable.");
        }
    }

    public async Task<IReadOnlyList<IntercompanyBreakDto>> BreaksAsync(string tenant, Guid sourceBookId, DateOnly asOf, CancellationToken ct = default)
    {
        var result = new List<IntercompanyBreakDto>();
        var proposals = await store.ListAsync(tenant, sourceBookId, ct).ConfigureAwait(false);
        var sourceBook = await journals.GetLedgerBookAsync(sourceBookId, ct).ConfigureAwait(false);
        var sourceOwner = sourceBook is null ? null : await owners.ResolveAsync(sourceBook.FundProfileId, ct).ConfigureAwait(false);
        if (sourceOwner is null || !sourceOwner.IsHeldBy(tenant)) throw new UnauthorizedAccessException("The intercompany source book is outside the current tenant.");
        var evaluation = new CounterpartEvaluation(journals, agreements, owners, tenant, sourceOwner.CompanyId, asOf);
        foreach (var proposal in proposals)
        {
            if (proposal.Source.EffectiveDate > asOf) continue;
            var classification = proposal.Status switch
            {
                IntercompanyProposalStatusDto.NeedsFix => "DeliveryBlocked",
                IntercompanyProposalStatusDto.Pending => "MissingCounterpart",
                _ => "AwaitingCounterpartPosting"
            };
            string? mismatch = null;
            var targetBook = proposal.ResolvedTargetBookId;
            var targetJournal = proposal.ResolvedTargetJournalId;
            if (targetBook is { } book && targetJournal is { } journal)
            {
                mismatch = await CounterpartMismatchAsync(proposal, book, journal, ct, evaluation).ConfigureAwait(false);
                if (mismatch is null) continue;
                classification = mismatch.StartsWith("An immutable posted counterpart", StringComparison.Ordinal)
                    ? "AwaitingCounterpartPosting"
                    : mismatch.Contains("currency", StringComparison.OrdinalIgnoreCase) ? "CurrencyMismatch" : "AmountOrIdentityMismatch";
            }
            IntercompanyAgreementDto? terms = null;
            try { terms = await evaluation.TermsAsync(proposal.Source.AgreementId, proposal.Source.AgreementRevisionId, ct).ConfigureAwait(false); }
            catch (InvalidOperationException error) { mismatch ??= error.Message; classification = "AgreementAuthorityUnavailable"; }
            var due = proposal.DueDate ?? (terms?.SettlementDueDays is { } days ? proposal.Source.EffectiveDate.AddDays(days) : (DateOnly?)null);
            var from = due ?? proposal.Source.EffectiveDate;
            result.Add(new(proposal.Source.ProposalId, proposal.Source.AgreementId, proposal.Source.TransactionId,
                classification, from, Math.Max(0, asOf.DayNumber - from.DayNumber),
                due is null ? "Source effective date" : "Contractual due date",
                mismatch ?? proposal.LastError ?? classification, proposal.Source.SourceJournalId, targetJournal));
        }
        return result;
    }

}

public sealed class IntercompanyProposalHostedService(IServiceProvider services, ILogger<IntercompanyProposalHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var delivery = services.GetService(typeof(IntercompanyProposalDeliveryService)) as IntercompanyProposalDeliveryService;
            if (delivery is null) continue;
            try { await delivery.RunOnceAsync("intercompany-proposal-worker", stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Intercompany counterpart delivery failed; retained proposals remain retryable."); }
        }
    }
}
