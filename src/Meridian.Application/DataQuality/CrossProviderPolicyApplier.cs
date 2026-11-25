using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.DataIntegration.Monitoring.DataQuality;

namespace Meridian.Application.DataQuality;

/// <summary>
/// Applies saved <c>DataQuality.CrossProvider</c> tolerances to a running
/// <see cref="CrossProviderComparisonService"/>, so tolerance edits never need a restart.
/// </summary>
/// <remarks>
/// Holds the service weakly: a host container that is disposed without detaching does not keep its
/// monitoring service alive, and the applier detaches itself the next time it is offered a save.
/// </remarks>
internal sealed class CrossProviderPolicyApplier : IConfigurationRuntimeApplier
{
    internal const string SectionPath = "DataQuality";
    internal const string PolicyPath = "DataQuality.CrossProvider";
    internal const string AuditPath = "DataQuality.CrossProviderAudit";

    private readonly WeakReference<CrossProviderComparisonService> _target;
    private IDisposable? _attachment;

    private CrossProviderPolicyApplier(CrossProviderComparisonService target)
        => _target = new WeakReference<CrossProviderComparisonService>(target);

    public string Name => nameof(CrossProviderPolicyApplier);

    /// <summary>Attaches an applier for <paramref name="target"/> to the configuration file.</summary>
    public static void Attach(string configPath, CrossProviderComparisonService target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var applier = new CrossProviderPolicyApplier(target);
        applier._attachment = ConfigurationRuntimeAppliers.Attach(configPath, applier);
    }

    public bool Handles(string path)
        => path.Equals(SectionPath, StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith(SectionPath + ".", StringComparison.OrdinalIgnoreCase);

    private static bool IsAudit(string path)
        => path.Equals(AuditPath, StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith(AuditPath + ".", StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ConfigurationApplyEntry>> ApplyAsync(
        AppConfig previous,
        AppConfig next,
        IReadOnlyList<string> changedPaths,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_target.TryGetTarget(out var service))
        {
            _attachment?.Dispose();
            return Task.FromResult<IReadOnlyList<ConfigurationApplyEntry>>([]);
        }

        ConfigurationApplyKind kind;
        string detail;
        try
        {
            service.UpdatePolicy(CrossProviderTolerancePolicy.FromConfig(next));
            kind = ConfigurationApplyKind.AppliedLive;
            detail = "Cross-provider comparisons now use the saved tolerances.";
        }
        catch (ArgumentException)
        {
            kind = ConfigurationApplyKind.Deferred;
            detail = "The saved tolerances are outside their supported bounds; the running host kept the previous tolerances.";
        }

        return Task.FromResult<IReadOnlyList<ConfigurationApplyEntry>>(changedPaths.Where(Handles)
            .Select(path => IsAudit(path)
                ? new ConfigurationApplyEntry(path, ConfigurationApplyKind.NoRuntimeEffect, "Audit history only.")
                : new ConfigurationApplyEntry(path, kind, detail))
            .ToArray());
    }
}
