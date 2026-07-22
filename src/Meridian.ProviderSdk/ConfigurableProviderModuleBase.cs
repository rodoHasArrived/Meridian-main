using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Abstract base class for provider modules that receive runtime configuration via
/// <see cref="ProviderModuleContext"/>. Inherit from this instead of implementing
/// <see cref="IProviderModule"/> directly when your module needs credentials, priority,
/// or provider-specific settings from external configuration.
/// </summary>
/// <remarks>
/// The loader calls <see cref="Configure"/> before <see cref="Register"/>, so
/// <see cref="Context"/> is always populated by the time <see cref="Register"/> runs
/// (when a matching config entry was provided).
///
/// Credential fall-through pattern used by built-in modules:
/// <code>
/// var apiKey = GetCredential("apiKey") ?? Environment.GetEnvironmentVariable("PROVIDER_API_KEY");
/// </code>
/// This lets the module work via env vars when no config entry exists, and use explicit
/// config values when provided.
/// </remarks>
public abstract class ConfigurableProviderModuleBase : IProviderModule
{
    /// <summary>
    /// The context injected by <see cref="ProviderModuleLoader"/> before registration,
    /// or null if no config entry was provided for this module's <see cref="ModuleId"/>.
    /// </summary>
    protected ProviderModuleContext? Context { get; private set; }

    /// <inheritdoc/>
    public abstract string ModuleId { get; }

    /// <inheritdoc/>
    public abstract string ModuleDisplayName { get; }

    /// <inheritdoc/>
    public virtual ProviderCapabilities[] Capabilities => [];

    /// <inheritdoc/>
    public virtual bool RequiresExternalConfig => false;

    /// <inheritdoc/>
    public void Configure(ProviderModuleContext context)
        => Context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc/>
    public virtual ValueTask<ModuleValidationResult> ValidateAsync(CancellationToken ct = default)
        => ValueTask.FromResult(ModuleValidationResult.Valid);

    /// <inheritdoc/>
    public abstract void Register(IServiceCollection services, DataSourceRegistry registry);

    // ------------------------------------------------------------------
    // Credential helpers — resolve from Context when available
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolves the "apiKey" credential from the injected context.
    /// Returns null when no context was provided or the key is absent.
    /// </summary>
    protected string? GetApiKey() => Context?.GetCredential("apiKey");

    /// <summary>
    /// Resolves the "keyId" credential from the injected context.
    /// Returns null when no context was provided or the key is absent.
    /// </summary>
    protected string? GetKeyId() => Context?.GetCredential("keyId");

    /// <summary>
    /// Resolves the "secretKey" credential from the injected context.
    /// Returns null when no context was provided or the key is absent.
    /// </summary>
    protected string? GetSecretKey() => Context?.GetCredential("secretKey");

    /// <summary>
    /// Resolves the "username" credential from the injected context.
    /// </summary>
    protected string? GetUsername() => Context?.GetCredential("username");

    /// <summary>
    /// Resolves the "password" credential from the injected context.
    /// </summary>
    protected string? GetPassword() => Context?.GetCredential("password");

    /// <summary>
    /// Resolves any credential by key from the injected context.
    /// </summary>
    protected string? GetCredential(string key) => Context?.GetCredential(key);

    /// <summary>
    /// Resolves a string setting by key from the injected context.
    /// </summary>
    protected string? GetSetting(string key) => Context?.GetSetting(key);

    /// <summary>
    /// Whether this module is enabled per the injected context.
    /// Returns true when no context was provided (fall-through to env var detection).
    /// </summary>
    protected bool IsContextEnabled => Context?.Enabled ?? true;

    /// <summary>
    /// The routing priority from the injected context. Returns 100 when no context provided.
    /// </summary>
    protected int ContextPriority => Context?.Priority ?? 100;
}
