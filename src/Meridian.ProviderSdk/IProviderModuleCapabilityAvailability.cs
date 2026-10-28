namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Optional late configuration gate for module-owned capabilities. Hosts evaluate this after
/// building the container, before resolving a capability's concrete factory. This establishes
/// configuration only, not connectivity, entitlement, or production readiness.
/// </summary>
public interface IProviderModuleCapabilityAvailability
{
    bool IsCapabilityConfigured(Type contract, IServiceProvider services);
}
