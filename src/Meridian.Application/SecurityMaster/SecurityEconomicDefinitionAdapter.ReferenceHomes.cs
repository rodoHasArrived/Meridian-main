using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.FSharp.Domain;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityEconomicDefinitionAdapter
{
    /// <summary>New typed homes appear only when populated; older absent module images stay absent.</summary>
    private static JsonElement AppendExactReferenceModules(JsonElement document, SecurityEconomicDefinition definition)
    {
        JsonNode? expanded = null;
        void Append(string name, object value)
        {
            expanded ??= JsonNode.Parse(document.GetRawText())!;
            expanded[name] = JsonSerializer.SerializeToNode(value);
        }

        if (definition.Terms.CommodityContract is { } commodity)
            Append("commodityContract", new
            {
                commodityType = commodity.Value.CommodityType,
                denomination = commodity.Value.Denomination is null ? null : commodity.Value.Denomination.Value,
                contractSize = commodity.Value.ContractSize is null ? (decimal?)null : commodity.Value.ContractSize.Value
            });
        if (definition.Terms.DigitalAsset is { } digital)
            Append("digitalAsset", new { network = digital.Value.Network is null ? null : digital.Value.Network.Value });
        if (definition.Terms.UnderlyingExposure is { } exposure)
            Append("underlyingExposure", new
            {
                underlyingAssetClass = exposure.Value.UnderlyingAssetClass,
                underlyingDescription = exposure.Value.UnderlyingDescription is null ? null : exposure.Value.UnderlyingDescription.Value,
                leverage = exposure.Value.Leverage is null ? (decimal?)null : exposure.Value.Leverage.Value
            });
        if (definition.Terms.Settlement is { } settlement)
            Append("settlement", new { settlementType = settlement.Value.SettlementType is null ? null : settlement.Value.SettlementType.Value });
        if (definition.Terms.ClassificationDetail is { } classification)
            Append("classificationDetail", new
            {
                category = classification.Value.Category,
                subType = classification.Value.SubType is null ? null : classification.Value.SubType.Value
            });
        if (definition.Terms.FundShare is { } fund)
            Append("fundShare", new
            {
                fundType = fund.Value.FundType is null ? null : fund.Value.FundType.Value,
                navCurrency = fund.Value.NavCurrency is null ? null : fund.Value.NavCurrency.Value,
                distributionPolicy = fund.Value.DistributionPolicy is null ? null : DistributionPolicyModule.Label(fund.Value.DistributionPolicy.Value),
                isStableNav = fund.Value.IsStableNav is null ? (bool?)null : fund.Value.IsStableNav.Value
            });
        if (definition.Terms.ReferenceDataSource is { } source)
            Append("referenceDataSource", new
            {
                pricingSource = source.Value.PricingSource is null ? null : source.Value.PricingSource.Value,
                fixingSource = source.Value.FixingSource is null ? null : source.Value.FixingSource.Value
            });

        return expanded is null ? document : JsonSerializer.SerializeToElement(expanded);
    }
}
