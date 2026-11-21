using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;

namespace Meridian.Application.Composition.Startup;

internal sealed class SingleSourceProviderConfigApplier : IConfigurationRuntimeApplier
{
    public string Name => nameof(SingleSourceProviderConfigApplier);

    public bool Handles(string path)
        => path.StartsWith("DataSources.Sources.", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(".Enabled", StringComparison.OrdinalIgnoreCase)
            && path.Split('.').Length == 4;

    public Task<IReadOnlyList<ConfigurationApplyEntry>> ApplyAsync(
        AppConfig previous,
        AppConfig next,
        IReadOnlyList<string> changedPaths,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var enablesFailover = next.DataSources is { EnableFailover: true, FailoverRules.Length: > 0 };
        var kind = enablesFailover ? ConfigurationApplyKind.Deferred : ConfigurationApplyKind.NoRuntimeEffect;
        var reason = enablesFailover
            ? "Saved; restart the host to start the configured failover group."
            : "Saved; single-source collection uses the host's provider selection. This flag controls failover eligibility.";
        return Task.FromResult<IReadOnlyList<ConfigurationApplyEntry>>(
            changedPaths.Where(Handles).Select(path => new ConfigurationApplyEntry(path, kind, reason)).ToArray());
    }
}
