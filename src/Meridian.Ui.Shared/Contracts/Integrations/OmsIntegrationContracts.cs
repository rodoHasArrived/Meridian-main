using System.Text.Json.Serialization;

namespace Meridian.Ui.Shared.Contracts.Integrations;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<OmsIntegrationScope>))]
public enum OmsIntegrationScope
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    Diagnostics = 1 << 2,
    Admin = 1 << 3
}

[JsonConverter(typeof(JsonStringEnumConverter<OmsIntegrationDirection>))]
public enum OmsIntegrationDirection
{
    Inbound,
    Outbound
}

[JsonConverter(typeof(JsonStringEnumConverter<OmsAdapterKind>))]
public enum OmsAdapterKind
{
    Fix,
    Sftp,
    FileDrop,
    ExcelBridge
}

[JsonConverter(typeof(JsonStringEnumConverter<OmsSyncMode>))]
public enum OmsSyncMode
{
    Pull,
    Push
}

[JsonConverter(typeof(JsonStringEnumConverter<OmsConflictResolutionPolicy>))]
public enum OmsConflictResolutionPolicy
{
    TimestampPrecedence,
    PullWins,
    PushWins,
    RejectOnConflict
}

public sealed record OmsInboundMessage(
    string SourceSystem,
    string ExternalOrderId,
    string EventType,
    DateTimeOffset EventTimestampUtc,
    string PayloadHash,
    string? DeduplicationKey,
    string CorrelationId);

public sealed record OmsOutboundContract(
    string MeridianOrderId,
    string ExternalOrderId,
    string Status,
    DateTimeOffset UpdatedAtUtc,
    string Version);

public sealed record OmsIngestionResult(string DeduplicationKey, bool ReplayDetected, string Detail);

public sealed record OmsRetryPolicy(
    int MaxAttempts,
    TimeSpan InitialDelay,
    TimeSpan MaxDelay,
    double BackoffMultiplier,
    bool UseJitter);

public sealed record OmsAdapterBoundary(
    OmsAdapterKind Kind,
    string AdapterId,
    string Transport,
    OmsIntegrationDirection Direction,
    OmsRetryPolicy RetryPolicy,
    bool RequiresRequestSigning,
    IReadOnlySet<OmsIntegrationScope> RequiredScopes);

public sealed record OmsAdapterDiagnostics(
    string Adapter,
    string Channel,
    int RetryCount,
    string LastStatus,
    DateTimeOffset CheckedAtUtc,
    OmsAdapterBoundary? Boundary = null,
    string? LastError = null,
    DateTimeOffset? NextRetryAtUtc = null);

public sealed record OmsSyncRecord(string EntityId, DateTimeOffset UpdatedAtUtc, IReadOnlyDictionary<string, string> Fields);

public sealed record OmsSyncRequest(string Mode, string EntityId, string CorrelationId, OmsSyncRecord PullRecord, OmsSyncRecord PushRecord);

public sealed record OmsSyncConflictResult(string Mode, OmsSyncRecord Winner, string Policy, bool ConflictDetected = true, string? Reason = null);

public sealed record OmsIntegrationAuditEntry(
    DateTimeOffset TimestampUtc,
    string ScopeId,
    string Action,
    string Actor,
    string CorrelationId,
    OmsIntegrationScope Scope = OmsIntegrationScope.None,
    string? RequestSignatureKeyId = null,
    string? Detail = null);

public sealed record OmsRequestSignatureInput(
    string KeyId,
    string TimestampUtc,
    string Signature,
    string CanonicalPayload);

public sealed record OmsRequestSignatureResult(bool IsValid, string Detail, string? KeyId = null);

public sealed record OmsKeyRotationRequest(string KeyId, string RequestedBy, string CorrelationId, DateTimeOffset EffectiveAtUtc);

public sealed record OmsKeyRotationResult(string KeyId, string Status, DateTimeOffset EffectiveAtUtc, string CorrelationId);

public interface IOmsInboundIngestionPipeline
{
    OmsIngestionResult Ingest(OmsInboundMessage message, OmsRequestSignatureInput? signature = null);
    IReadOnlyCollection<OmsInboundMessage> Snapshot();
}

public interface IOmsAdapterDiagnosticsProvider
{
    IReadOnlyCollection<OmsAdapterDiagnostics> AdapterDiagnostics();
}

public interface IExcelBridgeSyncService
{
    OmsSyncConflictResult ResolveSyncConflict(OmsSyncRequest request, OmsRequestSignatureInput? signature = null);
}

public interface IOmsIntegrationAuditReader
{
    IReadOnlyCollection<OmsIntegrationAuditEntry> AuditTrail(int take = 200);
}

public interface IOmsRequestSigningService
{
    OmsRequestSignatureResult VerifyRequestSignature(OmsRequestSignatureInput signature);
    OmsKeyRotationResult RotateSigningKey(OmsKeyRotationRequest request);
}

public interface IOmsIntegrationApiHandler :
    IOmsInboundIngestionPipeline,
    IOmsAdapterDiagnosticsProvider,
    IExcelBridgeSyncService,
    IOmsIntegrationAuditReader,
    IOmsRequestSigningService
{
}
