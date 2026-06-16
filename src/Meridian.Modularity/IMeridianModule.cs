namespace Meridian.Modularity;

/// <summary>
/// Canonical contract for a self-describing unit of behaviour that registers services into a
/// composition root. This is the single abstraction the various existing module patterns
/// (service-feature registration, provider modules, desktop feature modules) converge onto via
/// thin adapters, so discovery, ordering, validation, and capability declaration are expressed
/// one way across the platform.
/// </summary>
/// <remarks>
/// Modules are composed by <see cref="ModuleLoader"/>, which orders them by their declared
/// <see cref="DependsOn"/> edges (dependencies first, otherwise insertion order) before invoking
/// <see cref="Register"/>. All members except <see cref="ModuleId"/> and <see cref="Register"/>
/// have default implementations so adapters stay minimal.
/// </remarks>
public interface IMeridianModule
{
    /// <summary>
    /// Stable, unique identifier for the module (case-insensitive). Used for dependency
    /// resolution and duplicate detection.
    /// </summary>
    string ModuleId { get; }

    /// <summary>
    /// Identifiers of modules that must be registered before this one. Defaults to none.
    /// </summary>
    IReadOnlyCollection<string> DependsOn => Array.Empty<string>();

    /// <summary>
    /// Registers this module's services into the supplied context's service collection.
    /// </summary>
    void Register(ModuleRegistrationContext context);

    /// <summary>
    /// Validates that this module's prerequisites are satisfied before registration.
    /// Defaults to <see cref="ModuleValidationResult.Valid"/>.
    /// </summary>
    ValueTask<ModuleValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(ModuleValidationResult.Valid);

    /// <summary>
    /// Capabilities advertised by this module for diagnostics and capability gating.
    /// Defaults to none.
    /// </summary>
    IReadOnlyCollection<ModuleCapability> DeclareCapabilities() => Array.Empty<ModuleCapability>();
}
