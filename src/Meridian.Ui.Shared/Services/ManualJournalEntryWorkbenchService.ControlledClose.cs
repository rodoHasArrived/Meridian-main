using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    /// <summary>Reviewer attests the exact approved economics settle or close retained obligations.</summary>
    public async Task<ControlledCloseAuthorizationDto> AuthorizeControlledCloseAsync(
        ControlledCloseAuthorizationRequestDto request, string actor, CancellationToken ct = default)
    {
        var store = _controlledCloseAuthorizations
            ?? throw new InvalidOperationException("Controlled-close authorization storage is unavailable.");
        var journalStore = _journalStore ?? throw new InvalidOperationException("The authoritative ledger is unavailable.");
        var draft = await _draftStore.GetAsync(request.FundProfileId, request.JournalEntryId, ct,
            request.TenantId, request.CompanyId).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The journal draft was not found in the authorized scope.");
        if (draft.Version != request.DraftVersion || draft.Status != ManualJournalEntryStatusDto.Approved
            || string.IsNullOrWhiteSpace(draft.ApprovalId) || string.IsNullOrWhiteSpace(draft.ApprovedBy)
            || draft.ApprovedAtUtc is null || draft.ValidationIssues.Any(item => item.Severity == AccountingConfigurationValidationSeverityDto.Critical))
            throw new InvalidOperationException("Controlled close requires the exact approved journal version with no critical validation issues.");
        if (string.Equals(actor.Trim(), draft.PreparedBy.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The preparer cannot authorize their own controlled-close exception.");
        if (string.IsNullOrWhiteSpace(request.SourceObligationEvidence) || string.IsNullOrWhiteSpace(request.Reason)
            || !draft.EvidenceLinks.Contains(request.SourceObligationEvidence, StringComparer.Ordinal))
            throw new InvalidOperationException("The source obligation evidence must be retained on the approved journal before authorization.");
        var bookId = draft.LedgerBookId ?? throw new InvalidOperationException("A durable ledger book is required.");
        if (!Guid.TryParse(draft.PeriodId, out var periodId)) throw new InvalidOperationException("A durable period is required.");
        var book = await journalStore.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The ledger book was not found.");
        var period = await journalStore.GetPeriodAsync(periodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The ledger period was not found.");
        if (period.LedgerBookId != bookId || book.FundProfileId != draft.FundProfileId)
            throw new InvalidOperationException("The approved draft book scope does not match the authoritative period.");
        var lifecycleAuthority = _lifecycleCloseAuthority ?? throw new InvalidOperationException("Lifecycle review authority is unavailable.");
        var lifecycle = await lifecycleAuthority.CheckAsync(book, draft.TenantId, ct).ConfigureAwait(false);
        await using var lifecycleLease = lifecycle.Lease;
        if (lifecycle.Decisions.Any(item => !item.Allowed))
            throw new InvalidOperationException("Resolve lifecycle baseline and scope blockers before controlled-close review.");
        var lifecycleRevisions = lifecycle.Decisions.Where(item => item.Code.StartsWith("baseline:", StringComparison.Ordinal)
            && item.EvidenceRevision is not null).ToDictionary(item => item.Code["baseline:".Length..], item => item.EvidenceRevision!, StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var lifecycleRequest = new JournalEntryLifecycleActionRequestDto(draft.JournalEntryId, draft.FundProfileId,
            JournalEntryLifecycleActionDto.Post, actor, draft.Version, request.Reason,
            EvidenceLinks: draft.EvidenceLinks, ActionOrigin: OperationsActionOriginDto.HumanOperator);
        var command = BuildManualPostingCommand(draft, lifecycleRequest, bookId, periodId, period.Version, draft.EvidenceLinks, now);
        var write = await BuildManualJournalEntryWriteAsync(draft, book, periodId, command, draft.EvidenceLinks, now, ct).ConfigureAwait(false);
        if (request.Operation == Meridian.Contracts.FundStructure.EntityLifecycleOperationDto.Correction)
        {
            if (command.Intent is not (AccountingPostingIntentDto.Reversal or AccountingPostingIntentDto.Rebook or AccountingPostingIntentDto.Restatement or AccountingPostingIntentDto.Adjustment)
                || command.SourceJournalEntryId is not { } originalId)
                throw new InvalidOperationException("A correction exception requires typed correction intent and retained source-journal lineage.");
            var originals = await journalStore.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: bookId, JournalEntryId: originalId), ct).ConfigureAwait(false);
            if (originals.Count != 1)
                throw new InvalidOperationException("The correction source must be a retained journal in this exact ledger book.");
        }
        var authorization = new ControlledCloseAuthorizationDto(Guid.NewGuid(), draft.JournalEntryId, bookId,
            draft.FundProfileId, draft.Version, draft.TenantId, request.Operation, ControlledCloseFingerprint.Compute(write),
            draft.ApprovalId, request.SourceObligationEvidence, request.Reason.Trim(), actor.Trim(), now, command.SourceJournalEntryId, lifecycleRevisions);
        return await store.SaveCloseAuthorizationAsync(authorization, ct).ConfigureAwait(false);
    }
}
