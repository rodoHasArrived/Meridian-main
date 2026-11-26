using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Integrations;

/// <summary>
/// An append-only marker that a stored provider-integration actor field does not name a person.
/// <para>
/// Until PR rodoHasArrived/Meridian-main#3754 the provider-integration routes stored the actor the client
/// sent, and the browser sent the session's desk label or a Settings placeholder. Those records are never
/// rewritten (nothing recorded who acted, and <c>approvedBy</c> is part of each manifest version's
/// content digest); this flag sits beside them instead.
/// </para>
/// </summary>
/// <param name="FlagId">Deterministic from the record, field and value, so a re-scan never duplicates a flag.</param>
/// <param name="RecordPath">The record's path within the tenant's integration store, e.g. <c>manifest-versions/…/3.json</c>.</param>
/// <param name="RecordKind">The store category (<c>manifest-versions</c>, <c>connections</c>, <c>sync-runs</c>, …).</param>
/// <param name="SubjectId">The record's manifest, connection or sync-run id, when it carries one.</param>
/// <param name="FieldPath">The JSON path of the actor field inside the record.</param>
/// <param name="RecordedValue">The stored value, exactly as written.</param>
/// <param name="Reason">Why the value does not name a person (see <see cref="ProviderIntegrationActorLabelClassifier"/>).</param>
/// <param name="RecordedAt">The nearest timestamp on the flagged object, when there is one.</param>
/// <param name="FlaggedBy">The authenticated operator who ran the scan.</param>
/// <param name="FlaggedAt">When the flag was appended.</param>
public sealed record ProviderIntegrationActorFlagDto(
    string FlagId,
    string RecordPath,
    string RecordKind,
    string? SubjectId,
    string FieldPath,
    string RecordedValue,
    string Reason,
    string? RecordedAt,
    string FlaggedBy,
    DateTimeOffset FlaggedAt)
{
    public static string ComputeFlagId(string recordPath, string fieldPath, string recordedValue)
        => Sha256Digest.ComputeUtf8($"{recordPath}\n{fieldPath}\n{recordedValue}");
}

/// <summary>One stored actor value found by a raw scan of the integration store.</summary>
public sealed record ProviderIntegrationStoredActorValue(
    string RecordPath,
    string RecordKind,
    string? SubjectId,
    string Field,
    string FieldPath,
    string Value,
    string? RecordedAt);

/// <summary>Optional scan input: extra session display names to treat as desk labels.</summary>
public sealed record ProviderIntegrationActorFlagScanRequestDto(IReadOnlyList<string>? ExtraLabels = null);

/// <summary>Result of scanning a tenant's integration store and appending flags.</summary>
public sealed record ProviderIntegrationActorFlagScanResultDto(
    int ActorFieldsScanned,
    int FlagsAppended,
    int AlreadyFlagged,
    IReadOnlyList<ProviderIntegrationActorFlagDto> Flags);

/// <summary>
/// Append-only actor flags. Implementations never change or remove a flag, and never touch the flagged
/// record itself.
/// </summary>
public interface IProviderIntegrationActorFlagStore
{
    /// <summary>Every actor field persisted in this store, read without modifying anything.</summary>
    Task<IReadOnlyList<ProviderIntegrationStoredActorValue>> ScanStoredActorValuesAsync(CancellationToken ct = default);

    /// <summary>Appends <paramref name="flag"/>; false when a flag with the same id already exists.</summary>
    Task<bool> TryAppendActorFlagAsync(ProviderIntegrationActorFlagDto flag, CancellationToken ct = default);

    Task<IReadOnlyList<ProviderIntegrationActorFlagDto>> ListActorFlagsAsync(CancellationToken ct = default);
}

/// <summary>
/// Decides whether a stored actor value names a person. Mirrors
/// <c>build/scripts/audit_provider_integration_actors.py</c>; keep the two in sync.
/// </summary>
public static class ProviderIntegrationActorLabelClassifier
{
    /// <summary>The actor fields the provider-integration contracts persist.</summary>
    public static readonly IReadOnlySet<string> ActorFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "approvedBy", "checkedBy", "createdBy", "importedBy", "ownerUserId", "requestedBy", "reviewedBy", "savedBy"
    };

    /// <summary>Sibling fields that say when the flagged action happened.</summary>
    public static readonly IReadOnlyList<string> TimestampFields =
    [
        "approvedAt", "savedAt", "importedAt", "requestedAt", "checkedAt", "reviewedAt",
        "createdAt", "updatedAt", "startedAt", "completedAt"
    ];

    /// <summary>Why <paramref name="value"/> does not name a person, or null when it looks like a real principal.</summary>
    public static string? Classify(string? value, IReadOnlyCollection<string>? extraLabels = null)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.Trim();
        if (text.Length == 0)
        {
            return "blank actor";
        }

        if (string.Equals(text, "Meridian Operator", StringComparison.OrdinalIgnoreCase))
        {
            return "session desk label";
        }

        if (string.Equals(text, "settings-operator", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "settings-screen", StringComparison.OrdinalIgnoreCase))
        {
            return "settings placeholder";
        }

        if (extraLabels is not null && extraLabels.Any(label => string.Equals(label?.Trim(), text, StringComparison.OrdinalIgnoreCase)))
        {
            return "configured desk label";
        }

        // BuildDisplayName: "<strategy name> Desk". Usernames carry no spaces, so a spaced "... Desk" is a label.
        return text.Contains(' ', StringComparison.Ordinal) && text.EndsWith(" desk", StringComparison.OrdinalIgnoreCase)
            ? "strategy desk label"
            : null;
    }
}
