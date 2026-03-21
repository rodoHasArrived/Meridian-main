using Microsoft.Extensions.Hosting;

namespace Meridian.Ui.Shared.Endpoints;

internal enum AuthenticationMode
{
    Optional,
    Required
}

internal static class AuthenticationModeResolver
{
    private const string AuthModeEnvVar = "MDC_AUTH_MODE";

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
        return environment.IsDevelopment() || environment.IsEnvironment("Test")
            ? AuthenticationMode.Optional
            : AuthenticationMode.Required;
    }
}
