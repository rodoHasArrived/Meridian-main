namespace Meridian.Contracts.Pipeline;

/// <summary>
/// Metadata propagated across UFL command, event, and outbox workflow boundaries.
/// </summary>
public sealed record UflCommandMetadataDto(
    Guid? CommandId,
    Guid? CorrelationId,
    Guid? CausationId,
    string? SourceAssetClass,
    string? EventType,
    string? SourceSystem,
    bool IsReplay);

/// <summary>
/// Durable outbox envelope for UFL workflow publication.
/// </summary>
/// <typeparam name="TPayload">Published payload type.</typeparam>
public sealed record UflOutboxMessage<TPayload>(
    Guid MessageId,
    string Topic,
    UflCommandMetadataDto Metadata,
    TPayload Payload,
    DateTimeOffset EnqueuedAtUtc);
