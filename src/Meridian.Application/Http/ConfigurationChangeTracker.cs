using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;

namespace Meridian.Application.UI;

/// <summary>Host-lifetime pending state shared by stores for the same file. Retains hashes, never values.</summary>
internal sealed class ConfigurationChangeTracker
{
    private static readonly ConcurrentDictionary<string, ConfigurationChangeTracker> Trackers =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly object _gate = new();
    private Dictionary<string, string>? _baseline;
    private ConfigurationChangeStatus _status = new(ConfigurationChangeImpact.Live, [], [], null);

    internal static ConfigurationChangeTracker ForPath(string path) => Trackers.GetOrAdd(Path.GetFullPath(path), _ => new());

    internal ConfigurationChangeStatus Status
    {
        get { lock (_gate) return _status; }
    }

    internal void Record(AppConfig previous, AppConfig next)
    {
        var before = Fingerprints(previous);
        var after = Fingerprints(next);
        lock (_gate)
        {
            _baseline ??= before;
            var changes = Diff(before, after);
            // Live changes have no pending activation. Reverting to the host baseline clears pending state.
            var pending = Diff(_baseline, after).Where(change => change.Impact != ConfigurationChangeImpact.Live).ToArray();
            _status = new(changes.Count == 0 ? ConfigurationChangeImpact.Live : changes.Max(change => change.Impact),
                changes, pending, DateTimeOffset.UtcNow);
        }
    }

    internal static ConfigurationChangeImpact Classify(string path)
    {
        var root = path.Split('.')[0];
        if (root.Equals("Symbols", StringComparison.OrdinalIgnoreCase))
            return ConfigurationChangeImpact.Live;
        if (root.Equals("DataSource", StringComparison.OrdinalIgnoreCase)
            || root.Equals("Alpaca", StringComparison.OrdinalIgnoreCase)
            || root.Equals("IB", StringComparison.OrdinalIgnoreCase)
            || root.Equals("Polygon", StringComparison.OrdinalIgnoreCase))
            return ConfigurationChangeImpact.ReconnectProvider;
        // A reconnect alone does not rebuild backfill services, routing engines or mapping registries.
        // Until a runtime callback confirms those changes, conservatively require a host restart.
        return ConfigurationChangeImpact.RestartHost;
    }

    private static List<ConfigurationFieldChange> Diff(Dictionary<string, string> before, Dictionary<string, string> after)
        => before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Where(path => before.GetValueOrDefault(path) != after.GetValueOrDefault(path))
            .Select(path => new ConfigurationFieldChange(path, Classify(path))).ToList();

    private static Dictionary<string, string> Fingerprints(AppConfig config)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        Flatten(JsonSerializer.SerializeToNode(config, AppConfigJsonOptions.Write), "", fields);
        return fields;
    }

    private static void Flatten(JsonNode? node, string path, Dictionary<string, string> fields)
    {
        if (node is JsonObject obj && obj.Count > 0)
        {
            foreach (var (key, value) in obj)
                Flatten(value, path.Length == 0 ? key : path + "." + key, fields);
        }
        else if (node is JsonArray array && array.Count > 0)
        {
            for (var index = 0; index < array.Count; index++)
                Flatten(array[index], path + "." + index, fields);
        }
        else
            fields[path] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node?.ToJsonString() ?? "null")));
    }
}
