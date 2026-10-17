using Microsoft.Extensions.Hosting;

namespace Meridian.Identity;

public enum AuthenticationMode
{
    Optional,
    Required
}

public static class AuthenticationModeResolver
{
    private const string AuthModeEnvVar = "MDC_AUTH_MODE";
    private const string PackagedBuildEnvVar = "MDC_PACKAGED_BUILD";
    private const string CustomerBuildEnvVar = "MERIDIAN_CUSTOMER_BUILD";

    public static AuthenticationMode Resolve(IHostEnvironment environment, AuthenticationConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        configuration ??= AuthenticationConfiguration.FromEnvironment();
        var configuredMode = configuration[AuthModeEnvVar];
        if (!string.IsNullOrWhiteSpace(configuredMode))
        {
            return configuredMode.Trim().ToLowerInvariant() switch
            {
                "optional" => AuthenticationMode.Optional,
                "required" => AuthenticationMode.Required,
                "auto" => ResolveDefault(environment, configuration),
                _ => throw new InvalidOperationException(
                    $"Unrecognized {AuthModeEnvVar} value '{configuredMode}'. Supported values: optional, required, auto.")
            };
        }

        return ResolveDefault(environment, configuration);
    }

    private static AuthenticationMode ResolveDefault(IHostEnvironment environment, AuthenticationConfiguration configuration)
    {
        if (IsPackagedOrCustomerBuild(configuration))
        {
            return AuthenticationMode.Required;
        }

        return environment.IsDevelopment() || environment.IsEnvironment("Test")
            ? AuthenticationMode.Optional
            : AuthenticationMode.Required;
    }

    internal static bool IsPackagedOrCustomerBuild(AuthenticationConfiguration? configuration = null)
    {
        configuration ??= AuthenticationConfiguration.FromEnvironment();
        return IsTruthy(configuration[PackagedBuildEnvVar]) ||
               IsTruthy(configuration[CustomerBuildEnvVar]);
    }

    private static bool IsTruthy(string? value)
        => value is not null &&
           (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
