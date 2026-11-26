using Meridian.Contracts.Integrity;
using Meridian.Ledger;
using static Meridian.FinancialOperations.FundAdministration.RecurringJournalDocumentRules;

namespace Meridian.FinancialOperations.FundAdministration;

/// <summary>
/// An exclusive hold on one durable recurring journal document. The holder is the only writer until
/// it is disposed; <see cref="WriteAsync"/> must validate and durably replace the whole document.
/// </summary>
internal interface IRecurringJournalDocumentLease : IAsyncDisposable
{
    Task WriteAsync(RecurringJournalDocument document, CancellationToken ct);
}

/// <summary>
/// The shared session semantics for every recurring journal backend: definition versioning,
/// activation, deterministic occurrence claims and outcomes over one leased document.
/// </summary>
internal sealed class RecurringJournalDocumentSession(IRecurringJournalDocumentLease lease, RecurringJournalDocument initial) : IRecurringJournalSession
{
    private RecurringJournalDocument _document = initial;
    private bool _disposed;
    private bool _faulted;

    private void RequireUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted)
            throw new InvalidOperationException("The recurring journal write outcome is uncertain. Reopen the store before retrying.");
    }

    private RecurringJournalDocument Snapshot()
    {
        RequireUsable();
        return Copy(_document, RecurringJournalJsonContext.Default.RecurringJournalDocument);
    }

    public IReadOnlyList<RecurringTemplateDefinition> Templates => Snapshot().Templates;
    public IReadOnlyList<RecurringScheduleDefinition> Schedules => Snapshot().Schedules;
    public IReadOnlyList<RecurringScheduleDefinition> CurrentSchedules => _document.Schedules
        .Select(item => item.ScheduleId).Distinct(StringComparer.OrdinalIgnoreCase).Select(GetCurrentSchedule).ToArray();
    public IReadOnlyList<RecurringOccurrenceRecord> Occurrences => Snapshot().Occurrences;
    public IReadOnlyList<RecurringDefinitionActivation> Activations => Snapshot().Activations;

    public RecurringScheduleDefinition GetCurrentSchedule(string scheduleId)
    {
        RequireUsable();
        var activation = _document.Activations.LastOrDefault(item => item.ScheduleId is not null && Same(item.ScheduleId, scheduleId))
            ?? throw new InvalidOperationException("The recurring schedule has no active retained definition.");
        return Copy(_document.Schedules.Single(item => Same(item.ScheduleId, scheduleId) && item.Version == activation.ScheduleVersion),
            RecurringJournalJsonContext.Default.RecurringScheduleDefinition);
    }

    public RecurringTemplateDefinition GetCurrentTemplate(string templateId)
    {
        RequireUsable();
        var activation = _document.Activations.LastOrDefault(item => item.TemplateId is not null && Same(item.TemplateId, templateId))
            ?? throw new InvalidOperationException("The recurring template has no active retained definition.");
        return Copy(_document.Templates.Single(item => Same(item.TemplateId, templateId) && item.Version == activation.TemplateVersion),
            RecurringJournalJsonContext.Default.RecurringTemplateDefinition);
    }

    public async Task ActivateDefinitionsAsync(string scheduleId, int scheduleVersion, int templateVersion,
        string actor, string reason, DateTimeOffset now, CancellationToken ct = default)
    {
        RequireUsable();
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var schedule = _document.Schedules.SingleOrDefault(item => Same(item.ScheduleId, scheduleId) && item.Version == scheduleVersion)
            ?? throw new InvalidOperationException("The requested schedule version is not retained.");
        var template = _document.Templates.SingleOrDefault(item => Same(item.TemplateId, schedule.Schedule.TemplateId) && item.Version == templateVersion)
            ?? throw new InvalidOperationException("The requested template version is not retained.");
        // A calculated template has no fixed lines to plan; its terms are checked when it runs.
        if (template.Template.Calculation is null)
            _ = RecurringJournalPlanner.Plan(schedule.Schedule.ToSchedule(), template.Template, 1);
        await CommitAsync(_document with
        {
            Activations = [.. _document.Activations,
            new RecurringDefinitionActivation(schedule.ScheduleId, schedule.Version, template.TemplateId, template.Version,
                actor.Trim(), reason.Trim(), now.ToUniversalTime())]
        }, ct).ConfigureAwait(false);
    }

    private async Task CommitAsync(RecurringJournalDocument next, CancellationToken ct)
    {
        RequireUsable();
        if (next.Activations.Count > _document.Activations.Count)
            next = MarkDefinitionChanges(next, next.Activations[^1].ActivatedAtUtc);
        var copy = Copy(next, RecurringJournalJsonContext.Default.RecurringJournalDocument);
        try
        {
            await lease.WriteAsync(copy, ct).ConfigureAwait(false);
        }
        catch
        {
            // A post-rename flush can fail after the snapshot was committed. Do not let the
            // stale in-memory image overwrite that commit if a caller catches the exception.
            _faulted = true;
            throw;
        }
        _document = copy;
    }

    private static RecurringJournalDocument MarkDefinitionChanges(RecurringJournalDocument document, DateTimeOffset now)
    {
        var occurrences = document.Occurrences.Select(occurrence =>
        {
            var scheduleActivation = document.Activations.Last(item => item.ScheduleId is not null && Same(item.ScheduleId, occurrence.ScheduleDefinition.ScheduleId));
            var schedule = document.Schedules.Single(item => Same(item.ScheduleId, scheduleActivation.ScheduleId!) && item.Version == scheduleActivation.ScheduleVersion);
            var templateActivation = document.Activations.Last(item => item.TemplateId is not null && Same(item.TemplateId, schedule.Schedule.TemplateId));
            var template = document.Templates.Single(item => Same(item.TemplateId, templateActivation.TemplateId!) && item.Version == templateActivation.TemplateVersion);
            if ((schedule.ContentHash == occurrence.ScheduleDefinition.ContentHash && template.ContentHash == occurrence.TemplateDefinition.ContentHash)
                || occurrence.State == RecurringOccurrenceState.DefinitionChanged)
                return occurrence;
            const string reason = "The schedule or template definition changed. Explicitly restore the retained definitions before retrying this occurrence.";
            return occurrence with
            {
                State = RecurringOccurrenceState.DefinitionChanged,
                Reason = reason,
                UpdatedAtUtc = now,
                History = [.. occurrence.History, new RecurringOccurrenceTransition(RecurringOccurrenceState.DefinitionChanged,
                    now, reason, occurrence.PeriodId, occurrence.LockOwner, occurrence.ReopenPath)]
            };
        }).ToArray();
        return document with { Occurrences = occurrences };
    }

    public async Task<RecurringTemplateDefinition> RegisterTemplateAsync(JournalTemplate template, string actor,
        int expectedVersion, DateTimeOffset now, CancellationToken ct = default, RecurringJournalScope? scope = null)
    {
        var definition = PrepareTemplate(template, actor, expectedVersion, now, scope);
        await CommitAsync(_document with
        {
            Templates = [.. _document.Templates, definition],
            Activations = [.. _document.Activations, new RecurringDefinitionActivation(null, null, definition.TemplateId, definition.Version,
                actor.Trim(), "Registered template definition.", now.ToUniversalTime())]
        }, ct).ConfigureAwait(false);
        return Copy(definition, RecurringJournalJsonContext.Default.RecurringTemplateDefinition);
    }

    private RecurringTemplateDefinition PrepareTemplate(JournalTemplate template, string actor,
        int expectedVersion, DateTimeOffset now, RecurringJournalScope? scope)
    {
        RequireUsable();
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (scope is not null)
            RequireScope(scope);
        var version = _document.Templates.Where(item => Same(item.TemplateId, template.TemplateId))
            .Select(item => item.Version).DefaultIfEmpty().Max();
        var original = _document.Templates.FirstOrDefault(item => Same(item.TemplateId, template.TemplateId));
        if (original is not null && original.Scope != scope)
            throw new InvalidOperationException("A recurring template's accounting ownership scope cannot be replaced.");
        var currentVersion = original is null ? 0 : GetCurrentTemplate(template.TemplateId).Version;
        if (expectedVersion != currentVersion)
            throw new RecurringJournalDefinitionChangedException("The recurring template version is stale.");
        var definition = new RecurringTemplateDefinition(template.TemplateId, checked(version + 1), "", template, actor.Trim(), now.ToUniversalTime(), scope);
        return definition with { ContentHash = Hash(definition, RecurringJournalJsonContext.Default.RecurringTemplateDefinition) };
    }

    public async Task<RecurringScheduleDefinition> RegisterScheduleAsync(RecurringJournalSchedule schedule,
        RecurringJournalScope scope, IReadOnlyList<JournalEvidenceReference> evidence, string actor,
        int expectedVersion, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var definition = PrepareSchedule(schedule, GetCurrentTemplate(schedule.TemplateId), scope, evidence, actor, expectedVersion, now);
        await CommitAsync(_document with
        {
            Schedules = [.. _document.Schedules, definition],
            Activations = [.. _document.Activations, new RecurringDefinitionActivation(definition.ScheduleId, definition.Version, null, null,
                actor.Trim(), "Registered schedule definition.", now.ToUniversalTime())]
        }, ct).ConfigureAwait(false);
        return Copy(definition, RecurringJournalJsonContext.Default.RecurringScheduleDefinition);
    }

    private RecurringScheduleDefinition PrepareSchedule(RecurringJournalSchedule schedule, RecurringTemplateDefinition template,
        RecurringJournalScope scope, IReadOnlyList<JournalEvidenceReference> evidence, string actor, int expectedVersion, DateTimeOffset now)
    {
        RequireUsable();
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequireScope(scope);
        if (schedule.LedgerKey.LedgerView != LedgerViewKind.Actual || schedule.LedgerKey.ScenarioId is not null ||
            !Same(schedule.LedgerKey.ProjectId, scope.FundProfileId))
            throw new InvalidOperationException("Recurring approval drafts require an actual ledger in their retained fund scope, without a simulation scenario.");
        if (!Same(template.TemplateId, schedule.TemplateId))
            throw new InvalidOperationException("The recurring schedule references a different template.");
        if (template.Scope is not null && template.Scope != scope)
            throw new InvalidOperationException("The recurring template belongs to a different accounting scope.");
        // A fixed-line template must resolve to a balanced journal from the schedule's terms.
        // A calculated template has no lines to resolve; its terms are checked when it runs.
        if (template.Template.Calculation is null)
            _ = RecurringJournalPlanner.Plan(schedule, template.Template, 1);
        var version = _document.Schedules.Where(item => Same(item.ScheduleId, schedule.ScheduleId))
            .Select(item => item.Version).DefaultIfEmpty().Max();
        var original = _document.Schedules.FirstOrDefault(item => Same(item.ScheduleId, schedule.ScheduleId));
        if (original is not null && original.Scope != scope)
            throw new InvalidOperationException("A recurring schedule's accounting ownership scope cannot be replaced; register a new schedule identity.");
        var currentVersion = original is null ? 0 : GetCurrentSchedule(schedule.ScheduleId).Version;
        if (expectedVersion != currentVersion)
            throw new RecurringJournalDefinitionChangedException("The recurring schedule version is stale.");
        var definition = new RecurringScheduleDefinition(schedule.ScheduleId, checked(version + 1), "",
            RecurringJournalScheduleSnapshot.Capture(schedule), scope, evidence.Select(item => item.Normalize()).ToArray(), actor.Trim(), now.ToUniversalTime());
        return definition with { ContentHash = Hash(definition, RecurringJournalJsonContext.Default.RecurringScheduleDefinition) };
    }

    public async Task<RecurringScheduleDefinition> ConfigureAsync(RecurringJournalSchedule schedule, JournalTemplate template,
        RecurringJournalScope scope, IReadOnlyList<JournalEvidenceReference> evidence, string actor,
        int expectedScheduleVersion, int expectedTemplateVersion, DateTimeOffset now, CancellationToken ct = default)
    {
        var templateDefinition = PrepareTemplate(template, actor, expectedTemplateVersion, now, scope);
        var scheduleDefinition = PrepareSchedule(schedule, templateDefinition, scope, evidence, actor, expectedScheduleVersion, now);
        await CommitAsync(_document with
        {
            Templates = [.. _document.Templates, templateDefinition],
            Schedules = [.. _document.Schedules, scheduleDefinition],
            Activations = [.. _document.Activations, new RecurringDefinitionActivation(scheduleDefinition.ScheduleId, scheduleDefinition.Version,
                templateDefinition.TemplateId, templateDefinition.Version, actor.Trim(), "Configured recurring schedule and template atomically.", now.ToUniversalTime())]
        }, ct).ConfigureAwait(false);
        return Copy(scheduleDefinition, RecurringJournalJsonContext.Default.RecurringScheduleDefinition);
    }

    public async Task<RecurringOccurrenceRecord> ClaimAsync(string scheduleId, DateOnly date,
        int expectedScheduleVersion, int expectedTemplateVersion, DateTimeOffset now, CancellationToken ct = default)
    {
        RequireUsable();
        var schedule = GetCurrentSchedule(scheduleId);
        var template = GetCurrentTemplate(schedule.Schedule.TemplateId);
        var key = RecurringJournalIdentity.OccurrenceKey(scheduleId, date);
        var existing = _document.Occurrences.SingleOrDefault(item => item.OccurrenceKey == key);
        if (existing is not null && (existing.ScheduleDefinition.Version != schedule.Version
            || existing.TemplateDefinition.Version != template.Version
            || existing.ScheduleDefinition.ContentHash != schedule.ContentHash
            || existing.TemplateDefinition.ContentHash != template.ContentHash))
        {
            await SetOutcomeCoreAsync(existing, RecurringOccurrenceState.DefinitionChanged,
                "The schedule or template definition changed. Explicitly correct or supersede the retained draft before retrying.",
                existing.PeriodId, existing.LockOwner, existing.ReopenPath, now, ct).ConfigureAwait(false);
            throw new RecurringJournalDefinitionChangedException("The recurring occurrence is retained against a different schedule or template version.");
        }
        if (schedule.Version != expectedScheduleVersion || template.Version != expectedTemplateVersion)
            throw new RecurringJournalDefinitionChangedException("The recurring journal plan is stale; reload its schedule and template versions.");
        if (existing is not null)
        {
            if (existing.State == RecurringOccurrenceState.DefinitionChanged)
                return await SetOutcomeCoreAsync(existing, RecurringOccurrenceState.Claimed,
                    "The exact retained definitions were explicitly reactivated.", existing.PeriodId, null, null, now, ct).ConfigureAwait(false);
            return Copy(existing, RecurringJournalJsonContext.Default.RecurringOccurrenceRecord);
        }
        RequireOccurrenceDate(schedule.Schedule.ToSchedule(), date);
        var record = new RecurringOccurrenceRecord(key, RecurringJournalIdentity.DraftId(key), date,
            schedule, template, RecurringOccurrenceState.Claimed, now.ToUniversalTime(), now.ToUniversalTime(),
            [new RecurringOccurrenceTransition(RecurringOccurrenceState.Claimed, now.ToUniversalTime(), null)]);
        await CommitAsync(_document with { Occurrences = [.. _document.Occurrences, record] }, ct).ConfigureAwait(false);
        return Copy(record, RecurringJournalJsonContext.Default.RecurringOccurrenceRecord);
    }

    public async Task<RecurringOccurrenceRecord> PinEconomicObligationsAsync(
        string occurrenceKey, IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        RequireUsable();
        RecurringJournalIdentity.ValidateEconomicObligationKeys(keys);
        var normalized = keys.Order(StringComparer.Ordinal).ToArray();
        var existing = _document.Occurrences.SingleOrDefault(item => item.OccurrenceKey == occurrenceKey)
            ?? throw new InvalidOperationException("The recurring occurrence has not been claimed.");
        if (existing.EconomicObligationKeys is { } pinned)
        {
            if (!pinned.SequenceEqual(normalized, StringComparer.Ordinal))
                throw new InvalidOperationException("The calculated economic obligations differ from this occurrence's independently retained ownership.");
            return Copy(existing, RecurringJournalJsonContext.Default.RecurringOccurrenceRecord);
        }
        if (existing.State is RecurringOccurrenceState.DefinitionChanged or RecurringOccurrenceState.NoDraftRequired
            || existing.DraftEvidenceDigest is not null || existing.History.Any(item => item.State == RecurringOccurrenceState.Drafted))
            throw new InvalidOperationException("A completed recurring draft cannot acquire new economic obligation ownership; restore its trusted intake provenance.");
        var requested = normalized.ToHashSet(StringComparer.Ordinal);
        if (_document.Occurrences.Any(item => item.OccurrenceKey != occurrenceKey
                && item.EconomicObligationKeys?.Any(requested.Contains) == true))
            throw new InvalidOperationException("An economic obligation is already owned by another retained recurring occurrence; use its governed correction flow.");
        var next = existing with { EconomicObligationKeys = normalized };
        await CommitAsync(_document with
        {
            Occurrences = _document.Occurrences.Select(item => item.OccurrenceKey == occurrenceKey ? next : item).ToArray()
        }, ct).ConfigureAwait(false);
        return Copy(next, RecurringJournalJsonContext.Default.RecurringOccurrenceRecord);
    }

    public async Task<RecurringOccurrenceRecord> PinDraftEvidenceAsync(string key, string digest, CancellationToken ct = default)
    {
        RequireUsable();
        if (!Sha256Digest.IsCanonical(digest))
            throw new InvalidOperationException("Calculated recurring evidence requires an exact canonical digest.");
        var existing = _document.Occurrences.SingleOrDefault(item => item.OccurrenceKey == key)
            ?? throw new InvalidOperationException("The recurring occurrence has not been claimed.");
        if (existing.DraftEvidenceDigest is { } pinned)
        {
            if (!Sha256Digest.FixedEquals(pinned, digest))
                throw new InvalidOperationException("Calculated recurring evidence differs from its independently retained claim pin.");
            return Copy(existing, RecurringJournalJsonContext.Default.RecurringOccurrenceRecord);
        }
        if (existing.State == RecurringOccurrenceState.DefinitionChanged || existing.History.Any(item => item.State == RecurringOccurrenceState.Drafted))
            throw new InvalidOperationException("An existing recurring draft cannot acquire a new calculated evidence pin; restore its trusted intake provenance.");
        var next = existing with { DraftEvidenceDigest = digest };
        await CommitAsync(_document with { Occurrences = _document.Occurrences.Select(item => item.OccurrenceKey == key ? next : item).ToArray() }, ct).ConfigureAwait(false);
        return Copy(next, RecurringJournalJsonContext.Default.RecurringOccurrenceRecord);
    }

    public async Task<RecurringOccurrenceRecord> SetOutcomeAsync(string key, RecurringOccurrenceState state,
        string? reason, string? periodId, string? lockOwner, string? reopenPath, DateTimeOffset now, CancellationToken ct = default)
    {
        RequireUsable();
        if (state is not (RecurringOccurrenceState.Drafted or RecurringOccurrenceState.Blocked or RecurringOccurrenceState.NoDraftRequired))
            throw new InvalidOperationException("Only a drafted, blocked or no-draft-required result can complete recurring intake.");
        var existing = _document.Occurrences.SingleOrDefault(item => item.OccurrenceKey == key)
            ?? throw new InvalidOperationException("The recurring occurrence has not been claimed.");
        if (existing.State == RecurringOccurrenceState.DefinitionChanged)
            throw new RecurringJournalDefinitionChangedException("A changed recurring definition requires explicit correction or supersession.");
        var currentSchedule = GetCurrentSchedule(existing.ScheduleDefinition.ScheduleId);
        var currentTemplate = GetCurrentTemplate(currentSchedule.Schedule.TemplateId);
        if (currentSchedule.ContentHash != existing.ScheduleDefinition.ContentHash || currentTemplate.ContentHash != existing.TemplateDefinition.ContentHash)
            throw new RecurringJournalDefinitionChangedException("A recurring result cannot complete against a changed definition.");
        return await SetOutcomeCoreAsync(existing, state, reason, periodId, lockOwner, reopenPath, now, ct).ConfigureAwait(false);
    }

    private async Task<RecurringOccurrenceRecord> SetOutcomeCoreAsync(RecurringOccurrenceRecord existing,
        RecurringOccurrenceState state, string? reason, string? periodId, string? lockOwner, string? reopenPath,
        DateTimeOffset now, CancellationToken ct)
    {
        if (existing.State == state && existing.Reason == reason && existing.PeriodId == periodId
            && existing.LockOwner == lockOwner && existing.ReopenPath == reopenPath)
            return Copy(existing, RecurringJournalJsonContext.Default.RecurringOccurrenceRecord);
        var next = existing with
        {
            State = state,
            Reason = reason,
            PeriodId = periodId,
            LockOwner = lockOwner,
            ReopenPath = reopenPath,
            UpdatedAtUtc = now.ToUniversalTime(),
            History = [.. existing.History, new RecurringOccurrenceTransition(state, now.ToUniversalTime(), reason, periodId, lockOwner, reopenPath)]
        };
        await CommitAsync(_document with { Occurrences = _document.Occurrences.Select(item => item.OccurrenceKey == existing.OccurrenceKey ? next : item).ToArray() }, ct).ConfigureAwait(false);
        return Copy(next, RecurringJournalJsonContext.Default.RecurringOccurrenceRecord);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await lease.DisposeAsync().ConfigureAwait(false);
    }
}
