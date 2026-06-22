namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Application-owned port for appending corporate actions through a shared
/// validation and audit path.
/// </summary>
public interface ISecurityMasterCorporateActionCommandService
{
    Task<SecurityMasterCorporateActionAppendResultDto> AppendAsync(
        SecurityMasterCorporateActionAppendRequestDto request,
        CancellationToken ct = default);
}

public sealed record SecurityMasterCorporateActionAppendRequestDto(
    Guid SecurityId,
    CorporateActionDto CorporateAction,
    string SourceSystem,
    string Actor,
    string? SourceRecordId = null,
    string? Reason = null,
    string? CorrelationId = null);

public sealed record SecurityMasterCorporateActionAppendResultDto(
    CorporateActionDto CorporateAction,
    SecurityMasterCorporateActionAuditDto Audit);

public sealed record SecurityMasterCorporateActionAuditDto(
    string AuditId,
    Guid SecurityId,
    Guid CorporateActionId,
    string EventType,
    string SourceSystem,
    string Actor,
    DateTimeOffset RecordedAtUtc,
    string? SourceRecordId,
    string? Reason,
    string? CorrelationId);
