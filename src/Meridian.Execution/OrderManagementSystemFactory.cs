using Meridian.Execution.Events;
using Meridian.Execution.Models;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// Which host is composing the OMS. The two hosts historically built it separately; their
/// differences are kept exactly and named here so they cannot drift further.
/// </summary>
public enum OrderManagementSystemComposition
{
    /// <summary>
    /// <see cref="BrokerageServiceRegistration.AddBrokerageExecution"/>: library composition.
    /// Risk validator, portfolio state, and options are optional; paper-session persistence is
    /// not wired.
    /// </summary>
    BrokerageExecution,

    /// <summary>
    /// The workstation host (<c>UiServer</c>): order routing is fail-closed, so the pre-trade risk
    /// validator, portfolio state, and options are required, and paper-session persistence is wired.
    /// </summary>
    HostedWorkstation
}

/// <summary>
/// The single place that constructs <see cref="OrderManagementSystem"/> from a service provider.
/// Every OMS dependency added later (for example the order authority resolver) is wired here once,
/// so both hosts receive it.
/// </summary>
public static class OrderManagementSystemFactory
{
    public static OrderManagementSystem Create(IServiceProvider services, OrderManagementSystemComposition composition)
    {
        ArgumentNullException.ThrowIfNull(services);

        var hosted = composition switch
        {
            OrderManagementSystemComposition.BrokerageExecution => false,
            OrderManagementSystemComposition.HostedWorkstation => true,
            _ => throw new ArgumentOutOfRangeException(nameof(composition), composition, "Unknown OMS composition.")
        };

        var gateway = services.GetRequiredService<IExecutionGateway>();
        var logger = services.GetRequiredService<ILogger<OrderManagementSystem>>();

        // The hosted workstation refuses to start without these; the library composition leaves
        // them to the caller, and OrderManagementSystemOptions.RequireProductionSafetyDependencies
        // enforces them for production compositions inside the OMS constructor.
        var riskValidator = hosted
            ? services.GetRequiredService<IRiskValidator>()
            : services.GetService<IRiskValidator>();
        var portfolioState = hosted
            ? services.GetRequiredService<IPortfolioState>()
            : services.GetService<IPortfolioState>();
        var options = hosted
            ? services.GetRequiredService<OrderManagementSystemOptions>()
            : services.GetService<OrderManagementSystemOptions>();

        // Preserved difference: only the hosted workstation persists paper sessions through the OMS.
        var sessionPersistence = hosted ? services.GetService<PaperSessionPersistenceService>() : null;

        return new OrderManagementSystem(
            gateway,
            logger,
            riskValidator: riskValidator,
            securityMasterGate: services.GetService<ISecurityMasterGate>(),
            operatorControls: services.GetService<ExecutionOperatorControlService>(),
            auditTrail: services.GetService<ExecutionAuditTrailService>(),
            portfolioState: portfolioState,
            sessionPersistence: sessionPersistence,
            brokerageConfiguration: services.GetRequiredService<BrokerageConfiguration>(),
            liveOrderReadinessGate: services.GetService<ILiveOrderReadinessGate>(),
            options: options,
            tradeEventPublisher: services.GetService<ITradeEventPublisher>(),
            tradeFillHandoffFailureStore: services.GetService<ITradeFillHandoffFailureStore>(),
            escalationQueue: services.GetService<RiskEscalationQueueService>(),
            // Every brokerage-backed OMS must retain dispatch identities before routing, including a
            // broker's sandbox. In-process paper execution needs no broker recovery file.
            recoveryStore: gateway is IBrokerageGateway
                ? services.GetRequiredService<FileBrokerageOrderRecoveryStore>()
                : null,
            sessionPolicy: services.GetService<IOrderSessionPolicy>(),
            journal: services.GetService<ExecutionJournal>(),
            marketReferences: services.GetService<IOrderMarketReferenceSource>(),
            instrumentCoverage: services.GetService<IOrderInstrumentCoverageSource>(),
            strategyRunAuthority: services.GetService<IStrategyRunAuthoritySource>(),
            authorityOptions: services.GetService<OrderAuthorityOptions>());
    }
}
