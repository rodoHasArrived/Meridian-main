using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Application.Services;

/// <summary>
/// Adapter that wraps ConfigurationService to implement ICredentialResolver.
/// Enables using ConfigurationService's credential resolution with the unified ProviderFactory.
/// </summary>
public sealed class ConfigurationServiceCredentialAdapter : IProviderCredentialResolver
{
    private readonly ConfigurationService _configService;

    public ConfigurationServiceCredentialAdapter(ConfigurationService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null)
    {
        return _configService.CreateCredentialContext(providerType, configuredValues);
    }
}
