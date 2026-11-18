using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Resolves approved management-fee terms and profile revisions into a source-side fee draft (rule
/// <c>intercompany-fee-v1</c>). It never approves or posts: the draft enters the source book's ordinary review, and the
/// counterpart is produced by the existing proposal pipeline only after the source journal commits.
/// </summary>
public sealed class IntercompanyFeeAccrualDraftService(IntercompanyAccountingArrangementService arrangements,
    ILedgerJournalStore journals, IIntercompanyAccountBindingStore bindings, IAccountingConfigurationService configuration,
    IManualJournalEntryDraftStore drafts, ManualJournalEntryWorkbenchService workbench,
    IIntercompanyFeeAccrualPreviewService calculator, IConsolidationFxAuthority? fx = null, TimeProvider? time = null)
{
    private readonly TimeProvider _clock = time ?? TimeProvider.System;

    /// <summary>Read-only: computes the fee, its evidence and the draft that would be retained.</summary>
    public async Task<IntercompanyFeeAccrualProposalDto> PreviewAsync(string tenantId, string? companyId, string actor,
        IntercompanyFeeAccrualRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        IntercompanyFeeAccrualProposalDto Unavailable(string reason, IntercompanyFeeAccrualPreviewDto? computed = null)
        {
            var computation = computed ?? new(IntercompanyAccrualRules.StatusUnavailable, IntercompanyFeeAccrualRules.RuleVersion,
                null, null, request.AccrualFrom, request.AccrualTo, null, null, string.Empty, [], [], [reason]);
            return new(computation, computed is null ? [reason] : [.. computation.Diagnostics, reason]);
        }
        if (request.AccrualTo <= request.AccrualFrom
            || request.AccrualTo.DayNumber - request.AccrualFrom.DayNumber > IntercompanyAccrualRules.MaximumWindowDays)
            return Unavailable($"Select a nonempty fee window of at most {IntercompanyAccrualRules.MaximumWindowDays} days.");
        try
        {
            var book = await journals.GetLedgerBookAsync(request.SourceBookId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The source book is unavailable.");
            var history = await arrangements.GetHistoryAsync(tenantId, request.FeeArrangementId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The management-fee accounting profile was not found.");

            var authority = new List<IntercompanyAccrualAuthoritySegment>();
            ResolvedIntercompanyAccountingArrangementDto? posting = null;
            foreach (var (from, to, revisionId) in IntercompanyAccrualSourceBuilder.ResolveAuthority(history,
                request.AccrualFrom, request.AccrualTo, "management-fee"))
            {
                // The pinned legal revision must itself be the applicable one across the whole segment.
                var first = await arrangements.ResolveApprovedRevisionAsync(tenantId, request.FeeArrangementId,
                    revisionId, EndOfDay(from), ct).ConfigureAwait(false);
                posting = await arrangements.ResolveApprovedRevisionAsync(tenantId, request.FeeArrangementId,
                    revisionId, EndOfDay(to.AddDays(-1)), ct).ConfigureAwait(false);
                if (first.TemplateKind != IntercompanyAccountingTemplateKindDto.ManagementFee
                    || first.LegalRevision.PayloadHash != posting.LegalRevision.PayloadHash)
                    throw new InvalidOperationException("Every fee day requires one approved ManagementFee profile revision pinning its applicable legal revision.");
                authority.Add(new(from, to, request.FeeArrangementId, revisionId, first.LegalRevision));
            }
            if (posting is null)
                return Unavailable("No approved management-fee profile covers the window.");
            if (!posting.PartyMappings.Any(item => item.EntityId == book.FundStructureNodeId && item.LedgerBookId == book.LedgerBookId))
                return Unavailable("The management-fee profile does not map the source book.");

            var computation = calculator.Preview(new(request.AccrualFrom, request.AccrualTo, authority));
            if (!computation.IsReady)
                return new(computation, computation.Diagnostics);

            var feeJournals = await journals.QueryAsync(new(LedgerBookId: book.LedgerBookId,
                LineDimensions: new() { AccountingArrangementId = request.FeeArrangementId.ToString("D") }), ct).ConfigureAwait(false);
            if (IntercompanyFeeAccrualSourceBuilder.FindPostedOverlap(feeJournals, request.FeeArrangementId,
                request.AccrualFrom, request.AccrualTo) is { } overlap)
                return Unavailable($"Posted fee {overlap} already covers part of this window; reverse it before charging those days again.", computation);
            var generation = IntercompanyFeeAccrualSourceBuilder.PostedGeneration(feeJournals, request.FeeArrangementId,
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
                    return Unavailable("A cross-currency fee requires the governed FX authority, which is unavailable.", computation);
                var context = await fx.ResolveAsync(book.LedgerBookId, postingDate, ct).ConfigureAwait(false);
                if (context.Configuration.Policy.ReportingCurrency != book.BaseCurrency)
                    return Unavailable("The source book's FX policy must convert into its functional currency.", computation);
                conversion = ConsolidationFxTranslator.ConvertAmount(computation.Amount!.Value, posting.Currency, context, postingDate);
            }
            var accounts = await bindings.ListAsync(book.LedgerBookId, postingDate, ct).ConfigureAwait(false);
            var workspace = await configuration.GetWorkspaceAsync(book.FundProfileId, book.LedgerBookId, ct,
                tenantId, companyId).ConfigureAwait(false);
            return IntercompanyFeeAccrualSourceBuilder.BuildDraft(request, tenantId, companyId, actor, book, periods[0], posting,
                computation, accounts, workspace.ChartOfAccounts, conversion, _clock.GetUtcNow(), generation);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return Unavailable(error.Message);
        }
    }

    /// <summary>
    /// Retains the previewed fee as a draft in the source book's review queue. A changed input since the preview is
    /// refused; an unreviewed draft of the same window is recomputed in place; one already submitted is never overwritten.
    /// </summary>
    public async Task<ManualJournalEntryDraftDto> CreateDraftAsync(string tenantId, string? companyId, string actor,
        CreateIntercompanyFeeAccrualDraftRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(actor))
            throw new UnauthorizedAccessException("An authenticated preparer is required.");
        var proposal = await PreviewAsync(tenantId, companyId, actor, request.Accrual, ct).ConfigureAwait(false);
        if (!proposal.IsReady)
            throw new InvalidOperationException(proposal.Diagnostics.LastOrDefault() ?? "The fee is unavailable.");
        if (!Sha256Digest.FixedEquals(proposal.EvidenceDigest, request.ExpectedEvidenceDigest))
            throw new InvalidOperationException("The approved terms, profile or FX inputs changed after the preview. Preview the fee again before drafting.");
        var draft = proposal.Draft!;
        var existing = await drafts.GetAsync(draft.FundProfileId, draft.JournalEntryId, ct, tenantId, companyId).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.LedgerBookId != draft.LedgerBookId || !IntercompanyFeeAccrualEvidenceGuard.IsFeeEvidence(existing.IntercompanyAccrualEvidenceJson))
                throw new InvalidOperationException("The deterministic fee identity already retains a different journal.");
            if (existing.IntercompanyAccrualEvidenceDigest == draft.IntercompanyAccrualEvidenceDigest)
                return existing; // Exact retry: reviewer edits, decisions and posting are never overwritten.
            if (existing.Status is not (ManualJournalEntryStatusDto.Draft or ManualJournalEntryStatusDto.NeedsFix
                or ManualJournalEntryStatusDto.Rejected))
                throw new InvalidOperationException($"A fee for this window is already {existing.Status}; it cannot be recomputed.");
            draft = draft with { Version = existing.Version, CreatedAtUtc = existing.CreatedAtUtc };
        }
        return await workbench.SaveAutomatedDraftAsync(new(draft, actor, $"intercompany-fee:{draft.JournalEntryId:D}",
            LedgerBookId: draft.LedgerBookId, TenantId: tenantId, CompanyId: companyId), ct).ConfigureAwait(false);
    }

    private static DateTimeOffset EndOfDay(DateOnly day) => new(day.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
}
