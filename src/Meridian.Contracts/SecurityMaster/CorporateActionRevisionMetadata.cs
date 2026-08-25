using System.Runtime.CompilerServices;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Carries storage transaction time alongside corporate-action DTO instances without expanding
/// the public wire contract. Rows created outside the event store intentionally have no timestamp
/// and retain the legacy behavior of being known to the projection.
/// </summary>
internal static class CorporateActionRevisionMetadata
{
    private static readonly ConditionalWeakTable<CorporateActionDto, RecordedAtHolder> RecordedAt = new();

    internal static void SetRecordedAtUtc(CorporateActionDto action, DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(action);
        RecordedAt.GetOrCreateValue(action).Value = recordedAtUtc;
    }

    internal static DateTimeOffset? GetRecordedAtUtc(CorporateActionDto action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return RecordedAt.TryGetValue(action, out var holder) ? holder.Value : null;
    }

    internal static IReadOnlyList<CorporateActionDto> FilterKnown(
        IReadOnlyList<CorporateActionDto> actions,
        DateTimeOffset asOf)
        => actions
            .Where(action => GetRecordedAtUtc(action) is not { } recordedAt || recordedAt <= asOf)
            .ToArray();

    private sealed class RecordedAtHolder
    {
        internal DateTimeOffset Value { get; set; }
    }
}
