using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityEconomicDefinitionAdapter
{
    private static JsonElement AppendExactBondWorkflowModule(JsonElement document, SecurityEconomicDefinition definition)
    {
        if (definition.Terms.BondWorkflow is not { } workflow) return document;
        var expanded = JsonNode.Parse(document.GetRawText())!.AsObject();
        expanded["bondWorkflow"] = JsonNode.Parse(BondWorkflowTermsJson.write(workflow.Value).GetRawText());
        return JsonSerializer.SerializeToElement(expanded);
    }
}
