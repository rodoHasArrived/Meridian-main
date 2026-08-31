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
            ValidateStatically(_services);
            return Task.CompletedTask;
        }

        var violations = new SortedSet<string>(StringComparer.Ordinal);
        violations.UnionWith(ProductionServiceRegistrationPolicy.CollectStaticViolations(_services));
        violations.UnionWith(CollectFactoryViolations(cancellationToken));

        ProductionServiceRegistrationPolicy.ThrowIfViolations(violations);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Everything ADR-019 can decide from the service descriptors alone, with nothing resolved.
    /// </summary>
    /// <remarks>
    /// <para>Split out so <see cref="StaticProductionRegistrationGuardService"/> can run it in
    /// front of a shell. <c>ProductionServiceRegistrationPolicy</c> resolves nothing — every check
    /// it makes is a scan of <see cref="IServiceCollection"/> — so this half is cheap and answers
    /// immediately, which is what <c>IStartupRefusalGuard</c> requires. Only
    /// <see cref="CollectFactoryViolations"/> is expensive, and it stays behind the window where
    /// the ninth review round put it.</para>
    ///
    /// <para>W9-TRUTH-001: the supported local posture asserts durable money-path stores at startup
    /// unless the composition deliberately runs labeled — a pinned non-real provenance declaration
    /// is the only sanctioned way to keep fabricated in-memory stores, because the label then rides
    /// every workspace surface. An unlabeled local graph with in-memory durable bindings refuses
    /// startup naming the bindings.</para>
    /// </remarks>
    internal static void ValidateStatically(IServiceCollection services)
    {
        if (ProductionServiceRegistrationPolicy.IsProductionComposition(services))
        {
            ProductionServiceRegistrationPolicy.ThrowIfViolations(
                new SortedSet<string>(
                    ProductionServiceRegistrationPolicy.CollectStaticViolations(services),
                    StringComparer.Ordinal));
            return;
        }

        if (ProductionServiceRegistrationPolicy.IsSupportedLocalComposition(services))
        {
            var runsLabeled =
                ProductionServiceRegistrationPolicy.TryResolveForcedProvenance(services, out var forced)
                && forced.IsNonReal();
            ProductionServiceRegistrationPolicy.ValidateSupportedLocal(
                services,
                requireDurableStores: !runsLabeled);
        }
    }

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
                // A refusal, not a component failure: production policy has decided this composition
                // must not run. Raised as StartupRefusedException so a host that escalates refusals
                // -- see HostStartupEscalation.IsRefusal -- does not degrade past it. The other
                // refusal sites moved to the type; this one is inside the guard rather than the
                // policy, and leaving it bare kept the WPF shell's tolerant catch swallowing it.
                throw new StartupRefusedException(
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

        // ProductionRegistrationGuardService itself is deliberately NOT an IStartupRefusalGuard. In
        // a production composition it resolves every singleton registered by factory, to prove the
        // graph is constructible -- eager validation that is far too much work to put in front of a
        // shell, because a blocked constructor there would leave an authenticated operator with no
        // window at all. It keeps running as an ordinary hosted service, first in the chain.
        //
        // Its STATIC half does go in front of the shell, as a separate guard. Postponing that half
        // too meant a prohibited production graph stayed interactive -- MainWindow shown, workflows
        // started -- until hosted-service startup got round to shutting it down, and the descriptor
        // scan that would have refused it resolves nothing and answers immediately (Codex review
        // finding on PR #2871).
        // Appended, not inserted at the front: the ADR-019 hosted service has to stay the FIRST
        // IHostedService, and inserting a descriptor of any type at index 0 displaces it. Order
        // among refusal guards carries no meaning -- StartupRefusalPreflight runs all of them.
        services.AddSingleton(_ => new StaticProductionRegistrationGuardService(services));
        services.AddSingleton<IStartupRefusalGuard>(
            sp => sp.GetRequiredService<StaticProductionRegistrationGuardService>());

        return services;
    }
}

/// <summary>
/// The descriptor-only half of ADR-019's final-graph guard, run ahead of a shell.
/// </summary>
/// <remarks>
/// <para>Registered as an <see cref="IStartupRefusalGuard"/> because it satisfies both halves of
/// that contract: it asks a question about the composition rather than acting on it, so running it
/// twice changes nothing, and it answers from <see cref="IServiceCollection"/> alone with nothing
/// resolved, so it cannot block in front of a window.</para>
///
/// <para><see cref="ProductionRegistrationGuardService"/> still runs the same static checks behind
/// the shell, alongside the factory validation this one deliberately omits. That overlap is the
/// point: a host that does not pre-run the guards is covered by exactly the same rules.</para>
/// </remarks>
public sealed class StaticProductionRegistrationGuardService : IStartupRefusalGuard
{
    private readonly IServiceCollection _services;

    public StaticProductionRegistrationGuardService(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ProductionRegistrationGuardService.ValidateStatically(_services);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
