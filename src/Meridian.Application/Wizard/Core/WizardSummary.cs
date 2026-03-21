using Meridian.Application.Config;

namespace Meridian.Application.Wizard.Core;

/// <summary>
/// Human-readable summary produced at the end of a completed wizard run.
/// </summary>
/// <param name="Success">Whether the wizard completed successfully.</param>
/// <param name="Config">The assembled configuration, or <c>null</c> on failure/cancellation.</param>
/// <param name="ConfigPath">File path where the configuration was saved, or <c>null</c>.</param>
/// <param name="ConfiguredItems">Bullet-point list of what was configured during the run.</param>
/// <param name="NextSteps">Recommended actions for the user after setup.</param>
public sealed record WizardSummary(
    bool Success,
    AppConfig? Config,
    string? ConfigPath,
    IReadOnlyList<string> ConfiguredItems,
    IReadOnlyList<string> NextSteps)
{
    /// <summary>Returns an empty failed summary.</summary>
    public static WizardSummary Empty { get; } = new(
        Success: false,
        Config: null,
        ConfigPath: null,
        ConfiguredItems: Array.Empty<string>(),
        NextSteps: Array.Empty<string>());
}
