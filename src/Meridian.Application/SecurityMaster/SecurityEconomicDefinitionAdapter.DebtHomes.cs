using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.FSharp.Domain;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityEconomicDefinitionAdapter
{
    private static JsonElement AppendExactDebtModules(JsonElement document, SecurityEconomicDefinition definition)
    {
        if (definition.Terms.DebtClassification is null && definition.Terms.BondRedemption is null)
            return document;

        var expanded = JsonNode.Parse(document.GetRawText())!.AsObject();
        if (definition.Terms.DebtClassification is { } classification)
            expanded["debtClassification"] = JsonSerializer.SerializeToNode(new
            {
                subclass = BondSubclassModule.label(classification.Value.Subclass),
                seniority = classification.Value.Seniority?.Value
            });
        if (definition.Terms.BondRedemption is { } redemption)
            expanded["bondRedemption"] = JsonSerializer.SerializeToNode(new
            {
                par = redemption.Value.Par?.Value,
                legalFinalMaturity = redemption.Value.LegalFinalMaturity?.Value,
                preRefundDate = redemption.Value.PreRefundDate?.Value,
                mandatoryPutDate = redemption.Value.MandatoryPutDate?.Value
            });
        return JsonSerializer.SerializeToElement(expanded);
    }
}
