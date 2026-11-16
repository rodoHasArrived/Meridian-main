using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;

namespace Meridian.Application.UI;

/// <summary>One resolved configuration field: where its value comes from and what a change to it requires.</summary>
public sealed record EffectiveConfigurationEntry(string Key, string Value, string Source, ConfigurationChangeImpact Impact);

/// <summary>
/// Flattens the whole <see cref="AppConfig"/> document into source-annotated fields, so the
/// effective-configuration view covers every section instead of a hand-maintained subset.
/// Credential-bearing fields never leave this projection unmasked.
/// </summary>
public static class EffectiveConfigurationProjection
{
    /// <summary>Longer arrays (and the symbol list) are reported as a count instead of per-element fields.</summary>
    internal const int MaxExpandedArrayItems = 25;

    private const string SymbolsPath = "symbols";

    public static IReadOnlyList<EffectiveConfigurationEntry> Build(
        AppConfig config,
        IReadOnlyList<EnvironmentOverrideInfo> environmentVariables)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(environmentVariables);

        var current = Flatten(config);
        var defaults = Flatten(new AppConfig());
        var activeOverrides = environmentVariables
            .Where(static variable => variable.IsSet)
            .Select(static variable => (Variable: variable, Path: variable.ConfigPath.Replace(':', '.')))
            .ToArray();

        var entries = new List<EffectiveConfigurationEntry>(current.Count);
        foreach (var (path, node) in current.OrderBy(static field => field.Key, StringComparer.Ordinal))
        {
            var overrideMatch = activeOverrides.FirstOrDefault(candidate => Covers(candidate.Path, path));
            string source;
            if (overrideMatch.Variable is not null)
                source = $"env:{overrideMatch.Variable.EnvironmentVariable}";
            else if (defaults.TryGetValue(path, out var defaultNode) && JsonNode.DeepEquals(node, defaultNode))
                source = "default";
            else
                source = "config";

            var sensitive = overrideMatch.Variable?.IsSensitive == true || IsSensitivePath(path, node);
            entries.Add(new(path, Display(node, sensitive), source, ConfigStore.GetSettingImpact(path)));
        }

        return entries;
    }

    private static bool Covers(string overridePath, string fieldPath)
        => fieldPath.Equals(overridePath, StringComparison.OrdinalIgnoreCase)
            || (fieldPath.Length > overridePath.Length
                && fieldPath[overridePath.Length] == '.'
                && fieldPath.StartsWith(overridePath, StringComparison.OrdinalIgnoreCase));

    private static bool IsSensitivePath(string path, JsonNode? node)
    {
        foreach (var segment in path.Split('.'))
        {
            if (SensitiveValueMasker.IsSensitiveProperty(segment))
                return true;
            // Provider client ids are credentials; numeric API client ids (IB socket) are not.
            if (segment.Equals("clientId", StringComparison.OrdinalIgnoreCase)
                && node is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                return true;
        }

        return false;
    }

    private static string Display(JsonNode? node, bool sensitive)
    {
        if (node is null)
            return "null";
        var isString = node is JsonValue value && value.GetValueKind() == JsonValueKind.String;
        if (sensitive)
            return isString ? SensitiveValueMasker.MaskCompletely(node.GetValue<string>()) : "[set]";
        return isString ? node.GetValue<string>() : node.ToJsonString();
    }

    private static Dictionary<string, JsonNode?> Flatten(AppConfig config)
    {
        var fields = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        Flatten(JsonSerializer.SerializeToNode(config, AppConfigJsonOptions.Write), "", fields);
        return fields;
    }

    private static void Flatten(JsonNode? node, string path, Dictionary<string, JsonNode?> fields)
    {
        if (node is JsonObject obj && obj.Count > 0)
        {
            foreach (var (key, value) in obj)
                Flatten(value, path.Length == 0 ? key : path + "." + key, fields);
        }
        else if (node is JsonArray array && array.Count > 0)
        {
            if (array.Count > MaxExpandedArrayItems || path.Equals(SymbolsPath, StringComparison.OrdinalIgnoreCase))
            {
                fields[path + ".count"] = JsonValue.Create(array.Count);
                return;
            }

            for (var index = 0; index < array.Count; index++)
                Flatten(array[index], path + "." + index, fields);
        }
        else if (path.Length > 0)
            fields[path] = node;
    }
}
