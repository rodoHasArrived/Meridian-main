using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

public sealed record SecurityMasterEventEnvelope(
    long? GlobalSequence,
    Guid SecurityId,
    long StreamVersion,
    string EventType,
    DateTimeOffset EventTimestamp,
    string Actor,
    Guid? CorrelationId,
    Guid? CausationId,
    JsonElement Payload,
    JsonElement Metadata);
