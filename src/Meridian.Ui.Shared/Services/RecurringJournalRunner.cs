using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>Runs under the existing scheduled worker and admits only retained human-review drafts.</summary>
public sealed class RecurringJournalRunner(
    IRecurringJournalStore store,
    AutomatedJournalDraftIntakeService intake,
    IManualJournalEntryDraftStore drafts,
    IRecurringJournalPeriodAuthority periods) : IRecurringJournalQueueSource
{
    public async Task<IReadOnlyList<AutomatedJournalScheduledRunResult>> RunDueAsync(
        DateTimeOffset now, string? tenant = null, string? company = null,
        bool scopeSpecified = false, CancellationToken ct = default)
    {
        // A process-independent lease spans the retained claim, intake and completion. A crash
        // releases the lease, but preserves the claim and deterministic downstream draft identity.
        await using var session = await store.OpenSessionAsync(ct).ConfigureAwait(false);
        var results = new List<AutomatedJournalScheduledRunResult>();
        foreach (var definition in session.CurrentSchedules.Where(s => !scopeSpecified ||
                     (Same(s.Scope.TenantId, tenant) && Same(s.Scope.CompanyId, company))))
        {
            var template = session.GetCurrentTemplate(definition.Schedule.TemplateId);
            var schedule = definition.Schedule.ToSchedule();
            for (var index = 0; ; index++)
            {
                ct.ThrowIfCancellationRequested();
                var date = schedule.EffectiveDateFor(index);
                var instant = schedule.EffectiveAtUtcFor(index);
                if (instant > now || (schedule.EndsOn is { } end && date > end))
                    break;
                var key = RecurringJournalIdentity.OccurrenceKey(schedule.ScheduleId, date);
                RecurringOccurrenceRecord claim;
                try
                {
                    claim = await session.ClaimAsync(schedule.ScheduleId, date, definition.Version,
                        template.Version, now, ct).ConfigureAwait(false);
                }
                catch (RecurringJournalDefinitionChangedException ex)
                {
                    var stale = session.Occurrences.Single(o => o.OccurrenceKey == key);
                    results.Add(Result(stale, instant, ex.Message));
                    continue;
                }
                using var authority = FundScopeTenantAuthority.Enter(definition.Scope.TenantId,
                    $"recurring-journal:{schedule.ScheduleId}");
                try
                {
                    var sourceIssue = RecurringJournalEvidenceGuard.ValidateSources(claim.ScheduleDefinition.Evidence);
                    if (sourceIssue is not null)
                        throw new InvalidOperationException(sourceIssue);
                    var period = await periods.ResolveAsync(definition.Scope, date, ct).ConfigureAwait(false);
                    if (!period.IsOpen)
                    {
                        claim = await session.SetOutcomeAsync(key, RecurringOccurrenceState.Blocked,
                            period.Blocker, period.PeriodId, period.LockOwner, period.ReopenPath, now, ct).ConfigureAwait(false);
                        results.Add(Result(claim, instant, period.Blocker));
                        continue;
                    }
                    var existing = await drafts.GetAsync(definition.Scope.FundProfileId, claim.DraftId, ct,
                        definition.Scope.TenantId, definition.Scope.CompanyId).ConfigureAwait(false);
                    if (existing is null && claim.History.Any(h => h.State == RecurringOccurrenceState.Drafted))
                        throw new InvalidOperationException("The retained occurrence draft is missing. Restore durable draft state; automatic recreation is refused.");
                    var prepared = BuildIntake(claim, period);
                    // On recovery the original period version remains in the retained draft;
                    // period reopen/version increments never rewrite that source provenance.
                    if (existing is not null)
                    {
                        var problem = RecurringJournalEvidenceGuard.Validate(existing);
                        if (problem is not null)
                            throw new InvalidOperationException(problem);
                        var retained = RecurringJournalEvidenceGuard.Deserialize(existing.RecurringJournalEvidenceJson!)!;
                        if (retained.ScheduleVersion != definition.Version || retained.TemplateVersion != template.Version ||
                            retained.ScheduleId != definition.ScheduleId || retained.TemplateId != template.TemplateId ||
                            retained.ScheduleDefinitionJson != RecurringJournalIdentity.SerializeSchedule(claim.ScheduleDefinition) ||
                            retained.TemplateDefinitionJson != RecurringJournalIdentity.SerializeTemplate(claim.TemplateDefinition))
                            throw new InvalidOperationException("The retained draft belongs to a different schedule or template definition.");
                        prepared = prepared with
                        {
                            Drafts = [prepared.Drafts[0] with
                        { Metadata = prepared.Drafts[0].Metadata with { Tags = new Dictionary<string, string>
                        {
                            [RecurringJournalEvidenceGuard.EvidenceTag] = existing.RecurringJournalEvidenceJson!,
                            [RecurringJournalEvidenceGuard.DigestTag] = existing.RecurringJournalEvidenceDigest!
                        } } }]
                        };
                    }
                    var admitted = await intake.IntakeDraftsAsync(prepared, ct).ConfigureAwait(false);
                    claim = await session.SetOutcomeAsync(key, RecurringOccurrenceState.Drafted, null,
                        period.PeriodId, null, null, now, ct).ConfigureAwait(false);
                    var ready = Result(claim, instant, null);
                    if (admitted.NeedsFixCount > 0 || admitted.Skipped.Any(s => !s.IsReadyDuplicate))
                        ready = ready with
                        {
                            State = AutomatedJournalScheduleStateDto.NeedsInvestigation,
                            Summary = "The retained recurring draft needs operator review or correction; no duplicate was created."
                        };
                    results.Add(ready);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
                {
                    claim = await session.SetOutcomeAsync(key, RecurringOccurrenceState.Blocked,
                        ex.Message, claim.PeriodId, claim.LockOwner, claim.ReopenPath, now, ct).ConfigureAwait(false);
                    results.Add(Result(claim, instant, ex.Message));
                }
            }
        }
        return results;
    }

    private static AutomatedJournalPreparedDraftIntakeRequest BuildIntake(
        RecurringOccurrenceRecord claim, RecurringJournalPeriodState period)
    {
        var definition = claim.ScheduleDefinition;
        var scope = definition.Scope;
        var schedule = definition.Schedule.ToSchedule();
        var instant = new DateTimeOffset(claim.EffectiveDate.ToDateTime(schedule.PostingTime), TimeSpan.Zero);
        var evidence = new RecurringJournalEvidence(claim.OccurrenceKey, claim.DraftId,
            definition.ScheduleId, definition.Version, claim.TemplateDefinition.TemplateId, claim.TemplateDefinition.Version,
            RecurringJournalIdentity.SerializeSchedule(definition), RecurringJournalIdentity.SerializeTemplate(claim.TemplateDefinition),
            scope.FundProfileId, scope.LedgerBookId, scope.EntityId, scope.TenantId, scope.CompanyId,
            scope.Currency, claim.EffectiveDate, period.PeriodId, period.Version, definition.Evidence);
        var json = RecurringJournalEvidenceGuard.Serialize(evidence);
        var key = $"recurring|{claim.OccurrenceKey}";
        var metadata = new JournalEntryMetadata(ActivityType: "RecurringJournal", LedgerBook: schedule.LedgerKey.LedgerBook,
            EffectiveDate: claim.EffectiveDate, IdempotencyKey: key, EvidenceReferences: definition.Evidence,
            Tags: new Dictionary<string, string>
            {
                [RecurringJournalEvidenceGuard.EvidenceTag] = json,
                [RecurringJournalEvidenceGuard.DigestTag] = Sha256Digest.ComputeUtf8(json)
            });
        var instance = claim.TemplateDefinition.Template.Instantiate(new JournalTemplateInstantiation(instant,
            schedule.Parameters, schedule.Dimensions, metadata, schedule.Description));
        var journalEvent = new AutomatedJournalEvent(AutomatedJournalEventKind.RecurringTemplate, "RECURRING",
            instance.Lines.Sum(l => l.debit), instant, Description: instance.Description,
            SourceEventId: claim.OccurrenceKey, EffectiveDate: claim.EffectiveDate, IdempotencyKey: key,
            EvidenceReferences: definition.Evidence);
        return new(scope.FundProfileId, scope.Currency,
            [new AutomatedJournalDraft(journalEvent, instance.Description, instance.Lines, instance.Metadata)],
            definition.RegisteredBy, scope.LedgerBookId, period.PeriodId, scope.EntityId, scope.TenantId,
            scope.CompanyId, RecurringJournalEntryId: claim.DraftId);
    }

    public async Task<RecurringJournalQueueDto> GetQueueAsync(string fundProfileId, Guid ledgerBookId,
        string entityId, CancellationToken ct = default, string? tenantId = null, string? companyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        await using var session = await store.OpenSessionAsync(ct).ConfigureAwait(false);
        var rows = new List<RecurringJournalOccurrenceDto>();
        foreach (var item in session.Occurrences.Where(o => Same(o.ScheduleDefinition.Scope.FundProfileId, fundProfileId) &&
                     o.ScheduleDefinition.Scope.LedgerBookId == ledgerBookId && Same(o.ScheduleDefinition.Scope.EntityId, entityId) &&
                     Same(o.ScheduleDefinition.Scope.TenantId, tenantId) && Same(o.ScheduleDefinition.Scope.CompanyId, companyId)))
        {
            var draft = await drafts.GetAsync(fundProfileId, item.DraftId, ct, tenantId, companyId).ConfigureAwait(false);
            var reasons = new List<string>();
            if (item.Reason is not null)
                reasons.Add(item.Reason);
            if (draft is null && item.History.Any(h => h.State == RecurringOccurrenceState.Drafted))
                reasons.Add("Retained draft state is unavailable; restore it before continuing.");
            rows.Add(new(item.OccurrenceKey, item.ScheduleDefinition.ScheduleId, item.ScheduleDefinition.Version,
                item.TemplateDefinition.TemplateId, item.TemplateDefinition.Version, item.EffectiveDate,
                fundProfileId, ledgerBookId, entityId, item.PeriodId,
                reasons.Count > 0 && item.State == RecurringOccurrenceState.Drafted ? "Blocked" : item.State.ToString(),
                draft?.JournalEntryId, draft?.Status.ToString(), reasons,
                item.ScheduleDefinition.Evidence.Select(e => e.Uri).ToArray(), item.LockOwner, item.ReopenPath));
        }
        return new(fundProfileId, ledgerBookId, entityId, rows.OrderByDescending(r => r.EffectiveDate).ToArray());
    }

    private static AutomatedJournalScheduledRunResult Result(RecurringOccurrenceRecord claim, DateTimeOffset instant, string? reason)
        => new(claim.ScheduleDefinition.ScheduleId, claim.OccurrenceKey, instant,
            claim.State == RecurringOccurrenceState.Drafted ? AutomatedJournalScheduleStateDto.DraftReady : AutomatedJournalScheduleStateDto.Blocked,
            reason ?? "Recurring occurrence retained in the journal queue for human approval.",
            claim.State == RecurringOccurrenceState.Drafted ? [claim.DraftId] : [], reason is null ? [] : [reason]);
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
