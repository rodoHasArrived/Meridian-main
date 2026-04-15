namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Centralized build/runtime guidance for Interactive Brokers provider surfaces so
/// user-facing messages stay aligned across simulation, stub, and documentation paths.
/// </summary>
internal static class IBBuildGuidance
{
    internal const string SetupGuidePath = "docs/providers/interactive-brokers-setup.md";
    internal const string SmokeBuildScriptPath = "scripts/dev/build-ibapi-smoke.ps1";
    internal const string PreferredVendorBuildSwitch = "EnableIbApiVendor=true";

    internal static string BuildRealProviderMessage(string surfaceName)
        => $"{surfaceName} requires the official IBApi surface for real TWS/Gateway connectivity. "
         + $"Build with -p:{PreferredVendorBuildSwitch} (preferred) or legacy -p:DefineConstants=IBAPI when referencing the official vendor DLL/project, "
         + $"or use -p:EnableIbApiSmoke=true for compile-only smoke verification via {SmokeBuildScriptPath}. "
         + $"See {SetupGuidePath} for the supported setup paths.";

    internal static string BuildSimulationModeMessage()
        => "This build is using IBSimulationClient rather than the real TWS/Gateway integration. "
         + $"See {SetupGuidePath} for enabling the official IBApi path.";
}
