using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

/// <summary>Operator-facing draft that creates the initial organization-to-portfolio structure in one governed workflow.</summary>
public sealed record FundStructureSetupDraftDto(
    FundStructureSetupOrganizationDraftDto Organization,
    FundStructureSetupBusinessDraftDto BusinessLane,
    FundStructureSetupClientOrFundDraftDto ClientOrFund,
    FundStructureSetupLegalEntityDraftDto LegalEntity,
    FundStructureSetupVehicleDraftDto Vehicle,
    FundStructureSetupInvestmentPortfolioDraftDto InvestmentPortfolio,
    FundStructureSetupAccountHandoffDraftDto AccountHandoff,
    IReadOnlyList<FundStructureSetupOwnershipLinkDraftDto>? InitialOwnershipLinks = null,
    DateTimeOffset? EffectiveFrom = null,
    string? RequestedBy = null);

public sealed record FundStructureSetupOrganizationDraftDto(
    Guid? OrganizationId,
    string Code,
    string Name,
    string BaseCurrency,
    string? Description = null);

public sealed record FundStructureSetupBusinessDraftDto(
    Guid? BusinessId,
    BusinessKindDto BusinessKind,
    string Code,
    string Name,
    string BaseCurrency,
    string? Description = null);

public sealed record FundStructureSetupClientOrFundDraftDto(
    Guid? ClientId,
    Guid? FundId,
    bool CreateClient,
    string Code,
    string Name,
    string BaseCurrency,
    string? Description = null,
    ClientSegmentKind ClientSegmentKind = ClientSegmentKind.Unspecified);

public sealed record FundStructureSetupLegalEntityDraftDto(
    Guid? EntityId,
    LegalEntityTypeDto EntityType,
    string Code,
    string Name,
    string Jurisdiction,
    string BaseCurrency,
    string? Description = null);

public sealed record FundStructureSetupVehicleDraftDto(
    Guid? VehicleId,
    string Code,
    string Name,
    string BaseCurrency,
    string? Description = null);

public sealed record FundStructureSetupInvestmentPortfolioDraftDto(
    Guid? InvestmentPortfolioId,
    string Code,
    string Name,
    string BaseCurrency,
    string? Description = null);

public sealed record FundStructureSetupAccountHandoffDraftDto(
    string AccountCode,
    string DisplayName,
    AccountTypeDto AccountType,
    string BaseCurrency,
    string? Institution = null,
    string? LedgerReference = null,
    string? Notes = null);

public sealed record FundStructureSetupOwnershipLinkDraftDto(
    Guid? OwnershipLinkId,
    FundStructureSetupNodeAlias Parent,
    FundStructureSetupNodeAlias Child,
    OwnershipRelationshipTypeDto RelationshipType,
    decimal? OwnershipPercent = null,
    bool IsPrimary = false,
    string? Notes = null);

[JsonConverter(typeof(JsonStringEnumConverter<FundStructureSetupNodeAlias>))]
public enum FundStructureSetupNodeAlias
{
    Organization,
    BusinessLane,
    ClientOrFund,
    LegalEntity,
    Vehicle,
    InvestmentPortfolio
}

public sealed record FundStructureSetupValidationIssueDto(
    string Code,
    string Message,
    string FieldPath,
    bool IsBlocking);

public sealed record FundStructureSetupValidationSummaryDto(
    bool IsValid,
    IReadOnlyList<FundStructureSetupValidationIssueDto> Issues);

public sealed record FundStructureSetupPreviewLinkDto(
    FundStructureSetupNodeAlias Parent,
    FundStructureSetupNodeAlias Child,
    OwnershipRelationshipTypeDto RelationshipType,
    decimal? OwnershipPercent,
    bool IsPrimary,
    string? Notes);

public sealed record FundStructureSetupPreviewDto(
    IReadOnlyList<FundStructureNodeDto> Nodes,
    IReadOnlyList<FundStructureSetupPreviewLinkDto> OwnershipLinks,
    FundStructureSetupValidationSummaryDto ValidationSummary);

public sealed record FundStructureSetupResultDto(
    OrganizationSummaryDto Organization,
    BusinessSummaryDto BusinessLane,
    ClientSummaryDto? Client,
    FundSummaryDto? Fund,
    LegalEntitySummaryDto LegalEntity,
    VehicleSummaryDto Vehicle,
    InvestmentPortfolioSummaryDto InvestmentPortfolio,
    IReadOnlyList<OwnershipLinkDto> OwnershipLinks,
    FundStructureAssignmentDto AccountHandoffAssignment,
    FundStructureGraphDto Graph,
    FundStructureSetupValidationSummaryDto ValidationSummary);
