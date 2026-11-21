using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

public sealed partial class SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster
{
    private static JsonElement? ReadExactBondWorkflow(JsonElement economic, string assetClass)
    {
        var declarations = economic.EnumerateObject().Where(field => field.NameEquals("bondWorkflow")).ToArray();
        if (declarations.Length > 1)
            throw new InvalidOperationException("Repeated economic module bondWorkflow.");
        if (declarations.Length == 0 || declarations[0].Value.ValueKind == JsonValueKind.Null)
            return null;
        var workflow = declarations[0].Value;
        if (!SecurityAssetClassCatalog.GetOrDefault(assetClass).CarriesBondCouponStructure)
            throw new InvalidOperationException($"{assetClass} cannot recover bondWorkflow.");
        if (!SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.bondWorkflow",
            workflow.GetRawText(), out _, out var reason))
            throw new InvalidOperationException(reason);
        return SecurityAssetTermsSchema.IsPopulatedTermValue(workflow) ? workflow : null;
    }
}
