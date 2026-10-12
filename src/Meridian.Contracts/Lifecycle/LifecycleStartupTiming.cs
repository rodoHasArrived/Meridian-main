namespace Meridian.Contracts.Lifecycle;

/// <summary>Startup budget shared by the installed launcher and supervisor command gates.</summary>
public static class LifecycleStartupTiming
{
    /// <summary>
    /// Includes first-use dedicated database initialization, database start (with its tool allowance),
    /// and the host readiness deadline. Callers add their own receipt-observation allowance.
    /// </summary>
    public static TimeSpan GetStartupBudget(LifecycleSupervisorManifestDto manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.StartupTimeoutSeconds is < 1 or > 600)
            throw new ArgumentOutOfRangeException(nameof(manifest), "Startup timeout must be between 1 and 600 seconds.");
        if (manifest.DatabaseTimeoutSeconds is < 1 or > 600)
            throw new ArgumentOutOfRangeException(nameof(manifest), "Database timeout must be between 1 and 600 seconds.");
        if (!Enum.IsDefined(manifest.DatabaseMode))
            throw new ArgumentOutOfRangeException(nameof(manifest), "Database management mode is unsupported.");

        var databaseSeconds = manifest.DatabaseMode == LifecycleDatabaseManagementMode.Dedicated
            ? 2 * manifest.DatabaseTimeoutSeconds + 5
            : 0;
        return TimeSpan.FromSeconds(manifest.StartupTimeoutSeconds + databaseSeconds);
    }
}
