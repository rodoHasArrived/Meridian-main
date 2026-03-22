using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Application.Services;

/// <summary>
/// Adapter that wraps ConfigurationService to implement ICredentialResolver.
/// Enables using ConfigurationService's credential resolution with the unified ProviderFactory.
/// </summary>
public sealed class ConfigurationServiceCredentialAdapter : ICredentialResolver
{
    private readonly ConfigurationService _configService;

    public ConfigurationServiceCredentialAdapter(ConfigurationService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public (string? KeyId, string? SecretKey) ResolveAlpacaCredentials(string? configKeyId, string? configSecretKey)
        => _configService.ResolveAlpacaCredentials(configKeyId, configSecretKey);

    public string? ResolvePolygonCredentials(string? configApiKey)
        => _configService.ResolvePolygonCredentials(configApiKey);

    public string? ResolveTiingoCredentials(string? configToken)
        => _configService.ResolveTiingoCredentials(configToken);

    public string? ResolveFinnhubCredentials(string? configApiKey)
        => _configService.ResolveFinnhubCredentials(configApiKey);

    public string? ResolveAlphaVantageCredentials(string? configApiKey)
        => _configService.ResolveAlphaVantageCredentials(configApiKey);

    public string? ResolveFredCredentials(string? configApiKey)
        => _configService.ResolveFredCredentials(configApiKey);

    public string? ResolveNasdaqCredentials(string? configApiKey)
        => _configService.ResolveNasdaqCredentials(configApiKey);
}
