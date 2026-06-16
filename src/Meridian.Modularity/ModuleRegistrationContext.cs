using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Modularity;

/// <summary>
/// Carries the shared state a module needs while registering. Keeps <see cref="IMeridianModule"/>
/// independent of any host-specific options type: adapters capture their own host state (for
/// example composition options or a provider registry) at construction time, and use
/// <see cref="Items"/> only when state genuinely needs to flow between modules.
/// </summary>
public sealed class ModuleRegistrationContext
{
    /// <summary>
    /// Creates a context targeting the supplied service collection.
    /// </summary>
    public ModuleRegistrationContext(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// The service collection modules register into.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Optional property bag for state shared between modules during a single registration pass.
    /// </summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}
