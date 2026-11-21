using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityEconomicDefinitionAdapter
{
    private static JsonElement AppendExactRateModules(JsonElement document, SecurityEconomicDefinition definition)
    {
        JsonObject? expanded = null;
        if (definition.Terms.ContractualRate is { } rate)
        {
            expanded ??= JsonNode.Parse(document.GetRawText())!.AsObject();
            expanded["contractualRate"] = JsonSerializer.SerializeToNode(new
            {
                couponRate = rate.Value.CouponRate,
                fixedCouponBps = rate.Value.FixedCouponBps,
                feeRate = rate.Value.FeeRate,
                paymentFrequency = rate.Value.PaymentFrequency is null ? null : rate.Value.PaymentFrequency.Value,
                dayCount = rate.Value.DayCount is null ? null : DayCountConventionModule.Label(rate.Value.DayCount.Value)
            });
        }
        if (definition.Terms.UnderlyingSwap is { } underlying)
        {
            expanded ??= JsonNode.Parse(document.GetRawText())!.AsObject();
            expanded["underlyingSwap"] = JsonSerializer.SerializeToNode(new
            {
                underlyingMaturityDate = underlying.Value.MaturityDate,
                floatingIndex = underlying.Value.FloatingIndex is null ? null : underlying.Value.FloatingIndex.Value,
                fixedLegPaymentFrequency = underlying.Value.FixedLegPaymentFrequency is null ? null : underlying.Value.FixedLegPaymentFrequency.Value,
                fixedLegDayCount = underlying.Value.FixedLegDayCount is null ? null : DayCountConventionModule.Label(underlying.Value.FixedLegDayCount.Value)
            });
        }
        return expanded is null ? document : JsonSerializer.SerializeToElement(expanded);
    }
}
