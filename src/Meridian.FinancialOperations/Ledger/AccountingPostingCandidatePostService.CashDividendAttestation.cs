using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>Posts only producer-leased native cash-dividend preparer decisions, never ordinary approval.</summary>
public interface IAccountingCashDividendAttestedPostService
{
    Task<PostedPostingRuleJournalCandidateResultDto> PostAttestedCashDividendCandidateAsync(
        PostAttestedCashDividendPostingCandidateRequestDto request,
        CorporateActionCashDividendPostingLease lease,
        CancellationToken ct = default);
}

public sealed partial class AccountingPostingCandidatePostService
{
    public async Task<PostedPostingRuleJournalCandidateResultDto> PostAttestedCashDividendCandidateAsync(
        PostAttestedCashDividendPostingCandidateRequestDto request,
        CorporateActionCashDividendPostingLease lease,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidate);
        ArgumentNullException.ThrowIfNull(lease);
        ct.ThrowIfCancellationRequested();
        if (!OperationsOriginGuard.IsHumanOperator(request.ActionOrigin))
            throw new HumanOperatorRequiredException("post attested cash dividends", "An authenticated human operator is required.");
        var journalStore = _journalStore as ITransactionalLedgerJournalStore
            ?? throw new InvalidOperationException("Attested cash dividends require the native transactional journal store.");
        var eventStore = _assetAccountingEventStore as ITransactionalAssetAccountingEventProjectionStore
            ?? throw new InvalidOperationException("Attested cash dividends require a native transactional event spine.");
        var candidate = request.Candidate;
        var actor = RequireText(request.Actor, nameof(request.Actor));
        var reason = RequireText(request.Reason, nameof(request.Reason));
        var bookId = candidate.LedgerBookId
            ?? throw new InvalidOperationException("The native preparer receipt does not authorize this exact regular USD cash-dividend candidate.");
        var eventId = candidate.SourceEventId ?? Guid.Empty;
        var receipt = lease.Attestation;
        var projection = lease.Projection;
        var roleBinding = receipt.AccountRoleBinding
            ?? throw new InvalidOperationException("The native dividend receipt requires an explicit book-owned account-role binding.");
        if (bookId == Guid.Empty
            || candidate.SourceEventType != AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction)
            || candidate.Currency != CorporateActionCashDividendAttestationPolicy.Currency
            || candidate.AssetLotMutation is not null || candidate.EconomicEvent is null
            || candidate.SourceJournalEntryId is not null || candidate.PostingKind != LedgerPostingKindDto.Originating
            || candidate.AdjustmentApproval is not null
            || candidate.TenantId != request.TenantId || candidate.CompanyId != request.CompanyId
            || !string.Equals(candidate.Actor.Trim(), actor, StringComparison.OrdinalIgnoreCase)
            || request.AttestationId == Guid.Empty || request.AttestationId != receipt.AttestationId
            || projection.AccountingEventId != eventId || projection.ProjectionId != receipt.ProjectionId
            || !PayloadEquals(roleBinding, lease.Declaration.AccountRoleBinding)
            || candidate.RulePackReference is not { } rules || rules.RulePackId != roleBinding.RulePackId
            || rules.RulePackVersion != roleBinding.RulePackVersion || rules.SelectedRuleId != roleBinding.SelectedRuleId
            || rules.SelectedRuleVersion != roleBinding.SelectedRuleVersion
            || projection.LedgerBookId != bookId || projection.PeriodId != candidate.PeriodId
            || projection.TotalDebits != receipt.GrossImpactUsd || projection.TotalCredits != receipt.GrossImpactUsd)
            throw new InvalidOperationException("The native preparer receipt does not authorize this exact regular USD cash-dividend candidate.");

        var candidateFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidate);
        await lease.EnsureCurrentAsync(actor, bookId, candidate.PeriodId, candidateFingerprint,
            projection.PostingIntentHash, ct).ConfigureAwait(false);
        var authority = await ResolveAssetPostingAuthorityAsync(candidate, AssetAccountingEventKindDto.CorporateAction, ct).ConfigureAwait(false);
        var drafted = authority.Drafted.Projection;
        if (drafted.SpineVersion != projection.SpineVersion || drafted.EventVersion != projection.AccountingEventVersion
            || drafted.DraftedCandidateFingerprint != projection.DraftedCandidateFingerprint
            || drafted.Correction is not null || drafted.Direction != Meridian.Contracts.Accounting.Lots.OpenLotDirection.Long
            || drafted.DraftedLotMutation is not null || drafted.CorporateAction is not null
            || drafted.CapitalReturn is not null || drafted.FactorPaydown is not null)
            throw new InvalidOperationException("The retained Drafted event is not the exact non-lot cash-dividend projection.");
        await lease.EnsureCanonicalInstrumentAsync(drafted.DraftedCandidate!.InstrumentSymbol,
            drafted.Scope.SecurityId, drafted.Scope.ExpectedSecurityVersion,
            drafted.DraftedCandidate.AccountingTimestamp, ct).ConfigureAwait(false);
        var decision = new AssetAccountingPreparerAttestationDto(receipt, bookId, candidate.PeriodId,
            request.TenantId, request.CompanyId, eventId, drafted.EventVersion,
            candidateFingerprint, projection.PostingIntentHash);
        var decisionIssues = AssetAccountingPreparerAttestationDto.Validate(decision, bookId, candidate.PeriodId,
            eventId, drafted.EventVersion, drafted.Scope.TenantId, drafted.Scope.CompanyId, actor,
            drafted.Currency, drafted.EventAmount);
        var preparedAt = drafted.Stages.Single(stage => stage.Stage == AssetAccountingLifecycleStageDto.Drafted).AttestedAtUtc;
        if (decisionIssues.Count != 0 || receipt.AttestedAtUtc < preparedAt || receipt.AttestedAtUtc > DateTimeOffset.UtcNow
            || drafted.RetainedEvidence.Any(evidence => evidence.RetainedAtUtc > receipt.AttestedAtUtc || evidence.ReviewedAtUtc > receipt.AttestedAtUtc))
            throw new InvalidOperationException("Cash-dividend preparer authority, scope or evidence chronology is invalid.");

        var book = await journalStore.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The native ledger book was not found.");
        if (book.BaseCurrency != CorporateActionCashDividendAttestationPolicy.Currency || book.AccountingBasis != candidate.AccountingBasis)
            throw new InvalidOperationException("The attested cash dividend requires the exact USD accounting book and basis.");
        var rebuilt = await BuildCandidateWriteAsync(candidate, authority, ct).ConfigureAwait(false);
        rebuilt = AssetAccountingCandidateCanonicalizer.Canonicalize(rebuilt, bookId, eventId,
            drafted.EventVersion, drafted.SpineVersion);
        ValidateRebuiltAssetCandidate(authority, rebuilt);
        if (rebuilt.Candidate.HasBlockingIssues || rebuilt.Write is not { } write
            || write.PostingCommand is not { ApprovalState: AccountingPostingApprovalStateDto.Pending, ApprovalId: null } pending)
            throw new InvalidOperationException("The authoritative retained candidate is blocked or does not contain an unapproved pending posting command.");
        EnsureWriteLedgerBookScope(write, bookId);
        if (rebuilt.Candidate.GeneratedPostingLines.Count != 2 || write.Entry.Lines.Count != 2
            || rebuilt.Candidate.GeneratedPostingLines.Count(line => line.AccountPath == roleBinding.DividendReceivableAccount
                && line.Side == AccountingTemplateLineSideDto.Debit && line.Amount == receipt.GrossImpactUsd && line.Currency == "USD") != 1
            || rebuilt.Candidate.GeneratedPostingLines.Count(line => line.AccountPath == roleBinding.DividendIncomeAccount
                && line.Side == AccountingTemplateLineSideDto.Credit && line.Amount == receipt.GrossImpactUsd && line.Currency == "USD") != 1)
            throw new InvalidOperationException("The current authoritative rule does not produce exactly the declared receivable debit and dividend-income credit.");
        var existing = await FindExistingPostingAsync(journalStore, bookId, eventId, ct).ConfigureAwait(false);
        var postedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;
        var command = pending with
        {
            Actor = actor,
            PostingDate = postedAt,
            AggregateId = bookId,
            LedgerBookId = bookId,
            SourceEventId = eventId,
            CorrelationId = ResolveCorrelationId(request.CorrelationId, pending.CorrelationId, candidate.CorrelationId),
            CausationId = eventId,
            ApprovalState = AccountingPostingApprovalStateDto.PreparerAttested,
            ApprovalId = null,
            CashDividendAttestation = decision,
            ActionOrigin = request.ActionOrigin,
            OperatorRationale = reason
        };
        var attestedWrite = BindAssetPostingIntentToJournal(write with
        {
            AggregateId = bookId,
            CommandId = command.CommandId,
            CorrelationId = command.CorrelationId,
            SourceEventId = eventId,
            LedgerBookId = bookId,
            PostingCommand = command
        }, authority);
        attestedWrite = AccountingPostingCommandValidator.NormalizeAndValidate(attestedWrite,
            requirePostingCommand: true, requireExpectedVersion: true, allowPreparerAttestation: true);

        if (existing is not null)
        {
            if (existing.Entry.JournalEntryId != attestedWrite.Entry.JournalEntryId
                || existing.Entry.Metadata.PreparerAttestation is not { } prior
                || !PayloadEquals(prior, decision)
                || !RetainedPostingEquivalence.Matches(existing, attestedWrite, out _))
                throw new InvalidOperationException("The retained journal is not an exact replay of this native preparer decision.");
            var retainedImpact = BuildJournalImpact(existing, book.BaseCurrency);
            var latest = await lease.GetLatestSpineAsync(eventStore, eventId, drafted.EventVersion, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The retained native event spine disappeared.");
            var expectedStages = CashDividendStages(drafted, receipt, actor, existing.CreatedAt);
            if (!PayloadEquals(latest.Projection.PreparerAttestation, decision)
                || !HasStage(latest.Projection, AssetAccountingLifecycleStageDto.PreparerAttested)
                || !HasStage(latest.Projection, AssetAccountingLifecycleStageDto.Posted))
                throw new InvalidOperationException("The retained cash-dividend journal has no exact preparer-attested Posted spine outcome.");
            ValidatePostedProjectionOutcome(latest.Projection, retainedImpact, null, expectedStages);
            return new(rebuilt.Candidate with { PostingCommand = command }, BuildPostedResult(existing, bookId), true)
            { JournalImpact = retainedImpact, LifecycleStages = expectedStages };
        }

        var period = await journalStore.GetPeriodAsync(write.PeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The native ledger period was not found.");
        if (period.LedgerBookId != bookId || period.Status != "Open" || candidate.ExpectedPeriodVersion != period.Version)
            throw new InvalidOperationException("Attested cash dividends require the exact current open period and retained period version.");
        EnsureAccountingPeriodAcceptsPosting(attestedWrite, period);
        var current = await lease.GetLatestSpineAsync(eventStore, eventId, drafted.EventVersion, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The native Drafted spine disappeared before attestation.");
        if (!PayloadEquals(current.Projection, drafted) || current.CanonicalFingerprint != authority.Drafted.CanonicalFingerprint)
            throw new InvalidOperationException("The retained Drafted spine changed before the native preparer posting.");
        var preparerStage = CashDividendStages(drafted, receipt, actor, postedAt)[0];
        var attestedProjection = drafted with
        {
            SpineVersion = drafted.SpineVersion + 1,
            Stages = [.. drafted.Stages, preparerStage],
            PreparerAttestation = decision
        };
        await lease.AppendSpineAsync(eventStore, attestedProjection, drafted.SpineVersion,
            drafted.Scope.ExpectedBookPositionVersion, ct).ConfigureAwait(false);
        await lease.EnsureCurrentAsync(actor, bookId, candidate.PeriodId, candidateFingerprint,
            projection.PostingIntentHash, ct).ConfigureAwait(false);
        await lease.AppendJournalAsync(journalStore, attestedWrite, ct).ConfigureAwait(false);
        var posted = await lease.GetJournalAsync(journalStore, attestedWrite.Entry.JournalEntryId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The native journal was not retained in the posting transaction.");
        var impact = BuildJournalImpact(posted, book.BaseCurrency);
        var stages = CashDividendStages(drafted, receipt, actor, posted.CreatedAt);
        var postedProjection = attestedProjection with
        {
            SpineVersion = attestedProjection.SpineVersion + 1,
            Stages = [.. attestedProjection.Stages, stages[1]],
            PostedJournalImpact = impact
        };
        await lease.AppendSpineAsync(eventStore, postedProjection, attestedProjection.SpineVersion,
            drafted.Scope.ExpectedBookPositionVersion, ct).ConfigureAwait(false);
        return new(rebuilt.Candidate with { PostingCommand = command }, BuildPostedResult(posted, bookId), false)
        { JournalImpact = impact, LifecycleStages = stages };
    }

    private static IReadOnlyList<AssetAccountingStageEvidenceDto> CashDividendStages(
        AssetAccountingEventSpineDto drafted, CorporateActionCashDividendAttestationDto receipt,
        string actor, DateTimeOffset postedAt) =>
        [
            new(AssetAccountingLifecycleStageDto.PreparerAttested, receipt.AttestedAtUtc, actor,
                drafted.RetainedEvidence, PreparerAttestationApprovalBoundary.ReceiptReference(receipt.AttestationId), receipt.Reason),
            new(AssetAccountingLifecycleStageDto.Posted, postedAt, actor,
                drafted.RetainedEvidence, drafted.EventId.ToString("D"))
        ];
}
