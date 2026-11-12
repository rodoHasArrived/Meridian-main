using System.Data.Common;
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
    IRecurringJournalPeriodAuthority periods,
    IEnumerable<IRecurringJournalCalculation>? calculations = null) : IRecurringJournalQueueSource
{
    private readonly IReadOnlyDictionary<string, IRecurringJournalCalculation> _calculations =
        (calculations ?? []).ToDictionary(item => item.Kind, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a calculated template of this kind can be run by this host.</summary>
    public bool SupportsCalculation(string kind) => _calculations.ContainsKey(kind);

    /// <summary>The inputs a calculated template of this kind must state and does not.</summary>
    public IReadOnlyList<string> MissingCalculationInputs(JournalTemplateCalculation calculation)
        => _calculations.TryGetValue(calculation.Kind, out var implementation)
            ? implementation.RequiredInputs.Where(input => !calculation.Inputs.TryGetValue(input, out var value) || string.IsNullOrWhiteSpace(value)).ToArray()
            : [];

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
                // Kept once resolved so a blocked occurrence still names its period for close readiness.
                string? resolvedPeriodId = null;
                try
                {
                    // An empty window stays settled. An older runner might have retried it after
                    // definition restoration and saved a draft before recording Drafted.
                    if (claim.State == RecurringOccurrenceState.NoDraftRequired)
                    {
                        if (HasRetriedNoDraftOutcome(claim) && await drafts.GetAsync(definition.Scope.FundProfileId,
                                claim.DraftId, ct, definition.Scope.TenantId, definition.Scope.CompanyId).ConfigureAwait(false) is not null)
                            claim = claim with { State = RecurringOccurrenceState.Drafted };
                        results.Add(Result(claim, instant, null));
                        continue;
                    }

                    var sourceIssue = RecurringJournalEvidenceGuard.ValidateSources(claim.ScheduleDefinition.Evidence);
                    if (sourceIssue is not null)
                        throw new InvalidOperationException(sourceIssue);
                    var period = await periods.ResolveAsync(definition.Scope, date, ct).ConfigureAwait(false);
                    resolvedPeriodId = period.PeriodId;
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
                    // Amounts are calculated once, when the occurrence first becomes a draft. A
                    // retained draft is authoritative and is never recalculated on retry.
                    AutomatedJournalDraft? calculated = null;
                    if (claim.TemplateDefinition.Template.Calculation is { } calculation && existing is null)
                    {
                        _calculations.TryGetValue(calculation.Kind, out var dependent);
                        // Closing balances are not final until the effective day is over.
                        if (dependent is { ReadsClosingState: true } &&
                            now < new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
                            throw new InvalidOperationException(
                                $"This amount is measured at the close of {date:yyyy-MM-dd}; it is calculated once that day has ended.");
                        if (index > 0 && dependent is { DependsOnPostedHistory: true })
                            await RequirePreviousOccurrenceSettledAsync(session, definition, schedule.EffectiveDateFor(index - 1), ct).ConfigureAwait(false);
                        var outcome = await CalculateAsync(calculation, claim, period,
                            index == 0 ? period.StartDate : schedule.EffectiveDateFor(index - 1).AddDays(1), ct).ConfigureAwait(false);
                        if (outcome.Draft is null)
                        {
                            claim = await session.SetOutcomeAsync(key, RecurringOccurrenceState.NoDraftRequired,
                                outcome.Summary, period.PeriodId, null, null, now, ct).ConfigureAwait(false);
                            results.Add(Result(claim, instant, null));
                            continue;
                        }
                        calculated = outcome.Draft;
                    }
                    var prepared = BuildIntake(claim, period, calculated, existing);
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
                catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or DbException)
                {
                    claim = claim.State == RecurringOccurrenceState.NoDraftRequired
                        ? claim with { State = RecurringOccurrenceState.Blocked }
                        : await session.SetOutcomeAsync(key, RecurringOccurrenceState.Blocked,
                            ex.Message, claim.PeriodId ?? resolvedPeriodId, claim.LockOwner, claim.ReopenPath, now, ct).ConfigureAwait(false);
                    results.Add(Result(claim, instant, ex.Message));
                }
            }
        }
        return results;
    }

    /// <summary>
    /// A calculation that reads posted balances must not run while the previous occurrence's draft
    /// is still open: its amount is not in the ledger yet, so this occurrence would repeat it.
    /// </summary>
    private async Task RequirePreviousOccurrenceSettledAsync(IRecurringJournalSession session,
        RecurringScheduleDefinition definition, DateOnly previousDate, CancellationToken ct)
    {
        var previousKey = RecurringJournalIdentity.OccurrenceKey(definition.ScheduleId, previousDate);
        var previous = session.Occurrences.SingleOrDefault(item => item.OccurrenceKey == previousKey);
        var settledWithoutDraft = previous?.FindNoDraftOutcome() is not null;
        if (settledWithoutDraft && !HasRetriedNoDraftOutcome(previous!))
            return;
        string status;
        // A posted occurrence is marked Blocked once its period closes, so the retained draft's
        // own status decides, not the occurrence's current state.
        if (previous is null || (!settledWithoutDraft && previous.State != RecurringOccurrenceState.Drafted &&
                                 !previous.History.Any(static item => item.State == RecurringOccurrenceState.Drafted)))
        {
            status = previous is null ? "not yet processed" : previous.State.ToString();
        }
        else
        {
            var draft = await drafts.GetAsync(definition.Scope.FundProfileId, previous.DraftId, ct,
                definition.Scope.TenantId, definition.Scope.CompanyId).ConfigureAwait(false);
            if (draft is null && settledWithoutDraft)
                return;
            if (draft?.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked
                or ManualJournalEntryStatusDto.Rejected)
                return;
            // Marking a journal reversed or rebooked only opens its correction; the ledger still
            // holds the original amount until that correction posts.
            if (draft?.Status is ManualJournalEntryStatusDto.Reversed or ManualJournalEntryStatusDto.Rebooked)
            {
                var all = await drafts.ListAsync(definition.Scope.FundProfileId, definition.Scope.LedgerBookId, ct,
                    definition.Scope.TenantId, definition.Scope.CompanyId).ConfigureAwait(false);
                if (CorrectionIsPosted(draft, all))
                    return;
                throw new InvalidOperationException(
                    $"The {previousDate:yyyy-MM-dd} occurrence was reversed or rebooked and its correction journal is not posted yet. This amount depends on it; post the correction first.");
            }
            status = draft is null ? "missing its retained draft" : $"a {draft.Status} draft";
        }

        throw new InvalidOperationException(
            $"The {previousDate:yyyy-MM-dd} occurrence is {status}. This amount depends on what it posts; post or reject it first.");
    }

    private async Task<RecurringJournalCalculationResult> CalculateAsync(JournalTemplateCalculation calculation,
        RecurringOccurrenceRecord claim, RecurringJournalPeriodState period, DateOnly? windowStart, CancellationToken ct)
    {
        if (!_calculations.TryGetValue(calculation.Kind, out var implementation))
            throw new InvalidOperationException($"No posting calculation '{calculation.Kind}' is registered on this host.");
        var definition = claim.ScheduleDefinition;
        var schedule = definition.Schedule.ToSchedule();
        var context = new RecurringJournalCalculationContext(definition.Scope, definition.ScheduleId, claim.EffectiveDate,
            windowStart, new DateTimeOffset(claim.EffectiveDate.ToDateTime(schedule.PostingTime), TimeSpan.Zero),
            period.PeriodId, schedule.Parameters, calculation.Inputs, definition.Evidence);
        RecurringJournalCalculationResult outcome;
        try
        {
            outcome = await implementation.CalculateAsync(context, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is LedgerValidationException or NotSupportedException or FormatException or OverflowException
                                       or JsonException)
        {
            // Input and domain failures block this occurrence with their reason; they never stop
            // the remaining schedules from running.
            throw new InvalidOperationException($"Posting calculation '{calculation.Kind}' could not complete: {ex.Message}", ex);
        }
        if (outcome.Draft is { } draft && (draft.Lines.Count == 0 || !draft.IsBalanced || draft.TotalDebits <= 0m))
            throw new InvalidOperationException($"Posting calculation '{calculation.Kind}' produced an empty or unbalanced journal.");
        return outcome;
    }

    private static AutomatedJournalPreparedDraftIntakeRequest BuildIntake(
        RecurringOccurrenceRecord claim, RecurringJournalPeriodState period,
        AutomatedJournalDraft? calculated, ManualJournalEntryDraftDto? existing)
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
        var recurringTags = new Dictionary<string, string>
        {
            [RecurringJournalEvidenceGuard.EvidenceTag] = json,
            [RecurringJournalEvidenceGuard.DigestTag] = Sha256Digest.ComputeUtf8(json)
        };
        var metadata = new JournalEntryMetadata(ActivityType: "RecurringJournal", LedgerBook: schedule.LedgerKey.LedgerBook,
            EffectiveDate: claim.EffectiveDate, IdempotencyKey: key, EvidenceReferences: definition.Evidence,
            Tags: recurringTags);
        if (claim.TemplateDefinition.Template.Calculation is { } calculation)
        {
            // A calculated draft keeps its own accounts, activity and fund-event identity, but its
            // occurrence identity, dates and source evidence are always the retained claim's.
            var description = calculated?.Description ?? existing?.Memo ?? schedule.Description ?? claim.TemplateDefinition.Template.Name;
            var tags = new Dictionary<string, string>(calculated?.Metadata.Tags ?? new Dictionary<string, string>())
            {
                ["recurring.calculation"] = calculation.Kind
            };
            foreach (var (tag, value) in recurringTags)
                tags[tag] = value;
            var calculatedMetadata = (calculated?.Metadata ?? metadata) with
            {
                LedgerBook = schedule.LedgerKey.LedgerBook,
                EffectiveDate = claim.EffectiveDate,
                IdempotencyKey = key,
                EvidenceReferences = definition.Evidence,
                Tags = tags
            };
            var calculatedEvent = (calculated?.Event ?? new AutomatedJournalEvent(AutomatedJournalEventKind.RecurringTemplate,
                "RECURRING", existing?.TotalDebits ?? 0m, instant)) with
            {
                Timestamp = instant,
                Description = description,
                SourceEventId = claim.OccurrenceKey,
                EffectiveDate = claim.EffectiveDate,
                IdempotencyKey = key,
                EvidenceReferences = definition.Evidence
            };
            return new(scope.FundProfileId, scope.Currency,
                // The schedule's dimensions fill whatever the calculation left unset on a line.
                [new AutomatedJournalDraft(calculatedEvent, description,
                    (calculated?.Lines ?? []).Select(line => (line.account, line.debit, line.credit, WithScheduleDimensions(line.dimensions, schedule.Dimensions))).ToArray(),
                    calculatedMetadata.Normalize())],
                definition.RegisteredBy, scope.LedgerBookId, period.PeriodId, scope.EntityId, scope.TenantId,
                scope.CompanyId, RecurringJournalEntryId: claim.DraftId);
        }
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
        IReadOnlyList<ManualJournalEntryDraftDto>? allDrafts = null;
        foreach (var item in session.Occurrences.Where(o => Same(o.ScheduleDefinition.Scope.FundProfileId, fundProfileId) &&
                     o.ScheduleDefinition.Scope.LedgerBookId == ledgerBookId && Same(o.ScheduleDefinition.Scope.EntityId, entityId) &&
                     Same(o.ScheduleDefinition.Scope.TenantId, tenantId) && Same(o.ScheduleDefinition.Scope.CompanyId, companyId)))
        {
            var draft = await drafts.GetAsync(fundProfileId, item.DraftId, ct, tenantId, companyId).ConfigureAwait(false);
            var settled = item.FindNoDraftOutcome();
            var state = item.State;
            var reason = item.Reason;
            var periodId = item.PeriodId;
            if (settled is not null)
            {
                state = draft is null ? RecurringOccurrenceState.NoDraftRequired : RecurringOccurrenceState.Drafted;
                reason = draft is null ? settled.Reason : null;
                periodId = draft?.PeriodId ?? settled.PeriodId ?? item.PeriodId;
            }
            var reasons = new List<string>();
            if (reason is not null)
                reasons.Add(reason);
            if (draft is null && item.History.Any(h => h.State == RecurringOccurrenceState.Drafted))
                reasons.Add("Retained draft state is unavailable; restore it before continuing.");
            // A reversed or rebooked occurrence is settled only once its correction is posted.
            if (draft?.Status is ManualJournalEntryStatusDto.Reversed or ManualJournalEntryStatusDto.Rebooked)
            {
                allDrafts ??= await drafts.ListAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId).ConfigureAwait(false);
                if (!CorrectionIsPosted(draft, allDrafts))
                    reasons.Add("This occurrence's journal was reversed or rebooked and the correction journal is not posted yet.");
            }
            rows.Add(new(item.OccurrenceKey, item.ScheduleDefinition.ScheduleId, item.ScheduleDefinition.Version,
                item.TemplateDefinition.TemplateId, item.TemplateDefinition.Version, item.EffectiveDate,
                fundProfileId, ledgerBookId, entityId, periodId,
                reasons.Count > 0 && state == RecurringOccurrenceState.Drafted ? "Blocked" : state.ToString(),
                draft?.JournalEntryId, draft?.Status.ToString(), reasons,
                item.ScheduleDefinition.Evidence.Select(e => e.Uri).ToArray(), item.LockOwner, item.ReopenPath));
        }
        return new(fundProfileId, ledgerBookId, entityId, rows.OrderByDescending(r => r.EffectiveDate).ToArray());
    }

    private static bool HasRetriedNoDraftOutcome(RecurringOccurrenceRecord occurrence)
        => occurrence.History.SkipWhile(static item => item.State != RecurringOccurrenceState.NoDraftRequired)
            .Any(static item => item.State is RecurringOccurrenceState.Claimed or RecurringOccurrenceState.Blocked
                or RecurringOccurrenceState.DefinitionChanged);

    private static LedgerLineDimensionSet? WithScheduleDimensions(LedgerLineDimensionSet? line, LedgerLineDimensionSet? schedule)
    {
        if (line is null || schedule is null)
            return line ?? schedule;
        var external = new Dictionary<string, string>(schedule.ExternalGlDimensions, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in line.ExternalGlDimensions)
            external[key] = value;
        return line with
        {
            FundId = line.FundId ?? schedule.FundId,
            EntityId = line.EntityId ?? schedule.EntityId,
            SleeveId = line.SleeveId ?? schedule.SleeveId,
            StrategyId = line.StrategyId ?? schedule.StrategyId,
            InvestorId = line.InvestorId ?? schedule.InvestorId,
            CapitalAccountId = line.CapitalAccountId ?? schedule.CapitalAccountId,
            InstrumentId = line.InstrumentId ?? schedule.InstrumentId,
            TaxLotId = line.TaxLotId ?? schedule.TaxLotId,
            CostCenterId = line.CostCenterId ?? schedule.CostCenterId,
            CounterpartyId = line.CounterpartyId ?? schedule.CounterpartyId,
            ExternalGlDimensions = external,
            OrganizationId = line.OrganizationId ?? schedule.OrganizationId,
            PortfolioId = line.PortfolioId ?? schedule.PortfolioId,
            BookId = line.BookId ?? schedule.BookId,
            AccountId = line.AccountId ?? schedule.AccountId,
            CustomerId = line.CustomerId ?? schedule.CustomerId,
            VendorId = line.VendorId ?? schedule.VendorId,
            ProjectId = line.ProjectId ?? schedule.ProjectId,
            PositionId = line.PositionId ?? schedule.PositionId
        };
    }

    private static bool CorrectionIsPosted(ManualJournalEntryDraftDto corrected, IReadOnlyList<ManualJournalEntryDraftDto> all)
    {
        var current = corrected;
        for (var depth = 0; depth < 16; depth++)
        {
            var id = current.JournalEntryId;
            var next = current.Status == ManualJournalEntryStatusDto.Reversed
                ? all.FirstOrDefault(item => item.ReversalOfJournalEntryId == id && item.Status != ManualJournalEntryStatusDto.Rejected)
                : all.FirstOrDefault(item => item.RebookedFromJournalEntryId == id && item.Status != ManualJournalEntryStatusDto.Rejected);
            if (next is null)
                return false;
            if (next.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked)
                return true;
            if (next.Status is not (ManualJournalEntryStatusDto.Reversed or ManualJournalEntryStatusDto.Rebooked))
                return false;
            current = next;
        }

        return false;
    }

    private static AutomatedJournalScheduledRunResult Result(RecurringOccurrenceRecord claim, DateTimeOffset instant, string? reason)
        => claim.State == RecurringOccurrenceState.NoDraftRequired
            ? new(claim.ScheduleDefinition.ScheduleId, claim.OccurrenceKey, instant, AutomatedJournalScheduleStateDto.NoDraftRequired,
                claim.Reason ?? "The calculation produced nothing to post for this occurrence.", [], [])
            : new(claim.ScheduleDefinition.ScheduleId, claim.OccurrenceKey, instant,
                claim.State == RecurringOccurrenceState.Drafted ? AutomatedJournalScheduleStateDto.DraftReady : AutomatedJournalScheduleStateDto.Blocked,
                reason ?? "Recurring occurrence retained in the journal queue for human approval.",
                claim.State == RecurringOccurrenceState.Drafted ? [claim.DraftId] : [], reason is null ? [] : [reason]);
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
