using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityEconomicDefinitionAdapter
{
    private static JsonElement AppendExactCapitalModules(JsonElement document, SecurityEconomicDefinition definition)
    {
        if (definition.Terms.EquityCapital is null && definition.Terms.TrancheContract is null)
            return document;
        var expanded = JsonNode.Parse(document.GetRawText())!.AsObject();
        if (definition.Terms.EquityCapital is { } capital)
            expanded["equityCapital"] = JsonNode.Parse(EquityCapitalCodec.toJson(capital.Value));
        if (definition.Terms.TrancheContract is { } tranche)
            expanded["trancheContract"] = JsonNode.Parse(TrancheContractCodec.toJson(tranche.Value));
        return JsonSerializer.SerializeToElement(expanded);
    }
}
