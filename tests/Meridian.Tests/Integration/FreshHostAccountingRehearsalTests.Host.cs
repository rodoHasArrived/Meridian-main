using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Rebuild;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Execution;
using Meridian.Execution.Adapters;
using Meridian.Execution.Events;
using Meridian.Execution.Live;
using Meridian.Execution.PaperMatching;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Ledger;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Meridian.Storage;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.Operations;
using Meridian.Storage.SecurityMaster;
using Meridian.Strategies.Artifacts;
using Meridian.Strategies.Live;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NativePaperGateway = Meridian.Execution.PaperTradingGateway;

namespace Meridian.Tests.Integration;

public sealed partial class FreshHostAccountingRehearsalTests
{
    private enum Interruption { AfterHandoff, AfterJournalCommit }

    /// <summary>A host owns every mutable service. Reopening it never takes an old portfolio or ledger.</summary>
    private sealed class Host : IAsyncDisposable
    {
        public PostgresLedgerJournalStore Journals { get; }
        public StrategyRunStore Runs { get; }
        public StrategyArtifactRegistry Artifacts { get; }
        public JsonlPromotionRecordStore Promotions { get; }
        public PaperTradingPortfolio Portfolio { get; }
        public WalTradeFillPostingStore Handoffs { get; }
        public AutomatedTradeFillAccountingService Accounting { get; }
        public LedgerPostingConsumer Consumer { get; }
        public OrderManagementSystem Oms { get; }
        public LiveTradingEngine Engine { get; }
        public LiveMarketDataCache Market { get; } = new();
        public RehearsalFeed Feed { get; } = new();
        public BoundaryInterruption? Fault { get; }
        private readonly CanonicalPaperPortfolioBootstrapper _bootstrap;
        private readonly NativePaperGateway _gateway;
        private readonly DurableLedgerPostingTarget _target;
        private readonly ExecutionAuditTrailService _audit;
        private readonly ExecutionJournal _journal;

        public Host(Scope scope, Interruption? interruption = null)
        {
            Journals = CreateJournals(scope);
            Runs = new(new FileOperationalCaseHistoryStore(Path.Combine(scope.Root, "runs")));
            Artifacts = new(scope.Root);
            Promotions = new(Path.Combine(scope.Root, "promotions"), NullLogger<JsonlPromotionRecordStore>.Instance);
            var securities = new PostgresSecurityMasterStore(scope.Security);
            var events = new PostgresSecurityMasterEventStore(scope.Security);
            var securityQuery = new SecurityMasterQueryService(events, securities,
                new SecurityMasterAggregateRebuilder(events, new PostgresSecurityMasterSnapshotStore(scope.Security)));
            var securityGate = new SecurityValidationGateService(securityQuery,
                new SecurityValidationService(securities, AssetClassValidatorRegistry.CreateDefault()),
                new FileSecurityValidationSnapshotStore(new StorageOptions { RootPath = scope.Root }));
            var positions = new PostgresAssetOperationsProjectionStore(scope.Positions);
            var positionQuery = new AssetOperationsReadService(positions, securityQuery, positionProjectionStore: positions);
            var configuration = new PostgresAccountingConfigurationStore(scope.Ledger);
            var accounts = new ConfiguredLedgerAccountResolver(new AccountingConfigurationService(configuration, configuration));
            _target = new(Journals);
            Accounting = new(scope.Options, Path.Combine(scope.Root, "accounting-intents"), Journals, Journals,
                _target, accounts, new AccountingAutomaticPostingPolicyValidator(new AccountingPolicyService()), securityQuery, positionQuery);
            Handoffs = new(new TradeFillPostingStoreOptions(Path.Combine(scope.Root, "handoffs"), scope.Context),
                NullLogger<WalTradeFillPostingStore>.Instance);
            Fault = interruption is { } point ? new(Accounting, point) : null;
            Consumer = LedgerPostingConsumer.CreateWithAccountingAdapter(new GovernedTradeFillLedgerPostingTarget(_target, Journals),
                NullLogger<LedgerPostingConsumer>.Instance, Handoffs, scope.Context,
                Fault is null ? Accounting : Fault, requireCurrentAccountingEvidence: true,
                securityValidationGate: securityGate, requireSecurityMasterPostingGate: true);
            var costs = new PaperTradingCostOptions { CommissionKind = PaperCommissionKind.FixedPerOrder, CommissionRate = 1m, CommissionMinimum = 0m };
            Portfolio = CanonicalPaperPortfolioBootstrapper.CreatePortfolio(scope.Options, Market);
            _bootstrap = new(scope.Options, scope.Context, Portfolio, Journals, accounts, securityQuery, Consumer, costs: costs);
            _gateway = new(NullLogger<NativePaperGateway>.Instance,
                options: new PaperTradingGatewayOptions { AllowScaffoldMarketFills = true }, costOptions: costs);
            var controls = new ExecutionOperatorControlService(new(Path.Combine(scope.Root, "controls")),
                NullLogger<ExecutionOperatorControlService>.Instance);
            _audit = new(new(Path.Combine(scope.Root, "audit")), NullLogger<ExecutionAuditTrailService>.Instance);
            _journal = new(Path.Combine(scope.Root, "execution-journal"));
            Oms = new(_gateway, NullLogger<OrderManagementSystem>.Instance,
                riskValidator: new CompositeRiskValidator([new OrderRateThrottle(20, NullLogger<OrderRateThrottle>.Instance)],
                    NullLogger<CompositeRiskValidator>.Instance),
                operatorControls: controls, auditTrail: _audit, portfolioState: Portfolio,
                tradeEventPublisher: Consumer, securityMasterGate: new SecurityMasterGate(
                    (Meridian.Contracts.SecurityMaster.ISecurityResolver)new SecurityResolver(securities), NullLogger<SecurityMasterGate>.Instance),
                strategyRunAuthority: new StrategyRunAuthoritySource(Runs, Promotions),
                recoveryStore: new FileBrokerageOrderRecoveryStore(Path.Combine(scope.Root, "orders.json"), _gateway.GatewayId),
                journal: _journal, options: new OrderManagementSystemOptions { RequireProductionSafetyDependencies = true });
            Engine = new(new LiveStrategyCatalog().Register(SyntheticPriceStrategy.Id,
                    context => new SyntheticPriceStrategy(context.GetInt64("quantity", 10))),
                Feed, Market, new OmsGovernedExecutionOrderGateway(_gateway), Oms, Portfolio, Runs,
                auditTrail: _audit, artifacts: Artifacts);
        }

        public async Task StartAsync(CancellationToken ct)
        {
            // Production startup sequence: replay authoritative accounting before recovering the
            // cash/position projection, then admit OMS commands.
            await _bootstrap.InitializeAsync(ct);
            await _gateway.ConnectAsync(ct);
            await Oms.StartAsync(ct);
            await Engine.ResumePendingRunsAsync(ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Engine.DisposeAsync();
            await Oms.DisposeAsync();
            await Consumer.DisposeAsync();
            await Handoffs.DisposeAsync();
            await _gateway.DisposeAsync();
            await _journal.DisposeAsync();
            await _audit.DisposeAsync();
            Accounting.Dispose();
            _target.Dispose();
        }
    }

    /// <summary>Fault injection only: neither authority decisions nor financial results are fabricated.</summary>
    private sealed class BoundaryInterruption(IAutomatedTradeFillAccountingService inner, Interruption point)
        : IAutomatedTradeFillAccountingService
    {
        public TaskCompletionSource<TradeExecutedEvent> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<JournalEntry>?> TryRecoverCompletedAsync(TradeExecutedEvent fill,
            TradeFillLedgerPostingContext context, CancellationToken ct = default)
            => inner.TryRecoverCompletedAsync(fill, context, ct);

        public async Task<IReadOnlyList<JournalEntry>> PostAsync(TradeExecutedEvent fill,
            TradeFillLedgerPostingContext context, Guid securityId, CancellationToken ct = default)
        {
            if (point == Interruption.AfterJournalCommit)
                await inner.PostAsync(fill, context, securityId, ct);
            Reached.TrySetResult(fill);
            throw new IOException($"Synthetic interruption at {point}; durable handoff must survive.");
        }
    }
}
