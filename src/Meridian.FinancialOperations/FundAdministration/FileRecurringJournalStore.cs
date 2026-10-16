using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.FundAdministration;

/// <summary>
/// Durable recurring definitions and claims. All processes sharing this directory serialize
/// their entire intake cycle using an OS-exclusive lease, not an instance-only semaphore.
/// The snapshot is flushed before atomic replacement. Missing/corrupt state never becomes empty state.
/// </summary>
public sealed class FileRecurringJournalStore : IRecurringJournalStore
{
    private const string Marker = "meridian-recurring-journals-v1";
    private readonly string _directory;
    private string StatePath => Path.Combine(_directory, "recurring-journals.json");
    private string MarkerPath => Path.Combine(_directory, "initialized");

    public FileRecurringJournalStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    /// <summary>
    /// Explicit provisioning only; never called by the runner. The permanent marker refuses
    /// reinitialization if an initialized snapshot is later deleted or lost.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        if (File.Exists(MarkerPath))
        {
            _ = await ReadAsync(ct).ConfigureAwait(false);
            return;
        }

        if (File.Exists(StatePath))
            _ = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        else
            await WriteAsync(new RecurringJournalDocument(1, [], [], [], []), ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(MarkerPath, Marker, ct).ConfigureAwait(false);
    }

    public async Task<IRecurringJournalSession> OpenSessionAsync(CancellationToken ct = default)
    {
        // Directory creation on read would disguise an unavailable mount or an incorrect data root.
        if (!Directory.Exists(_directory))
            throw new InvalidOperationException("The durable recurring journal store is unavailable or has not been provisioned.");
        var lease = await AcquireAsync(ct).ConfigureAwait(false);
        try
        {
            return new Session(this, lease, await ReadAsync(ct).ConfigureAwait(false));
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<FileStream> AcquireAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Never unlink: replacing the inode while another process owns it could grant two leases.
                return new FileStream(Path.Combine(_directory, "recurring-journals.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33)
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<RecurringJournalDocument> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(MarkerPath) || await File.ReadAllTextAsync(MarkerPath, ct).ConfigureAwait(false) != Marker)
            throw new InvalidOperationException("The durable recurring journal initialization marker is unavailable or invalid.");
        return await ReadSnapshotAsync(ct).ConfigureAwait(false);
    }

    private async Task<RecurringJournalDocument> ReadSnapshotAsync(CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(StatePath, ct).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize(bytes, RecurringJournalJsonContext.Default.RecurringJournalEnvelope)
                ?? throw new InvalidOperationException("The durable recurring journal state is empty.");
            if (envelope.Document is null || envelope.Document.SchemaVersion != 1
                || !MatchesHash(envelope.ContentHash, envelope.Document, RecurringJournalJsonContext.Default.RecurringJournalDocument))
                throw new InvalidOperationException("The durable recurring journal state failed integrity or schema validation.");
            Validate(envelope.Document);
            return envelope.Document;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NullReferenceException)
        {
            throw new InvalidOperationException("The durable recurring journal state is unavailable or invalid; restore the retained store before retrying.", ex);
        }
    }

    private async Task WriteAsync(RecurringJournalDocument document, CancellationToken ct)
    {
        Validate(document);
        var envelope = new RecurringJournalEnvelope(Hash(document, RecurringJournalJsonContext.Default.RecurringJournalDocument), document);
        await AtomicFileWriter.WriteAsync(StatePath,
            JsonSerializer.SerializeToUtf8Bytes(envelope, RecurringJournalJsonContext.Default.RecurringJournalEnvelope), ct).ConfigureAwait(false);
    }

    private static string Hash<T>(T value, JsonTypeInfo<T> metadata)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(value, metadata));

    private static bool MatchesHash<T>(string digest, T value, JsonTypeInfo<T> metadata)
        => Sha256Digest.IsCanonical(digest) && Sha256Digest.FixedEquals(digest, Hash(value, metadata));

    private static T Copy<T>(T value, JsonTypeInfo<T> metadata)
        => JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(value, metadata), metadata)!;

    private static void Validate(RecurringJournalDocument document)
    {
        if (document.Templates is null || document.Schedules is null || document.Occurrences is null || document.Activations is null)
            throw new InvalidOperationException("Recurring journal state is missing required collections.");
        foreach (var template in document.Templates)
        {
            if (template.Scope is not null)
                RequireScope(template.Scope);
            if (template.Version < 1 || !Same(template.TemplateId, template.Template.TemplateId)
                || !MatchesHash(template.ContentHash, template with { ContentHash = "" }, RecurringJournalJsonContext.Default.RecurringTemplateDefinition))
                throw new InvalidOperationException("A retained recurring template definition is invalid.");
        }
        foreach (var schedule in document.Schedules)
        {
            RequireScope(schedule.Scope);
            if (schedule.Version < 1 || !Same(schedule.ScheduleId, schedule.Schedule.ScheduleId)
                || schedule.Evidence is null
                || !MatchesHash(schedule.ContentHash, schedule with { ContentHash = "" }, RecurringJournalJsonContext.Default.RecurringScheduleDefinition)
                || !document.Templates.Any(template => Same(template.TemplateId, schedule.Schedule.TemplateId)))
                throw new InvalidOperationException("A retained recurring schedule definition is invalid.");
            _ = schedule.Schedule.ToSchedule();
        }
        if (document.Templates.GroupBy(item => (item.TemplateId.ToUpperInvariant(), item.Version)).Any(group => group.Count() != 1)
            || document.Schedules.GroupBy(item => (item.ScheduleId.ToUpperInvariant(), item.Version)).Any(group => group.Count() != 1)
            || document.Occurrences.GroupBy(item => item.OccurrenceKey).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Recurring journal state contains duplicate identities.");
        foreach (var activation in document.Activations)
        {
            if (string.IsNullOrWhiteSpace(activation.Actor) || string.IsNullOrWhiteSpace(activation.Reason)
                || (activation.ScheduleId is not null && !document.Schedules.Any(item => Same(item.ScheduleId, activation.ScheduleId) && item.Version == activation.ScheduleVersion))
                || (activation.TemplateId is not null && !document.Templates.Any(item => Same(item.TemplateId, activation.TemplateId) && item.Version == activation.TemplateVersion)))
                throw new InvalidOperationException("A recurring definition activation has lost its retained evidence.");
        }
        foreach (var occurrence in document.Occurrences)
        {
            var expectedKey = RecurringJournalIdentity.OccurrenceKey(occurrence.ScheduleDefinition.ScheduleId, occurrence.EffectiveDate);
            if (occurrence.OccurrenceKey != expectedKey || occurrence.DraftId != RecurringJournalIdentity.DraftId(expectedKey)
                || !document.Schedules.Any(item => Same(item.ScheduleId, occurrence.ScheduleDefinition.ScheduleId)
                    && item.Version == occurrence.ScheduleDefinition.Version && item.ContentHash == occurrence.ScheduleDefinition.ContentHash)
                || !document.Templates.Any(item => Same(item.TemplateId, occurrence.TemplateDefinition.TemplateId)
                    && item.Version == occurrence.TemplateDefinition.Version && item.ContentHash == occurrence.TemplateDefinition.ContentHash)
                || !MatchesHash(occurrence.ScheduleDefinition.ContentHash, occurrence.ScheduleDefinition with { ContentHash = "" }, RecurringJournalJsonContext.Default.RecurringScheduleDefinition)
                || !MatchesHash(occurrence.TemplateDefinition.ContentHash, occurrence.TemplateDefinition with { ContentHash = "" }, RecurringJournalJsonContext.Default.RecurringTemplateDefinition)
                || occurrence.History.Count == 0 || occurrence.History[^1].State != occurrence.State
                || !Enum.IsDefined(occurrence.State))
                throw new InvalidOperationException("A retained recurring occurrence no longer matches its definition or identity.");
        }
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void RequireScope(RecurringJournalScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.EntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.CompanyId);
        if (scope.LedgerBookId == Guid.Empty)
            throw new ArgumentException("A recurring journal requires an authoritative ledger book.", nameof(scope));
    }

    private sealed class Session(FileRecurringJournalStore owner, FileStream lease, RecurringJournalDocument initial) : IRecurringJournalSession
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
                await owner.WriteAsync(copy, ct).ConfigureAwait(false);
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

        public async Task<RecurringOccurrenceRecord> SetOutcomeAsync(string key, RecurringOccurrenceState state,
            string? reason, string? periodId, string? lockOwner, string? reopenPath, DateTimeOffset now, CancellationToken ct = default)
        {
            RequireUsable();
            if (state is not (RecurringOccurrenceState.Drafted or RecurringOccurrenceState.Blocked))
                throw new InvalidOperationException("Only a drafted or blocked result can complete recurring intake.");
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

    private static void RequireOccurrenceDate(RecurringJournalSchedule schedule, DateOnly date)
    {
        var dayDelta = date.DayNumber - schedule.AnchorDate.DayNumber;
        var monthDelta = (date.Year - schedule.AnchorDate.Year) * 12 + date.Month - schedule.AnchorDate.Month;
        var index = schedule.Cadence switch
        {
            RecurringJournalCadence.Daily => dayDelta,
            RecurringJournalCadence.Weekly => dayDelta / 7,
            RecurringJournalCadence.Monthly => monthDelta,
            RecurringJournalCadence.Quarterly => monthDelta / 3,
            RecurringJournalCadence.SemiAnnually => monthDelta / 6,
            RecurringJournalCadence.Annually => monthDelta / 12,
            _ => -1
        };
        if (index < 0 || date < schedule.AnchorDate || (schedule.EndsOn is { } end && date > end)
            || schedule.EffectiveDateFor(index) != date)
            throw new InvalidOperationException("The requested date is not an occurrence of the retained recurring schedule.");
    }
}
