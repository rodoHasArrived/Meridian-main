using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>A security's full economic state as it applied at <see cref="ValidAt"/>, as known at <see cref="KnownAt"/>.</summary>
public sealed record SecurityTermsAsOf(
    Guid SecurityId,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    long StreamVersion,
    DateTimeOffset AssertedValidFrom,
    DateTimeOffset RecordedAt,
    SecurityEconomicDefinitionRecord Definition);

/// <summary>
/// ADR-034 bitemporal term reads (review E1 / #3662 Phase 7, owner decision 2026-11-25: full valid time).
/// Every economic event is a full-state assertion that holds from its valid-from (the request's intended
/// effective date, or the state's own effective-from on events written before ADR-034) until the next
/// later valid-from. Recording time is the event timestamp. "Terms valid at V as known at T" is the event
/// recorded by T with the latest valid-from not after V; equal valid-froms resolve to the later recording,
/// so a back-dated correction recorded later wins for the dates it covers without rewriting history.
/// </summary>
public sealed class SecurityMasterValidTimeQuery
{
    private readonly ISecurityMasterEventStore _events;

    public SecurityMasterValidTimeQuery(ISecurityMasterEventStore events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public async Task<SecurityTermsAsOf?> GetAsync(
        Guid securityId,
        DateTimeOffset validAt,
        DateTimeOffset? knownAt = null,
        CancellationToken ct = default)
    {
        var asKnownAt = knownAt ?? DateTimeOffset.UtcNow;
        var events = await _events.LoadAsync(securityId, ct).ConfigureAwait(false);
        SecurityTermsAsOf? best = null;
        foreach (var @event in events)
        {
            if (@event.EventTimestamp > asKnownAt)
                continue;

            SecurityEconomicDefinitionRecord definition;
            try
            {
                definition = SecurityMasterMapping.FromEventEnvelope(@event);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or JsonException)
            {
                // An event this node cannot replay is not silently skipped: valid-time reads refuse
                // exactly as replay does.
                throw new InvalidOperationException(
                    $"Event {@event.StreamVersion} of security '{securityId:D}' cannot be replayed for a valid-time read: {ex.Message}", ex);
            }

            var assertedFrom = ReadValidFrom(@event.Metadata) ?? definition.EffectiveFrom;
            if (assertedFrom > validAt)
                continue;

            if (best is null
                || assertedFrom > best.AssertedValidFrom
                || (assertedFrom == best.AssertedValidFrom && @event.StreamVersion > best.StreamVersion))
            {
                best = new SecurityTermsAsOf(securityId, validAt, asKnownAt, @event.StreamVersion, assertedFrom, @event.EventTimestamp, definition);
            }
        }

        return best;
    }

    internal static DateTimeOffset? ReadValidFrom(JsonElement metadata)
        => metadata.ValueKind == JsonValueKind.Object
           && metadata.TryGetProperty(SecurityMasterEventPayloads.ValidFromMetadataKey, out var value)
           && value.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
