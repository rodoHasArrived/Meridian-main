namespace Meridian.Contracts.Workstation;

/// <summary>An immutable amount address. Tenant and company are checked against the authenticated request.</summary>
public sealed record AmountPassportIdentityDto(
    string SubjectKind,
    string SubjectId,
    AmountPassportScopeDto Scope,
    string AsOf,
    string Revision);

public sealed record AmountPassportScopeDto(
    string TenantId,
    string CompanyId,
    string Basis,
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    Guid? PeriodId = null,
    string? EntityId = null,
    string? AccountId = null,
    string? OrganizationId = null);

/// <summary>Small server-issued handle projected beside an amount in its source view.</summary>
public sealed record AmountPassportReferenceDto(
    AmountPassportIdentityDto Identity,
    string? Currency,
    string Unit,
    string Origin,
    bool IsZero,
    string? Freshness = null,
    string? ExactValue = null);

public sealed record AmountPassportFreshnessDto(
    string Status,
    DateTimeOffset EvaluatedAt,
    string Policy,
    string Detail,
    DateTimeOffset? ObservedAt = null,
    long? AgeMinutes = null);

public sealed record AmountPassportPermissionsDto(string View, string Amend, string Detail, string? PeriodStatus = null);

public sealed record AmountPassportComponentDto(string Component, string Status, string Detail);

public sealed record AmountPassportRecordDto(
    string Kind,
    string Id,
    string Label,
    string? Href = null,
    AmountPassportIdentityDto? Passport = null,
    string? ContentHash = null);

/// <summary>Server-resolved facts. ExactValue is a decimal string and may be absent for unavailable evidence.</summary>
public sealed record AmountPassportDto(
    AmountPassportReferenceDto Reference,
    string Label,
    string? ExactValue,
    string Status,
    string? EffectiveDate,
    string? Calculation,
    string? ExchangeRateRule,
    AmountPassportFreshnessDto Freshness,
    AmountPassportPermissionsDto Permissions,
    IReadOnlyList<AmountPassportComponentDto> Components,
    IReadOnlyList<AmountPassportRecordDto> Sources,
    IReadOnlyList<AmountPassportRecordDto> Evidence,
    IReadOnlyList<AmountPassportRecordDto> History,
    IReadOnlyList<string> Gaps)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AccountingPreparerAttestationDisclosureDto>? AccountingPreparerAttestations { get; init; }
}
