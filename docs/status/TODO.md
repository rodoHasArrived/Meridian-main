# TODO / FIXME / HACK / NOTE Scan

Total items: **80**

| File | Line | Tag | Linked Issue | Text |
|---|---:|---|:---:|---|
| `.claude/agents/meridian-cleanup.md` | 423 | `TODO` | ❌ | - `// TODO: implement` in methods that are already implemented. |
| `.claude/agents/meridian-cleanup.md` | 430 | `TODO` | ❌ | - `// TODO:` or `// FIXME:` comments that describe genuine open work items — flag them |
| `.claude/skills/meridian-code-review/evals/evals.json` | 166 | `TODO` | ❌ | "prompt": "Review this ViewModel and its paired View code-behind together for MVVM compliance:\n\nFile 1: SymbolsViewModel.cs\n```csharp\nusing System.Collections.ObjectModel;\nusing Meridian.Ui.Services;\nusing Meridian.Contracts;\n\nnamespace Meridian.Wpf.ViewModels;\n\npublic class SymbolsViewModel : BindableBase\n{\n    private readonly ISymbolService _symbolService;\n    private ObservableCollection<SymbolStatus> _symbols = new();\n    private string _searchText = string.Empty;\n    private bool _isLoading;\n\n    public SymbolsViewModel(ISymbolService symbolService)\n    {\n        _symbolService = symbolService;\n        LoadSymbolsCommand = new RelayCommand(async _ => await LoadSymbolsAsync());\n        RemoveSymbolCommand = new RelayCommand(async p => await RemoveSymbolAsync((string)p!));\n    }\n\n    public ObservableCollection<SymbolStatus> Symbols\n    {\n        get => _symbols;\n        private set => SetProperty(ref _symbols, value);\n    }\n\n    public string SearchText\n    {\n        get => _searchText;\n        set\n        {\n            SetProperty(ref _searchText, value);\n            FilterSymbols();\n        }\n    }\n\n    public bool IsLoading\n    {\n        get => _isLoading;\n        private set => SetProperty(ref _isLoading, value);\n    }\n\n    public RelayCommand LoadSymbolsCommand { get; }\n    public RelayCommand RemoveSymbolCommand { get; }\n\n    private async Task LoadSymbolsAsync()\n    {\n        IsLoading = true;\n        var symbols = await _symbolService.GetSymbolsAsync();\n        Symbols = new ObservableCollection<SymbolStatus>(symbols);\n        IsLoading = false;\n    }\n\n    private async Task RemoveSymbolAsync(string symbol)\n    {\n        await _symbolService.RemoveSymbolAsync(symbol);\n        var item = _symbols.FirstOrDefault(s => s.Symbol == symbol);\n        if (item != null) _symbols.Remove(item);\n    }\n\n    private void FilterSymbols()\n    {\n        // TODO: implement filtering\n    }\n}\n```\n\nFile 2: SymbolsPage.xaml.cs\n```csharp\nusing System.Windows.Controls;\nusing Meridian.Wpf.ViewModels;\nusing Meridian.Ui.Services;\n\nnamespace Meridian.Wpf.Views;\n\npublic partial class SymbolsPage : Page\n{\n    private readonly SymbolsViewModel _viewModel;\n\n    public SymbolsPage(ISymbolService symbolService)\n    {\n        InitializeComponent();\n        _viewModel = new SymbolsViewModel(symbolService);\n        DataContext = _viewModel;\n        Loaded += async (_, _) => await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n\n    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)\n    {\n        _viewModel.SearchText = ((TextBox)sender).Text;\n    }\n\n    private async void RemoveButton_Click(object sender, System.Windows.RoutedEventArgs e)\n    {\n        var symbol = (string)((System.Windows.FrameworkElement)sender).Tag;\n        await _viewModel._symbolService.RemoveSymbolAsync(symbol);\n        await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n}\n```", |
| `.github/agents/cleanup-agent.md` | 385 | `TODO` | ❌ | - `// TODO: implement` in methods that are already implemented. |
| `.github/agents/cleanup-agent.md` | 393 | `TODO` | ❌ | - `// TODO:` or `// FIXME:` comments that describe genuine open work items — flag |
| `.github/agents/provider-builder-agent.md` | 81 | `TODO` | ❌ | Read the full template before writing any code. Templates contain inline `// TODO:` comments |
| `.github/workflows/desktop-builds.yml` | 9 | `NOTE` | ❌ | # NOTE: UWP/WinUI 3 application has been removed. WPF is the sole desktop client. |
| `.github/workflows/test-matrix.yml` | 5 | `NOTE` | ❌ | # NOTE: This workflow intentionally does NOT use reusable-dotnet-build.yml because it needs |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/linux-arm/sosdocsunix.txt` | 2 | `NOTE` | ❌ | NOTE: THIS FILE CONTAINS SOS DOCUMENTATION. THE FORMAT OF THE FILE IS: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/linux-arm/sosdocsunix.txt` | 974 | `NOTE` | ❌ | NOTE: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/linux-arm64/sosdocsunix.txt` | 2 | `NOTE` | ❌ | NOTE: THIS FILE CONTAINS SOS DOCUMENTATION. THE FORMAT OF THE FILE IS: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/linux-arm64/sosdocsunix.txt` | 974 | `NOTE` | ❌ | NOTE: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/linux-musl-arm/sosdocsunix.txt` | 2 | `NOTE` | ❌ | NOTE: THIS FILE CONTAINS SOS DOCUMENTATION. THE FORMAT OF THE FILE IS: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/linux-musl-arm/sosdocsunix.txt` | 974 | `NOTE` | ❌ | NOTE: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/linux-musl-arm64/sosdocsunix.txt` | 2 | `NOTE` | ❌ | NOTE: THIS FILE CONTAINS SOS DOCUMENTATION. THE FORMAT OF THE FILE IS: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/linux-musl-arm64/sosdocsunix.txt` | 974 | `NOTE` | ❌ | NOTE: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/linux-musl-x64/sosdocsunix.txt` | 2 | `NOTE` | ❌ | NOTE: THIS FILE CONTAINS SOS DOCUMENTATION. THE FORMAT OF THE FILE IS: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/linux-musl-x64/sosdocsunix.txt` | 974 | `NOTE` | ❌ | NOTE: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/linux-x64/sosdocsunix.txt` | 2 | `NOTE` | ❌ | NOTE: THIS FILE CONTAINS SOS DOCUMENTATION. THE FORMAT OF THE FILE IS: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/linux-x64/sosdocsunix.txt` | 974 | `NOTE` | ❌ | NOTE: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/osx-arm64/sosdocsunix.txt` | 2 | `NOTE` | ❌ | NOTE: THIS FILE CONTAINS SOS DOCUMENTATION. THE FORMAT OF THE FILE IS: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/osx-arm64/sosdocsunix.txt` | 974 | `NOTE` | ❌ | NOTE: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/osx-x64/sosdocsunix.txt` | 2 | `NOTE` | ❌ | NOTE: THIS FILE CONTAINS SOS DOCUMENTATION. THE FORMAT OF THE FILE IS: |
| `.tools/.store/dotnet-dump/9.0.661903/dotnet-dump/9.0.661903/tools/net8.0/any/osx-x64/sosdocsunix.txt` | 974 | `NOTE` | ❌ | NOTE: |
| `AGENTS.md` | 124 | `TODO` | ❌ | TODO: `src/Meridian.Application/Commands/EtlCommands.cs` exposes `--etl-import`, |
| `AGENTS.md` | 128 | `TODO` | ❌ | TODO: `SecurityMasterCommands` and `ProviderCalibrationCommand` expose `--security-master-ingest` |
| `AGENTS.md` | 133 | `TODO` | ❌ | TODO: `docs/HELP.md` mentions `--diagnostics`, but `DiagnosticsCommands` currently handles |
| `AGENTS.md` | 138 | `TODO` | ❌ | TODO: `docs/HELP.md` includes `--package --package-format csv`, but `PackageCommands` currently |
| `AGENTS.md` | 142 | `TODO` | ❌ | TODO: `--replay` is exposed in `CliArguments` and listed in `docs/status/FEATURE_INVENTORY.md`, but |
| `AGENTS.md` | 146 | `TODO` | ❌ | TODO: `docs/status/FEATURE_INVENTORY.md` lists `--simulate-execution` as a planned simulation CLI |
| `AGENTS.md` | 301 | `TODO` | ❌ | TODO: `README.md` and `.codex/skills/_shared/project-context.md` mention `make desktop-run`, |
| `AGENTS.md` | 305 | `TODO` | ❌ | TODO: `docs/development/desktop-workflow-automation.md` mentions `make desktop-workflow`, |
| `AGENTS.md` | 309 | `TODO` | ❌ | TODO: `docs/development/desktop-testing-guide.md` still references `make desktop-dev-bootstrap`, |
| `AGENTS.md` | 315 | `TODO` | ❌ | TODO: `docs/operations/msix-packaging.md` documents `make desktop-publish`, but the current |
| `AGENTS.md` | 472 | `TODO` | ❌ | TODO: `make doctor-fix` exists, but current `make/diagnostics.mk` says auto-fix is not yet |
| `AGENTS.md` | 476 | `TODO` | ❌ | TODO: `make ai-docs-archive-execute` exists, but it moves stale docs. Run `make ai-docs-archive` |
| `benchmarks/run-bottleneck-benchmarks.sh` | 111 | `NOTE` | ❌ | # Note: --filter is intentionally not added here; each phase below supplies its own |
| `config/appsettings.sample.json` | 379 | `NOTE` | ❌ | // NOTE: This key is a duplicate of the one near the top of this file for documentation purposes. |
| `config/appsettings.sample.json` | 388 | `NOTE` | ❌ | // NOTE: Credentials are resolved from environment variables - do NOT add them here. |
| `docs/architecture/deterministic-canonicalization.md` | 365 | `NOTE` | ❌ | Note: Polygon does not define buyer-initiated codes. Only ~5% of trades carry definitive aggressor inference. The canonicalization layer preserves `Unknown` as a valid canonical value rather than attempting inference. |
| `docs/architecture/domains.md` | 111 | `NOTE` | ❌ | > Note: not every enum member is currently emitted by the three core collectors (`TradeDataCollector`, `MarketDepthCollector`, `QuoteCollector`); several are used by adapters, backfill paths, or the `L3OrderBookCollector`. |
| `docs/docfx/api/Meridian.Application.Monitoring.MigrationDiagnostics.yml` | 69 | `NOTE` | ❌ | NOTE: This class lives in the Core project (not Application) so that |
| `docs/docfx/api/Meridian.Application.Serialization.AlpacaJsonContext.yml` | 41 | `NOTE` | ❌ | NOTE: Alpaca payloads use both "T" and "t" keys in the same object. |
| `docs/docfx/api/Meridian.Ui.Services.StorageRetentionPolicy.yml` | 36 | `NOTE` | ❌ | Note: Renamed from RetentionPolicy to avoid conflict with Meridian.Ui.Services.RetentionPolicy |
| `docs/evaluations/high-value-low-cost-improvements-brainstorm.md` | 670 | `NOTE` | ❌ | Note: IB would add L2 depth but requires TWS running |
| `docs/evaluations/nautilus-inspired-restructuring-proposal.md` | 185 | `TODO` | ❌ | // TODO: Add provider-specific dependencies (HttpClient, config, etc.) |
| `docs/evaluations/nautilus-inspired-restructuring-proposal.md` | 187 | `TODO` | ❌ | public bool IsEnabled => true; // TODO: Wire to configuration |
| `docs/operations/operator-runbook.md` | 205 | `NOTE` | ❌ | - note: L2 depth requires provider depth entitlements |
| `docs/plans/quantscript-l3-multiinstance-round2-roadmap.md` | 370 | `NOTE` | ❌ | Note: `ISecurityMasterQueryService` is at `src/Meridian.Contracts/SecurityMaster/ISecurityMasterQueryService.cs` (not `src/Meridian.Application/SecurityMaster/`). |
| `docs/plans/quantscript-l3-multiinstance-round2-roadmap.md` | 1092 | `NOTE` | ❌ | Note: `BacktestResult.TcaReport` already exists — no schema change needed there. |
| `docs/providers/interactive-brokers-free-equity-reference.md` | 254 | `NOTE` | ❌ | Note: It is important to understand the concept of market data lines since it has an impact not only on the live real time requests but also for requesting market depth and real time bars. |
| `docs/providers/interactive-brokers-free-equity-reference.md` | 265 | `NOTE` | ❌ | - Note: BID_ASK requests count as **two** requests |
| `src/Meridian.Application/Commands/SecurityMasterCommands.cs` | 22 | `NOTE` | ❌ | // NOTE: _importService is null when the Security Master database is not configured at CLI |
| `src/Meridian.Application/Http/Endpoints/ArchiveMaintenanceEndpoints.cs` | 32 | `NOTE` | ❌ | // NOTE: GET /schedules, GET /schedules/{id}, POST /schedules, POST /schedules/{id}/enable, |
| `src/Meridian.Application/Http/Endpoints/ArchiveMaintenanceEndpoints.cs` | 109 | `NOTE` | ❌ | // NOTE: POST /schedules/{id}/enable and POST /schedules/{id}/disable are registered |
| `src/Meridian.Backtesting/Metrics/BacktestMetricsEngine.cs` | 270 | `NOTE` | ❌ | /// NOTE: This is an independent computation over fill events for metric attribution purposes. |
| `src/Meridian.Backtesting/Portfolio/SimulatedPortfolio.cs` | 841 | `NOTE` | ❌ | /// NOTE: This must stay consistent with <c>BacktestMetricsEngine.ComputeRealisedPnl</c>, |
| `src/Meridian.Core/Monitoring/MigrationDiagnostics.cs` | 17 | `NOTE` | ❌ | /// NOTE: This class lives in the Core project (not Application) so that |
| `src/Meridian.Core/Serialization/MarketDataJsonContext.cs` | 171 | `NOTE` | ❌ | /// NOTE: Alpaca payloads use both "T" and "t" keys in the same object. |
| `src/Meridian.Execution/BrokerageServiceRegistration.cs` | 135 | `NOTE` | ❌ | // NOTE: We intentionally use GetRequiredKeyedService here rather than |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 95 | `NOTE` | ❌ | Note: item.ResolutionNote), ct).ConfigureAwait(false); |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 192 | `NOTE` | ❌ | Note: request.ReviewNote), ct).ConfigureAwait(false); |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 258 | `NOTE` | ❌ | Note: request.ResolutionNote), ct).ConfigureAwait(false); |
| `src/Meridian.Ui.Services/Services/AdminMaintenanceModels.cs` | 266 | `NOTE` | ❌ | /// Note: Renamed from RetentionPolicy to avoid conflict with Meridian.Ui.Services.RetentionPolicy |
| `src/Meridian.Ui.Services/Services/AdminMaintenanceModels.cs` | 398 | `NOTE` | ❌ | // NOTE: SelfTest*, ErrorCodes*, ShowConfig*, QuickCheck* models are defined in DiagnosticsService.cs |
| `src/Meridian.Ui.Services/Services/ProviderHealthService.cs` | 514 | `NOTE` | ❌ | // NOTE: ProviderComparison is defined in AdvancedAnalyticsModels.cs for cross-provider comparison |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.test.tsx` | 22 | `NOTE` | ❌ | note: "Realtime subscriptions are stable." |
| `src/Meridian.Ui/dashboard/src/types.ts` | 413 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Wpf/GlobalUsings.cs` | 7 | `NOTE` | ❌ | // NOTE: Type aliases and Contracts namespaces are NOT re-defined here because |
| `tests/Meridian.Tests/Application/Backfill/BackfillWorkerServiceTests.cs` | 28 | `NOTE` | ❌ | // NOTE: Using null! because validation throws before dependencies are accessed |
| `tests/Meridian.Tests/Application/Backfill/BackfillWorkerServiceTests.cs` | 56 | `NOTE` | ❌ | // NOTE: Using null! because validation throws before dependencies are accessed |
| `tests/Meridian.Tests/Application/Backfill/BackfillWorkerServiceTests.cs` | 86 | `NOTE` | ❌ | // NOTE: Using null! dependencies - we only verify that ArgumentOutOfRangeException is not thrown |
| `tests/Meridian.Tests/Application/Monitoring/DataQuality/DataFreshnessSlaMonitorTests.cs` | 525 | `NOTE` | ❌ | // NOTE: Actual result depends on current time, so we check the logic is working |
| `tests/Meridian.Tests/Application/Pipeline/FSharpEventValidatorTests.cs` | 72 | `NOTE` | ❌ | // Note: Trade.ctor only checks Price > 0, so $2,000,000 is constructible. |
| `tests/Meridian.Tests/Storage/StorageChecksumServiceTests.cs` | 121 | `NOTE` | ❌ | // NOTE: File.WriteAllTextAsync uses UTF-8 with BOM by default on some platforms, |
| `tests/Meridian.Ui.Tests/Services/DiagnosticsServiceTests.cs` | 9 | `NOTE` | ❌ | /// Note: The service methods require a running backend (ApiClientService), |
| `tests/Meridian.Ui.Tests/Services/ScheduledMaintenanceServiceTests.cs` | 85 | `NOTE` | ❌ | // NOTE: since this is a singleton shared across tests, if StartScheduler was |
| `tests/Meridian.Ui.Tests/Services/StorageAnalyticsServiceTests.cs` | 9 | `NOTE` | ❌ | /// Note: Full analytics calculation requires file I/O, so these tests |
| `tests/Meridian.Wpf.Tests/Services/OfflineTrackingPersistenceServiceTests.cs` | 27 | `NOTE` | ❌ | // NOTE: Singleton state may persist across tests. |
| `tests/Meridian.Wpf.Tests/Services/PendingOperationsQueueServiceTests.cs` | 30 | `NOTE` | ❌ | // NOTE: This may not be false if other tests have run InitializeAsync. |
