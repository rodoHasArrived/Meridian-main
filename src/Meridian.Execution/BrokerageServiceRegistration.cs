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

        // Register the DI-resolvable IOrderGateway. In paper mode this is the fully functional
        // PaperTradingGateway (paper sessions have no live broker and no gate stack to bypass).
        // In live mode the raw BrokerageGatewayAdapter is an internal detail owned by the OMS;
        // the resolvable gateway is a read-only view whose submit/cancel members are blocked, so
        // live order placement cannot bypass the OMS pre-trade gate stack via IOrderGateway.
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

            // Resolve the named brokerage gateway via keyed registration, then expose it as an
            // OMS-governed read-only view rather than a directly submittable gateway.
            var gateway = ResolveBrokerageGateway(sp, brokerageConfig.Gateway);
            var adapterLogger = sp.GetRequiredService<ILogger<BrokerageGatewayAdapter>>();
            var adapter = new BrokerageGatewayAdapter(gateway, adapterLogger, brokerageConfig);
            return new OmsGovernedBrokerageOrderGateway(adapter);
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
                tradeEventPublisher: sp.GetService<ITradeEventPublisher>(),
                tradeFillHandoffFailureStore: sp.GetService<ITradeFillHandoffFailureStore>());
        });

        services.TryAddSingleton<BrokerageExecutionReconciliationService>();

        return services;
    }

    /// <summary>
    /// Explicitly composes accepted execution fills into one caller-owned ledger scope.
    /// This registration deliberately requires an authoritative durable posting target and
    /// durable handoff store because brokerage execution cannot infer or fake an accounting book.
    /// An in-memory ledger is optional and is updated only as a projection after persistence.
    /// </summary>
    public static IServiceCollection AddTradeFillLedgerPosting(
        this IServiceCollection services,
        TradeFillLedgerPostingContext postingContext,
        Func<IServiceProvider, ITradeFillLedgerPostingTarget> postingTargetFactory,
        Func<IServiceProvider, ITradeFillPostingStore> postingStoreFactory,
        Func<IServiceProvider, ITradeFillHandoffFailureStore> handoffFailureStoreFactory,
        Func<IServiceProvider, Ledger.Ledger?>? projectionLedgerFactory = null,
        Action<TradeFillLedgerPostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(postingContext);
        ArgumentNullException.ThrowIfNull(postingTargetFactory);
        ArgumentNullException.ThrowIfNull(postingStoreFactory);
        ArgumentNullException.ThrowIfNull(handoffFailureStoreFactory);

        postingContext = postingContext.Validate();
        var expectedScopeIdentity = TradeFillPostingScopeIdentity.FromContext(postingContext);
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
                || descriptor.ServiceType == typeof(ITradeFillLedgerPostingTarget)
                || descriptor.ServiceType == typeof(ITradeFillHandoffFailureStore)
                || descriptor.ServiceType == typeof(LedgerPostingConsumer)))
        {
            throw new InvalidOperationException(
                "A trade-fill publisher or posting store is already registered. Compose multiple ledger scopes explicitly instead of allowing one registration to shadow another.");
        }

        services.AddSingleton<ITradeFillPostingStore>(sp =>
        {
            var store = postingStoreFactory(sp)
                ?? throw new InvalidOperationException("The trade-fill posting store factory returned null.");
            TradeFillPostingScopeIdentity storeScopeIdentity;
            try
            {
                storeScopeIdentity = store.ScopeIdentity.Validate();
            }
            catch
            {
                store.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw;
            }

            if (!storeScopeIdentity.IsExact || storeScopeIdentity != expectedScopeIdentity)
            {
                store.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw new InvalidOperationException(
                    $"Trade-fill posting store scope identity '{storeScopeIdentity}' does not match ledger scope identity '{expectedScopeIdentity}'.");
            }

            return store;
        });
        services.AddSingleton<ITradeFillLedgerPostingTarget>(postingTargetFactory);
        services.AddSingleton<ITradeFillHandoffFailureStore>(sp =>
        {
            var store = handoffFailureStoreFactory(sp)
                ?? throw new InvalidOperationException("The trade-fill handoff-failure store factory returned null.");
            TradeFillPostingScopeIdentity storeScopeIdentity;
            try
            {
                storeScopeIdentity = store.ScopeIdentity.Validate();
            }
            catch
            {
                store.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw;
            }

            if (!storeScopeIdentity.IsExact || storeScopeIdentity != expectedScopeIdentity)
            {
                store.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw new InvalidOperationException(
                    $"Handoff-failure store scope identity '{storeScopeIdentity}' does not match ledger scope identity '{expectedScopeIdentity}'.");
            }

            return store;
        });
        services.AddSingleton<LedgerPostingConsumer>(sp =>
        {
            var securityGate = sp.GetRequiredService<ISecurityValidationGateService>();

            return new LedgerPostingConsumer(
                sp.GetRequiredService<ITradeFillLedgerPostingTarget>(),
                sp.GetRequiredService<ILogger<LedgerPostingConsumer>>(),
                sp.GetRequiredService<ITradeFillPostingStore>(),
                postingContext,
                projectionLedger: projectionLedgerFactory?.Invoke(sp),
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

/// <summary>
/// Configuration-owned exact ledger scope for the production execution host. A period or book
/// rollover requires updated values and a host restart so retained fills can never cross scopes.
/// </summary>
public sealed class TradeFillLedgerPostingHostOptions
{
    public const string SectionKey = "Execution:TradeFillLedgerPosting";

    public bool Enabled { get; set; }

    public Guid AggregateId { get; set; }

    public Guid PeriodId { get; set; }

    public Guid LedgerBookId { get; set; }

    public string AccountingPolicyId { get; set; } = "execution-trade-fill";

    public string AccountingPolicyVersion { get; set; } = "1";

    public long? ExpectedPeriodVersion { get; set; }

    public int ChannelCapacity { get; set; } = 10_000;

    public TimeSpan? DrainTimeout { get; set; }

    public TimeSpan? CancellationTimeout { get; set; }

    public TradeFillLedgerPostingContext BuildContext()
    {
        if (!Enabled)
            throw new InvalidOperationException("Trade-fill ledger posting is not enabled.");
        if (AggregateId == Guid.Empty)
            throw new InvalidOperationException("Execution trade-fill posting requires AggregateId.");
        if (PeriodId == Guid.Empty)
            throw new InvalidOperationException("Execution trade-fill posting requires PeriodId.");
        if (LedgerBookId == Guid.Empty)
            throw new InvalidOperationException("Execution trade-fill posting requires LedgerBookId.");
        if (!ExpectedPeriodVersion.HasValue || ExpectedPeriodVersion.Value < 0)
            throw new InvalidOperationException(
                "Execution trade-fill posting requires the authoritative ExpectedPeriodVersion.");

        return new TradeFillLedgerPostingContext(
            $"ledger-book/{LedgerBookId:D}/period/{PeriodId:D}/aggregate/{AggregateId:D}",
            AggregateId,
            PeriodId,
            LedgerBookId,
            AccountingPolicyId,
            AccountingPolicyVersion,
            ExpectedPeriodVersion).Validate();
    }
}
