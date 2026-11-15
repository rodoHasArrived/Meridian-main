using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Infrastructure.Adapters.NYSE;

/// <summary>Refuses NYSE activation where credential ownership is required but not implemented.</summary>
public sealed record NyseCredentialAccessPolicy(bool RequireExplicitOwnership)
{
    internal const string UnavailableMessage =
        "NYSE activation is unavailable in production, packaged, or customer builds until " +
        "tenant and account-scoped credential ownership is supported. No provider request was sent.";

    internal static void EnsureAccessAllowed(IServiceProvider? services = null, IConfiguration? configuration = null)
    {
        if (!IsAccessAllowed(services, configuration))
            throw new InvalidOperationException(UnavailableMessage);
    }

    internal static bool IsAccessAllowed(IServiceProvider? services = null, IConfiguration? configuration = null)
        => services?.GetService<NyseCredentialAccessPolicy>()?.RequireExplicitOwnership != true &&
            !RequiresOwnership(configuration ?? services?.GetService<IConfiguration>()) &&
            !RequiresOwnership(null);

    private static bool RequiresOwnership(IConfiguration? configuration)
    {
        string? Read(string name) => configuration is null ? Environment.GetEnvironmentVariable(name) : configuration[name];
        // Keep direct public registration/construction fail-closed before the Application host
        // supplies its final typed policy. These are the host's production and distribution flags.
        var environment = Read("ASPNETCORE_ENVIRONMENT") ?? Read("DOTNET_ENVIRONMENT")
            ?? Read("MERIDIAN_ENVIRONMENT") ?? Read("MERIDIAN_DEPLOYMENT_ENVIRONMENT");
        return Same(environment, "Production") || Same(Read("MERIDIAN_MODE"), "Production") ||
            Same(Read("MERIDIAN_MODE"), "Live") || Same(Read("MERIDIAN_API_DEPLOYMENT_MODE"), "ProductionApi") ||
            IsEnabled(Read("MDC_PACKAGED_BUILD")) || IsEnabled(Read("MERIDIAN_CUSTOMER_BUILD"));
    }

    private static bool IsEnabled(string? value) => Same(value, "true") || value == "1" || Same(value, "yes");
    private static bool Same(string? value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
