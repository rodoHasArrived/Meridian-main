using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// DI registration helpers for the brokerage execution layer.
/// Registers brokerage gateways, the OMS, and the <see cref="IOrderGateway"/>
/// adapter that bridges <see cref="IBrokerageGateway"/> into the existing
/// execution framework.
/// </summary>
public static class BrokerageServiceRegistration
{
    /// <summary>
    /// Registers the brokerage execution layer services.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBrokerageExecution(
        this IServiceCollection services,
        Action<BrokerageConfiguration>? configure = null)
    {
        var config = new BrokerageConfiguration();
        configure?.Invoke(config);
        services.AddSingleton(config);

        // Register the BrokerageGatewayAdapter that bridges IBrokerageGateway → IOrderGateway
        services.TryAddSingleton<IOrderGateway>(sp =>
        {
            var brokerageConfig = sp.GetRequiredService<BrokerageConfiguration>();

            // If live execution is not enabled, always use PaperTradingGateway
            if (!brokerageConfig.LiveExecutionEnabled || brokerageConfig.Gateway == "paper")
            {
                var paperLogger = sp.GetRequiredService<ILogger<Adapters.PaperTradingGateway>>();
                return new Adapters.PaperTradingGateway(paperLogger);
            }

            // Resolve the named brokerage gateway
            var gateway = ResolveBrokerageGateway(sp, brokerageConfig.Gateway);
            var adapterLogger = sp.GetRequiredService<ILogger<BrokerageGatewayAdapter>>();
            return new BrokerageGatewayAdapter(gateway, adapterLogger);
        });

        // Register the OMS with the resolved gateway
        services.TryAddSingleton<IOrderManager>(sp =>
        {
            var gateway = sp.GetRequiredService<IExecutionGateway>();
            var logger = sp.GetRequiredService<ILogger<OrderManagementSystem>>();
            var riskValidator = sp.GetService<IRiskValidator>();
            return new OrderManagementSystem(gateway, logger, riskValidator);
        });

        return services;
    }

    /// <summary>
    /// Registers a specific <see cref="IBrokerageGateway"/> implementation as a named gateway.
    /// </summary>
    /// <typeparam name="TGateway">The concrete brokerage gateway type.</typeparam>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBrokerageGateway<TGateway>(this IServiceCollection services)
        where TGateway : class, IBrokerageGateway
    {
        services.AddSingleton<TGateway>();
        services.AddSingleton<IBrokerageGateway>(sp => sp.GetRequiredService<TGateway>());
        return services;
    }

    /// <summary>
    /// Registers a factory-created <see cref="IBrokerageGateway"/> as a named gateway.
    /// </summary>
    public static IServiceCollection AddBrokerageGateway(
        this IServiceCollection services,
        Func<IServiceProvider, IBrokerageGateway> factory)
    {
        services.AddSingleton(factory);
        return services;
    }

    private static IBrokerageGateway ResolveBrokerageGateway(IServiceProvider sp, string gatewayId)
    {
        // Try to find a registered IBrokerageGateway whose GatewayId matches
        var gateways = sp.GetServices<IBrokerageGateway>();
        var match = gateways.FirstOrDefault(g =>
            string.Equals(g.GatewayId, gatewayId, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            return match;

        throw new InvalidOperationException(
            $"No brokerage gateway registered with ID '{gatewayId}'. " +
            $"Available gateways: {string.Join(", ", gateways.Select(g => g.GatewayId))}. " +
            "Register gateways using AddBrokerageGateway<T>() before calling AddBrokerageExecution().");
    }
}
