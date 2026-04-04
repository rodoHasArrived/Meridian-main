# Interface Reference

> Auto-generated: 2026-04-04. Source: all `public interface` declarations in `src/`.

This document lists all public interfaces across Meridian projects, grouped by project.
Use this as a quick-reference for dependency injection contracts and extension points.

---

## Meridian.Application

| Interface | Source |
|-----------|--------|
| `IBankingService` | [`src/Meridian.Application/Banking/IBankingService.cs`](../../src/Meridian.Application/Banking/IBankingService.cs) |
| `ICliCommand` | [`src/Meridian.Application/Commands/ICliCommand.cs`](../../src/Meridian.Application/Commands/ICliCommand.cs) |
| `IClusterCoordinator` | [`src/Meridian.Application/Coordination/IClusterCoordinator.cs`](../../src/Meridian.Application/Coordination/IClusterCoordinator.cs) |
| `IConfigValidationStage` | [`src/Meridian.Application/Config/IConfigValidator.cs`](../../src/Meridian.Application/Config/IConfigValidator.cs) |
| `IConfigValidator` | [`src/Meridian.Application/Config/IConfigValidator.cs`](../../src/Meridian.Application/Config/IConfigValidator.cs) |
| `ICoordinationStore` | [`src/Meridian.Application/Coordination/ICoordinationStore.cs`](../../src/Meridian.Application/Coordination/ICoordinationStore.cs) |
| `ICredentialStore` | [`src/Meridian.Application/Credentials/ICredentialStore.cs`](../../src/Meridian.Application/Credentials/ICredentialStore.cs) |
| `IDedupStore` | [`src/Meridian.Application/Pipeline/IDedupStore.cs`](../../src/Meridian.Application/Pipeline/IDedupStore.cs) |
| `IDirectLendingCommandService` | [`src/Meridian.Application/DirectLending/IDirectLendingCommandService.cs`](../../src/Meridian.Application/DirectLending/IDirectLendingCommandService.cs) |
| `IDirectLendingQueryService` | [`src/Meridian.Application/DirectLending/IDirectLendingQueryService.cs`](../../src/Meridian.Application/DirectLending/IDirectLendingQueryService.cs) |
| `IDirectLendingService` | [`src/Meridian.Application/DirectLending/IDirectLendingService.cs`](../../src/Meridian.Application/DirectLending/IDirectLendingService.cs) |
| `IEventCanonicalizer` | [`src/Meridian.Application/Canonicalization/IEventCanonicalizer.cs`](../../src/Meridian.Application/Canonicalization/IEventCanonicalizer.cs) |
| `IEventMetrics` | [`src/Meridian.Application/Monitoring/IEventMetrics.cs`](../../src/Meridian.Application/Monitoring/IEventMetrics.cs) |
| `IEventValidator` | [`src/Meridian.Application/Pipeline/IEventValidator.cs`](../../src/Meridian.Application/Pipeline/IEventValidator.cs) |
| `IFundAccountService` | [`src/Meridian.Application/FundAccounts/IFundAccountService.cs`](../../src/Meridian.Application/FundAccounts/IFundAccountService.cs) |
| `ILeaseManager` | [`src/Meridian.Application/Coordination/ILeaseManager.cs`](../../src/Meridian.Application/Coordination/ILeaseManager.cs) |
| `ILivePositionCorporateActionAdjuster` | [`src/Meridian.Application/SecurityMaster/ILivePositionCorporateActionAdjuster.cs`](../../src/Meridian.Application/SecurityMaster/ILivePositionCorporateActionAdjuster.cs) |
| `IMmfLiquidityService` | [`src/Meridian.Application/Treasury/IMmfLiquidityService.cs`](../../src/Meridian.Application/Treasury/IMmfLiquidityService.cs) |
| `IMoneyMarketFundService` | [`src/Meridian.Application/Treasury/IMoneyMarketFundService.cs`](../../src/Meridian.Application/Treasury/IMoneyMarketFundService.cs) |
| `IOperationalScheduler` | [`src/Meridian.Application/Scheduling/IOperationalScheduler.cs`](../../src/Meridian.Application/Scheduling/IOperationalScheduler.cs) |
| `IQualityAnalysisEngine` | [`src/Meridian.Application/Monitoring/DataQuality/IQualityAnalyzer.cs`](../../src/Meridian.Application/Monitoring/DataQuality/IQualityAnalyzer.cs) |
| `IQualityAnalyzer<TData>` | [`src/Meridian.Application/Monitoring/DataQuality/IQualityAnalyzer.cs`](../../src/Meridian.Application/Monitoring/DataQuality/IQualityAnalyzer.cs) |
| `IQualityAnalyzerMetadata` | [`src/Meridian.Application/Monitoring/DataQuality/IQualityAnalyzer.cs`](../../src/Meridian.Application/Monitoring/DataQuality/IQualityAnalyzer.cs) |
| `IQualityAnalyzerRegistry` | [`src/Meridian.Application/Monitoring/DataQuality/IQualityAnalyzer.cs`](../../src/Meridian.Application/Monitoring/DataQuality/IQualityAnalyzer.cs) |
| `IScheduledWorkOwnershipService` | [`src/Meridian.Application/Coordination/IScheduledWorkOwnershipService.cs`](../../src/Meridian.Application/Coordination/IScheduledWorkOwnershipService.cs) |
| `ISecurityMasterQueryService` | [`src/Meridian.Application/SecurityMaster/ISecurityMasterQueryService.cs`](../../src/Meridian.Application/SecurityMaster/ISecurityMasterQueryService.cs) |
| `ISecurityResolver` | [`src/Meridian.Application/SecurityMaster/ISecurityResolver.cs`](../../src/Meridian.Application/SecurityMaster/ISecurityResolver.cs) |
| `IServiceFeatureRegistration` | [`src/Meridian.Application/Composition/Features/IServiceFeatureRegistration.cs`](../../src/Meridian.Application/Composition/Features/IServiceFeatureRegistration.cs) |
| `ISubscriptionOwnershipService` | [`src/Meridian.Application/Coordination/ISubscriptionOwnershipService.cs`](../../src/Meridian.Application/Coordination/ISubscriptionOwnershipService.cs) |
| `ITradingCalendarProvider` | [`src/Meridian.Application/Scheduling/IOperationalScheduler.cs`](../../src/Meridian.Application/Scheduling/IOperationalScheduler.cs) |
| `IWizardStep` | [`src/Meridian.Application/Wizard/Core/IWizardStep.cs`](../../src/Meridian.Application/Wizard/Core/IWizardStep.cs) |

## Meridian.Backtesting

| Interface | Source |
|-----------|--------|
| `ICommissionModel` | [`src/Meridian.Backtesting/Portfolio/ICommissionModel.cs`](../../src/Meridian.Backtesting/Portfolio/ICommissionModel.cs) |
| `ICorporateActionAdjustmentService` | [`src/Meridian.Backtesting/ICorporateActionAdjustmentService.cs`](../../src/Meridian.Backtesting/ICorporateActionAdjustmentService.cs) |

## Meridian.Backtesting.Sdk

| Interface | Source |
|-----------|--------|
| `IBacktestContext` | [`src/Meridian.Backtesting.Sdk/IBacktestContext.cs`](../../src/Meridian.Backtesting.Sdk/IBacktestContext.cs) |
| `IBacktestStrategy` | [`src/Meridian.Backtesting.Sdk/IBacktestStrategy.cs`](../../src/Meridian.Backtesting.Sdk/IBacktestStrategy.cs) |

## Meridian.Contracts

| Interface | Source |
|-----------|--------|
| `ICanonicalSymbolRegistry` | [`src/Meridian.Contracts/Catalog/ICanonicalSymbolRegistry.cs`](../../src/Meridian.Contracts/Catalog/ICanonicalSymbolRegistry.cs) |
| `IConnectivityProbeService` | [`src/Meridian.Contracts/Services/IConnectivityProbeService.cs`](../../src/Meridian.Contracts/Services/IConnectivityProbeService.cs) |
| `IMarketEventPayload` | [`src/Meridian.Contracts/Domain/Events/IMarketEventPayload.cs`](../../src/Meridian.Contracts/Domain/Events/IMarketEventPayload.cs) |
| `IPositionSnapshotStore` | [`src/Meridian.Contracts/Domain/IPositionSnapshotStore.cs`](../../src/Meridian.Contracts/Domain/IPositionSnapshotStore.cs) |
| `ISchemaUpcaster<out` | [`src/Meridian.Contracts/Schema/ISchemaUpcaster.cs`](../../src/Meridian.Contracts/Schema/ISchemaUpcaster.cs) |
| `ISecretProvider` | [`src/Meridian.Contracts/Credentials/ISecretProvider.cs`](../../src/Meridian.Contracts/Credentials/ISecretProvider.cs) |
| `ISecurityMasterAmender` | [`src/Meridian.Contracts/SecurityMaster/ISecurityMasterAmender.cs`](../../src/Meridian.Contracts/SecurityMaster/ISecurityMasterAmender.cs) |
| `ISecurityMasterQueryService` | [`src/Meridian.Contracts/SecurityMaster/ISecurityMasterQueryService.cs`](../../src/Meridian.Contracts/SecurityMaster/ISecurityMasterQueryService.cs) |
| `ISecurityMasterService` | [`src/Meridian.Contracts/SecurityMaster/ISecurityMasterService.cs`](../../src/Meridian.Contracts/SecurityMaster/ISecurityMasterService.cs) |

## Meridian.Core

| Interface | Source |
|-----------|--------|
| `IAlertDispatcher` | [`src/Meridian.Core/Monitoring/Core/IAlertDispatcher.cs`](../../src/Meridian.Core/Monitoring/Core/IAlertDispatcher.cs) |
| `IConfigurationProvider` | [`src/Meridian.Core/Config/IConfigurationProvider.cs`](../../src/Meridian.Core/Config/IConfigurationProvider.cs) |
| `IConnectionHealthMonitor` | [`src/Meridian.Core/Monitoring/IConnectionHealthMonitor.cs`](../../src/Meridian.Core/Monitoring/IConnectionHealthMonitor.cs) |
| `IFlushable` | [`src/Meridian.Core/Services/IFlushable.cs`](../../src/Meridian.Core/Services/IFlushable.cs) |
| `IHealthCheckAggregator` | [`src/Meridian.Core/Monitoring/Core/IHealthCheckProvider.cs`](../../src/Meridian.Core/Monitoring/Core/IHealthCheckProvider.cs) |
| `IHealthCheckProvider` | [`src/Meridian.Core/Monitoring/Core/IHealthCheckProvider.cs`](../../src/Meridian.Core/Monitoring/Core/IHealthCheckProvider.cs) |
| `IReconnectionMetrics` | [`src/Meridian.Core/Monitoring/IReconnectionMetrics.cs`](../../src/Meridian.Core/Monitoring/IReconnectionMetrics.cs) |

## Meridian.Domain

| Interface | Source |
|-----------|--------|
| `IBackpressureSignal` | [`src/Meridian.Domain/Events/IBackpressureSignal.cs`](../../src/Meridian.Domain/Events/IBackpressureSignal.cs) |
| `IMarketEventPublisher` | [`src/Meridian.Domain/Events/IMarketEventPublisher.cs`](../../src/Meridian.Domain/Events/IMarketEventPublisher.cs) |
| `IQuoteStateStore` | [`src/Meridian.Domain/Collectors/IQuoteStateStore.cs`](../../src/Meridian.Domain/Collectors/IQuoteStateStore.cs) |

## Meridian.Execution

| Interface | Source |
|-----------|--------|
| `IAccountPortfolio` | [`src/Meridian.Execution/Interfaces/IAccountPortfolio.cs`](../../src/Meridian.Execution/Interfaces/IAccountPortfolio.cs) |
| `IAllocationEngine` | [`src/Meridian.Execution/Allocation/IAllocationEngine.cs`](../../src/Meridian.Execution/Allocation/IAllocationEngine.cs) |
| `IDerivativePosition` | [`src/Meridian.Execution/Derivatives/IDerivativePosition.cs`](../../src/Meridian.Execution/Derivatives/IDerivativePosition.cs) |
| `IExecutionContext` | [`src/Meridian.Execution/Interfaces/IExecutionContext.cs`](../../src/Meridian.Execution/Interfaces/IExecutionContext.cs) |
| `IFxRateProvider` | [`src/Meridian.Execution/MultiCurrency/IFxRateProvider.cs`](../../src/Meridian.Execution/MultiCurrency/IFxRateProvider.cs) |
| `ILiveFeedAdapter` | [`src/Meridian.Execution/Interfaces/ILiveFeedAdapter.cs`](../../src/Meridian.Execution/Interfaces/ILiveFeedAdapter.cs) |
| `IMarginModel` | [`src/Meridian.Execution/Margin/IMarginModel.cs`](../../src/Meridian.Execution/Margin/IMarginModel.cs) |
| `IMultiAccountPortfolioState` | [`src/Meridian.Execution/Models/IMultiAccountPortfolioState.cs`](../../src/Meridian.Execution/Models/IMultiAccountPortfolioState.cs) |
| `IOrderGateway` | [`src/Meridian.Execution/Interfaces/IOrderGateway.cs`](../../src/Meridian.Execution/Interfaces/IOrderGateway.cs) |
| `IPaperSessionStore` | [`src/Meridian.Execution/Services/IPaperSessionStore.cs`](../../src/Meridian.Execution/Services/IPaperSessionStore.cs) |
| `IPortfolioState` | [`src/Meridian.Execution/Models/IPortfolioState.cs`](../../src/Meridian.Execution/Models/IPortfolioState.cs) |
| `IRiskValidator` | [`src/Meridian.Execution/IRiskValidator.cs`](../../src/Meridian.Execution/IRiskValidator.cs) |
| `ISecurityMasterGate` | [`src/Meridian.Execution/ISecurityMasterGate.cs`](../../src/Meridian.Execution/ISecurityMasterGate.cs) |
| `ITaxLotSelector` | [`src/Meridian.Execution/TaxLotAccounting/ITaxLotSelector.cs`](../../src/Meridian.Execution/TaxLotAccounting/ITaxLotSelector.cs) |
| `ITradeEventPublisher` | [`src/Meridian.Execution/Events/ITradeEventPublisher.cs`](../../src/Meridian.Execution/Events/ITradeEventPublisher.cs) |

## Meridian.Execution.Sdk

| Interface | Source |
|-----------|--------|
| `IBrokerageGateway` | [`src/Meridian.Execution.Sdk/IBrokerageGateway.cs`](../../src/Meridian.Execution.Sdk/IBrokerageGateway.cs) |
| `IBrokeragePositionSync` | [`src/Meridian.Execution.Sdk/IBrokeragePositionSync.cs`](../../src/Meridian.Execution.Sdk/IBrokeragePositionSync.cs) |
| `IExecutionGateway` | [`src/Meridian.Execution.Sdk/IExecutionGateway.cs`](../../src/Meridian.Execution.Sdk/IExecutionGateway.cs) |
| `IOrderManager` | [`src/Meridian.Execution.Sdk/IOrderManager.cs`](../../src/Meridian.Execution.Sdk/IOrderManager.cs) |
| `IPositionTracker` | [`src/Meridian.Execution.Sdk/IPositionTracker.cs`](../../src/Meridian.Execution.Sdk/IPositionTracker.cs) |

## Meridian.IbApi.SmokeStub

| Interface | Source |
|-----------|--------|
| `EReaderSignal` | [`src/Meridian.IbApi.SmokeStub/IBApiSmokeStub.cs`](../../src/Meridian.IbApi.SmokeStub/IBApiSmokeStub.cs) |
| `EWrapper` | [`src/Meridian.IbApi.SmokeStub/IBApiSmokeStub.cs`](../../src/Meridian.IbApi.SmokeStub/IBApiSmokeStub.cs) |

## Meridian.Infrastructure

| Interface | Source |
|-----------|--------|
| `ICorporateActionProvider` | [`src/Meridian.Infrastructure/Adapters/Core/ICorporateActionProvider.cs`](../../src/Meridian.Infrastructure/Adapters/Core/ICorporateActionProvider.cs) |
| `IFilterableSymbolSearchProvider` | [`src/Meridian.Infrastructure/Adapters/Core/ISymbolSearchProvider.cs`](../../src/Meridian.Infrastructure/Adapters/Core/ISymbolSearchProvider.cs) |
| `IHistoricalDataProvider` | [`src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs`](../../src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs) |
| `IRateLimitAwareProvider` | [`src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs`](../../src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs) |
| `ISftpClient` | [`src/Meridian.Infrastructure/Etl/Sftp/ISftpClientFactory.cs`](../../src/Meridian.Infrastructure/Etl/Sftp/ISftpClientFactory.cs) |
| `ISftpClientFactory` | [`src/Meridian.Infrastructure/Etl/Sftp/ISftpClientFactory.cs`](../../src/Meridian.Infrastructure/Etl/Sftp/ISftpClientFactory.cs) |
| `ISftpFileEntry` | [`src/Meridian.Infrastructure/Etl/Sftp/ISftpClientFactory.cs`](../../src/Meridian.Infrastructure/Etl/Sftp/ISftpClientFactory.cs) |
| `ISftpFilePublisher` | [`src/Meridian.Infrastructure/Etl/ISftpFilePublisher.cs`](../../src/Meridian.Infrastructure/Etl/ISftpFilePublisher.cs) |
| `ISymbolResolver` | [`src/Meridian.Infrastructure/Adapters/Core/SymbolResolution/ISymbolResolver.cs`](../../src/Meridian.Infrastructure/Adapters/Core/SymbolResolution/ISymbolResolver.cs) |
| `ISymbolSearchProvider` | [`src/Meridian.Infrastructure/Adapters/Core/ISymbolSearchProvider.cs`](../../src/Meridian.Infrastructure/Adapters/Core/ISymbolSearchProvider.cs) |
| `ISymbolStateStore<T>` | [`src/Meridian.Infrastructure/Shared/ISymbolStateStore.cs`](../../src/Meridian.Infrastructure/Shared/ISymbolStateStore.cs) |
| `ITradingParametersBackfillService` | [`src/Meridian.Infrastructure/Adapters/Polygon/ITradingParametersBackfillService.cs`](../../src/Meridian.Infrastructure/Adapters/Polygon/ITradingParametersBackfillService.cs) |

## Meridian.Infrastructure.CppTrader

| Interface | Source |
|-----------|--------|
| `ICppTraderExecutionTranslator` | [`src/Meridian.Infrastructure.CppTrader/Translation/ICppTraderExecutionTranslator.cs`](../../src/Meridian.Infrastructure.CppTrader/Translation/ICppTraderExecutionTranslator.cs) |
| `ICppTraderHostManager` | [`src/Meridian.Infrastructure.CppTrader/Host/ICppTraderHostManager.cs`](../../src/Meridian.Infrastructure.CppTrader/Host/ICppTraderHostManager.cs) |
| `ICppTraderItchIngestionService` | [`src/Meridian.Infrastructure.CppTrader/Providers/ICppTraderItchIngestionService.cs`](../../src/Meridian.Infrastructure.CppTrader/Providers/ICppTraderItchIngestionService.cs) |
| `ICppTraderReplayService` | [`src/Meridian.Infrastructure.CppTrader/Replay/ICppTraderReplayService.cs`](../../src/Meridian.Infrastructure.CppTrader/Replay/ICppTraderReplayService.cs) |
| `ICppTraderSessionClient` | [`src/Meridian.Infrastructure.CppTrader/Host/ICppTraderSessionClient.cs`](../../src/Meridian.Infrastructure.CppTrader/Host/ICppTraderSessionClient.cs) |
| `ICppTraderSessionDiagnosticsService` | [`src/Meridian.Infrastructure.CppTrader/Diagnostics/ICppTraderSessionDiagnosticsService.cs`](../../src/Meridian.Infrastructure.CppTrader/Diagnostics/ICppTraderSessionDiagnosticsService.cs) |
| `ICppTraderSnapshotTranslator` | [`src/Meridian.Infrastructure.CppTrader/Translation/ICppTraderSnapshotTranslator.cs`](../../src/Meridian.Infrastructure.CppTrader/Translation/ICppTraderSnapshotTranslator.cs) |
| `ICppTraderStatusService` | [`src/Meridian.Infrastructure.CppTrader/Diagnostics/ICppTraderStatusService.cs`](../../src/Meridian.Infrastructure.CppTrader/Diagnostics/ICppTraderStatusService.cs) |
| `ICppTraderSymbolMapper` | [`src/Meridian.Infrastructure.CppTrader/Symbols/ICppTraderSymbolMapper.cs`](../../src/Meridian.Infrastructure.CppTrader/Symbols/ICppTraderSymbolMapper.cs) |

## Meridian.Ledger

| Interface | Source |
|-----------|--------|
| `IReadOnlyLedger` | [`src/Meridian.Ledger/IReadOnlyLedger.cs`](../../src/Meridian.Ledger/IReadOnlyLedger.cs) |

## Meridian.ProviderSdk

| Interface | Source |
|-----------|--------|
| `ICorporateActionSource` | [`src/Meridian.ProviderSdk/IHistoricalDataSource.cs`](../../src/Meridian.ProviderSdk/IHistoricalDataSource.cs) |
| `ICredentialContext` | [`src/Meridian.ProviderSdk/ICredentialContext.cs`](../../src/Meridian.ProviderSdk/ICredentialContext.cs) |
| `IDailyBarSource` | [`src/Meridian.ProviderSdk/IHistoricalDataSource.cs`](../../src/Meridian.ProviderSdk/IHistoricalDataSource.cs) |
| `IDataSource` | [`src/Meridian.ProviderSdk/IDataSource.cs`](../../src/Meridian.ProviderSdk/IDataSource.cs) |
| `IDepthSource` | [`src/Meridian.ProviderSdk/IRealtimeDataSource.cs`](../../src/Meridian.ProviderSdk/IRealtimeDataSource.cs) |
| `IHistoricalBarWriter` | [`src/Meridian.ProviderSdk/IHistoricalBarWriter.cs`](../../src/Meridian.ProviderSdk/IHistoricalBarWriter.cs) |
| `IHistoricalDataSource` | [`src/Meridian.ProviderSdk/IHistoricalDataSource.cs`](../../src/Meridian.ProviderSdk/IHistoricalDataSource.cs) |
| `IIntradayBarSource` | [`src/Meridian.ProviderSdk/IHistoricalDataSource.cs`](../../src/Meridian.ProviderSdk/IHistoricalDataSource.cs) |
| `IMarketDataClient` | [`src/Meridian.ProviderSdk/IMarketDataClient.cs`](../../src/Meridian.ProviderSdk/IMarketDataClient.cs) |
| `IOptionsChainProvider` | [`src/Meridian.ProviderSdk/IOptionsChainProvider.cs`](../../src/Meridian.ProviderSdk/IOptionsChainProvider.cs) |
| `IProviderMetadata` | [`src/Meridian.ProviderSdk/IProviderMetadata.cs`](../../src/Meridian.ProviderSdk/IProviderMetadata.cs) |
| `IProviderModule` | [`src/Meridian.ProviderSdk/IProviderModule.cs`](../../src/Meridian.ProviderSdk/IProviderModule.cs) |
| `IQuoteSource` | [`src/Meridian.ProviderSdk/IRealtimeDataSource.cs`](../../src/Meridian.ProviderSdk/IRealtimeDataSource.cs) |
| `IRealtimeDataSource` | [`src/Meridian.ProviderSdk/IRealtimeDataSource.cs`](../../src/Meridian.ProviderSdk/IRealtimeDataSource.cs) |
| `ITradeSource` | [`src/Meridian.ProviderSdk/IRealtimeDataSource.cs`](../../src/Meridian.ProviderSdk/IRealtimeDataSource.cs) |

## Meridian.QuantScript

| Interface | Source |
|-----------|--------|
| `IQuantDataContext` | [`src/Meridian.QuantScript/Api/IQuantDataContext.cs`](../../src/Meridian.QuantScript/Api/IQuantDataContext.cs) |
| `IQuantScriptCompiler` | [`src/Meridian.QuantScript/Compilation/IQuantScriptCompiler.cs`](../../src/Meridian.QuantScript/Compilation/IQuantScriptCompiler.cs) |
| `IQuantScriptNotebookStore` | [`src/Meridian.QuantScript/Documents/IQuantScriptNotebookStore.cs`](../../src/Meridian.QuantScript/Documents/IQuantScriptNotebookStore.cs) |
| `IScriptRunner` | [`src/Meridian.QuantScript/Compilation/IScriptRunner.cs`](../../src/Meridian.QuantScript/Compilation/IScriptRunner.cs) |

## Meridian.Risk

| Interface | Source |
|-----------|--------|
| `IRiskRule` | [`src/Meridian.Risk/IRiskRule.cs`](../../src/Meridian.Risk/IRiskRule.cs) |

## Meridian.Storage

| Interface | Source |
|-----------|--------|
| `IArchiveMaintenanceScheduleManager` | [`src/Meridian.Storage/Maintenance/IArchiveMaintenanceScheduleManager.cs`](../../src/Meridian.Storage/Maintenance/IArchiveMaintenanceScheduleManager.cs) |
| `IArchiveMaintenanceService` | [`src/Meridian.Storage/Maintenance/IArchiveMaintenanceService.cs`](../../src/Meridian.Storage/Maintenance/IArchiveMaintenanceService.cs) |
| `IDirectLendingOperationsStore` | [`src/Meridian.Storage/DirectLending/IDirectLendingOperationsStore.cs`](../../src/Meridian.Storage/DirectLending/IDirectLendingOperationsStore.cs) |
| `IDirectLendingStateStore` | [`src/Meridian.Storage/DirectLending/IDirectLendingStateStore.cs`](../../src/Meridian.Storage/DirectLending/IDirectLendingStateStore.cs) |
| `IFundAccountStore` | [`src/Meridian.Storage/FundAccounts/IFundAccountStore.cs`](../../src/Meridian.Storage/FundAccounts/IFundAccountStore.cs) |
| `IMaintenanceExecutionHistory` | [`src/Meridian.Storage/Maintenance/IMaintenanceExecutionHistory.cs`](../../src/Meridian.Storage/Maintenance/IMaintenanceExecutionHistory.cs) |
| `IMarketDataStore` | [`src/Meridian.Storage/Interfaces/IMarketDataStore.cs`](../../src/Meridian.Storage/Interfaces/IMarketDataStore.cs) |
| `ISecurityMasterEventStore` | [`src/Meridian.Storage/SecurityMaster/ISecurityMasterEventStore.cs`](../../src/Meridian.Storage/SecurityMaster/ISecurityMasterEventStore.cs) |
| `ISecurityMasterSnapshotStore` | [`src/Meridian.Storage/SecurityMaster/ISecurityMasterSnapshotStore.cs`](../../src/Meridian.Storage/SecurityMaster/ISecurityMasterSnapshotStore.cs) |
| `ISecurityMasterStore` | [`src/Meridian.Storage/SecurityMaster/ISecurityMasterStore.cs`](../../src/Meridian.Storage/SecurityMaster/ISecurityMasterStore.cs) |
| `ISourceRegistry` | [`src/Meridian.Storage/Interfaces/ISourceRegistry.cs`](../../src/Meridian.Storage/Interfaces/ISourceRegistry.cs) |
| `IStorageCatalogService` | [`src/Meridian.Storage/Interfaces/IStorageCatalogService.cs`](../../src/Meridian.Storage/Interfaces/IStorageCatalogService.cs) |
| `IStoragePolicy` | [`src/Meridian.Storage/Interfaces/IStoragePolicy.cs`](../../src/Meridian.Storage/Interfaces/IStoragePolicy.cs) |
| `IStorageSink` | [`src/Meridian.Storage/Interfaces/IStorageSink.cs`](../../src/Meridian.Storage/Interfaces/IStorageSink.cs) |
| `ISymbolRegistryService` | [`src/Meridian.Storage/Interfaces/ISymbolRegistryService.cs`](../../src/Meridian.Storage/Interfaces/ISymbolRegistryService.cs) |

## Meridian.Strategies

| Interface | Source |
|-----------|--------|
| `IAggregatePortfolioService` | [`src/Meridian.Strategies/Services/IAggregatePortfolioService.cs`](../../src/Meridian.Strategies/Services/IAggregatePortfolioService.cs) |
| `ILiveStrategy` | [`src/Meridian.Strategies/Interfaces/ILiveStrategy.cs`](../../src/Meridian.Strategies/Interfaces/ILiveStrategy.cs) |
| `IReconciliationRunRepository` | [`src/Meridian.Strategies/Services/IReconciliationRunRepository.cs`](../../src/Meridian.Strategies/Services/IReconciliationRunRepository.cs) |
| `IReconciliationRunService` | [`src/Meridian.Strategies/Services/IReconciliationRunService.cs`](../../src/Meridian.Strategies/Services/IReconciliationRunService.cs) |
| `ISecurityReferenceLookup` | [`src/Meridian.Strategies/Services/ISecurityReferenceLookup.cs`](../../src/Meridian.Strategies/Services/ISecurityReferenceLookup.cs) |
| `IStrategyLifecycle` | [`src/Meridian.Strategies/Interfaces/IStrategyLifecycle.cs`](../../src/Meridian.Strategies/Interfaces/IStrategyLifecycle.cs) |
| `IStrategyRepository` | [`src/Meridian.Strategies/Interfaces/IStrategyRepository.cs`](../../src/Meridian.Strategies/Interfaces/IStrategyRepository.cs) |

## Meridian.Ui.Services

| Interface | Source |
|-----------|--------|
| `IAdminMaintenanceService` | [`src/Meridian.Ui.Services/Contracts/IAdminMaintenanceService.cs`](../../src/Meridian.Ui.Services/Contracts/IAdminMaintenanceService.cs) |
| `IArchiveHealthService` | [`src/Meridian.Ui.Services/Contracts/IArchiveHealthService.cs`](../../src/Meridian.Ui.Services/Contracts/IArchiveHealthService.cs) |
| `IBackgroundTaskSchedulerService` | [`src/Meridian.Ui.Services/Contracts/IBackgroundTaskSchedulerService.cs`](../../src/Meridian.Ui.Services/Contracts/IBackgroundTaskSchedulerService.cs) |
| `IConfigService` | [`src/Meridian.Ui.Services/Contracts/IConfigService.cs`](../../src/Meridian.Ui.Services/Contracts/IConfigService.cs) |
| `ICredentialService` | [`src/Meridian.Ui.Services/Contracts/ICredentialService.cs`](../../src/Meridian.Ui.Services/Contracts/ICredentialService.cs) |
| `IDataQualityApiClient` | [`src/Meridian.Ui.Services/Services/DataQuality/IDataQualityApiClient.cs`](../../src/Meridian.Ui.Services/Services/DataQuality/IDataQualityApiClient.cs) |
| `IDataQualityPresentationService` | [`src/Meridian.Ui.Services/Services/DataQuality/IDataQualityPresentationService.cs`](../../src/Meridian.Ui.Services/Services/DataQuality/IDataQualityPresentationService.cs) |
| `IDataQualityRefreshService` | [`src/Meridian.Ui.Services/Services/DataQuality/IDataQualityRefreshService.cs`](../../src/Meridian.Ui.Services/Services/DataQuality/IDataQualityRefreshService.cs) |
| `ILoggingService` | [`src/Meridian.Ui.Services/Contracts/ILoggingService.cs`](../../src/Meridian.Ui.Services/Contracts/ILoggingService.cs) |
| `IMessagingService` | [`src/Meridian.Ui.Services/Contracts/IMessagingService.cs`](../../src/Meridian.Ui.Services/Contracts/IMessagingService.cs) |
| `INotificationService` | [`src/Meridian.Ui.Services/Contracts/INotificationService.cs`](../../src/Meridian.Ui.Services/Contracts/INotificationService.cs) |
| `IOfflineTrackingPersistenceService` | [`src/Meridian.Ui.Services/Contracts/IOfflineTrackingPersistenceService.cs`](../../src/Meridian.Ui.Services/Contracts/IOfflineTrackingPersistenceService.cs) |
| `IPendingOperationsQueueService` | [`src/Meridian.Ui.Services/Contracts/IPendingOperationsQueueService.cs`](../../src/Meridian.Ui.Services/Contracts/IPendingOperationsQueueService.cs) |
| `IRefreshScheduler` | [`src/Meridian.Ui.Services/Contracts/IRefreshScheduler.cs`](../../src/Meridian.Ui.Services/Contracts/IRefreshScheduler.cs) |
| `ISchemaService` | [`src/Meridian.Ui.Services/Contracts/ISchemaService.cs`](../../src/Meridian.Ui.Services/Contracts/ISchemaService.cs) |
| `IStatusService` | [`src/Meridian.Ui.Services/Contracts/IStatusService.cs`](../../src/Meridian.Ui.Services/Contracts/IStatusService.cs) |
| `IThemeService` | [`src/Meridian.Ui.Services/Contracts/IThemeService.cs`](../../src/Meridian.Ui.Services/Contracts/IThemeService.cs) |
| `IWatchlistService` | [`src/Meridian.Ui.Services/Contracts/IWatchlistService.cs`](../../src/Meridian.Ui.Services/Contracts/IWatchlistService.cs) |

## Meridian.Wpf

| Interface | Source |
|-----------|--------|
| `ICommandContextProvider` | [`src/Meridian.Wpf/Services/ICommandContextProvider.cs`](../../src/Meridian.Wpf/Services/ICommandContextProvider.cs) |
| `IConnectionService` | [`src/Meridian.Wpf/Contracts/IConnectionService.cs`](../../src/Meridian.Wpf/Contracts/IConnectionService.cs) |
| `IFundProfileCatalog` | [`src/Meridian.Wpf/Services/IFundProfileCatalog.cs`](../../src/Meridian.Wpf/Services/IFundProfileCatalog.cs) |
| `INavigationService` | [`src/Meridian.Wpf/Contracts/INavigationService.cs`](../../src/Meridian.Wpf/Contracts/INavigationService.cs) |
| `IPageActionBarProvider` | [`src/Meridian.Wpf/ViewModels/IPageActionBarProvider.cs`](../../src/Meridian.Wpf/ViewModels/IPageActionBarProvider.cs) |
| `IQuantScriptLayoutService` | [`src/Meridian.Wpf/Services/IQuantScriptLayoutService.cs`](../../src/Meridian.Wpf/Services/IQuantScriptLayoutService.cs) |

---

*Total: 164 public interfaces across 19 projects.*
