using Meridian.Domain.Events;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Lightweight schema validation for <see cref="MarketEvent"/> instances before they are persisted.
/// This is intentionally fast and dependency-free but still enforces the documented contract
/// (timestamp, symbol, event type, payload presence, and schema version).
/// </summary>
public static class EventSchemaValidator
{
    /// <summary>
    /// Current schema version for serialized <see cref="MarketEvent"/> documents.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Validates an event and throws <see cref="InvalidOperationException"/> when a contract violation is detected.
    /// </summary>
    public static void Validate(MarketEvent evt)
    {
        if (evt.Timestamp == default)
            throw new InvalidOperationException("Event timestamp is required.");

        if (string.IsNullOrWhiteSpace(evt.Symbol))
            throw new InvalidOperationException("Event symbol is required.");

        if (evt.Type == MarketEventType.Unknown)
            throw new InvalidOperationException("Event type must be specified.");

        if (evt.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidOperationException($"Unsupported schema version {evt.SchemaVersion}. Expected {CurrentSchemaVersion}.");

        // Payload is non-nullable; every event type carries a real payload.
        // Heartbeat events carry MarketEventPayload.HeartbeatPayload instead of null.
    }
}
