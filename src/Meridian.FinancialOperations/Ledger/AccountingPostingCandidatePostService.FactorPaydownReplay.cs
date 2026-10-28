using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidatePostService
{
    private async Task<PostedPostingRuleJournalCandidateResultDto> ReplayRetainedFactorPaydownAsync(
        AssetPostingAuthoritySnapshot authority, PostPostingRuleJournalCandidateRequestDto request,
        LedgerJournalEntryRecord existing, LedgerBookRecord book, string actor, CancellationToken ct)
    {
        // A successful paydown changes the very lots that the new-write builder must validate.
        // An exact retry instead proves the immutable original journal and atomic receipt, then
        // delegates the complete command fingerprint to the existing storage replay boundary.
        ValidateExistingAssetJournalAgainstDraft(authority, request, existing, book);
        RequireAssetAssertion(request.ApprovalEvidence.All(evidence =>
                evidence.ReviewedAtUtc <= existing.CreatedAt && evidence.RetainedAtUtc <= existing.CreatedAt),
            "The retained factor journal cannot precede its approval review or evidence retention.");
        var batchId = CreateMutationBatchId(book.LedgerBookId, authority.Context.EventId, AssetLotMutationIntentDto.FactorPaydown);
        var receipt = await _journalStore!.GetAtomicTaxLotPostingAsync(batchId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Exact factor replay requires the retained journal-backed atomic receipt.");
        RequireAssetAssertion(receipt.MutationBatchId == batchId && receipt.MutationKind == AtomicTaxLotMutationKind.FactorPaydown,
            "The retained atomic receipt does not identify the original factor mutation batch.");
        RequireAssetAssertion(PayloadEquals(receipt.Journal, existing),
            "The retained atomic receipt does not contain the exact existing factor journal.");
        RequireAssetAssertion(PayloadEquals(receipt.FactorPaydown, authority.Drafted.Projection.DraftedLotMutation!.FactorPaydown),
            "The retained atomic receipt does not contain the exact reviewed factor instruction.");
        // Storage normalizes evidence order before retention and command fingerprinting.
        RequireAssetAssertion(PayloadEquals(OrderedEvidence(receipt.RetainedEvidence),
                OrderedEvidence(request.Candidate.RetainedEvidence.Concat(request.ApprovalEvidence))),
            "The retained atomic receipt does not contain the exact original factor evidence.");
        var candidate = BuildApprovedAssetCandidateResult(authority, request, actor, book.LedgerBookId,
            authority.Context.EventId, RetainedPostingActor(existing), existing.CreatedAt);
        var originalAt = authority.Drafted.Projection.DraftedCandidate!.AccountingTimestamp;
        var previews = authority.Drafted.Projection.DraftedCandidateResult!.DryRunResult.GeneratedLines;
        var lines = existing.Entry.Lines.Select((line, index) =>
        {
            var preview = previews[index];
            var debit = preview.Side == AccountingTemplateLineSideDto.Debit ? preview.Amount : 0m;
            var credit = preview.Side == AccountingTemplateLineSideDto.Credit ? preview.Amount : 0m;
            RequireAssetAssertion(RetainedPostingEquivalence.AccountingTimestampsMatch(line.Timestamp, originalAt) &&
                    line.Currency is { } currency && currency.TransactionCurrency == authority.Drafted.Projection.Currency &&
                    currency.FunctionalCurrency == authority.Drafted.Projection.Currency && currency.FxRateToFunctional == 1m &&
                    currency.TransactionDebit == debit && currency.TransactionCredit == credit,
                "The retained factor line timestamp or currency differs from the original approved instruction.");
            // The exact original timestamp and decimal representation are retained in the draft.
            // PostgreSQL stores lower precision; restore the original command shape only after
            // proving the durable values, so its immutable full fingerprint can be checked again.
            return new LedgerEntry(line.EntryId, line.JournalEntryId, originalAt, line.Account, debit, credit,
                line.Description, line.Dimensions, line.Currency);
        }).ToArray();
        var originalEntry = new JournalEntry(existing.Entry.JournalEntryId, originalAt,
            existing.Entry.Description, lines, existing.Entry.Metadata);
        var write = new LedgerJournalEntryWrite(originalEntry, existing.AggregateId, existing.PeriodId,
            existing.CommandId, existing.CorrelationId, existing.AccountingBasis, existing.AccountingPolicyId,
            existing.AccountingPolicyVersion, existing.RuleId, existing.RuleVersion, existing.SourceEventId,
            existing.SourceJournalEntryId, existing.PostingKind, existing.AdjustmentApproval,
            candidate.PostingCommand, book.LedgerBookId);
        write = BindAssetPostingIntentToJournal(write, authority);
        var replay = await AppendAtomicLotPostingAsync(request, write, book, authority.Context.EventId,
            authority, hasExistingJournal: true, ct).ConfigureAwait(false);
        RequireAssetAssertion(replay.IsExactReplay && replay.MutationBatchId == batchId &&
                replay.CanonicalFingerprint == receipt.CanonicalFingerprint && PayloadEquals(replay.Journal, existing),
            "The atomic factor replay differs from the retained original financial receipt.");
        var impact = BuildJournalImpact(replay.Journal, book.BaseCurrency);
        await EnsureAssetProjectionApprovedAsync(authority, request, actor, ct).ConfigureAwait(false);
        await EnsureAssetProjectionPostedAsync(authority, request, actor, replay.Journal, impact, batchId, ct).ConfigureAwait(false);
        return new PostedPostingRuleJournalCandidateResultDto(candidate, BuildPostedResult(replay.Journal, book.LedgerBookId),
            WasReplay: true)
        {
            JournalImpact = impact,
            TaxLotMutationBatchId = batchId,
            LifecycleStages = BuildPostedLifecycleStages(request, actor, replay.Journal.CreatedAt)
        };
    }

    private static RetainedEvidenceIdentityDto[] OrderedEvidence(IEnumerable<RetainedEvidenceIdentityDto> evidence)
        => evidence.OrderBy(static item => item.EvidenceId, StringComparer.Ordinal)
            .ThenBy(static item => item.EvidenceVersion)
            .ThenBy(static item => item.ContentHashSha256, StringComparer.Ordinal).ToArray();
}
