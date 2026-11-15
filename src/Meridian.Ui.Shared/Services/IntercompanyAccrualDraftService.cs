using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Resolves approved terms, profile revisions and posted principal into a source-side accrual draft.
/// It never approves or posts: the draft enters the source book's ordinary review, and the counterpart
/// is produced by the existing proposal pipeline only after the source journal commits.
/// </summary>
public sealed class IntercompanyAccrualDraftService(IntercompanyAccountingArrangementService arrangements,
    ILedgerJournalStore journals, IIntercompanyAccountBindingStore bindings, IAccountingConfigurationService configuration,
    IManualJournalEntryDraftStore drafts, ManualJournalEntryWorkbenchService workbench,
    IIntercompanyAccrualPreviewService calculator, IConsolidationFxAuthority? fx = null, TimeProvider? time = null)
{
    private readonly TimeProvider _clock = time ?? TimeProvider.System;

    /// <summary>Read-only: computes the amount, its evidence and the draft that would be retained.</summary>
    public async Task<IntercompanyAccrualProposalDto> PreviewAsync(string tenantId, string? companyId, string actor,
        IntercompanyAccrualRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        IntercompanyAccrualProposalDto Unavailable(string reason, IntercompanyAccrualPreviewDto? computed = null)
        {
            var computation = computed ?? new(IntercompanyAccrualRules.StatusUnavailable, IntercompanyAccrualRules.RuleVersion,
                null, null, request.AccrualFrom, request.AccrualTo, null, null, string.Empty, [], [], [reason]);
            return new(computation, computed is null ? [reason] : [.. computation.Diagnostics, reason]);
        }
        if (request.AccrualTo <= request.AccrualFrom
            || request.AccrualTo.DayNumber - request.AccrualFrom.DayNumber > IntercompanyAccrualRules.MaximumWindowDays)
            return Unavailable($"Select a nonempty accrual window of at most {IntercompanyAccrualRules.MaximumWindowDays} days.");
        if (request.InterestArrangementId == request.PrincipalArrangementId)
            return Unavailable("Interest and principal use separately reviewed accounting profiles.");
        try
        {
            var book = await journals.GetLedgerBookAsync(request.SourceBookId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The source book is unavailable.");
            var interestHistory = await arrangements.GetHistoryAsync(tenantId, request.InterestArrangementId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The loan-interest accounting profile was not found.");
            var principalHistory = await arrangements.GetHistoryAsync(tenantId, request.PrincipalArrangementId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The loan principal accounting profile was not found.");

            var authority = new List<IntercompanyAccrualAuthoritySegment>();
            ResolvedIntercompanyAccountingArrangementDto? posting = null;
            foreach (var (from, to, revisionId) in IntercompanyAccrualSourceBuilder.ResolveAuthority(interestHistory,
                request.AccrualFrom, request.AccrualTo))
            {
                // The pinned legal revision must itself be the applicable one across the whole segment; an
                // amendment without a re-reviewed profile revision leaves those days without authority.
                var first = await arrangements.ResolveApprovedRevisionAsync(tenantId, request.InterestArrangementId,
                    revisionId, EndOfDay(from), ct).ConfigureAwait(false);
                posting = await arrangements.ResolveApprovedRevisionAsync(tenantId, request.InterestArrangementId,
                    revisionId, EndOfDay(to.AddDays(-1)), ct).ConfigureAwait(false);
                if (first.TemplateKind != IntercompanyAccountingTemplateKindDto.LoanInterest
                    || first.LegalRevision.PayloadHash != posting.LegalRevision.PayloadHash)
                    throw new InvalidOperationException("Every accrual day requires one approved LoanInterest profile revision pinning its applicable legal revision.");
                authority.Add(new(from, to, request.InterestArrangementId, revisionId, first.LegalRevision));
            }
            if (posting is null)
                return Unavailable("No approved loan-interest profile covers the window.");

            var principalRevisions = principalHistory.Events
                .Where(item => item.Action == IntercompanyAccountingArrangementActionDto.Approve)
                .Select(item => item.AffectedRevision).ToArray();
            if (principalRevisions.Length == 0
                || principalRevisions.Any(item => item.TemplateKind != IntercompanyAccountingTemplateKindDto.Loan
                    || item.CanonicalAgreementId != posting.CanonicalAgreementId))
                return Unavailable("Principal must come from an approved Loan profile of the same legal agreement.");
            var principalMaps = principalRevisions.SelectMany(item => item.PartyMappings)
                .Where(item => item.EntityId == book.FundStructureNodeId && item.LedgerBookId == book.LedgerBookId).ToArray();
            if (principalMaps.Length == 0)
                return Unavailable("The loan principal profile does not map the source book.");
            var principalJournals = await journals.QueryAsync(new(LedgerBookId: book.LedgerBookId,
                LineDimensions: new() { AccountingArrangementId = request.PrincipalArrangementId.ToString("D") }), ct).ConfigureAwait(false);
            var principal = IntercompanyAccrualSourceBuilder.BuildPrincipal(principalJournals, request.PrincipalArrangementId,
                book.LedgerBookId, principalMaps.Select(item => item.ReceivableAccountNodeId).ToHashSet(StringComparer.Ordinal),
                principalMaps.Select(item => item.PayableAccountNodeId).ToHashSet(StringComparer.Ordinal),
                posting.Currency, book.BaseCurrency, request.AccrualFrom, request.AccrualTo);
            if (principal.Evidence is null)
                return Unavailable(principal.Refusal ?? "Posted principal is unavailable.");

            var computation = calculator.Preview(new(request.AccrualFrom, request.AccrualTo, authority, principal.Segments));
            if (!computation.IsReady)
                return new(computation, computation.Diagnostics);

            var interestJournals = await journals.QueryAsync(new(LedgerBookId: book.LedgerBookId,
                LineDimensions: new() { AccountingArrangementId = request.InterestArrangementId.ToString("D") }), ct).ConfigureAwait(false);
            if (IntercompanyAccrualSourceBuilder.FindPostedOverlap(interestJournals, request.InterestArrangementId,
                request.AccrualFrom, request.AccrualTo) is { } overlap)
                return Unavailable($"Posted accrual {overlap} already covers part of this window; reverse it before accruing those days again.", computation);
            var generation = IntercompanyAccrualSourceBuilder.PostedGeneration(interestJournals, request.InterestArrangementId,
                request.AccrualFrom, request.AccrualTo);

            var postingDate = request.AccrualTo.AddDays(-1);
            var periods = (await journals.ListPeriodsAsync(book.LedgerBookId, ct: ct).ConfigureAwait(false))
                .Where(item => item.Status == "Open" && item.StartDate <= postingDate && postingDate <= item.EndDate).ToArray();
            if (periods.Length != 1)
                return Unavailable("Exactly one open source period must contain the last accrued day; no date is moved automatically.", computation);

            ConsolidationFxAmountDto? conversion = null;
            if (posting.Currency != book.BaseCurrency)
            {
                if (fx is null)
                    return Unavailable("A cross-currency loan requires the governed FX authority, which is unavailable.", computation);
                var context = await fx.ResolveAsync(book.LedgerBookId, postingDate, ct).ConfigureAwait(false);
                if (context.Configuration.Policy.ReportingCurrency != book.BaseCurrency)
                    return Unavailable("The source book's FX policy must convert into its functional currency.", computation);
                conversion = ConsolidationFxTranslator.ConvertAmount(computation.Amount!.Value, posting.Currency, context, postingDate);
            }
            var accounts = await bindings.ListAsync(book.LedgerBookId, postingDate, ct).ConfigureAwait(false);
            var workspace = await configuration.GetWorkspaceAsync(book.FundProfileId, book.LedgerBookId, ct,
                tenantId, companyId).ConfigureAwait(false);
            return IntercompanyAccrualSourceBuilder.BuildDraft(request, tenantId, companyId, actor, book, periods[0], posting,
                computation, principal.Evidence, accounts, workspace.ChartOfAccounts, conversion, _clock.GetUtcNow(), generation);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return Unavailable(error.Message);
        }
    }

    /// <summary>
    /// Retains the previewed computation as a draft in the source book's review queue. A changed input since
    /// the preview is refused. An unreviewed draft of the same window is recomputed in place; one already
    /// submitted, approved or posted is never overwritten.
    /// </summary>
    public async Task<ManualJournalEntryDraftDto> CreateDraftAsync(string tenantId, string? companyId, string actor,
        CreateIntercompanyAccrualDraftRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(actor))
            throw new UnauthorizedAccessException("An authenticated preparer is required.");
        var proposal = await PreviewAsync(tenantId, companyId, actor, request.Accrual, ct).ConfigureAwait(false);
        if (!proposal.IsReady)
            throw new InvalidOperationException(proposal.Diagnostics.LastOrDefault() ?? "The accrual is unavailable.");
        if (!Sha256Digest.FixedEquals(proposal.EvidenceDigest, request.ExpectedEvidenceDigest))
            throw new InvalidOperationException("The approved terms, profile, principal or FX inputs changed after the preview. Preview the accrual again before drafting.");
        var draft = proposal.Draft!;
        var existing = await drafts.GetAsync(draft.FundProfileId, draft.JournalEntryId, ct, tenantId, companyId).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.LedgerBookId != draft.LedgerBookId || !IntercompanyAccrualEvidenceGuard.IsAccrualDraft(existing))
                throw new InvalidOperationException("The deterministic accrual identity already retains a different journal.");
            if (existing.IntercompanyAccrualEvidenceDigest == draft.IntercompanyAccrualEvidenceDigest)
                return existing; // Exact retry: reviewer edits, decisions and posting are never overwritten.
            if (existing.Status is not (ManualJournalEntryStatusDto.Draft or ManualJournalEntryStatusDto.NeedsFix
                or ManualJournalEntryStatusDto.Rejected))
                throw new InvalidOperationException($"An accrual for this window is already {existing.Status}; it cannot be recomputed.");
            draft = draft with { Version = existing.Version, CreatedAtUtc = existing.CreatedAtUtc };
        }
        return await workbench.SaveAutomatedDraftAsync(new(draft, actor, $"intercompany-accrual:{draft.JournalEntryId:D}",
            LedgerBookId: draft.LedgerBookId, TenantId: tenantId, CompanyId: companyId), ct).ConfigureAwait(false);
    }

    private static DateTimeOffset EndOfDay(DateOnly day) => new(day.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
}
