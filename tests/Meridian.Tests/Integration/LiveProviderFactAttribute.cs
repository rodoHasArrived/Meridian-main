using Xunit;

namespace Meridian.Tests.Integration;

/// <summary>
/// Opt-in gate for tests that call real external market-data providers over the network.
/// They are reported as skipped unless <see cref="OptInVariable"/> is <c>true</c>. Previously
/// these tests returned early and reported a pass when the provider was unreachable, so a plain
/// <c>dotnet test</c> in an offline environment counted them as passing without exercising
/// anything. Once opted in, an unreachable provider is a failure, not a silent pass.
/// </summary>
internal static class LiveProviderTestGate
{
    public const string OptInVariable = "MERIDIAN_RUN_LIVE_PROVIDER_TESTS";

    public static string? SkipReason =>
        string.Equals(Environment.GetEnvironmentVariable(OptInVariable), "true", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"Live provider tests call external services; set {OptInVariable}=true to run them.";
}

/// <summary><see cref="FactAttribute"/> that runs only when live provider tests are opted in.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveProviderFactAttribute : FactAttribute
{
    public LiveProviderFactAttribute() => Skip = LiveProviderTestGate.SkipReason;
}

/// <summary><see cref="TheoryAttribute"/> that runs only when live provider tests are opted in.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveProviderTheoryAttribute : TheoryAttribute
{
    public LiveProviderTheoryAttribute() => Skip = LiveProviderTestGate.SkipReason;
}
