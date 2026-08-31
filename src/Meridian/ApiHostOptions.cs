using Meridian.Application.Composition;
using Microsoft.Extensions.Configuration;

namespace Meridian;

/// <summary>
/// Host-facing options that separate local workstation hosting from remote API deployment.
/// </summary>
public sealed record ApiHostOptions
{
    public const string SectionName = "ApiHost";
    public const string CorsPolicyName = "MeridianWorkstationCors";

    public MeridianApiDeploymentMode DeploymentMode { get; init; } = MeridianApiDeploymentMode.LocalWorkstation;
    public string[] Urls { get; init; } = [];
    public string[] AllowedOrigins { get; init; } = [];
    public bool AllowInsecureTransportForReverseProxy { get; init; }
    public bool ServeWorkstationAssets { get; init; } = true;

    public bool IsProductionApi => DeploymentMode == MeridianApiDeploymentMode.ProductionApi;

    /// <summary>
    /// Maps the host deployment mode onto the shared typed posture (ADR-019) so the production
    /// registration policy resolves the same production answer as the host configuration.
    /// </summary>
    public MeridianDeploymentPosture ToDeploymentPosture()
        => DeploymentMode switch
        {
            MeridianApiDeploymentMode.LocalWorkstation => MeridianDeploymentPosture.LocalWorkstation,
            MeridianApiDeploymentMode.ProductionApi => MeridianDeploymentPosture.ProductionApi,
            MeridianApiDeploymentMode.Worker => MeridianDeploymentPosture.Worker,
            MeridianApiDeploymentMode.Migration => MeridianDeploymentPosture.Migration,
            _ => MeridianDeploymentPosture.Unspecified
        };

    public static ApiHostOptions FromConfiguration(IConfiguration configuration, int port)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        var deploymentMode = ResolveDeploymentMode(section["DeploymentMode"], configuration["MERIDIAN_API_DEPLOYMENT_MODE"]);
        var urls = ReadStringList(section.GetSection("Urls"));

        if (urls.Length == 0)
        {
            urls = SplitDelimited(configuration["MERIDIAN_API_URLS"] ?? configuration["ASPNETCORE_URLS"]);
        }

        if (urls.Length == 0)
        {
            urls = [$"http://localhost:{port}"];
        }

        var allowedOrigins = ReadStringList(section.GetSection("AllowedOrigins"));
        if (allowedOrigins.Length == 0)
        {
            allowedOrigins = SplitDelimited(configuration["MERIDIAN_WORKSTATION_ALLOWED_ORIGINS"]);
        }

        return new ApiHostOptions
        {
            DeploymentMode = deploymentMode,
            Urls = NormalizeUrls(urls),
            AllowedOrigins = NormalizeOrigins(allowedOrigins),
            AllowInsecureTransportForReverseProxy = ReadBoolean(section["AllowInsecureTransportForReverseProxy"])
                || ReadBoolean(configuration["MERIDIAN_ALLOW_INSECURE_REVERSE_PROXY_TRANSPORT"]),
            ServeWorkstationAssets = !ReadFalse(section["ServeWorkstationAssets"])
        };
    }

    private static MeridianApiDeploymentMode ResolveDeploymentMode(params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            // ADR-019 fail-closed parsing: an unrecognized deployment mode must stop the host
            // rather than silently demote it to the local-workstation posture. Numeric values
            // are rejected so an out-of-range integer cannot bypass the defined set.
            if (Enum.TryParse<MeridianApiDeploymentMode>(value, ignoreCase: true, out var mode)
                && Enum.IsDefined(mode)
                && !char.IsAsciiDigit(value.Trim()[0]))
            {
                return mode;
            }

            throw new InvalidOperationException(
                $"Unrecognized ApiHost deployment mode '{value}'. Supported values: " +
                $"{string.Join(", ", Enum.GetNames<MeridianApiDeploymentMode>())}.");
        }

        return MeridianApiDeploymentMode.LocalWorkstation;
    }

    private static string[] ReadStringList(IConfigurationSection section)
        => section.GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();

    private static string[] SplitDelimited(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] NormalizeUrls(IEnumerable<string> urls)
        => urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] NormalizeOrigins(IEnumerable<string> origins)
        => origins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool ReadBoolean(string? value)
        => bool.TryParse(value, out var parsed) && parsed;

    private static bool ReadFalse(string? value)
        => bool.TryParse(value, out var parsed) && !parsed;
}

public enum MeridianApiDeploymentMode
{
    LocalWorkstation,
    ProductionApi,
    Worker,
    Migration
}
