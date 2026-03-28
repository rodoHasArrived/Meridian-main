namespace Meridian.Application.Config;

/// <summary>
/// Represents a validated, normalized configuration that has passed through the full configuration pipeline.
/// This is the single output type from all configuration entry points (wizard, auto-config, file load, hot reload).
/// </summary>
/// <remarks>
/// <para>The configuration pipeline ensures that:</para>
/// <list type="bullet">
/// <item><description>Environment overlays have been applied</description></item>
/// <item><description>Environment variable overrides have been applied</description></item>
/// <item><description>Credentials have been resolved from all sources</description></item>
/// <item><description>Self-healing fixes have been applied (if enabled)</description></item>
/// <item><description>Validation has been performed</description></item>
/// </list>
/// </remarks>
public sealed record ValidatedConfig
{
    /// <summary>
    /// The normalized and validated configuration.
    /// </summary>
    public required AppConfig Config { get; init; }

    /// <summary>
    /// Whether the configuration passed validation.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// The source path of the configuration file (if loaded from file).
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// Validation errors, if any.
    /// </summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Self-healing fixes that were applied to the configuration.
    /// </summary>
    public IReadOnlyList<string> AppliedFixes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Self-healing fixes that were <b>not</b> applied because production-strictness mode
    /// refused them (<c>MDC_CONFIG_STRICTNESS=production</c>).
    /// Each entry is an actionable description of the required manual correction.
    /// </summary>
    public IReadOnlyList<string> BlockedFixes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Warnings that should be surfaced to the user but don't prevent operation.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The environment name used for overlays (e.g., "Production", "Development").
    /// </summary>
    public string? EnvironmentName { get; init; }

    /// <summary>
    /// When the configuration was loaded/validated.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// How the configuration was sourced.
    /// </summary>
    public ConfigurationOrigin Source { get; init; } = ConfigurationOrigin.File;

    /// <summary>
    /// Creates a validated config from a raw AppConfig after running it through validation.
    /// </summary>
    public static ValidatedConfig FromConfig(
        AppConfig config,
        bool isValid,
        string? sourcePath = null,
        IReadOnlyList<string>? validationErrors = null,
        IReadOnlyList<string>? appliedFixes = null,
        IReadOnlyList<string>? warnings = null,
        string? environmentName = null,
        ConfigurationOrigin source = ConfigurationOrigin.File,
        IReadOnlyList<string>? blockedFixes = null)
    {
        return new ValidatedConfig
        {
            Config = config,
            IsValid = isValid,
            SourcePath = sourcePath,
            ValidationErrors = validationErrors ?? Array.Empty<string>(),
            AppliedFixes = appliedFixes ?? Array.Empty<string>(),
            BlockedFixes = blockedFixes ?? Array.Empty<string>(),
            Warnings = warnings ?? Array.Empty<string>(),
            EnvironmentName = environmentName,
            Source = source,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a ValidatedConfig representing a failure state.
    /// </summary>
    public static ValidatedConfig Failed(
        AppConfig? config,
        IReadOnlyList<string> errors,
        string? sourcePath = null,
        ConfigurationOrigin source = ConfigurationOrigin.File)
    {
        return new ValidatedConfig
        {
            Config = config ?? new AppConfig(),
            IsValid = false,
            SourcePath = sourcePath,
            ValidationErrors = errors,
            Source = source,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a ValidatedConfig with default/empty configuration.
    /// </summary>
    public static ValidatedConfig Default() => new()
    {
        Config = new AppConfig(),
        IsValid = true,
        Source = ConfigurationOrigin.Default,
        Timestamp = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Implicit conversion to AppConfig for backwards compatibility.
    /// </summary>
    public static implicit operator AppConfig(ValidatedConfig validated) => validated.Config;
}

/// <summary>
/// Indicates how the configuration was sourced/created.
/// </summary>
public enum ConfigurationOrigin : byte
{
    /// <summary>Default configuration (no file loaded).</summary>
    Default,

    /// <summary>Loaded from a configuration file.</summary>
    File,

    /// <summary>Created by the interactive wizard.</summary>
    Wizard,

    /// <summary>Created by auto-configuration.</summary>
    AutoConfig,

    /// <summary>Updated via hot reload.</summary>
    HotReload,

    /// <summary>Programmatically constructed.</summary>
    Programmatic
}
