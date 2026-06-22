using Microsoft.Extensions.Hosting;

namespace Meridian.Identity;

internal enum AuthenticationMode
{
    Optional,
    Required
}

internal static class AuthenticationModeResolver
{
    private const string AuthModeEnvVar = "MDC_AUTH_MODE";
    private const string PackagedBuildEnvVar = "MDC_PACKAGED_BUILD";
    private const string CustomerBuildEnvVar = "MERIDIAN_CUSTOMER_BUILD";

    public static AuthenticationMode Resolve(IHostEnvironment environment)
    {
        var configuredMode = Environment.GetEnvironmentVariable(AuthModeEnvVar);
        if (!string.IsNullOrWhiteSpace(configuredMode))
        {
            return configuredMode.Trim().ToLowerInvariant() switch
            {
                "optional" => AuthenticationMode.Optional,
                "required" => AuthenticationMode.Required,
                "auto" => ResolveDefault(environment),
                _ => ResolveDefault(environment)
            };
        }

        return ResolveDefault(environment);
    }

    private static AuthenticationMode ResolveDefault(IHostEnvironment environment)
    {
        if (IsPackagedOrCustomerBuild())
        {
            return AuthenticationMode.Required;
        }

        return environment.IsDevelopment() || environment.IsEnvironment("Test")
            ? AuthenticationMode.Optional
            : AuthenticationMode.Required;
    }

    private static bool IsPackagedOrCustomerBuild()
        => IsTruthy(Environment.GetEnvironmentVariable(PackagedBuildEnvVar)) ||
           IsTruthy(Environment.GetEnvironmentVariable(CustomerBuildEnvVar));

    private static bool IsTruthy(string? value)
        => value is not null &&
           (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
