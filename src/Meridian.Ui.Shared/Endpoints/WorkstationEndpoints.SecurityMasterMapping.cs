using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Security Master DTO mapping helpers for the workstation API surface: maps security
/// summary/detail records to workstation DTOs, identity drill-in, and economic-definition
/// summaries, plus asset-class sub-type derivation. Split out of the WorkstationEndpoints
/// core partial.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static SecurityMasterWorkstationDto MapToWorkstationSecurity(SecuritySummaryDto summary)
        => new(
            SecurityId: summary.SecurityId,
            DisplayName: summary.DisplayName,
            Status: summary.Status,
            Classification: new SecurityClassificationSummaryDto(
                AssetClass: summary.AssetClass,
                SubType: DeriveSubType(summary.AssetClass),
                PrimaryIdentifierKind: null,
                PrimaryIdentifierValue: summary.PrimaryIdentifier,
                MatchedIdentifierKind: null,
                MatchedIdentifierValue: null,
                MatchedProvider: null),
            EconomicDefinition: new SecurityEconomicDefinitionSummaryDto(
                Currency: summary.Currency,
                Version: summary.Version,
                EffectiveFrom: null,
                EffectiveTo: null));

    private static SecurityMasterWorkstationDto MapToWorkstationSecurity(SecurityDetailDto detail)
    {
        var primaryIdentifier = detail.Identifiers
            .FirstOrDefault(static identifier => identifier.IsPrimary)
            ?? detail.Identifiers.FirstOrDefault();

        return new SecurityMasterWorkstationDto(
            SecurityId: detail.SecurityId,
            DisplayName: detail.DisplayName,
            Status: detail.Status,
            Classification: new SecurityClassificationSummaryDto(
                AssetClass: detail.AssetClass,
                SubType: DeriveSubType(detail.AssetClass),
                PrimaryIdentifierKind: primaryIdentifier?.Kind.ToString(),
                PrimaryIdentifierValue: primaryIdentifier?.Value,
                MatchedIdentifierKind: null,
                MatchedIdentifierValue: null,
                MatchedProvider: null),
            EconomicDefinition: new SecurityEconomicDefinitionSummaryDto(
                Currency: detail.Currency,
                Version: detail.Version,
                EffectiveFrom: detail.EffectiveFrom,
                EffectiveTo: detail.EffectiveTo));
    }

    private static SecurityIdentityDrillInDto MapToIdentityDrillIn(SecurityDetailDto detail)
        => new(
            SecurityId: detail.SecurityId,
            DisplayName: detail.DisplayName,
            AssetClass: detail.AssetClass,
            Status: detail.Status,
            Version: detail.Version,
            EffectiveFrom: detail.EffectiveFrom,
            EffectiveTo: detail.EffectiveTo,
            Identifiers: detail.Identifiers,
            Aliases: detail.Aliases);

    private static SecurityEconomicDefinitionSummaryDto MapToEconomicDefinitionSummary(SecurityEconomicDefinitionRecord record)
        => new(
            Currency: record.Currency,
            Version: record.Version,
            EffectiveFrom: record.EffectiveFrom,
            EffectiveTo: record.EffectiveTo,
            SubType: record.SubType,
            AssetFamily: record.AssetFamily,
            IssuerType: record.IssuerType,
            RiskCountry: record.RiskCountry,
            TypeName: record.TypeName);

    /// <summary>
    /// Derives the most specific sub-type available from the asset-class string without requiring
    /// a full aggregate rebuild. Returns null for asset classes that may map to multiple sub-types.
    /// </summary>
    private static string? DeriveSubType(string? assetClass) => assetClass switch
    {
        "Bond" => "Bond",
        "TreasuryBill" => "TreasuryBill",
        "Option" => "OptionContract",
        "Future" => "FutureContract",
        "Swap" => "SwapContract",
        "DirectLoan" => "DirectLoan",
        "Deposit" => "Deposit",
        "MoneyMarketFund" => "MoneyMarket",
        "CertificateOfDeposit" => "CertificateOfDeposit",
        "CommercialPaper" => "CommercialPaper",
        "Repo" => "Repo",
        _ => null
    };
}
