using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityEconomicDefinitionAdapter
{
    private static JsonElement AppendExactScheduleModules(JsonElement document, SecurityEconomicDefinition definition)
    {
        if (definition.Terms.DeclaredSchedule is not { } declared
            || (ScheduleConventionsModule.IsEmpty(declared.Value.Conventions)
                && CallPutSchedulesModule.IsEmpty(declared.Value.Exercises)))
            return document;
        var assetClass = definition.Classification.TypeName;
        var block = SecurityAssetTermsSchema.Field(assetClass, "scheduleConventions")
            ?? throw new InvalidOperationException($"{assetClass} has no declared schedule home.");
        var calls = SecurityAssetTermsSchema.Field(assetClass, "callSchedule")!;
        var version = SecurityAssetClassCatalog.GetStampFloor(assetClass);
        if (!ScheduleConventionsModule.IsEmpty(declared.Value.Conventions))
            version = Math.Max(version, block.SinceTermsVersion);
        if (!CallPutSchedulesModule.IsEmpty(declared.Value.Exercises))
            version = Math.Max(version, calls.SinceTermsVersion);
        if (definition.Terms.RateConvention is not null)
            version = Math.Max(version, SecurityAssetTermsSchema.Field(assetClass, "rateUnit")?.SinceTermsVersion ?? version);
        var node = JsonNode.Parse(document.GetRawText())!.AsObject();
        node["declaredSchedule"] = JsonNode.Parse(StructuralTermsJson.declaredSchedule(
            declared.Value, version >= block.SinceTermsVersion, version >= calls.SinceTermsVersion).GetRawText());
        return JsonSerializer.SerializeToElement(node);
    }
}
