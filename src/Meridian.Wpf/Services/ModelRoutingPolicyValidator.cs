using System.Text.Json;

namespace Meridian.Wpf.Services;

public sealed class ModelRoutingPolicyValidator
{
    private static readonly string[] RequiredTelemetrySignals =
    [
        "route_id",
        "selected_model",
        "input_tokens",
        "output_tokens",
        "estimated_cost_usd",
        "latency_ms",
        "error_class",
        "handoff_path"
    ];

    public ModelRoutingPolicyValidationResult Validate(string json)
    {
        var errors = new List<string>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ModelRoutingPolicyValidationResult.Fail($"Invalid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            ValidateRequiredString(root, "version", errors);
            ValidateRequiredString(root, "defaultRouteId", errors);

            var modelClassIds = ReadModelClassIds(root, errors);
            var telemetrySignals = ReadTelemetrySignals(root, errors);
            ValidateRequiredTelemetry(telemetrySignals, errors);
            ValidateRoutingRules(root, modelClassIds, telemetrySignals, errors);
        }

        return errors.Count == 0
            ? ModelRoutingPolicyValidationResult.Success()
            : ModelRoutingPolicyValidationResult.Fail(errors);
    }

    private static HashSet<string> ReadModelClassIds(JsonElement root, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("modelClasses", out var modelClasses) || modelClasses.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Missing required array: modelClasses.");
            return ids;
        }

        foreach (var item in modelClasses.EnumerateArray())
        {
            if (!TryReadNonEmptyString(item, "id", out var id))
            {
                errors.Add("Each modelClasses entry must include a non-empty id.");
                continue;
            }

            if (!ids.Add(id))
            {
                errors.Add($"Duplicate model class id '{id}'.");
            }
        }

        return ids;
    }

    private static HashSet<string> ReadTelemetrySignals(JsonElement root, List<string> errors)
    {
        var signals = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("telemetrySignals", out var telemetry) || telemetry.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Missing required array: telemetrySignals.");
            return signals;
        }

        foreach (var item in telemetry.EnumerateArray())
        {
            if (!TryReadNonEmptyString(item, "signal", out var signal))
            {
                errors.Add("Each telemetrySignals entry must include a non-empty signal.");
                continue;
            }

            signals.Add(signal);
        }

        return signals;
    }

    private static void ValidateRequiredTelemetry(HashSet<string> signals, List<string> errors)
    {
        foreach (var requiredSignal in RequiredTelemetrySignals)
        {
            if (!signals.Contains(requiredSignal))
            {
                errors.Add($"Missing required telemetry signal '{requiredSignal}'.");
            }
        }
    }

    private static void ValidateRoutingRules(
        JsonElement root,
        HashSet<string> modelClassIds,
        HashSet<string> telemetrySignals,
        List<string> errors)
    {
        if (!root.TryGetProperty("routingRules", out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Missing required array: routingRules.");
            return;
        }

        foreach (var rule in rules.EnumerateArray())
        {
            if (!TryReadNonEmptyString(rule, "id", out var ruleId))
            {
                errors.Add("Each routingRules entry must include a non-empty id.");
                continue;
            }

            if (!TryReadNonEmptyString(rule, "modelClass", out var modelClass))
            {
                errors.Add($"routingRules[{ruleId}] must include a non-empty modelClass.");
            }
            else if (!modelClassIds.Contains(modelClass))
            {
                errors.Add($"routingRules[{ruleId}] references unknown modelClass '{modelClass}'.");
            }

            if (!rule.TryGetProperty("telemetrySignals", out var signals) || signals.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"routingRules[{ruleId}] must include a telemetrySignals array.");
            }
            else
            {
                foreach (var signal in signals.EnumerateArray())
                {
                    if (signal.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(signal.GetString()))
                    {
                        errors.Add($"routingRules[{ruleId}] includes an empty telemetry signal.");
                        continue;
                    }

                    var signalName = signal.GetString()!;
                    if (!telemetrySignals.Contains(signalName))
                    {
                        errors.Add($"routingRules[{ruleId}] references unknown telemetry signal '{signalName}'.");
                    }
                }
            }
        }
    }

    private static void ValidateRequiredString(JsonElement element, string propertyName, List<string> errors)
    {
        if (!TryReadNonEmptyString(element, propertyName, out _))
        {
            errors.Add($"Missing required string: {propertyName}.");
        }
    }

    private static bool TryReadNonEmptyString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}

public sealed record ModelRoutingPolicyValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ModelRoutingPolicyValidationResult Success() => new(true, []);

    public static ModelRoutingPolicyValidationResult Fail(params string[] errors) => new(false, errors);

    public static ModelRoutingPolicyValidationResult Fail(IReadOnlyList<string> errors) => new(false, errors);
}
