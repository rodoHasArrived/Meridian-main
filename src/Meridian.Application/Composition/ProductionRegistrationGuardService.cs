using Meridian.Contracts.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Application.Composition;

/// <summary>
/// Final-graph production guard (ADR-019). The composition root inserts this as the first
/// <see cref="IHostedService"/> so it runs before any other hosted service and re-validates the
/// complete service collection — including registrations added after <c>AddMarketDataServices</c>
/// — once the host starts. In production postures it additionally resolves non-keyed singleton
/// factory descriptors so factory-hidden implementations are checked by their actual runtime
/// type; any violation aborts startup with the full list of prohibited bindings.
/// </summary>
public sealed class ProductionRegistrationGuardService : IHostedService
{
    private readonly IServiceCollection _services;
    private readonly IServiceProvider _provider;

    public ProductionRegistrationGuardService(IServiceCollection services, IServiceProvider provider)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!ProductionServiceRegistrationPolicy.IsProductionComposition(_services))
        {
            // W9-TRUTH-001: the supported local posture asserts durable money-path stores at
            // startup unless the composition deliberately runs labeled — a pinned non-real
            // provenance declaration is the only sanctioned way to keep fabricated in-memory
            // stores, because the label then rides every workspace surface. An unlabeled local
            // graph with in-memory durable bindings refuses startup naming the bindings.
            if (ProductionServiceRegistrationPolicy.IsSupportedLocalComposition(_services))
            {
                var runsLabeled =
                    ProductionServiceRegistrationPolicy.TryResolveForcedProvenance(_services, out var forced)
                    && forced.IsNonReal();
                ProductionServiceRegistrationPolicy.ValidateSupportedLocal(
                    _services,
                    requireDurableStores: !runsLabeled);
            }

            return Task.CompletedTask;
        }

        var violations = new SortedSet<string>(StringComparer.Ordinal);
        violations.UnionWith(ProductionServiceRegistrationPolicy.CollectStaticViolations(_services));
        violations.UnionWith(CollectFactoryViolations(cancellationToken));

        ProductionServiceRegistrationPolicy.ThrowIfViolations(violations);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private IEnumerable<string> CollectFactoryViolations(CancellationToken cancellationToken)
    {
        // Keyed factory descriptors are excluded: their instances cannot be enumerated without
        // knowing every key, and the static descriptor pass still covers keyed implementation
        // types. Eager resolution is intentional fail-fast for production postures only.
        var factorySingletonServiceTypes = _services
            .Where(descriptor => descriptor.Lifetime == ServiceLifetime.Singleton
                                 && !descriptor.IsKeyedService
                                 && descriptor.ImplementationFactory is not null
                                 && !descriptor.ServiceType.IsGenericTypeDefinition)
            .Select(descriptor => descriptor.ServiceType)
            .Distinct()
            .ToArray();

        foreach (var serviceType in factorySingletonServiceTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            object?[] instances;
            try
            {
                instances = _provider.GetServices(serviceType).ToArray();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Production startup policy could not construct singleton service '{serviceType.FullName}' " +
                    "during final-graph validation; production postures require every registered binding to be constructible.",
                    ex);
            }

            foreach (var instance in instances)
            {
                var implementationType = instance?.GetType();
                if (implementationType is not null &&
                    ProductionServiceRegistrationPolicy.IsNonProductionOnlyImplementation(implementationType))
                {
                    yield return implementationType.FullName ?? implementationType.Name;
                }
            }
        }
    }
}

public static class ProductionRegistrationGuardServiceCollectionExtensions
{
    /// <summary>
    /// Registers the final-graph production guard as the first <see cref="IHostedService"/>.
    /// Idempotent; <c>AddMarketDataServices</c> calls this for every composed host, and hosts
    /// that compose without the shared root can call it directly.
    /// </summary>
    public static IServiceCollection AddProductionRegistrationGuard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ProductionRegistrationGuardService)))
        {
            return services;
        }

        services.AddSingleton(sp => new ProductionRegistrationGuardService(services, sp));
        services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(
            sp => sp.GetRequiredService<ProductionRegistrationGuardService>()));
        return services;
    }
}
