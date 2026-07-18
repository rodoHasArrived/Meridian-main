using System.Text.Json.Serialization;

namespace Meridian.Contracts.Lifecycle;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(RuntimeLifecycleState))]
[JsonSerializable(typeof(RuntimeReadinessStatus))]
[JsonSerializable(typeof(LifecycleCheckRequirement))]
[JsonSerializable(typeof(LifecycleCheckStatus))]
[JsonSerializable(typeof(LifecycleShutdownReason))]
[JsonSerializable(typeof(LifecycleShutdownStage))]
[JsonSerializable(typeof(LifecycleShutdownOutcome))]
[JsonSerializable(typeof(LifecycleDatabaseManagementMode))]
[JsonSerializable(typeof(RuntimeLifecycleCheckDto))]
[JsonSerializable(typeof(IReadOnlyList<RuntimeLifecycleCheckDto>))]
[JsonSerializable(typeof(RuntimeLifecycleSnapshotDto))]
[JsonSerializable(typeof(LifecycleShutdownRequestDto))]
[JsonSerializable(typeof(LifecycleShutdownAcceptedDto))]
[JsonSerializable(typeof(LifecycleShutdownStageDto))]
[JsonSerializable(typeof(IReadOnlyList<LifecycleShutdownStageDto>))]
[JsonSerializable(typeof(LifecycleShutdownOperationDto))]
[JsonSerializable(typeof(LifecycleShutdownParticipantReceiptDto))]
[JsonSerializable(typeof(IReadOnlyList<LifecycleShutdownParticipantReceiptDto>))]
[JsonSerializable(typeof(LifecycleShutdownReceiptDto))]
[JsonSerializable(typeof(LifecycleSessionReceiptDto))]
[JsonSerializable(typeof(LifecycleSupervisorManifestDto))]
[JsonSerializable(typeof(LifecycleOwnedProcessDto))]
[JsonSerializable(typeof(LifecycleDatabaseIdentityDto))]
[JsonSerializable(typeof(LifecycleSupervisorStatusDto))]
[JsonSerializable(typeof(LifecycleSupervisorMessageDto))]
public sealed partial class LifecycleContractsJsonContext : JsonSerializerContext;
