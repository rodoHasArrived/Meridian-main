using Meridian.Contracts.Workstation;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>One selected USD book for the bounded economic attestation policy.</summary>
public sealed record CashDividendAttestationDeclarationDto(
    Guid DeclarationId,
    long Revision,
    string TenantId,
    string CompanyId,
    Guid LedgerBookId,
    bool Enabled,
    string PrincipalId,
    string PolicyVersion,
    string PolicyHash,
    string ChangedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CashDividendAccountRoleBindingDto? AccountRoleBinding = null);

public sealed record CashDividendDeclarationAuthorityDto(
    bool CanAdministerDeclaration,
    OperationsActionOriginDto Origin = OperationsActionOriginDto.HumanOperator,
    bool CanBindAccountingRoles = false);

public sealed record ChangeCashDividendAttestationDeclarationRequestDto(
    Guid CommandId,
    long ExpectedRevision,
    string TenantId,
    string CompanyId,
    Guid LedgerBookId,
    bool Enabled,
    string PolicyVersion,
    string PolicyHash,
    string Reason,
    string Actor,
    CashDividendDeclarationAuthorityDto? Authority = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CashDividendAccountRoleBindingDto? AccountRoleBinding = null);

public sealed record CashDividendAttestationDeclarationResultDto(
    CashDividendAttestationDeclarationDto Declaration,
    bool Replayed);

public sealed record CashDividendAttestationDeclarationStateDto(
    string TenantId,
    string CompanyId,
    bool PolicyEnabled,
    string PolicyVersion,
    string PolicyHash,
    CashDividendAttestationDeclarationDto? Declaration,
    IReadOnlyList<CashDividendAttestationDeclarationDto> History);
