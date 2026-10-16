using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.FundAdministration;

/// <summary>The accounting authority that must resolve before an occurrence can become a draft.</summary>
public sealed record RecurringJournalScope(
    string FundProfileId,
    Guid LedgerBookId,
    string EntityId,
    string Currency,
    string TenantId,
    string CompanyId,
    string? FundAccountId = null);

/// <summary>A serializable, immutable copy of every schedule input.</summary>
public sealed record RecurringJournalScheduleSnapshot(
    string ScheduleId,
    string TemplateId,
    LedgerBookKey LedgerKey,
    RecurringJournalCadence Cadence,
    DateOnly AnchorDate,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyDictionary<string, decimal> Parameters,
    TimeOnly PostingTime,
    DateOnly? EndsOn,
    LedgerLineDimensionSet? Dimensions,
    string? Description)
{
    public RecurringJournalSchedule ToSchedule() => new(ScheduleId, TemplateId, LedgerKey, Cadence,
        AnchorDate, CreatedBy, CreatedAtUtc, Parameters, PostingTime, EndsOn, Dimensions, Description);

    public static RecurringJournalScheduleSnapshot Capture(RecurringJournalSchedule schedule) => new(
        schedule.ScheduleId, schedule.TemplateId, schedule.LedgerKey, schedule.Cadence,
        schedule.AnchorDate, schedule.CreatedBy, schedule.CreatedAtUtc,
        new SortedDictionary<string, decimal>(schedule.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal),
        schedule.PostingTime, schedule.EndsOn, schedule.Dimensions, schedule.Description);
}

public sealed record RecurringTemplateDefinition(
    string TemplateId, int Version, string ContentHash, JournalTemplate Template,
    string RegisteredBy, DateTimeOffset RegisteredAtUtc, RecurringJournalScope? Scope = null);

public sealed record RecurringScheduleDefinition(
    string ScheduleId, int Version, string ContentHash, RecurringJournalScheduleSnapshot Schedule,
    RecurringJournalScope Scope, IReadOnlyList<JournalEvidenceReference> Evidence,
    string RegisteredBy, DateTimeOffset RegisteredAtUtc);

public sealed record RecurringDefinitionActivation(
    string? ScheduleId, int? ScheduleVersion, string? TemplateId, int? TemplateVersion,
    string Actor, string Reason, DateTimeOffset ActivatedAtUtc);

public enum RecurringOccurrenceState
{
    Claimed,
    Drafted,
    Blocked,
    DefinitionChanged
}

public sealed record RecurringOccurrenceTransition(
    RecurringOccurrenceState State, DateTimeOffset RecordedAtUtc, string? Reason,
    string? PeriodId = null, string? LockOwner = null, string? ReopenPath = null);

/// <summary>
/// One retained claim per schedule/date. Definitions and evidence are copies, never references
/// to the mutable current registry. Draft identity does not change when a runner restarts.
/// </summary>
public sealed record RecurringOccurrenceRecord(
    string OccurrenceKey,
    Guid DraftId,
    DateOnly EffectiveDate,
    RecurringScheduleDefinition ScheduleDefinition,
    RecurringTemplateDefinition TemplateDefinition,
    RecurringOccurrenceState State,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<RecurringOccurrenceTransition> History,
    string? Reason = null,
    string? PeriodId = null,
    string? LockOwner = null,
    string? ReopenPath = null);

/// <summary>A definition change cannot silently reinterpret a retained approval draft.</summary>
public sealed class RecurringJournalDefinitionChangedException(string message) : InvalidOperationException(message);

public interface IRecurringJournalStore
{
    Task<IRecurringJournalSession> OpenSessionAsync(CancellationToken ct = default);
}

/// <summary>
/// The lease covers planning, claim retention, intake and completion. Dispose before reentering
/// the store. Every mutating method commits before returning; disposal does not commit changes.
/// </summary>
public interface IRecurringJournalSession : IAsyncDisposable
{
    IReadOnlyList<RecurringTemplateDefinition> Templates { get; }
    IReadOnlyList<RecurringScheduleDefinition> Schedules { get; }
    IReadOnlyList<RecurringScheduleDefinition> CurrentSchedules { get; }
    IReadOnlyList<RecurringOccurrenceRecord> Occurrences { get; }
    IReadOnlyList<RecurringDefinitionActivation> Activations { get; }
    RecurringScheduleDefinition GetCurrentSchedule(string scheduleId);
    RecurringTemplateDefinition GetCurrentTemplate(string templateId);
    Task ActivateDefinitionsAsync(string scheduleId, int scheduleVersion, int templateVersion,
        string actor, string reason, DateTimeOffset now, CancellationToken ct = default);
    Task<RecurringScheduleDefinition> ConfigureAsync(RecurringJournalSchedule schedule, JournalTemplate template,
        RecurringJournalScope scope, IReadOnlyList<JournalEvidenceReference> evidence, string actor,
        int expectedScheduleVersion, int expectedTemplateVersion, DateTimeOffset now, CancellationToken ct = default);
    Task<RecurringTemplateDefinition> RegisterTemplateAsync(JournalTemplate template, string actor,
        int expectedVersion, DateTimeOffset now, CancellationToken ct = default, RecurringJournalScope? scope = null);
    Task<RecurringScheduleDefinition> RegisterScheduleAsync(RecurringJournalSchedule schedule,
        RecurringJournalScope scope, IReadOnlyList<JournalEvidenceReference> evidence, string actor,
        int expectedVersion, DateTimeOffset now, CancellationToken ct = default);
    Task<RecurringOccurrenceRecord> ClaimAsync(string scheduleId, DateOnly date,
        int expectedScheduleVersion, int expectedTemplateVersion, DateTimeOffset now, CancellationToken ct = default);
    Task<RecurringOccurrenceRecord> SetOutcomeAsync(string key, RecurringOccurrenceState state,
        string? reason, string? periodId, string? lockOwner, string? reopenPath,
        DateTimeOffset now, CancellationToken ct = default);
}

internal sealed record RecurringJournalDocument(
    int SchemaVersion,
    IReadOnlyList<RecurringTemplateDefinition> Templates,
    IReadOnlyList<RecurringScheduleDefinition> Schedules,
    IReadOnlyList<RecurringOccurrenceRecord> Occurrences,
    IReadOnlyList<RecurringDefinitionActivation> Activations);

internal sealed record RecurringJournalEnvelope(string ContentHash, RecurringJournalDocument Document);

/// <summary>Source-generated wire format also defines the retained content fingerprint.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RecurringJournalEnvelope))]
[JsonSerializable(typeof(RecurringJournalDocument))]
[JsonSerializable(typeof(RecurringTemplateDefinition))]
[JsonSerializable(typeof(RecurringScheduleDefinition))]
[JsonSerializable(typeof(RecurringOccurrenceRecord))]
[JsonSerializable(typeof(JournalTemplate))]
[JsonSerializable(typeof(RecurringJournalScheduleSnapshot))]
internal partial class RecurringJournalJsonContext : JsonSerializerContext;

public static class RecurringJournalIdentity
{
    public static string SerializeSchedule(RecurringScheduleDefinition definition)
        => JsonSerializer.Serialize(definition, RecurringJournalJsonContext.Default.RecurringScheduleDefinition);

    public static string SerializeTemplate(RecurringTemplateDefinition definition)
        => JsonSerializer.Serialize(definition, RecurringJournalJsonContext.Default.RecurringTemplateDefinition);

    public static string OccurrenceKey(string scheduleId, DateOnly date)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        // A structured encoding prevents ids containing delimiters from aliasing one another.
        var normalizedId = scheduleId.Trim().ToUpperInvariant();
        return Sha256Digest.ComputeUtf8($"{normalizedId.Length}:{normalizedId}:{date:yyyy-MM-dd}");
    }

    public static Guid DraftId(string occurrenceKey) => new(Convert.FromHexString(occurrenceKey)[..16]);
}
