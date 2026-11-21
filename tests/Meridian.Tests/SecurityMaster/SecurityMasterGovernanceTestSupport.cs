using Meridian.Application.SecurityMaster;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Builds the governance requirements seam the way a host does, so tests of its readers exercise the
/// same decision the composed graph makes instead of a stand-in for it.
/// </summary>
internal static class GovernanceRequirementsFor
{
    /// <summary>A host that is not a production composition: each requirement follows the options.</summary>
    public static SecurityMasterGovernanceRequirements NonProduction(
        IOptionsMonitor<SecurityMasterWorkbenchOptions> options,
        ILogger<SecurityMasterGovernanceRequirements>? logger = null)
        => new(options, SecurityMasterGovernanceHostPosture.NonProduction, logger: logger);

    /// <summary>A production composition: the Institutional profile whatever the options say.</summary>
    public static SecurityMasterGovernanceRequirements Production(
        IOptionsMonitor<SecurityMasterWorkbenchOptions> options,
        ILogger<SecurityMasterGovernanceRequirements>? logger = null)
        => new(options, SecurityMasterGovernanceHostPosture.Production, logger: logger);

    /// <summary>
    /// The Institutional profile with none of the three settings configured, reached either by the
    /// option on an ordinary host or by a production composition with no profile configured.
    /// </summary>
    public static SecurityMasterGovernanceRequirements Institutional(bool byProductionComposition)
        => byProductionComposition
            ? Production(new MutableWorkbenchOptionsMonitor(new SecurityMasterWorkbenchOptions()))
            : NonProduction(new MutableWorkbenchOptionsMonitor(new SecurityMasterWorkbenchOptions
            {
                GovernanceProfile = SecurityMasterGovernanceProfile.Institutional
            }));
}

/// <summary>An options monitor a test can reload; listeners are notified as on a configuration reload.</summary>
internal sealed class MutableWorkbenchOptionsMonitor(SecurityMasterWorkbenchOptions initial)
    : IOptionsMonitor<SecurityMasterWorkbenchOptions>
{
    private readonly List<Action<SecurityMasterWorkbenchOptions, string?>> _listeners = [];

    public SecurityMasterWorkbenchOptions CurrentValue { get; private set; } = initial;

    public void Set(SecurityMasterWorkbenchOptions value)
    {
        CurrentValue = value;
        foreach (var listener in _listeners.ToArray())
        {
            listener(value, Microsoft.Extensions.Options.Options.DefaultName);
        }
    }

    public SecurityMasterWorkbenchOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<SecurityMasterWorkbenchOptions, string?> listener)
    {
        _listeners.Add(listener);
        return null;
    }
}
