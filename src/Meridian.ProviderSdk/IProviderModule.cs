using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Defines a self-describing provider module that bundles all capability types for a single
/// external data provider (streaming, historical, symbol search, brokerage) behind one DI
/// registration entry point.
/// </summary>
/// <remarks>
/// Implement once per external provider (e.g., <c>AlpacaProviderModule</c>).
/// Use <see cref="ProviderModuleLoader"/> to discover and load modules from assemblies.
///
/// All new properties have default interface implementations so existing callers of
/// <see cref="Register"/> remain source-compatible without changes.
/// </remarks>
[ImplementsAdr("ADR-001", "Module-based provider registration entry point")]
[ImplementsAdr("ADR-005", "Module-based provider discovery alongside attribute-based discovery")]
public interface IProviderModule
{
    /// <summary>
    /// Unique provider identifier (e.g., "alpaca", "polygon"). Lower-case, no spaces.
    /// Default: derived from the implementing class name by stripping "ProviderModule".
    /// </summary>
    string ModuleId => GetType().Name.Replace("ProviderModule", "").ToLowerInvariant();

    /// <summary>
    /// Human-readable display name (e.g., "Alpaca Markets").
    /// Default: derived from the implementing class name by stripping "ProviderModule".
    /// </summary>
    string ModuleDisplayName => GetType().Name.Replace("ProviderModule", "");

    /// <summary>
    /// Capabilities advertised by this module before registration.
    /// Used by <see cref="ProviderModuleLoader"/> for capability-based filtering and
    /// load diagnostics. An empty array means the module does not self-declare capabilities.
    /// </summary>
    ProviderCapabilities[] Capabilities => Array.Empty<ProviderCapabilities>();

    /// <summary>
    /// Validates that prerequisites for this module are satisfied before registration.
    /// Default implementation always returns <see cref="ModuleValidationResult.Valid"/>.
    /// </summary>
    /// <remarks>
    /// Override to check credential availability, required services, or version
    /// compatibility. Returning an invalid result causes <see cref="ProviderModuleLoader"/>
    /// to skip this module and record a failure instead of throwing.
    /// </remarks>
    ValueTask<ModuleValidationResult> ValidateAsync(CancellationToken ct = default)
        => ValueTask.FromResult(ModuleValidationResult.Valid);

    /// <summary>
    /// Register provider services into the DI container.
    /// </summary>
    void Register(IServiceCollection services, DataSourceRegistry registry);
}

/// <summary>
/// Result of a module's self-validation step.
/// </summary>
/// <param name="IsValid">Whether the module passed validation and may proceed to registration.</param>
/// <param name="FailureReason">Human-readable explanation when <paramref name="IsValid"/> is false.</param>
public sealed record ModuleValidationResult(bool IsValid, string? FailureReason = null)
{
    /// <summary>Singleton valid result — avoids repeated allocations on the happy path.</summary>
    public static readonly ModuleValidationResult Valid = new(true);

    /// <summary>Creates a failure result with the specified reason.</summary>
    public static ModuleValidationResult Failure(string reason) => new(false, reason);
}
