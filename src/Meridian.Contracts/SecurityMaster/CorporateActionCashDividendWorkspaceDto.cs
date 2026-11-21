namespace Meridian.Contracts.SecurityMaster;

/// <summary>Server-owned C2 scope, capabilities and exact preview; never a client eligibility calculation.</summary>
public sealed record CorporateActionCashDividendWorkspaceDto(
    int SchemaVersion,
    string Availability,
    string PolicyVersion,
    string PolicyHash,
    bool PolicyEnabled,
    string Actor,
    CorporateActionCaseScopeDto Scope,
    CorporateActionProcessingCaseDto Case,
    bool CanAdministerDeclaration,
    bool CanAttestAccounting,
    bool CanPostAttestedAccounting,
    CashDividendAttestationDeclarationDto? Declaration,
    IReadOnlyList<CashDividendAttestationDeclarationDto> DeclarationHistory,
    CorporateActionCashDividendPreviewDto? Preview,
    CorporateActionCashDividendAttestationDto? Attestation,
    CorporateActionCashDividendPostingDto? Posting,
    string? EvidenceReference,
    string? EvidenceHash,
    string Disclosure,
    string Confirmation,
    decimal RelatedEventGrossLimitUsd,
    decimal BookCalendarMonthGrossLimitUsd,
    IReadOnlyList<string> Blockers,
    bool CanBindAccountingRoles = false);
