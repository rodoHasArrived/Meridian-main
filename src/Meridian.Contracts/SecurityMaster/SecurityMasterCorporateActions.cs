using Meridian.Contracts.Workstation;

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

/// <summary>
/// <see cref="FundProfileId"/> optionally scopes downstream restatement-impact resolution
/// when the append supersedes a prior event; a null scope yields an unscoped impact and no
/// restatement proposal, mirroring the revision-publish path's deliberate scoping rule.
/// </summary>
public sealed record SecurityMasterCorporateActionAppendRequestDto(
    Guid SecurityId,
    CorporateActionDto CorporateAction,
    string SourceSystem,
    string Actor,
    string? SourceRecordId = null,
    string? Reason = null,
    string? CorrelationId = null,
    string? FundProfileId = null);

public sealed record SecurityMasterCorporateActionAppendResultDto(
    CorporateActionDto CorporateAction,
    SecurityMasterCorporateActionAuditDto Audit,
    SecurityMasterCorporateActionRestatementDto? Restatement = null);

/// <summary>
/// Period-aware restatement outcome for a superseding corporate action (amendment or
/// cancellation) whose economics land in a closed ledger period: whether a governed
/// restatement proposal is required, and the report packs proposed for it. The operator
/// approves each candidate via the existing governed report-pack restatement path.
/// </summary>
public sealed record SecurityMasterCorporateActionRestatementDto(
    bool RestatementRequired,
    IReadOnlyList<RestatementCandidateDto> Candidates);

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
