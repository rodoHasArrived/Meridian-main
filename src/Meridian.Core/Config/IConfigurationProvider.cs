using Meridian.Infrastructure.Contracts;

namespace Meridian.Application.Config;

/// <summary>
/// Configuration change notification event args.
/// </summary>
/// <param name="Section">Configuration section that changed.</param>
/// <param name="Key">Specific key that changed (null if entire section).</param>
/// <param name="OldValue">Previous value.</param>
/// <param name="NewValue">New value.</param>
/// <param name="Source">Source of the change.</param>
public sealed record ConfigurationChangedEventArgs(
    string Section,
    string? Key,
    object? OldValue,
    object? NewValue,
    ConfigurationSource Source);

/// <summary>
/// Sources of configuration values.
/// </summary>
public enum ConfigurationSource : byte
{
    /// <summary>Default/hardcoded value.</summary>
    Default,

    /// <summary>Loaded from configuration file.</summary>
    File,

    /// <summary>Loaded from environment variable.</summary>
    Environment,

    /// <summary>Set at runtime via API.</summary>
    Runtime,

    /// <summary>Loaded from command line arguments.</summary>
    CommandLine,

    /// <summary>Loaded from remote configuration service.</summary>
    Remote
}

/// <summary>
/// Metadata about a configuration value.
/// </summary>
/// <param name="Section">Configuration section.</param>
/// <param name="Key">Configuration key.</param>
/// <param name="ValueType">Type of the configuration value.</param>
/// <param name="Description">Human-readable description.</param>
/// <param name="IsRequired">Whether the configuration is required.</param>
/// <param name="DefaultValue">Default value if not configured.</param>
/// <param name="EnvironmentVariable">Environment variable that can override.</param>
/// <param name="ValidationRules">Validation constraints.</param>
public sealed record ConfigurationMetadata(
    string Section,
    string Key,
    Type ValueType,
    string Description,
    bool IsRequired = false,
    object? DefaultValue = null,
    string? EnvironmentVariable = null,
    IReadOnlyList<string>? ValidationRules = null)
{
    /// <summary>
    /// Full path to the configuration value (Section:Key).
    /// </summary>
    public string FullPath => string.IsNullOrEmpty(Section) ? Key : $"{Section}:{Key}";
}

/// <summary>
/// Result of configuration validation.
/// </summary>
/// <param name="IsValid">Whether configuration is valid.</param>
/// <param name="Errors">Validation errors.</param>
/// <param name="Warnings">Validation warnings.</param>
public sealed record ConfigurationValidationResult(
    bool IsValid,
    IReadOnlyList<ConfigurationValidationError> Errors,
    IReadOnlyList<ConfigurationValidationWarning> Warnings);

/// <summary>
/// Configuration validation error.
/// </summary>
/// <param name="Path">Configuration path that failed validation.</param>
/// <param name="Message">Error message.</param>
/// <param name="Value">Invalid value.</param>
public sealed record ConfigurationValidationError(string Path, string Message, object? Value = null);

/// <summary>
/// Configuration validation warning.
/// </summary>
/// <param name="Path">Configuration path with warning.</param>
/// <param name="Message">Warning message.</param>
public sealed record ConfigurationValidationWarning(string Path, string Message);

/// <summary>
/// Unified configuration provider interface for consistent access to
/// application configuration across all components.
/// </summary>
/// <remarks>
/// Addresses scattered configuration patterns by providing:
/// - Single interface for all configuration access
/// - Type-safe configuration retrieval
/// - Change notification support
/// - Validation with metadata
/// - Environment variable override support
///
/// Consolidates:
/// - AppConfig direct access
/// - ConfigWatcher file monitoring
/// - ConfigEnvironmentOverride environment handling
/// - Various Options/Config record types (19+ types)
/// </remarks>
[ImplementsAdr("ADR-001", "Unified configuration provider")]
public interface IConfigurationProvider
{
    /// <summary>
    /// Gets a configuration value.
    /// </summary>
    /// <typeparam name="T">Expected type.</typeparam>
    /// <param name="section">Configuration section.</param>
    /// <param name="key">Configuration key.</param>
    /// <param name="defaultValue">Default value if not found.</param>
    /// <returns>Configuration value.</returns>
    T Get<T>(string section, string key, T defaultValue = default!);

    /// <summary>
    /// Gets a configuration section as a typed object.
    /// </summary>
    /// <typeparam name="T">Section type.</typeparam>
    /// <param name="section">Section name.</param>
    /// <returns>Configuration section.</returns>
    T GetSection<T>(string section) where T : class, new();

    /// <summary>
    /// Tries to get a configuration value.
    /// </summary>
    /// <typeparam name="T">Expected type.</typeparam>
    /// <param name="section">Configuration section.</param>
    /// <param name="key">Configuration key.</param>
    /// <param name="value">Retrieved value.</param>
    /// <returns>True if value exists.</returns>
    bool TryGet<T>(string section, string key, out T? value);

    /// <summary>
    /// Sets a configuration value at runtime.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="section">Configuration section.</param>
    /// <param name="key">Configuration key.</param>
    /// <param name="value">Value to set.</param>
    void Set<T>(string section, string key, T value);

    /// <summary>
    /// Gets the source of a configuration value.
    /// </summary>
    /// <param name="section">Configuration section.</param>
    /// <param name="key">Configuration key.</param>
    /// <returns>Configuration source.</returns>
    ConfigurationSource GetSource(string section, string key);

    /// <summary>
    /// Validates all configuration.
    /// </summary>
    /// <returns>Validation result.</returns>
    ConfigurationValidationResult Validate();

    /// <summary>
    /// Validates a specific section.
    /// </summary>
    /// <param name="section">Section to validate.</param>
    /// <returns>Validation result.</returns>
    ConfigurationValidationResult ValidateSection(string section);

    /// <summary>
    /// Gets metadata for all registered configuration.
    /// </summary>
    /// <returns>List of configuration metadata.</returns>
    IReadOnlyList<ConfigurationMetadata> GetMetadata();

    /// <summary>
    /// Gets metadata for a specific section.
    /// </summary>
    /// <param name="section">Section name.</param>
    /// <returns>List of configuration metadata for the section.</returns>
    IReadOnlyList<ConfigurationMetadata> GetSectionMetadata(string section);

    /// <summary>
    /// Registers configuration metadata.
    /// </summary>
    /// <param name="metadata">Metadata to register.</param>
    void RegisterMetadata(ConfigurationMetadata metadata);

    /// <summary>
    /// Reloads configuration from all sources.
    /// </summary>
    void Reload();

    /// <summary>
    /// Event raised when configuration changes.
    /// </summary>
    event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;
}

/// <summary>
/// Extension methods for common configuration patterns.
/// </summary>
public static class ConfigurationProviderExtensions
{
    /// <summary>
    /// Gets a connection string.
    /// </summary>
    public static string? GetConnectionString(this IConfigurationProvider config, string name)
        => config.Get<string>("ConnectionStrings", name, null!);

    /// <summary>
    /// Gets provider options.
    /// </summary>
    public static T GetProviderOptions<T>(this IConfigurationProvider config, string providerName)
        where T : class, new()
        => config.GetSection<T>($"Providers:{providerName}");

    /// <summary>
    /// Checks if a feature is enabled.
    /// </summary>
    public static bool IsFeatureEnabled(this IConfigurationProvider config, string featureName)
        => config.Get("Features", featureName, false);

    /// <summary>
    /// Gets storage configuration.
    /// </summary>
    public static T GetStorageConfig<T>(this IConfigurationProvider config)
        where T : class, new()
        => config.GetSection<T>("Storage");

    /// <summary>
    /// Gets backfill configuration.
    /// </summary>
    public static T GetBackfillConfig<T>(this IConfigurationProvider config)
        where T : class, new()
        => config.GetSection<T>("Backfill");

    /// <summary>
    /// Registers provider options metadata.
    /// </summary>
    public static void RegisterProviderConfig<T>(
        this IConfigurationProvider config,
        string providerName,
        string description,
        bool required = false)
    {
        config.RegisterMetadata(new ConfigurationMetadata(
            Section: $"Providers:{providerName}",
            Key: "",
            ValueType: typeof(T),
            Description: description,
            IsRequired: required));
    }
}

/// <summary>
/// Base class for strongly-typed configuration sections.
/// Provides common functionality for Options/Config record types.
/// </summary>
[ImplementsAdr("ADR-001", "Base configuration section")]
public abstract record ConfigurationSection
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public abstract string SectionName { get; }

    /// <summary>
    /// Validates the configuration section.
    /// </summary>
    /// <returns>List of validation errors.</returns>
    public virtual IReadOnlyList<string> Validate() => Array.Empty<string>();
}

/// <summary>
/// Base class for provider-specific options.
/// </summary>
/// <param name="Enabled">Whether the provider is enabled.</param>
[ImplementsAdr("ADR-001", "Base provider options")]
public abstract record ProviderOptionsBase(bool Enabled = true) : ConfigurationSection
{
    /// <summary>
    /// Provider name for registration.
    /// </summary>
    public abstract string ProviderName { get; }

    /// <inheritdoc/>
    public override string SectionName => $"Providers:{ProviderName}";
}
