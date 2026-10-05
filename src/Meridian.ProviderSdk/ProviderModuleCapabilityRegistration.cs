namespace Meridian.Infrastructure.DataSources;

/// <summary>
/// A provider capability whose concrete service was published by a successful module
/// registration. The module owns construction and lifetime; composition resolves the
/// implementation from its service provider rather than constructing an attributed type.
/// </summary>
/// <param name="ProviderId">Canonical provider-family identifier.</param>
/// <param name="Contract">Recognized capability contract implemented by the provider.</param>
/// <param name="ImplementationType">Concrete service type registered by the module.</param>
public sealed record ProviderModuleCapabilityRegistration(
    string ProviderId,
    Type Contract,
    Type ImplementationType);
