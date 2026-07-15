using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Execution.Adapters;
using Meridian.Execution.Events;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
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

        // Register IExecutionGateway for the OMS (OrderManagementSystem).
        // For paper mode, use the Execution-layer PaperTradingGateway which implements IExecutionGateway.
        // For live mode, the IBrokerageGateway itself implements IExecutionGateway.
        services.TryAddSingleton<IExecutionGateway>(sp =>
        {
            var brokerageConfig = sp.GetRequiredService<BrokerageConfiguration>();
            if (!brokerageConfig.LiveExecutionEnabled || brokerageConfig.Gateway == "paper")
            {
                var paperLogger = sp.GetRequiredService<ILogger<PaperTradingGateway>>();
                return new PaperTradingGateway(paperLogger, options: sp.GetService<Adapters.PaperTradingGatewayOptions>());
            }
            return ResolveBrokerageGateway(sp, brokerageConfig.Gateway);
        });

        // Register the BrokerageGatewayAdapter that bridges IBrokerageGateway → IOrderGateway
        services.TryAddSingleton<IOrderGateway>(sp =>
        {
            var brokerageConfig = sp.GetRequiredService<BrokerageConfiguration>();

            // If live execution is not enabled, always use the adapter-layer PaperTradingGateway
            if (!brokerageConfig.LiveExecutionEnabled || brokerageConfig.Gateway == "paper")
            {
                var paperLogger = sp.GetRequiredService<ILogger<Adapters.PaperTradingGateway>>();
                var secMaster = sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>();
                return new Adapters.PaperTradingGateway(paperLogger, secMaster, sp.GetService<Adapters.PaperTradingGatewayOptions>());
            }

            // Resolve the named brokerage gateway via keyed registration
            var gateway = ResolveBrokerageGateway(sp, brokerageConfig.Gateway);
            var adapterLogger = sp.GetRequiredService<ILogger<BrokerageGatewayAdapter>>();
            return new BrokerageGatewayAdapter(gateway, adapterLogger, brokerageConfig);
        });

        // Register the OMS with the resolved gateway
        services.TryAddSingleton<IOrderManager>(sp =>
        {
            var gateway = sp.GetRequiredService<IExecutionGateway>();
            var logger = sp.GetRequiredService<ILogger<OrderManagementSystem>>();
            var riskValidator = sp.GetService<IRiskValidator>();
            var securityMasterGate = sp.GetService<ISecurityMasterGate>();
            var operatorControls = sp.GetService<ExecutionOperatorControlService>();
            var auditTrail = sp.GetService<ExecutionAuditTrailService>();
            var portfolioState = sp.GetService<Meridian.Execution.Models.IPortfolioState>();
            var brokerageConfiguration = sp.GetRequiredService<BrokerageConfiguration>();
            var liveOrderReadinessGate = sp.GetService<ILiveOrderReadinessGate>();
            var orderManagementOptions = sp.GetService<OrderManagementSystemOptions>();

            return new OrderManagementSystem(
                gateway,
                logger,
                riskValidator,
                securityMasterGate,
                operatorControls,
                auditTrail,
                portfolioState,
                brokerageConfiguration: brokerageConfiguration,
                liveOrderReadinessGate: liveOrderReadinessGate,
                options: orderManagementOptions,
                tradeEventPublisher: sp.GetService<ITradeEventPublisher>());
        });

        services.TryAddSingleton<BrokerageExecutionReconciliationService>();

        return services;
    }

    /// <summary>
    /// Explicitly composes accepted execution fills into one caller-owned ledger scope.
    /// This registration deliberately requires ledger and durable-store factories because
    /// brokerage execution alone cannot infer the correct accounting book or period.
    /// </summary>
    public static IServiceCollection AddTradeFillLedgerPosting(
        this IServiceCollection services,
        string postingScope,
        Func<IServiceProvider, Ledger.Ledger> ledgerFactory,
        Func<IServiceProvider, ITradeFillPostingStore> postingStoreFactory,
        Action<TradeFillLedgerPostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(postingScope);
        ArgumentNullException.ThrowIfNull(ledgerFactory);
        ArgumentNullException.ThrowIfNull(postingStoreFactory);

        var normalizedScope = postingScope.Trim();
        var options = new TradeFillLedgerPostingOptions();
        configure?.Invoke(options);
        if (options.ChannelCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.ChannelCapacity), "Trade-fill channel capacity must be positive.");
        if (options.DrainTimeout is { } drainTimeout
            && (drainTimeout <= TimeSpan.Zero || drainTimeout == Timeout.InfiniteTimeSpan))
        {
            throw new ArgumentOutOfRangeException(nameof(options.DrainTimeout), "Trade-fill drain timeout must be positive and finite.");
        }
        if (options.CancellationTimeout is { } cancellationTimeout
            && (cancellationTimeout <= TimeSpan.Zero || cancellationTimeout == Timeout.InfiniteTimeSpan))
        {
            throw new ArgumentOutOfRangeException(nameof(options.CancellationTimeout), "Trade-fill cancellation timeout must be positive and finite.");
        }
        if (services.Any(static descriptor =>
                descriptor.ServiceType == typeof(ITradeEventPublisher)
                || descriptor.ServiceType == typeof(ITradeFillPostingStore)
                || descriptor.ServiceType == typeof(LedgerPostingConsumer)))
        {
            throw new InvalidOperationException(
                "A trade-fill publisher or posting store is already registered. Compose multiple ledger scopes explicitly instead of allowing one registration to shadow another.");
        }

        services.AddSingleton<ITradeFillPostingStore>(postingStoreFactory);
        services.AddSingleton<LedgerPostingConsumer>(sp =>
        {
            var securityGate = sp.GetRequiredService<ISecurityValidationGateService>();

            return new LedgerPostingConsumer(
                ledgerFactory(sp),
                sp.GetRequiredService<ILogger<LedgerPostingConsumer>>(),
                sp.GetRequiredService<ITradeFillPostingStore>(),
                normalizedScope,
                channelCapacity: options.ChannelCapacity,
                securityValidationGate: securityGate,
                requireSecurityMasterPostingGate: true,
                drainTimeout: options.DrainTimeout,
                cancellationTimeout: options.CancellationTimeout);
        });
        services.AddSingleton<ITradeEventPublisher>(sp =>
            sp.GetRequiredService<LedgerPostingConsumer>());

        return services;
    }

    /// <summary>
    /// Registers a specific <see cref="IBrokerageGateway"/> implementation as a keyed named gateway.
    /// Use the same <paramref name="gatewayId"/> when configuring <see cref="BrokerageConfiguration.Gateway"/>.
    /// </summary>
    /// <typeparam name="TGateway">The concrete brokerage gateway type.</typeparam>
    /// <param name="services">The DI service collection.</param>
    /// <param name="gatewayId">
    /// The gateway identifier (e.g., "alpaca", "ib"). Must match the value of
    /// <see cref="BrokerageConfiguration.Gateway"/> when this gateway should be selected.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBrokerageGateway<TGateway>(
        this IServiceCollection services, string gatewayId)
        where TGateway : class, IBrokerageGateway
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        services.AddSingleton<TGateway>();
        services.AddKeyedSingleton<IBrokerageGateway>(gatewayId,
            (sp, _) => sp.GetRequiredService<TGateway>());
        return services;
    }

    /// <summary>
    /// Registers a factory-created <see cref="IBrokerageGateway"/> as a keyed named gateway.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="gatewayId">
    /// The gateway identifier (e.g., "alpaca", "ib"). Must match the value of
    /// <see cref="BrokerageConfiguration.Gateway"/> when this gateway should be selected.
    /// </param>
    /// <param name="factory">Factory function that creates the gateway.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBrokerageGateway(
        this IServiceCollection services,
        string gatewayId,
        Func<IServiceProvider, IBrokerageGateway> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddKeyedSingleton<IBrokerageGateway>(gatewayId, (sp, _) => factory(sp));
        return services;
    }

    private static IBrokerageGateway ResolveBrokerageGateway(IServiceProvider sp, string gatewayId)
    {
        // NOTE: We intentionally use GetRequiredKeyedService here rather than
        // GetServices<IBrokerageGateway>() to avoid instantiating every registered gateway.
        // Enumerating all registered gateways would construct each one, which can fail if
        // some gateways validate credentials or perform I/O in their constructors even when
        // they are not the selected gateway.
        var gateway = sp.GetKeyedService<IBrokerageGateway>(gatewayId);
        if (gateway is not null)
            return gateway;

        throw new InvalidOperationException(
            $"No brokerage gateway registered with key '{gatewayId}'. " +
            "Register gateways using AddBrokerageGateway<T>(gatewayId) before calling AddBrokerageExecution().");
    }
}

/// <summary>Runtime controls for the explicit fill-to-ledger consumer.</summary>
public sealed class TradeFillLedgerPostingOptions
{
    public int ChannelCapacity { get; set; } = 10_000;

    public TimeSpan? DrainTimeout { get; set; }

    public TimeSpan? CancellationTimeout { get; set; }
}
