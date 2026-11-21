using System.Collections.Concurrent;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;

namespace Meridian.Application.UI;

/// <summary>
/// A running host component that can apply part of a saved configuration without a restart.
/// </summary>
/// <remarks>
/// Appliers are attached per configuration file through <see cref="ConfigurationRuntimeAppliers"/>,
/// because desktop mode runs the UI server and the collector in separate DI containers that share
/// one process and one configuration file.
/// </remarks>
public interface IConfigurationRuntimeApplier
{
    /// <summary>Short name used in logs.</summary>
    string Name { get; }

    /// <summary>Reconciles previously deferred paths when the next save touches this runtime.</summary>
    bool ReconcilePendingPaths => false;

    /// <summary>Whether this applier owns the flattened setting path (for example <c>DataSources.FailoverRules.0.FailoverThreshold</c>).</summary>
    bool Handles(string path);

    /// <summary>
    /// Applies a saved configuration. Returns one entry per handled path in <paramref name="changedPaths"/>.
    /// Implementations must be idempotent: the same change can arrive from a UI save and a file watcher.
    /// </summary>
    Task<IReadOnlyList<ConfigurationApplyEntry>> ApplyAsync(
        AppConfig previous,
        AppConfig next,
        IReadOnlyList<string> changedPaths,
        CancellationToken ct);
}

/// <summary>Process-wide registry of runtime appliers keyed by configuration file path.</summary>
public static class ConfigurationRuntimeAppliers
{
    private static readonly ConcurrentDictionary<string, ImmutableApplierList> Appliers =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>Attaches an applier for a configuration file until the returned handle is disposed.</summary>
    public static IDisposable Attach(string configPath, IConfigurationRuntimeApplier applier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentNullException.ThrowIfNull(applier);
        var key = Path.GetFullPath(configPath);
        Appliers.AddOrUpdate(key, _ => new([applier]), (_, list) => new([.. list.Items, applier]));
        return new Attachment(key, applier);
    }

    /// <summary>Appliers currently attached to a configuration file.</summary>
    public static IReadOnlyList<IConfigurationRuntimeApplier> For(string configPath)
        => Appliers.TryGetValue(Path.GetFullPath(configPath), out var list) ? list.Items : [];

    private static void Detach(string key, IConfigurationRuntimeApplier applier)
    {
        while (Appliers.TryGetValue(key, out var list))
        {
            var remaining = list.Items.Where(item => !ReferenceEquals(item, applier)).ToArray();
            var updated = remaining.Length == 0
                ? Appliers.TryRemove(new KeyValuePair<string, ImmutableApplierList>(key, list))
                : Appliers.TryUpdate(key, new(remaining), list);
            if (updated)
                return;
        }
    }

    private sealed record ImmutableApplierList(IConfigurationRuntimeApplier[] Items);

    private sealed class Attachment(string key, IConfigurationRuntimeApplier applier) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Detach(key, applier);
        }
    }
}
