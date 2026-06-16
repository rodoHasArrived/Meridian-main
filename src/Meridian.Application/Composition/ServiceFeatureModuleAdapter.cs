using Meridian.Application.Composition.Features;
using Meridian.Modularity;

namespace Meridian.Application.Composition;

/// <summary>
/// Adapts an existing <see cref="IServiceFeatureRegistration"/> to the unified
/// <see cref="IMeridianModule"/> contract without rewriting the feature module. The required
/// <see cref="CompositionOptions"/> are captured at construction time so the module's
/// <see cref="Register"/> can satisfy the contract's parameterless-context signature.
/// </summary>
/// <remarks>
/// This is the bridge that lets <see cref="ServiceCompositionRoot"/> migrate its hand-maintained
/// feature array onto <see cref="ModuleLoader"/> incrementally: each feature can be wrapped and
/// composed through the loader while still being authored as an <see cref="IServiceFeatureRegistration"/>.
/// </remarks>
public sealed class ServiceFeatureModuleAdapter : IMeridianModule
{
    private readonly IServiceFeatureRegistration _inner;
    private readonly CompositionOptions _options;
    private readonly IReadOnlyCollection<string> _dependsOn;

    /// <summary>
    /// Wraps the supplied feature registration, optionally declaring ordering dependencies on
    /// other module ids.
    /// </summary>
    public ServiceFeatureModuleAdapter(
        IServiceFeatureRegistration inner,
        CompositionOptions options,
        IReadOnlyCollection<string>? dependsOn = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _dependsOn = dependsOn ?? Array.Empty<string>();
    }

    /// <inheritdoc />
    public string ModuleId => _inner.GetType().Name;

    /// <inheritdoc />
    public IReadOnlyCollection<string> DependsOn => _dependsOn;

    /// <inheritdoc />
    public void Register(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _inner.Register(context.Services, _options);
    }
}
