using System.Globalization;
using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// One economic event's place on the ADR-034 valid-time axis: it asserts the security's full state
/// over <c>[ValidFrom, ValidTo)</c>, or never governs when a later recording with the same valid-from
/// replaced it (<see cref="SupersededByStreamVersion"/>).
/// </summary>
public sealed record SecurityValidTimeAssertion(
    long StreamVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    long? SupersededByStreamVersion)
{
    /// <summary>True when the assertion governs some period (no later recording replaced it).</summary>
    public bool Governs => SupersededByStreamVersion is null;
}

/// <summary>
/// One governing period of a security's terms: the full economic state that applied over
/// <c>[ValidFrom, ValidTo)</c>, as known at the time of the read.
/// </summary>
public sealed record SecurityTermsValidTimeSegment(
    Guid SecurityId,
    long StreamVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    SecurityEconomicDefinitionRecord Definition)
{
    /// <summary>True when <paramref name="instant"/> falls inside <c>[ValidFrom, ValidTo)</c>.</summary>
    public bool Covers(DateTimeOffset instant) => instant >= ValidFrom && (ValidTo is not { } end || instant < end);
}

/// <summary>
/// ADR-034 bitemporal term reads for consumers outside the application layer. "Terms valid at V as
/// known at T" is the event recorded by T with the latest valid-from not after V; equal valid-froms
/// resolve to the later recording.
/// </summary>
public interface ISecurityTermsValidTimeReader
{
    /// <summary>The terms that applied at <paramref name="validAt"/>, as known at <paramref name="knownAt"/> (default now); null before the first valid-from.</summary>
    Task<SecurityTermsValidTimeSegment?> GetValidAtAsync(
        Guid securityId,
        DateTimeOffset validAt,
        DateTimeOffset? knownAt = null,
        CancellationToken ct = default);

    /// <summary>Every governing period of the security's terms, ordered by valid-from, as known at <paramref name="knownAt"/> (default now).</summary>
    Task<IReadOnlyList<SecurityTermsValidTimeSegment>> GetTimelineAsync(
        Guid securityId,
        DateTimeOffset? knownAt = null,
        CancellationToken ct = default);
}

/// <summary>The ADR-034 valid-time rule, shared by every reader so they cannot disagree.</summary>
public static class SecurityTermsValidTime
{
    /// <summary>The recorded valid-from in an event's metadata, or null on events written before ADR-034.</summary>
    public static DateTimeOffset? ReadValidFrom(JsonElement metadata)
        => metadata.ValueKind == JsonValueKind.Object
           && metadata.TryGetProperty(SecurityMasterEventPayloads.ValidFromMetadataKey, out var value)
           && value.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// True when two states carry the same economics (asset class, currency, common, economic and retained
    /// asset-specific terms). An amendment that only changed identifiers or display data is economically
    /// equal to its predecessor, so it does not start a new economic period.
    /// </summary>
    public static bool EconomicallyEqual(SecurityEconomicDefinitionRecord left, SecurityEconomicDefinitionRecord right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return string.Equals(left.AssetClass, right.AssetClass, StringComparison.Ordinal)
               && string.Equals(left.Currency, right.Currency, StringComparison.Ordinal)
               && JsonEqual(left.CommonTerms, right.CommonTerms)
               && JsonEqual(left.EconomicTerms, right.EconomicTerms)
               && JsonEqual(left.LegacyAssetSpecificTerms, right.LegacyAssetSpecificTerms);
    }

    private static bool JsonEqual(JsonElement? left, JsonElement? right)
        => (left, right) switch
        {
            (null, null) => true,
            ({ } l, { } r) => JsonElement.DeepEquals(l, r),
            _ => false
        };

    /// <summary>
    /// Places each event on the valid-time axis. For each distinct valid-from, the latest recording
    /// (highest stream version) governs and earlier recordings at that valid-from are superseded by it.
    /// A governing assertion holds until the next later governing valid-from.
    /// </summary>
    public static IReadOnlyList<SecurityValidTimeAssertion> Assign(
        IEnumerable<(long StreamVersion, DateTimeOffset RecordedAt, DateTimeOffset ValidFrom)> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var all = events.ToArray();
        var winners = all
            .GroupBy(static item => item.ValidFrom)
            .Select(static group => group.MaxBy(static item => item.StreamVersion))
            .OrderBy(static item => item.ValidFrom)
            .ToArray();
        var winnerByValidFrom = winners.ToDictionary(static item => item.ValidFrom, static item => item.StreamVersion);
        var nextValidFrom = new Dictionary<DateTimeOffset, DateTimeOffset?>();
        for (var index = 0; index < winners.Length; index++)
        {
            nextValidFrom[winners[index].ValidFrom] = index + 1 < winners.Length ? winners[index + 1].ValidFrom : null;
        }

        return all
            .OrderBy(static item => item.StreamVersion)
            .Select(item =>
            {
                var winner = winnerByValidFrom[item.ValidFrom];
                return winner == item.StreamVersion
                    ? new SecurityValidTimeAssertion(item.StreamVersion, item.RecordedAt, item.ValidFrom, nextValidFrom[item.ValidFrom], null)
                    : new SecurityValidTimeAssertion(item.StreamVersion, item.RecordedAt, item.ValidFrom, null, winner);
            })
            .ToArray();
    }
}
