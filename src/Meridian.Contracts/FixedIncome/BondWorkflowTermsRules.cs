using System.Text.Json;

namespace Meridian.Contracts.FixedIncome;

/// <summary>Semantic admission for the v5 convention block; historical, undeclared blocks stay inert.</summary>
public static class BondWorkflowTermsRules
{
    public static bool TryValidate(JsonElement block, out string? reason)
    {
        reason = null;
        if (block.ValueKind == JsonValueKind.Null)
            return true;
        if (block.ValueKind != JsonValueKind.Object)
        { reason = "bondWorkflow must be an object or null."; return false; }
        foreach (var (key, maximum) in new[] { ("lookbackBusinessDays", 30), ("lockoutBusinessDays", 30),
            ("exCouponBusinessDays", 30), ("lagMonths", 24) })
        {
            if (!block.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
                continue;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var count) || count < 0 || count > maximum)
            { reason = $"bondWorkflow.{key} must be a whole count from zero to {maximum}."; return false; }
        }
        foreach (var key in new[] { "couponFloorRatio", "redemptionFloorRatio" })
            if (block.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null
                && (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var ratio) || ratio < 0m))
            { reason = $"bondWorkflow.{key} must be a nonnegative explicit index ratio."; return false; }
        var method = block.TryGetProperty("floatingMethod", out var methodValue) && methodValue.ValueKind == JsonValueKind.String
            ? methodValue.GetString() : null;
        if (method == "CompoundedOvernight")
        {
            foreach (var key in new[] { "fixingCalendarId", "lookbackBusinessDays", "observationMethod", "observationWeighting", "lockoutBusinessDays" })
                if (!block.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null
                    || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
                { reason = $"CompoundedOvernight requires an explicit bondWorkflow.{key}."; return false; }
        }
        return true;
    }
}
