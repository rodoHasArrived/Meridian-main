using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Application.Wizard.Metadata;

namespace Meridian.Application.Wizard.Core;

/// <summary>
/// Mutable bag of state threaded through all wizard steps.
/// Each step reads from it, updates it, and the coordinator persists it across calls.
/// </summary>
public sealed class WizardContext
{
    // ── Resolved provider metadata ──────────────────────────────────────────

    /// <summary>Providers detected from environment variables.</summary>
    public IReadOnlyList<AutoConfigurationService.DetectedProvider> DetectedProviders { get; set; }
        = Array.Empty<AutoConfigurationService.DetectedProvider>();

    // ── User selections ──────────────────────────────────────────────────────

    /// <summary>Use-case chosen by the user in <c>SelectUseCaseStep</c>.</summary>
    public UseCase? SelectedUseCase { get; set; }

    /// <summary>Data-source selection assembled by <c>ConfigureDataSourceStep</c>.</summary>
    public DataSourceSelection? DataSource { get; set; }

    /// <summary>Symbol list assembled by <c>ConfigureSymbolsStep</c>.</summary>
    public SymbolConfig[]? Symbols { get; set; }

    /// <summary>Storage config assembled by <c>ConfigureStorageStep</c>.</summary>
    public StorageConfig? Storage { get; set; }

    /// <summary>Backfill config assembled by <c>ConfigureBackfillStep</c>.</summary>
    public BackfillConfig? Backfill { get; set; }

    // ── Final outputs ────────────────────────────────────────────────────────

    /// <summary>The assembled application config, available after <c>ReviewConfigurationStep</c>.</summary>
    public AppConfig? FinalConfig { get; set; }

    /// <summary>File path where configuration was saved by <c>SaveConfigurationStep</c>.</summary>
    public string? SavedConfigPath { get; set; }

    // ── Arbitrary metadata for cross-step communication ──────────────────────

    /// <summary>
    /// Open bag for steps to exchange information that does not fit a typed property.
    /// Keys are step-specific strings; values must be serialisable.
    /// </summary>
    public Dictionary<string, object> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

    // ── Flags ────────────────────────────────────────────────────────────────

    /// <summary>When <c>true</c> the coordinator stops execution immediately.</summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Gets a typed metadata value set by a previous step, or the default if not present.
    /// </summary>
    public T? GetMeta<T>(string key) =>
        Metadata.TryGetValue(key, out var raw) && raw is T typed ? typed : default;

    /// <summary>Stores a typed metadata value.</summary>
    public void SetMeta<T>(string key, T value) where T : notnull =>
        Metadata[key] = value;
}
