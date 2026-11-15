using System.Globalization;
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
    DefinitionChanged,
    /// <summary>A calculated occurrence that correctly produced nothing to post; it is not retried.</summary>
    NoDraftRequired
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
    string? ReopenPath = null)
{
    /// <summary>Write-once digest of trusted calculated intake evidence, independent of the editable draft store.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DraftEvidenceDigest { get; init; }

    /// <summary>Write-once daily economic obligations owned by this claim, independent of definition versions.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? EconomicObligationKeys { get; init; }
}

/// <summary>A definition change cannot silently reinterpret a retained approval draft.</summary>
public sealed class RecurringJournalDefinitionChangedException(string message) : InvalidOperationException(message);

/// <summary>
/// Opens sessions for reading and updating recurring journal schedules and templates.
/// </summary>
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
    Task<RecurringOccurrenceRecord> PinDraftEvidenceAsync(string key, string digest, CancellationToken ct = default)
        => Task.FromException<RecurringOccurrenceRecord>(new NotSupportedException("This recurring registry cannot retain calculated draft evidence authority."));
    Task<RecurringOccurrenceRecord> PinEconomicObligationsAsync(string occurrenceKey, IReadOnlyList<string> keys, CancellationToken ct = default)
        => Task.FromException<RecurringOccurrenceRecord>(new NotSupportedException("This recurring registry cannot retain economic obligation authority."));
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

/// <summary>
/// Produces canonical serialized identities for recurring journal definitions and occurrences.
/// </summary>
public static class RecurringJournalIdentity
{
    /// <summary>
    /// Frozen ownership of one actual held lot's interest day. Its identity domain and encoding
    /// stay unchanged across schedule, template, descriptor and calculation-version updates.
    /// </summary>
    public static string FixedIncomeInterestObligationKey(RecurringJournalScope scope, Guid securityId,
        Guid bookPositionId, string lotId, DateOnly accrualDate)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.CompanyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lotId);
        if (scope.LedgerBookId == Guid.Empty || securityId == Guid.Empty || bookPositionId == Guid.Empty || accrualDate == default)
            throw new ArgumentException("Economic obligation identity requires a book, security, position and accrual date.");
        string[] components = ["fixed-income-interest-v1", scope.TenantId.Trim().ToUpperInvariant(),
            scope.CompanyId.Trim().ToUpperInvariant(), scope.LedgerBookId.ToString("N"), securityId.ToString("N"),
            bookPositionId.ToString("N"), lotId, accrualDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)];
        return Sha256Digest.ComputeUtf8(string.Concat(components.Select(static value =>
            value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value)));
    }

    /// <summary>Checks a nonempty set of unique canonical economic identities before it gains authority.</summary>
    public static void ValidateEconomicObligationKeys(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0 || keys.Any(static key => !Sha256Digest.IsCanonical(key))
            || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
            throw new ArgumentException("Economic obligations require a nonempty set of unique canonical identities.", nameof(keys));
    }

    /// <summary>The retained set digest is independent of the calculation's enumeration order.</summary>
    public static string EconomicObligationDigest(IReadOnlyList<string> keys)
    {
        ValidateEconomicObligationKeys(keys);
        return Sha256Digest.ComputeUtf8("recurring-economic-obligations-v1:" +
            string.Concat(keys.Order(StringComparer.Ordinal)));
    }

    public static string SerializeSchedule(RecurringScheduleDefinition definition)
        => JsonSerializer.Serialize(definition, RecurringJournalJsonContext.Default.RecurringScheduleDefinition);

    public static string SerializeTemplate(RecurringTemplateDefinition definition)
        => JsonSerializer.Serialize(definition, RecurringJournalJsonContext.Default.RecurringTemplateDefinition);

    public static RecurringTemplateDefinition DeserializeTemplate(string json)
        => JsonSerializer.Deserialize(json, RecurringJournalJsonContext.Default.RecurringTemplateDefinition)
            ?? throw new JsonException("The retained recurring template definition is missing.");

    public static string OccurrenceKey(string scheduleId, DateOnly date)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        // A structured encoding prevents ids containing delimiters from aliasing one another.
        var normalizedId = scheduleId.Trim().ToUpperInvariant();
        return Sha256Digest.ComputeUtf8($"{normalizedId.Length}:{normalizedId}:{date:yyyy-MM-dd}");
    }

    public static Guid DraftId(string occurrenceKey) => new(Convert.FromHexString(occurrenceKey)[..16]);
}
