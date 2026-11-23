using System.Collections;
using System.Globalization;
using System.Text.Json.Serialization.Metadata;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;

namespace Meridian.Application.UI;

/// <summary>A setting's displayed value, provenance and activation impact.</summary>
public sealed record EffectiveConfigurationEntry(string Key, string Value, string Source, ConfigurationChangeImpact Impact);

/// <summary>Projects declared configuration settings without traversing operator-defined keys or values.</summary>
public static class EffectiveConfigurationProjection
{
    private const int MaxExpandedArrayItems = 25;
    private const string Redacted = "[set]";

    public static IReadOnlyList<EffectiveConfigurationEntry> Build(
        AppConfig config,
        IReadOnlyList<EnvironmentOverrideInfo> environmentVariables)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(environmentVariables);

        var current = Project(config);
        var defaults = Project(new AppConfig());
        var overrides = environmentVariables
            .Where(static variable => variable.IsSet)
            .Select(static variable => (Variable: variable, Path: variable.ConfigPath.Replace(':', '.')))
            .ToArray();
        var entries = new List<EffectiveConfigurationEntry>(current.Count);
        foreach (var (path, display) in current.OrderBy(static field => field.Key, StringComparer.Ordinal))
        {
            var matchedOverride = overrides.FirstOrDefault(candidate =>
                path.Equals(candidate.Path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(candidate.Path + ".", StringComparison.OrdinalIgnoreCase)).Variable;
            var source = matchedOverride is not null ? $"env:{matchedOverride.EnvironmentVariable}"
                : defaults.GetValueOrDefault(path) == display ? "default" : "config";
            var value = matchedOverride?.IsSensitive == true && display != "null" ? Redacted : display;
            entries.Add(new(path, value, source, ConfigStore.GetSettingImpact(path)));
        }

        return entries;
    }

    private static Dictionary<string, string> Project(AppConfig config)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        Project(config, typeof(AppConfig), "", fields);
        return fields;
    }

    private static void Project(object? value, Type declaredType, string path, Dictionary<string, string> fields)
    {
        if (path == "symbols")
        {
            fields.Add("symbols.count", Count(((Array?)value)?.Length ?? 0));
            return;
        }

        if (value is null)
        {
            fields.Add(path, "null");
            return;
        }

        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (type == typeof(string))
        {
            // The endpoint owns its compatibility fields. Additional text may include credentials
            // even when stored under names such as "baseUrl", "notes" or "description".
            fields.Add(path, string.IsNullOrEmpty((string)value) ? "[empty]" : Redacted);
            return;
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(DateOnly)
            || type == typeof(TimeOnly) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan))
        {
            fields.Add(path, value is bool boolean ? (boolean ? "true" : "false")
                : Convert.ToString(value, CultureInfo.InvariantCulture)!);
            return;
        }

        var contract = AppConfigJsonOptions.Write.GetTypeInfo(type);
        switch (contract.Kind)
        {
            case JsonTypeInfoKind.Object:
                foreach (var property in contract.Properties)
                {
                    if (property.IsExtensionData || property.Get is null)
                        continue;

                    var field = property.Get(value);
                    if (property.ShouldSerialize is { } shouldSerialize && !shouldSerialize(value, field))
                        continue;

                    var fieldPath = path.Length == 0 ? property.Name : path + "." + property.Name;
                    if (SensitiveValueMasker.IsSensitiveProperty(property.Name))
                        fields.Add(fieldPath, field is null ? "null" : Redacted);
                    else
                        Project(field, property.PropertyType, fieldPath, fields);
                }
                break;

            case JsonTypeInfoKind.Dictionary when value is ICollection map:
                fields.Add(path + ".count", Count(map.Count));
                break;

            case JsonTypeInfoKind.Enumerable when value is IList list && contract.ElementType is { } elementType:
                if (list.Count == 0 || list.Count > MaxExpandedArrayItems)
                {
                    fields.Add(path + ".count", Count(list.Count));
                    break;
                }

                for (var i = 0; i < list.Count; i++)
                    Project(list[i], elementType, path + "." + Count(i), fields);
                break;

            default:
                fields.Add(path, Redacted);
                break;
        }
    }

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);
}
