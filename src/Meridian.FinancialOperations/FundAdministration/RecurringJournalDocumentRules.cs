using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.FundAdministration;

/// <summary>
/// Validation, hashing and envelope rules shared by every durable recurring journal backend, so the
/// file and PostgreSQL stores retain and verify exactly the same document.
/// </summary>
internal static class RecurringJournalDocumentRules
{
    internal const int SchemaVersion = 1;

    /// <summary>Validates <paramref name="document"/> and returns its content hash and serialized envelope.</summary>
    internal static (string ContentHash, byte[] Envelope) SerializeEnvelope(RecurringJournalDocument document)
    {
        Validate(document);
        var envelope = new RecurringJournalEnvelope(Hash(document, RecurringJournalJsonContext.Default.RecurringJournalDocument), document);
        return (envelope.ContentHash, JsonSerializer.SerializeToUtf8Bytes(envelope, RecurringJournalJsonContext.Default.RecurringJournalEnvelope));
    }

    /// <summary>
    /// Reads a retained envelope and refuses it unless its schema, content hash and every retained
    /// definition and occurrence still verify. Callers translate failures into an unavailable store.
    /// </summary>
    internal static RecurringJournalDocument ReadEnvelope(ReadOnlySpan<byte> bytes)
    {
        var envelope = JsonSerializer.Deserialize(bytes, RecurringJournalJsonContext.Default.RecurringJournalEnvelope)
            ?? throw new InvalidOperationException("The durable recurring journal state is empty.");
        if (envelope.Document is null || envelope.Document.SchemaVersion != SchemaVersion
            || !MatchesHash(envelope.ContentHash, envelope.Document, RecurringJournalJsonContext.Default.RecurringJournalDocument))
            throw new InvalidOperationException("The durable recurring journal state failed integrity or schema validation.");
        Validate(envelope.Document);
        return envelope.Document;
    }

    internal static RecurringJournalDocument Empty() => new(SchemaVersion, [], [], [], []);

    internal static string Hash<T>(T value, JsonTypeInfo<T> metadata)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(value, metadata));

    internal static bool MatchesHash<T>(string digest, T value, JsonTypeInfo<T> metadata)
        => Sha256Digest.IsCanonical(digest) && Sha256Digest.FixedEquals(digest, Hash(value, metadata));

    internal static T Copy<T>(T value, JsonTypeInfo<T> metadata)
        => JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(value, metadata), metadata)!;

    internal static void Validate(RecurringJournalDocument document)
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

    internal static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    internal static void RequireScope(RecurringJournalScope scope)
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

    internal static void RequireOccurrenceDate(RecurringJournalSchedule schedule, DateOnly date)
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
