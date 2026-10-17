using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <param name="PayloadType">
/// The payload family (<see cref="SecurityMasterEventPayloads"/>). Persisted in the
/// <c>security_events.payload_type</c> column; <see langword="null"/> on an envelope built by a
/// caller that leaves resolution to the store.
/// </param>
/// <param name="PayloadVersion">
/// The schema version of the payload family, persisted in <c>security_events.payload_version</c>
/// so replay dispatches on a declared version instead of inspecting the payload's shape.
/// </param>
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
    JsonElement Metadata,
    string? PayloadType = null,
    int? PayloadVersion = null);

/// <summary>A resolved event payload family and version.</summary>
public readonly record struct SecurityMasterEventPayloadDescriptor(string PayloadType, int PayloadVersion);

/// <summary>
/// The payload families a Security Master event can carry, and the one place that resolves which
/// family and version a stored or incoming event belongs to.
/// </summary>
public static class SecurityMasterEventPayloads
{
    /// <summary>
    /// The economic definition record (classification, economic terms and retained flat terms)
    /// written by every current command path.
    /// </summary>
    public const string EconomicDefinition = "SecurityEconomicDefinition";

    /// <summary>The current <see cref="EconomicDefinition"/> payload version.</summary>
    public const int EconomicDefinitionVersion = EconomicTermsSchema.Current;

    /// <summary>
    /// The flat projection record written before the economic definition existed. Still replayed
    /// by converting it into an economic definition.
    /// </summary>
    public const string LegacyProjection = "SecurityProjection";

    /// <summary>The only <see cref="LegacyProjection"/> payload version.</summary>
    public const int LegacyProjectionVersion = 1;

    /// <summary>The metadata key that historically carried the payload family.</summary>
    public const string PayloadTypeMetadataKey = "payloadType";

    /// <summary>The metadata key that historically carried the payload version.</summary>
    public const string SchemaVersionMetadataKey = "schemaVersion";

    /// <summary>Every payload family this node can replay, with its newest supported version.</summary>
    public static IReadOnlyDictionary<string, int> SupportedVersions { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [EconomicDefinition] = EconomicDefinitionVersion,
        [LegacyProjection] = LegacyProjectionVersion
    };

    /// <summary>
    /// Resolves an event's payload family and version: explicit envelope values first, then the
    /// metadata keys every economic write has carried, then the payload's shape (an economic
    /// definition carries <c>classification</c> and <c>economicTerms</c>; anything else is a legacy
    /// projection). Always returns a descriptor, so every persisted event gets a payload version.
    /// </summary>
    public static SecurityMasterEventPayloadDescriptor Resolve(SecurityMasterEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.PayloadType is { } explicitType)
        {
            return new SecurityMasterEventPayloadDescriptor(
                explicitType,
                envelope.PayloadVersion ?? DefaultVersionFor(explicitType));
        }

        // The metadata version is only meaningful alongside the metadata type it was written with;
        // a type inferred from the payload shape takes that family's default version.
        if (ReadMetadataString(envelope.Metadata, PayloadTypeMetadataKey) is { } metadataType)
        {
            return new SecurityMasterEventPayloadDescriptor(
                metadataType,
                envelope.PayloadVersion
                ?? ReadMetadataInt(envelope.Metadata, SchemaVersionMetadataKey)
                ?? DefaultVersionFor(metadataType));
        }

        var inferredType = InferTypeFromShape(envelope.Payload);
        return new SecurityMasterEventPayloadDescriptor(
            inferredType,
            envelope.PayloadVersion ?? DefaultVersionFor(inferredType));
    }

    /// <summary>
    /// Fails when a payload family or version is newer than this node can replay. Replaying it with
    /// an older decoder would drop whatever the newer payload added.
    /// </summary>
    public static void EnsureReplayable(SecurityMasterEventPayloadDescriptor descriptor, Guid securityId, long streamVersion)
    {
        if (!SupportedVersions.TryGetValue(descriptor.PayloadType, out var supported))
        {
            throw new InvalidOperationException(
                $"Security event {securityId}@{streamVersion} carries payload type '{descriptor.PayloadType}', which this node cannot replay.");
        }

        if (descriptor.PayloadVersion > supported || descriptor.PayloadVersion < 1)
        {
            throw new InvalidOperationException(
                $"Security event {securityId}@{streamVersion} carries {descriptor.PayloadType} payload version " +
                $"{descriptor.PayloadVersion}; this node replays versions 1 through {supported}.");
        }
    }

    /// <summary>Whether a payload has the economic definition shape.</summary>
    public static bool IsEconomicDefinitionShape(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
           && payload.TryGetProperty("classification", out _)
           && payload.TryGetProperty("economicTerms", out _);

    private static string InferTypeFromShape(JsonElement payload)
        => IsEconomicDefinitionShape(payload) ? EconomicDefinition : LegacyProjection;

    private static int DefaultVersionFor(string type)
        => type == EconomicDefinition ? EconomicDefinitionVersion : LegacyProjectionVersion;

    private static string? ReadMetadataString(JsonElement metadata, string key)
        => metadata.ValueKind == JsonValueKind.Object
           && metadata.TryGetProperty(key, out var value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static int? ReadMetadataInt(JsonElement metadata, string key)
        => metadata.ValueKind == JsonValueKind.Object
           && metadata.TryGetProperty(key, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var number)
            ? number
            : null;
}
