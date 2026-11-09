using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Marks a typed governance payload. <see cref="PostgresSecurityGovernanceEventLog.AppendAsync{TPayload}"/>
/// accepts only these, so every payload written to <c>security_governance_events</c> has a declared
/// contract, a source-generated serializer and an explicit <c>payload_version</c>.
/// </summary>
public interface ISecurityGovernancePayload;

// ---------------------------------------------------------------------------------------------
// Version-1 payload shapes. Each record reproduces, property for property and in the same order,
// the anonymous object its writer serialized reflectively before these contracts existed (no
// naming policy, so the camelCase names were the anonymous members' own spelling). The
// SecurityGovernanceJsonContext camelCase policy regenerates exactly those names; the one
// PascalCase shape (rebuild record entries) pins its names explicitly. Enum-valued members stay
// strings because writers always stored ToString() output, and a newer node's value must still read.
// Changing a shape means a new payload version with a reader for the old one, never an edit here.
// ---------------------------------------------------------------------------------------------

/// <summary><c>Aliases</c> / <c>AliasRevisionRecorded</c>: one explicit alias revision.</summary>
public sealed record AliasRevisionRecordedPayload(
    Guid AliasId,
    string AliasKind,
    string AliasValue,
    string? Provider,
    string Scope,
    string? Reason,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool IsEnabled) : ISecurityGovernancePayload;

/// <summary><c>Aliases</c> / <c>AliasRevisionsSynchronized</c>: revisions appended by projection replacement.</summary>
public sealed record AliasRevisionsSynchronizedPayload(int AppendedRevisions) : ISecurityGovernancePayload;

/// <summary><c>ProjectionRebuild</c> / <c>ProjectionBatchRebuilt</c>: one persisted rebuild batch.</summary>
public sealed record ProjectionBatchRebuiltPayload(
    string ProjectionName,
    long LastGlobalSequence,
    IReadOnlyList<ProjectionBatchRebuiltRecord> Records) : ISecurityGovernancePayload;

/// <summary>A record written by a rebuild batch. Version 1 stored these two names in PascalCase.</summary>
public sealed record ProjectionBatchRebuiltRecord(
    [property: JsonPropertyName("SecurityId")] Guid SecurityId,
    [property: JsonPropertyName("Version")] long Version);

/// <summary><c>OperatorOverrides</c> / <c>Patched</c>: staged overlay values awaiting review.</summary>
public sealed record OperatorOverridesPatchedPayload(
    IReadOnlyList<string> SetKeys,
    IReadOnlyList<string> RemovedKeys,
    IReadOnlyDictionary<string, string> PreviousValues,
    IReadOnlyDictionary<string, string> Values,
    string? ReasonCode,
    string ApprovalStatus,
    long? ExpectedCanonicalVersion) : ISecurityGovernancePayload;

/// <summary><c>OperatorOverrides</c> / <c>Approved</c> or <c>Rejected</c>: a recorded review decision.</summary>
public sealed record OperatorOverridesDecisionPayload(
    string Decision,
    string Reviewer,
    string StagedBy,
    string? Comment,
    string? ReasonCode,
    bool RestoresPriorDecision,
    IReadOnlyDictionary<string, string> Values) : ISecurityGovernancePayload;

/// <summary>A declared underlying link as the <c>UnderlyingGraph</c> stream records it.</summary>
public sealed record UnderlyingLinkPayload(
    Guid LinkId,
    Guid SecurityId,
    string Role,
    Guid? UnderlyingSecurityId,
    string? Reference,
    decimal? Weight,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    string? Notes,
    string DeclaredBy,
    DateTimeOffset DeclaredAt);

/// <summary><c>UnderlyingGraph</c> / <c>UnderlyingLinkDeclared</c> or <c>UnderlyingLinkAmended</c>.</summary>
public sealed record UnderlyingLinkDeclaredPayload(
    UnderlyingLinkPayload Link,
    UnderlyingLinkPayload? Previous,
    IReadOnlyList<string> Changes) : ISecurityGovernancePayload;

/// <summary><c>UnderlyingGraph</c> / <c>UnderlyingLinkRemoved</c>: the removed declaration.</summary>
public sealed record UnderlyingLinkRemovedPayload(UnderlyingLinkPayload Link) : ISecurityGovernancePayload;

/// <summary><c>Curves</c> / <c>CurveDefined</c>.</summary>
public sealed record CurveDefinedPayload(
    Guid CurveId,
    string CurveKey,
    string Kind,
    string Currency,
    string? IndexName,
    string? Tenor,
    string DayCount,
    string Interpolation,
    string Source,
    string? Description) : ISecurityGovernancePayload;

/// <summary><c>Curves</c> / <c>CurveVersionPublished</c>.</summary>
public sealed record CurveVersionPublishedPayload(
    Guid CurveId,
    Guid CurveVersionId,
    string CurveKey,
    string Kind,
    string Currency,
    string? IndexName,
    long Version,
    DateOnly AsOf,
    int PointCount,
    string ContentSha256,
    string Source,
    DateTimeOffset? SourcedAt) : ISecurityGovernancePayload;

/// <summary>A collateral movement inside an exposure-snapshot payload.</summary>
public sealed record OtcCollateralMovementPayload(string Kind, string Mover, decimal Amount, string Currency);

/// <summary><c>OtcCollateral</c> / <c>ExposureSnapshotRecorded</c>.</summary>
public sealed record OtcExposureSnapshotRecordedPayload(
    Guid SnapshotId,
    Guid NettingSetId,
    Guid? CollateralAgreementId,
    DateOnly AsOfDate,
    int Version,
    string Status,
    string NettingBasis,
    string Provenance,
    string Currency,
    decimal NetExposure,
    decimal GrossPositive,
    decimal GrossNegative,
    IReadOnlyList<OtcCollateralMovementPayload> Movements,
    int BlockerCount,
    string PayloadSha256) : ISecurityGovernancePayload;

/// <summary>A collateral balance as the <c>OtcCollateral</c> stream records it.</summary>
public sealed record OtcCollateralBalancePayload(
    Guid BalanceId,
    Guid CollateralAgreementId,
    string Direction,
    string Kind,
    string EligibleCollateralDescription,
    Guid? SecurityId,
    string Currency,
    decimal Quantity,
    decimal UnitValue,
    decimal ValuedAmount,
    decimal HaircutPercent,
    DateOnly ValuedAsOf,
    string? Reference,
    string? Notes,
    long Version,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary><c>OtcCollateral</c> / <c>CollateralBalanceRecorded</c> or <c>CollateralBalanceAmended</c>.</summary>
public sealed record OtcCollateralBalanceChangedPayload(
    Guid MasterAgreementId,
    OtcCollateralBalancePayload Balance,
    OtcCollateralBalancePayload? Previous,
    IReadOnlyList<string> Changes) : ISecurityGovernancePayload;

/// <summary>A margin call as the <c>OtcCollateral</c> stream records it.</summary>
public sealed record OtcMarginCallPayload(
    Guid CallId,
    Guid NettingSetId,
    Guid CollateralAgreementId,
    Guid SnapshotId,
    string Kind,
    string Mover,
    string Currency,
    decimal AmountCalled,
    decimal? AmountAgreed,
    string Status,
    DateOnly IssuedOn,
    DateOnly? DueOn,
    string? DisputeReason,
    DateOnly? SettledOn,
    decimal? SettledAmount,
    string? Notes,
    long Version,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary>The exposure snapshot a margin call was issued from.</summary>
public sealed record OtcMarginCallSnapshotReferencePayload(
    Guid SnapshotId,
    int Version,
    DateOnly AsOfDate,
    string PayloadSha256);

/// <summary><c>OtcCollateral</c> / <c>MarginCallIssued</c>.</summary>
public sealed record OtcMarginCallIssuedPayload(
    OtcMarginCallPayload Call,
    OtcMarginCallSnapshotReferencePayload Snapshot) : ISecurityGovernancePayload;

/// <summary><c>OtcCollateral</c> / <c>MarginCallAgreed</c>, <c>MarginCallDisputed</c>, <c>MarginCallSettled</c> or <c>MarginCallCancelled</c>.</summary>
public sealed record OtcMarginCallTransitionedPayload(
    OtcMarginCallPayload Call,
    OtcMarginCallPayload Previous,
    string Action,
    string? Reason) : ISecurityGovernancePayload;

/// <summary>
/// Source-generated metadata for every governance payload version. The camelCase policy and the
/// default encoder and null handling reproduce the reflective <c>JsonSerializer.Serialize(object)</c>
/// output the version-1 writers produced; dictionary keys are written as stored.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AliasRevisionRecordedPayload))]
[JsonSerializable(typeof(AliasRevisionsSynchronizedPayload))]
[JsonSerializable(typeof(ProjectionBatchRebuiltPayload))]
[JsonSerializable(typeof(OperatorOverridesPatchedPayload))]
[JsonSerializable(typeof(OperatorOverridesDecisionPayload))]
[JsonSerializable(typeof(UnderlyingLinkDeclaredPayload))]
[JsonSerializable(typeof(UnderlyingLinkRemovedPayload))]
[JsonSerializable(typeof(CurveDefinedPayload))]
[JsonSerializable(typeof(CurveVersionPublishedPayload))]
[JsonSerializable(typeof(OtcExposureSnapshotRecordedPayload))]
[JsonSerializable(typeof(OtcCollateralBalanceChangedPayload))]
[JsonSerializable(typeof(OtcMarginCallIssuedPayload))]
[JsonSerializable(typeof(OtcMarginCallTransitionedPayload))]
public sealed partial class SecurityGovernanceJsonContext : JsonSerializerContext;

/// <summary>
/// The declared payload contract of one governance stream/event type: its payload type, the
/// version new writes carry, and a reader for every version that may be stored.
/// </summary>
public sealed class SecurityGovernancePayloadContract
{
    private readonly JsonTypeInfo _typeInfo;
    private readonly IReadOnlyDictionary<int, Func<JsonElement, object?>> _readers;

    private SecurityGovernancePayloadContract(
        string stream,
        string eventType,
        Type payloadType,
        int currentVersion,
        JsonTypeInfo typeInfo,
        IReadOnlyDictionary<int, Func<JsonElement, object?>> readers)
    {
        Stream = stream;
        EventType = eventType;
        PayloadType = payloadType;
        CurrentVersion = currentVersion;
        _typeInfo = typeInfo;
        _readers = readers;
    }

    public string Stream { get; }
    public string EventType { get; }
    public Type PayloadType { get; }

    /// <summary>The <c>payload_version</c> new writes record.</summary>
    public int CurrentVersion { get; }

    /// <summary>The stored versions this node can read, ascending.</summary>
    public IReadOnlyList<int> ReadableVersions => _readers.Keys.Order().ToArray();

    /// <summary>
    /// A version-1 contract. Version 1 is every row written before payload versions were set
    /// explicitly (the column default, migration 040), so its reader must accept those shapes.
    /// A later version adds a reader that upcasts the older JSON to the current type.
    /// </summary>
    internal static SecurityGovernancePayloadContract Initial<TPayload>(string stream, string eventType, JsonTypeInfo<TPayload> typeInfo)
        where TPayload : class, ISecurityGovernancePayload
        => new(
            stream,
            eventType,
            typeof(TPayload),
            SecurityGovernancePayloadContracts.InitialPayloadVersion,
            typeInfo,
            new Dictionary<int, Func<JsonElement, object?>>
            {
                [SecurityGovernancePayloadContracts.InitialPayloadVersion] = element => element.Deserialize(typeInfo)
            });

    internal string Serialize(object payload) => JsonSerializer.Serialize(payload, _typeInfo);

    internal object Read(int payloadVersion, JsonElement payload)
    {
        if (!_readers.TryGetValue(payloadVersion, out var reader))
            throw new SecurityGovernancePayloadVersionException(Stream, EventType, payloadVersion, ReadableVersions);

        return reader(payload)
            ?? throw new JsonException($"Governance payload {Stream}/{EventType} version {payloadVersion} is null.");
    }
}

/// <summary>The registry of declared governance payload contracts, keyed by stream and event type.</summary>
public static class SecurityGovernancePayloadContracts
{
    /// <summary>The version of every payload shape written before explicit versioning (migration 040's default).</summary>
    public const int InitialPayloadVersion = 1;

    private static readonly SecurityGovernanceJsonContext Json = SecurityGovernanceJsonContext.Default;

    /// <summary>Every declared contract.</summary>
    public static IReadOnlyList<SecurityGovernancePayloadContract> All { get; } =
    [
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.Aliases, "AliasRevisionRecorded", Json.AliasRevisionRecordedPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.Aliases, "AliasRevisionsSynchronized", Json.AliasRevisionsSynchronizedPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.ProjectionRebuild, "ProjectionBatchRebuilt", Json.ProjectionBatchRebuiltPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.OperatorOverrides, "Patched", Json.OperatorOverridesPatchedPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.OperatorOverrides, "Approved", Json.OperatorOverridesDecisionPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.OperatorOverrides, "Rejected", Json.OperatorOverridesDecisionPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.UnderlyingGraph, SecurityUnderlyingLinkWriter.DeclaredEvent, Json.UnderlyingLinkDeclaredPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.UnderlyingGraph, SecurityUnderlyingLinkWriter.AmendedEvent, Json.UnderlyingLinkDeclaredPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.UnderlyingGraph, SecurityUnderlyingLinkWriter.RemovedEvent, Json.UnderlyingLinkRemovedPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.Curves, PostgresSecurityCurveStore.DefinedEvent, Json.CurveDefinedPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.Curves, PostgresSecurityCurveStore.VersionPublishedEvent, Json.CurveVersionPublishedPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.OtcCollateral, PostgresOtcCollateralStore.SnapshotRecordedEvent, Json.OtcExposureSnapshotRecordedPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.OtcCollateral, PostgresOtcCollateralStore.BalanceRecordedEvent, Json.OtcCollateralBalanceChangedPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.OtcCollateral, PostgresOtcCollateralStore.BalanceAmendedEvent, Json.OtcCollateralBalanceChangedPayload),
        SecurityGovernancePayloadContract.Initial(SecurityGovernanceStreams.OtcCollateral, PostgresOtcCollateralStore.CallIssuedEvent, Json.OtcMarginCallIssuedPayload),
        .. new[]
        {
            Meridian.Contracts.SecurityMaster.OtcMarginCallAction.Agree,
            Meridian.Contracts.SecurityMaster.OtcMarginCallAction.Dispute,
            Meridian.Contracts.SecurityMaster.OtcMarginCallAction.Settle,
            Meridian.Contracts.SecurityMaster.OtcMarginCallAction.Cancel
        }.Select(static action => SecurityGovernancePayloadContract.Initial(
            SecurityGovernanceStreams.OtcCollateral,
            Meridian.Contracts.SecurityMaster.OtcMarginCallRules.EventType(action),
            Json.OtcMarginCallTransitionedPayload))
    ];

    private static readonly IReadOnlyDictionary<(string Stream, string EventType), SecurityGovernancePayloadContract> ByKey =
        All.ToDictionary(static contract => (contract.Stream, contract.EventType));

    /// <summary>The contract declared for <paramref name="stream"/>/<paramref name="eventType"/>, if any.</summary>
    public static bool TryResolve(string stream, string eventType, out SecurityGovernancePayloadContract contract)
        => ByKey.TryGetValue((stream, eventType), out contract!);

    /// <summary>The contract declared for <paramref name="stream"/>/<paramref name="eventType"/>.</summary>
    public static SecurityGovernancePayloadContract Resolve(string stream, string eventType)
        => TryResolve(stream, eventType, out var contract)
            ? contract
            : throw new SecurityGovernancePayloadContractException(stream, eventType);

    internal static SecurityGovernancePayloadContract ResolveForWrite<TPayload>(string stream, string eventType)
        where TPayload : class, ISecurityGovernancePayload
    {
        var contract = Resolve(stream, eventType);
        if (contract.PayloadType != typeof(TPayload))
        {
            throw new ArgumentException(
                $"Governance event {stream}/{eventType} records {contract.PayloadType.Name}, not {typeof(TPayload).Name}.");
        }

        return contract;
    }

    /// <summary>
    /// Reads a stored payload through its declared contract, dispatching on
    /// <paramref name="payloadVersion"/>. Throws when the event type has no contract, the version has
    /// no reader on this node, or <typeparamref name="TPayload"/> is not the declared payload type.
    /// </summary>
    public static TPayload Read<TPayload>(string stream, string eventType, int payloadVersion, JsonElement payload)
        where TPayload : class, ISecurityGovernancePayload
    {
        var contract = Resolve(stream, eventType);
        if (!typeof(TPayload).IsAssignableFrom(contract.PayloadType))
        {
            throw new InvalidOperationException(
                $"Governance event {stream}/{eventType} records {contract.PayloadType.Name}, not {typeof(TPayload).Name}.");
        }

        return (TPayload)contract.Read(payloadVersion, payload);
    }
}

/// <summary>A governance event's stream/event type has no declared payload contract on this node.</summary>
public sealed class SecurityGovernancePayloadContractException(string stream, string eventType)
    : NotSupportedException($"No governance payload contract is declared for {stream}/{eventType}.")
{
    public string Stream { get; } = stream;
    public string EventType { get; } = eventType;
}

/// <summary>A stored governance payload's version has no reader on this node (for example, a newer writer's version).</summary>
public sealed class SecurityGovernancePayloadVersionException(string stream, string eventType, int payloadVersion, IReadOnlyList<int> readableVersions)
    : NotSupportedException(
        $"Governance payload {stream}/{eventType} version {payloadVersion} cannot be read; readable versions: {string.Join(", ", readableVersions)}.")
{
    public string Stream { get; } = stream;
    public string EventType { get; } = eventType;
    public int PayloadVersion { get; } = payloadVersion;
}
