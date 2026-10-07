### .NET CI test project summary

- Total projects: 19
- Passed: 19
- Failed: 0

- Tests: {"passed": 19639, "failed": 0, "skipped": 5, "other": 0}
- Run attempt: local; cache hit: not reported
- Queue time is reported separately by ci-metrics.py from completed Actions jobs.

| Shard | Project | Status | Exit code | Duration (s) | Passed / failed / skipped | Log |
| --- | --- | --- | ---: | ---: | --- | --- |
| `core-application` | `tests/Meridian.Tests/Meridian.Tests.csproj` | ✅ passed | 0 | 50.242 | 2202 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/core-application/dotnet-test.log` |
| `core-ui-workstation-endpoints` | `tests/Meridian.Tests/Meridian.Tests.csproj` | ✅ passed | 0 | 482.276 | 473 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/core-ui-workstation-endpoints/dotnet-test.log` |
| `core-ui-other` | `tests/Meridian.Tests/Meridian.Tests.csproj` | ✅ passed | 0 | 312.104 | 2693 / 0 / 5 | `/workspace/Meridian-main/artifacts/test-results/dotnet/core-ui-other/dotnet-test.log` |
| `core-infrastructure` | `tests/Meridian.Tests/Meridian.Tests.csproj` | ✅ passed | 0 | 24.025 | 1188 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/core-infrastructure/dotnet-test.log` |
| `core-storage` | `tests/Meridian.Tests/Meridian.Tests.csproj` | ✅ passed | 0 | 31.976 | 1585 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/core-storage/dotnet-test.log` |
| `core-data` | `tests/Meridian.Tests/Meridian.Tests.csproj` | ✅ passed | 0 | 16.014 | 577 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/core-data/dotnet-test.log` |
| `core-execution-strategy` | `tests/Meridian.Tests/Meridian.Tests.csproj` | ✅ passed | 0 | 230.075 | 3086 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/core-execution-strategy/dotnet-test.log` |
| `core-market-instruments` | `tests/Meridian.Tests/Meridian.Tests.csproj` | ✅ passed | 0 | 26.452 | 226 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/core-market-instruments/dotnet-test.log` |
| `core-platform-domain-root` | `tests/Meridian.Tests/Meridian.Tests.csproj` | ✅ passed | 0 | 137.383 | 4663 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/core-platform-domain-root/dotnet-test.log` |
| `core-reporting` | `tests/Meridian.Tests/Meridian.Tests.csproj` | ✅ passed | 0 | 18.017 | 179 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/core-reporting/dotnet-test.log` |
| `fsharp` | `tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj` | ✅ passed | 0 | 7.000 | 456 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/fsharp/dotnet-test.log` |
| `ui` | `tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj` | ✅ passed | 0 | 9.212 | 1478 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/ui/dotnet-test.log` |
| `backtesting` | `tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj` | ✅ passed | 0 | 6.393 | 404 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/backtesting/dotnet-test.log` |
| `directlending` | `tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj` | ✅ passed | 0 | 2.974 | 11 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/directlending/dotnet-test.log` |
| `fundstructure` | `tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj` | ✅ passed | 0 | 5.473 | 118 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/fundstructure/dotnet-test.log` |
| `quantscript` | `tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj` | ✅ passed | 0 | 94.218 | 166 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/quantscript/dotnet-test.log` |
| `designmodules` | `tests/Meridian.DesignModules.Tests/Meridian.DesignModules.Tests.csproj` | ✅ passed | 0 | 3.489 | 3 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/designmodules/dotnet-test.log` |
| `lifecycle` | `tests/Meridian.Lifecycle.Tests/Meridian.Lifecycle.Tests.csproj` | ✅ passed | 0 | 3.169 | 18 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/lifecycle/dotnet-test.log` |
| `core-remainder` | `tests/Meridian.Tests/Meridian.Tests.csproj` | ✅ passed | 0 | 32.154 | 113 / 0 / 0 | `/workspace/Meridian-main/artifacts/test-results/dotnet/core-remainder/dotnet-test.log` |

#### Reproduce (after restore/build)

core-application: passed

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --no-build --filter '(Category!=Integration&Category!=Performance)&(FullyQualifiedName~Meridian.Tests.Application)' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=core-application' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/core-application /p:EnableWindowsTargeting=true
```

core-ui-workstation-endpoints: passed

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --no-build --filter '(Category!=Integration&Category!=Performance)&(FullyQualifiedName~Meridian.Tests.Ui.WorkstationEndpointsTests)' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=core-ui-workstation-endpoints' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/core-ui-workstation-endpoints /p:EnableWindowsTargeting=true
```

core-ui-other: passed

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --no-build --filter '(Category!=Integration&Category!=Performance)&(FullyQualifiedName~Meridian.Tests.Ui&FullyQualifiedName!~Meridian.Tests.Ui.WorkstationEndpointsTests)' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=core-ui-other' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/core-ui-other /p:EnableWindowsTargeting=true
```

core-infrastructure: passed

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --no-build --filter '(Category!=Integration&Category!=Performance)&(FullyQualifiedName~Meridian.Tests.Infrastructure)' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=core-infrastructure' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/core-infrastructure /p:EnableWindowsTargeting=true
```

core-storage: passed

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --no-build --filter '(Category!=Integration&Category!=Performance)&(FullyQualifiedName~Meridian.Tests.Storage)' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=core-storage' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/core-storage /p:EnableWindowsTargeting=true
```

core-data: passed

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --no-build --filter '(Category!=Integration&Category!=Performance)&(FullyQualifiedName~Meridian.Tests.DataIntegration)' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=core-data' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/core-data /p:EnableWindowsTargeting=true
```

core-execution-strategy: passed

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --no-build --filter '(Category!=Integration&Category!=Performance)&(FullyQualifiedName~Meridian.Tests.Execution|FullyQualifiedName~Meridian.Tests.Strategies|FullyQualifiedName~Meridian.Tests.Backfill|FullyQualifiedName~Meridian.Tests.SecurityMaster)' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=core-execution-strategy' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/core-execution-strategy /p:EnableWindowsTargeting=true
```

core-market-instruments: passed

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --no-build --filter '(Category!=Integration&Category!=Performance)&(FullyQualifiedName~Meridian.Tests.Integration|FullyQualifiedName~Meridian.Tests.PortfolioRecords|FullyQualifiedName~Meridian.Tests.Credentials|FullyQualifiedName~Meridian.Tests.Commodities|FullyQualifiedName~Meridian.Tests.CertificatesOfDeposit|FullyQualifiedName~Meridian.Tests.CryptoCurrency|FullyQualifiedName~Meridian.Tests.Deposits|FullyQualifiedName~Meridian.Tests.MoneyMarketFunds|FullyQualifiedName~Meridian.Tests.Entities|FullyQualifiedName~Meridian.Tests.Futures|FullyQualifiedName~Meridian.Tests.Options|FullyQualifiedName~Meridian.Tests.Equity|FullyQualifiedName~Meridian.Tests.FixedIncome|FullyQualifiedName~Meridian.Tests.FxSpot)' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=core-market-instruments' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/core-market-instruments /p:EnableWindowsTargeting=true
```

core-platform-domain-root: passed

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --no-build --filter '(Category!=Integration&Category!=Performance)&(FullyQualifiedName~Meridian.Tests.Pipeline|FullyQualifiedName~Meridian.Tests.Platform|FullyQualifiedName~Meridian.Tests.ProviderSdk|FullyQualifiedName~Meridian.Tests.Providers|FullyQualifiedName~Meridian.Tests.Monitoring|FullyQualifiedName~Meridian.Tests.FinancialOperations|FullyQualifiedName~Meridian.Tests.Ledger|FullyQualifiedName~Meridian.Tests.Core|FullyQualifiedName~Meridian.Tests.Domain|FullyQualifiedName~Meridian.Tests.Models|FullyQualifiedName~Meridian.Tests.Reconciliation|FullyQualifiedName~Meridian.Tests.Treasury|FullyQualifiedName~Meridian.Tests.Instruments|FullyQualifiedName~Meridian.Tests.Contracts|FullyQualifiedName~Meridian.Tests.Risk|FullyQualifiedName~Meridian.Tests.Config|FullyQualifiedName~Meridian.Tests.Architecture|FullyQualifiedName~Meridian.Tests.Workflow|FullyQualifiedName~Meridian.Tests.Services|FullyQualifiedName~Meridian.Tests.Derivatives|FullyQualifiedName~Meridian.Tests.Indicators|FullyQualifiedName~Meridian.Tests.AssetOperations|FullyQualifiedName~Meridian.Tests.ReferenceData|FullyQualifiedName~Meridian.Tests.Identity|FullyQualifiedName~Meridian.Tests.Wpf|FullyQualifiedName~Meridian.Tests.Compliance|FullyQualifiedName~Meridian.Tests.Serialization|FullyQualifiedName~Meridian.Tests.TradingCalendarTests|FullyQualifiedName~Meridian.Tests.CronExpressionParserTests|FullyQualifiedName~Meridian.Tests.SymbolSearch|FullyQualifiedName~Meridian.Tests.OrderEventPayloadTests|FullyQualifiedName~Meridian.Tests.MarketDepthCollectorTests|FullyQualifiedName~Meridian.Tests.CliModeResolverTests|FullyQualifiedName~Meridian.Tests.OptionContractSpecTests|FullyQualifiedName~Meridian.Tests.L3OrderBookCollectorTests|FullyQualifiedName~Meridian.Tests.OptionQuoteTests|FullyQualifiedName~Meridian.Tests.GreeksSnapshotTests|FullyQualifiedName~Meridian.Tests.TradeDataCollectorTests|FullyQualifiedName~Meridian.Tests.OptionTradeTests|FullyQualifiedName~Meridian.Tests.OptionChainSnapshotTests|FullyQualifiedName~Meridian.Tests.GracefulShutdownTests|FullyQualifiedName~Meridian.Tests.OpenInterestUpdateTests|FullyQualifiedName~Meridian.Tests.LiveDataAccessTests|FullyQualifiedName~Meridian.Tests.FilePermissionsServiceTests|FullyQualifiedName~Meridian.Tests.TradeModelTests|FullyQualifiedName~Meridian.Tests.BboQuotePayloadTests|FullyQualifiedName~Meridian.Tests.OrderBookLevelTests|FullyQualifiedName~Meridian.Tests.AlpacaQuoteRoutingTests|FullyQualifiedName~Meridian.Tests.PrometheusMetricsTests|FullyQualifiedName~Meridian.Tests.SessionStatsCollectorTests|FullyQualifiedName~Meridian.Tests.QuoteCollectorTests|FullyQualifiedName~Meridian.Tests.StatementReconciliationServiceTests|FullyQualifiedName~Meridian.Tests.WebSocketResiliencePolicyTests|FullyQualifiedName~Meridian.Tests.CompositePublisherTests|FullyQualifiedName~Meridian.Tests.ConnectionRetryIntegrationTests|FullyQualifiedName~Meridian.Tests.FilePermissionsDiagnosticTests|FullyQualifiedName~Meridian.Tests.WebSocketHeartbeatTests|FullyQualifiedName~Meridian.Tests.PrometheusMetricsUpdaterTests|FullyQualifiedName~Meridian.Tests.ExponentialBackoffTests|FullyQualifiedName~Meridian.Tests.CircuitBreakerTests)' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=core-platform-domain-root' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/core-platform-domain-root /p:EnableWindowsTargeting=true
```

core-reporting: passed

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --no-build --filter '(Category!=Integration&Category!=Performance)&(FullyQualifiedName~Meridian.Tests.Reporting)' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=core-reporting' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/core-reporting /p:EnableWindowsTargeting=true
```

fsharp: passed

```sh
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj -c Release --no-restore --no-build --filter 'Category!=Integration&Category!=Performance' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=fsharp' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/fsharp /p:EnableWindowsTargeting=true
```

ui: passed

```sh
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj -c Release --no-restore --no-build --filter 'Category!=Integration&Category!=Performance' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=ui' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/ui /p:EnableWindowsTargeting=true
```

backtesting: passed

```sh
dotnet test tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj -c Release --no-restore --no-build --filter 'Category!=Integration&Category!=Performance' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=backtesting' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/backtesting /p:EnableWindowsTargeting=true
```

directlending: passed

```sh
dotnet test tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj -c Release --no-restore --no-build --filter 'Category!=Integration&Category!=Performance' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=directlending' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/directlending /p:EnableWindowsTargeting=true
```

fundstructure: passed

```sh
dotnet test tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj -c Release --no-restore --no-build --filter 'Category!=Integration&Category!=Performance' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=fundstructure' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/fundstructure /p:EnableWindowsTargeting=true
```

quantscript: passed

```sh
dotnet test tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj -c Release --no-restore --no-build --filter 'Category!=Integration&Category!=Performance' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=quantscript' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/quantscript /p:EnableWindowsTargeting=true
```

designmodules: passed

```sh
dotnet test tests/Meridian.DesignModules.Tests/Meridian.DesignModules.Tests.csproj -c Release --no-restore --no-build --filter 'Category!=Integration&Category!=Performance' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=designmodules' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/designmodules /p:EnableWindowsTargeting=true
```

lifecycle: passed

```sh
dotnet test tests/Meridian.Lifecycle.Tests/Meridian.Lifecycle.Tests.csproj -c Release --no-restore --no-build --filter 'Category!=Integration&Category!=Performance' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=lifecycle' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/lifecycle /p:EnableWindowsTargeting=true
```

core-remainder: passed

```sh
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --no-build --filter '(Category!=Integration&Category!=Performance)&(FullyQualifiedName~Meridian.Tests&FullyQualifiedName!~Meridian.Tests.AlpacaQuoteRoutingTests&FullyQualifiedName!~Meridian.Tests.Application&FullyQualifiedName!~Meridian.Tests.Architecture&FullyQualifiedName!~Meridian.Tests.AssetOperations&FullyQualifiedName!~Meridian.Tests.Backfill&FullyQualifiedName!~Meridian.Tests.BboQuotePayloadTests&FullyQualifiedName!~Meridian.Tests.CertificatesOfDeposit&FullyQualifiedName!~Meridian.Tests.CircuitBreakerTests&FullyQualifiedName!~Meridian.Tests.CliModeResolverTests&FullyQualifiedName!~Meridian.Tests.Commodities&FullyQualifiedName!~Meridian.Tests.Compliance&FullyQualifiedName!~Meridian.Tests.CompositePublisherTests&FullyQualifiedName!~Meridian.Tests.Config&FullyQualifiedName!~Meridian.Tests.ConnectionRetryIntegrationTests&FullyQualifiedName!~Meridian.Tests.Contracts&FullyQualifiedName!~Meridian.Tests.Core&FullyQualifiedName!~Meridian.Tests.Credentials&FullyQualifiedName!~Meridian.Tests.CronExpressionParserTests&FullyQualifiedName!~Meridian.Tests.CryptoCurrency&FullyQualifiedName!~Meridian.Tests.DataIntegration&FullyQualifiedName!~Meridian.Tests.Deposits&FullyQualifiedName!~Meridian.Tests.Derivatives&FullyQualifiedName!~Meridian.Tests.Domain&FullyQualifiedName!~Meridian.Tests.Entities&FullyQualifiedName!~Meridian.Tests.Equity&FullyQualifiedName!~Meridian.Tests.Execution&FullyQualifiedName!~Meridian.Tests.ExponentialBackoffTests&FullyQualifiedName!~Meridian.Tests.FilePermissionsDiagnosticTests&FullyQualifiedName!~Meridian.Tests.FilePermissionsServiceTests&FullyQualifiedName!~Meridian.Tests.FinancialOperations&FullyQualifiedName!~Meridian.Tests.FixedIncome&FullyQualifiedName!~Meridian.Tests.Futures&FullyQualifiedName!~Meridian.Tests.FxSpot&FullyQualifiedName!~Meridian.Tests.GracefulShutdownTests&FullyQualifiedName!~Meridian.Tests.GreeksSnapshotTests&FullyQualifiedName!~Meridian.Tests.Identity&FullyQualifiedName!~Meridian.Tests.Indicators&FullyQualifiedName!~Meridian.Tests.Infrastructure&FullyQualifiedName!~Meridian.Tests.Instruments&FullyQualifiedName!~Meridian.Tests.Integration&FullyQualifiedName!~Meridian.Tests.L3OrderBookCollectorTests&FullyQualifiedName!~Meridian.Tests.Ledger&FullyQualifiedName!~Meridian.Tests.LiveDataAccessTests&FullyQualifiedName!~Meridian.Tests.MarketDepthCollectorTests&FullyQualifiedName!~Meridian.Tests.Models&FullyQualifiedName!~Meridian.Tests.MoneyMarketFunds&FullyQualifiedName!~Meridian.Tests.Monitoring&FullyQualifiedName!~Meridian.Tests.OpenInterestUpdateTests&FullyQualifiedName!~Meridian.Tests.OptionChainSnapshotTests&FullyQualifiedName!~Meridian.Tests.OptionContractSpecTests&FullyQualifiedName!~Meridian.Tests.OptionQuoteTests&FullyQualifiedName!~Meridian.Tests.OptionTradeTests&FullyQualifiedName!~Meridian.Tests.Options&FullyQualifiedName!~Meridian.Tests.OrderBookLevelTests&FullyQualifiedName!~Meridian.Tests.OrderEventPayloadTests&FullyQualifiedName!~Meridian.Tests.Pipeline&FullyQualifiedName!~Meridian.Tests.Platform&FullyQualifiedName!~Meridian.Tests.PortfolioRecords&FullyQualifiedName!~Meridian.Tests.PrometheusMetricsTests&FullyQualifiedName!~Meridian.Tests.PrometheusMetricsUpdaterTests&FullyQualifiedName!~Meridian.Tests.ProviderSdk&FullyQualifiedName!~Meridian.Tests.Providers&FullyQualifiedName!~Meridian.Tests.QuoteCollectorTests&FullyQualifiedName!~Meridian.Tests.Reconciliation&FullyQualifiedName!~Meridian.Tests.ReferenceData&FullyQualifiedName!~Meridian.Tests.Reporting&FullyQualifiedName!~Meridian.Tests.Risk&FullyQualifiedName!~Meridian.Tests.SecurityMaster&FullyQualifiedName!~Meridian.Tests.Serialization&FullyQualifiedName!~Meridian.Tests.Services&FullyQualifiedName!~Meridian.Tests.SessionStatsCollectorTests&FullyQualifiedName!~Meridian.Tests.StatementReconciliationServiceTests&FullyQualifiedName!~Meridian.Tests.Storage&FullyQualifiedName!~Meridian.Tests.Strategies&FullyQualifiedName!~Meridian.Tests.SymbolSearch&FullyQualifiedName!~Meridian.Tests.TradeDataCollectorTests&FullyQualifiedName!~Meridian.Tests.TradeModelTests&FullyQualifiedName!~Meridian.Tests.TradingCalendarTests&FullyQualifiedName!~Meridian.Tests.Treasury&FullyQualifiedName!~Meridian.Tests.Ui&FullyQualifiedName!~Meridian.Tests.Ui.WorkstationEndpointsTests&FullyQualifiedName!~Meridian.Tests.WebSocketHeartbeatTests&FullyQualifiedName!~Meridian.Tests.WebSocketResiliencePolicyTests&FullyQualifiedName!~Meridian.Tests.Workflow&FullyQualifiedName!~Meridian.Tests.Wpf)' --blame-hang --blame-hang-timeout 10m --logger 'trx;LogFilePrefix=core-remainder' --results-directory /workspace/Meridian-main/artifacts/test-results/dotnet/core-remainder /p:EnableWindowsTargeting=true
```


#### Build evidence

| Build | Status | Duration (s) | Log |
| --- | --- | ---: | --- |
| `build:default-roster` | passed | 119.938 | `/workspace/Meridian-main/artifacts/test-results/dotnet/dotnet-build.log` |
