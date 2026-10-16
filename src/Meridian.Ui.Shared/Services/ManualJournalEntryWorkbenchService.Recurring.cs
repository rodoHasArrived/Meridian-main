using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private readonly IRecurringJournalStore? _recurringStore;

    private async Task<T> WithRecurringRegistryLeaseAsync<T>(string fund, Guid journalEntryId,
        string? tenant, string? company, Func<IRecurringJournalSession?, Task<T>> execute, CancellationToken ct)
    {
        var candidates = (await _draftStore.ListAsync(NormalizeFundProfileId(fund), ct: ct,
                tenantId: tenant, companyId: company).ConfigureAwait(false))
            .Where(draft => draft.JournalEntryId == journalEntryId).Take(2).ToArray();
        if (candidates.Length > 1)
            throw new InvalidOperationException("The manual journal scope is ambiguous; supply its tenant and company before retrying.");
        var draft = candidates.SingleOrDefault();
        if (draft is null || !IsRecurringDraft(draft))
            return await execute(null).ConfigureAwait(false);

        var store = _recurringStore ?? throw new InvalidOperationException(
            "The durable recurring journal registry is required before a recurring lifecycle command.");
        // Always acquire the recurring registry before the manual mutation lease, matching the
        // worker's claim -> intake lock order. Keep it through command recovery and all effects.
        await using var session = await store.OpenSessionAsync(ct).ConfigureAwait(false);
        draft = await _draftStore.GetAsync(draft.FundProfileId, draft.JournalEntryId, ct,
            draft.TenantId, draft.CompanyId).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained recurring journal draft is unavailable.");
        await RequireCurrentRecurringClaimAsync(session, draft, ct).ConfigureAwait(false);
        return await execute(session).ConfigureAwait(false);
    }

    private async Task ValidateRecurringBeforeRecoveryAsync(IRecurringJournalSession? session, string fund,
        Guid journalEntryId, string? tenant, string? company, IReadOnlyList<ManualJournalMutationIntent> pending,
        CancellationToken ct)
    {
        var draft = await _draftStore.GetAsync(NormalizeFundProfileId(fund), journalEntryId, ct, tenant, company).ConfigureAwait(false);
        var recurringPending = pending.Where(intent => PendingMatchesScope(intent, RecoveryScope(fund, tenant, company), journalEntryId))
            .SelectMany(intent => intent.After.Concat(intent.Before.OfType<ManualJournalEntryDraftDto>()))
            .Any(IsRecurringDraft);
        if ((draft is not null && IsRecurringDraft(draft)) || recurringPending)
        {
            if (session is null)
                throw new InvalidOperationException("Recurring journal state changed while awaiting its command lease; restore any missing retained draft and retry with the durable registry.");
            if (draft is null)
                throw new InvalidOperationException("The retained recurring journal draft is unavailable.");
            await RequireCurrentRecurringClaimAsync(session, draft, ct).ConfigureAwait(false);
        }
    }

    private static bool IsRecurringDraft(ManualJournalEntryDraftDto draft)
        => draft.RequiresRecurringJournalEvidence || draft.RecurringJournalEvidenceJson is not null ||
           RecurringJournalEvidenceGuard.IsRecurring(draft.TreasuryContext?.IdempotencyKey);

    private async Task RequireCurrentRecurringClaimAsync(IRecurringJournalSession session,
        ManualJournalEntryDraftDto draft, CancellationToken ct)
    {
        if (RecurringJournalEvidenceGuard.Validate(draft) is { } invalid)
            throw new InvalidOperationException(invalid);
        var evidence = RecurringJournalEvidenceGuard.Deserialize(draft.RecurringJournalEvidenceJson!)!;
        var claim = session.Occurrences.SingleOrDefault(item => item.OccurrenceKey == evidence.OccurrenceKey)
            ?? throw new InvalidOperationException("The retained recurring occurrence is unavailable.");
        var scope = claim.ScheduleDefinition.Scope;
        if (claim.DraftId != evidence.JournalEntryId || claim.EffectiveDate != evidence.EffectiveDate ||
            claim.ScheduleDefinition.ScheduleId != evidence.ScheduleId ||
            claim.ScheduleDefinition.Version != evidence.ScheduleVersion ||
            claim.TemplateDefinition.TemplateId != evidence.TemplateId ||
            claim.TemplateDefinition.Version != evidence.TemplateVersion ||
            RecurringJournalIdentity.SerializeSchedule(claim.ScheduleDefinition) != evidence.ScheduleDefinitionJson ||
            RecurringJournalIdentity.SerializeTemplate(claim.TemplateDefinition) != evidence.TemplateDefinitionJson ||
            scope.FundProfileId != evidence.FundProfileId || scope.LedgerBookId != evidence.LedgerBookId ||
            scope.EntityId != evidence.EntityId || scope.TenantId != evidence.TenantId ||
            scope.CompanyId != evidence.CompanyId || scope.Currency != evidence.Currency ||
            (claim.PeriodId is not null && claim.PeriodId != evidence.PeriodId) ||
            !claim.ScheduleDefinition.Evidence.SequenceEqual(evidence.SourceEvidence))
            throw new InvalidOperationException("Recurring journal provenance differs from its retained occurrence.");
        var currentSchedule = session.GetCurrentSchedule(evidence.ScheduleId);
        var currentTemplate = session.GetCurrentTemplate(evidence.TemplateId);
        if (currentSchedule.Version != evidence.ScheduleVersion || currentTemplate.Version != evidence.TemplateVersion ||
            currentSchedule.ContentHash != claim.ScheduleDefinition.ContentHash ||
            currentTemplate.ContentHash != claim.TemplateDefinition.ContentHash)
            throw new RecurringJournalDefinitionChangedException(
                "The recurring schedule or template changed. Restore the retained definitions through governed correction before continuing.");

        // A correction retains the original provenance, but it must also be connected by the
        // actual server-retained source/target relationship, not only client-supplied ids.
        var seen = new HashSet<Guid>();
        var current = draft;
        while (current.JournalEntryId != claim.DraftId)
        {
            if (!seen.Add(current.JournalEntryId) || seen.Count > 32)
                throw new InvalidOperationException("Recurring correction ancestry is cyclic or exceeds the supported depth.");
            var reversal = current.ReversalOfJournalEntryId;
            var rebook = current.RebookedFromJournalEntryId;
            if ((reversal is null) == (rebook is null))
                throw new InvalidOperationException("Recurring correction ancestry is incomplete.");
            var sourceId = reversal ?? rebook!.Value;
            var source = await _draftStore.GetAsync(current.FundProfileId, sourceId, ct,
                current.TenantId, current.CompanyId).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The retained source for this recurring correction is unavailable.");
            if (RecurringJournalEvidenceGuard.Validate(source) is not null ||
                source.RecurringJournalEvidenceJson != draft.RecurringJournalEvidenceJson ||
                source.RecurringJournalEvidenceDigest != draft.RecurringJournalEvidenceDigest ||
                source.LedgerBookId != draft.LedgerBookId ||
                (reversal is not null
                    ? source.Reversal?.OriginalJournalEntryId != sourceId || source.Reversal.ReversalJournalEntryId != current.JournalEntryId
                    : source.Rebook?.OriginalJournalEntryId != sourceId || source.Rebook.RebookJournalEntryId != current.JournalEntryId))
                throw new InvalidOperationException("Recurring correction does not match its retained source and target relationship.");
            current = source;
        }
    }
}
