namespace Meridian.Contracts.Workstation;

/// <summary>Versioned, non-authoritative preparer assertions. These are never approval receipts.</summary>
public static class SinglePrincipalAttestationPolicy
{
    public const string Version = "single-principal-evidence-v1";
    public const string EvidenceAcknowledgment = "ATTEST-EVIDENCE-ACK";
    public const string DraftCheck = "ATTEST-DRAFT-CHECK";
    public const string Disclosure = "Attested by preparer — not independently reviewed";
    public const string Confirmation = "I inspected the exact retained evidence and accept responsibility for this preparer assertion. This is not independent review or approval.";
    public const string DecisionKind = "PreparerAttestation";
    public const int SchemaVersion = 1;
}

public sealed record SinglePrincipalAttestationScopeDto(
    string TenantId,
    string CompanyId,
    Guid? LedgerBookId = null);

public sealed record ChangeSinglePrincipalDeclarationRequestDto(
    Guid CommandId,
    long ExpectedRevision,
    bool Enabled,
    string PrincipalId,
    string PolicyVersion,
    string Reason,
    OperationsActionOriginDto Origin = OperationsActionOriginDto.HumanOperator);

public sealed record RecordPreparerAttestationRequestDto(
    Guid CommandId,
    long ExpectedDeclarationRevision,
    string ActionId,
    string SubjectKind,
    string SubjectId,
    string SubjectRevision,
    string SubjectHash,
    string Reason,
    string Confirmation,
    OperationsActionOriginDto Origin = OperationsActionOriginDto.HumanOperator);

public sealed record RevokePreparerAttestationRequestDto(
    Guid CommandId,
    Guid ReceiptId,
    long ExpectedReceiptRevision,
    string Reason,
    OperationsActionOriginDto Origin = OperationsActionOriginDto.HumanOperator);

public sealed record SinglePrincipalDeclarationDto(
    Guid DeclarationId,
    long Revision,
    SinglePrincipalAttestationScopeDto Scope,
    bool Enabled,
    string PrincipalId,
    string PolicyVersion,
    string PolicyHash,
    string ChangedBy,
    DateTimeOffset RecordedAt,
    string Reason);

/// <summary>The assertion has its own task identity; source creator/importer is retained separately.</summary>
public sealed record PreparerAttestationReceiptDto(
    Guid ReceiptId,
    Guid CommandId,
    Guid AssertionId,
    int SchemaVersion,
    string DecisionKind,
    string Disclosure,
    SinglePrincipalAttestationScopeDto Scope,
    string ActionId,
    string SubjectKind,
    string SubjectId,
    string SubjectRevision,
    string SubjectHash,
    string SourceArtifactId,
    string SourceHash,
    string? SourceActor,
    string SourceProvenance,
    string PreparedBy,
    string AttestedBy,
    OperationsActionOriginDto Origin,
    Guid DeclarationId,
    long DeclarationRevision,
    string PolicyVersion,
    string PolicyHash,
    DateTimeOffset RecordedAt,
    string Reason,
    string Confirmation,
    IReadOnlyList<string> Checks,
    string? SourceSubjectKind = null,
    string? SourceSubjectId = null,
    string? SubjectSnapshotJson = null);

public sealed record PreparerAttestationRevocationDto(
    Guid CommandId,
    Guid ReceiptId,
    long Revision,
    string Actor,
    DateTimeOffset RecordedAt,
    string Reason);

public sealed record SinglePrincipalAttestationAuditEventDto(
    Guid CommandId,
    long Sequence,
    string Action,
    string Actor,
    DateTimeOffset RecordedAt,
    string Reason,
    Guid? ReceiptId = null);

public sealed record SinglePrincipalAttestationStateDto(
    SinglePrincipalAttestationScopeDto Scope,
    bool PolicyEnabled,
    string PolicyVersion,
    string PolicyHash,
    string Disclosure,
    string Confirmation,
    IReadOnlyList<string> AllowedActions,
    SinglePrincipalDeclarationDto? Declaration,
    IReadOnlyList<SinglePrincipalDeclarationDto> DeclarationHistory,
    IReadOnlyList<PreparerAttestationReceiptDto> Receipts,
    IReadOnlyList<PreparerAttestationRevocationDto> Revocations,
    IReadOnlyList<SinglePrincipalAttestationAuditEventDto> AuditTrail);

public sealed record SinglePrincipalAttestationWorkspaceDto(
    SinglePrincipalAttestationStateDto State,
    string Actor,
    bool CanAdministerDeclaration,
    bool CanAttest,
    bool CanRevoke);

/// <summary>Server-resolved target revision and source identity; it is not an authority grant.</summary>
public sealed record SinglePrincipalAttestationSubjectSnapshotDto(
    string SubjectKind,
    string SubjectId,
    string Revision,
    string Hash,
    string SourceArtifactId,
    string SourceHash,
    string? SourceActor,
    string SourceProvenance,
    bool Eligible,
    IReadOnlyList<string> Checks,
    string? SourceSubjectKind = null,
    string? SourceSubjectId = null,
    string? SubjectSnapshotJson = null);
