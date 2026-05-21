# TODO / FIXME / HACK / NOTE Scan

Total items: **124**

| File | Line | Tag | Linked Issue | Text |
| --- | ---: | --- | :---: | --- |
| `.agents/skills/meridian-code-review/evals/evals.json` | 166 | `TODO` | ❌ | "prompt": "Review this ViewModel and its paired View code-behind together for MVVM compliance:\n\nFile 1: SymbolsViewModel.cs\n```csharp\nusing System.Collections.ObjectModel;\nusing Meridian.Ui.Services;\nusing Meridian.Contracts;\n\nnamespace Meridian.Wpf.ViewModels;\n\npublic class SymbolsViewModel : BindableBase\n{\n    private readonly ISymbolService _symbolService;\n    private ObservableCollection<SymbolStatus> _symbols = new();\n    private string _searchText = string.Empty;\n    private bool _isLoading;\n\n    public SymbolsViewModel(ISymbolService symbolService)\n    {\n        _symbolService = symbolService;\n        LoadSymbolsCommand = new RelayCommand(async _ => await LoadSymbolsAsync());\n        RemoveSymbolCommand = new RelayCommand(async p => await RemoveSymbolAsync((string)p!));\n    }\n\n    public ObservableCollection<SymbolStatus> Symbols\n    {\n        get => _symbols;\n        private set => SetProperty(ref _symbols, value);\n    }\n\n    public string SearchText\n    {\n        get => _searchText;\n        set\n        {\n            SetProperty(ref _searchText, value);\n            FilterSymbols();\n        }\n    }\n\n    public bool IsLoading\n    {\n        get => _isLoading;\n        private set => SetProperty(ref _isLoading, value);\n    }\n\n    public RelayCommand LoadSymbolsCommand { get; }\n    public RelayCommand RemoveSymbolCommand { get; }\n\n    private async Task LoadSymbolsAsync()\n    {\n        IsLoading = true;\n        var symbols = await _symbolService.GetSymbolsAsync();\n        Symbols = new ObservableCollection<SymbolStatus>(symbols);\n        IsLoading = false;\n    }\n\n    private async Task RemoveSymbolAsync(string symbol)\n    {\n        await _symbolService.RemoveSymbolAsync(symbol);\n        var item = _symbols.FirstOrDefault(s => s.Symbol == symbol);\n        if (item != null) _symbols.Remove(item);\n    }\n\n    private void FilterSymbols()\n    {\n        // TODO: implement filtering\n    }\n}\n```\n\nFile 2: SymbolsPage.xaml.cs\n```csharp\nusing System.Windows.Controls;\nusing Meridian.Wpf.ViewModels;\nusing Meridian.Ui.Services;\n\nnamespace Meridian.Wpf.Views;\n\npublic partial class SymbolsPage : Page\n{\n    private readonly SymbolsViewModel _viewModel;\n\n    public SymbolsPage(ISymbolService symbolService)\n    {\n        InitializeComponent();\n        _viewModel = new SymbolsViewModel(symbolService);\n        DataContext = _viewModel;\n        Loaded += async (_, _) => await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n\n    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)\n    {\n        _viewModel.SearchText = ((TextBox)sender).Text;\n    }\n\n    private async void RemoveButton_Click(object sender, System.Windows.RoutedEventArgs e)\n    {\n        var symbol = (string)((System.Windows.FrameworkElement)sender).Tag;\n        await _viewModel._symbolService.RemoveSymbolAsync(symbol);\n        await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n}\n```", |
| `.claude/agents/meridian-cleanup.md` | 423 | `TODO` | ❌ | - placeholder implementation comments (for example, `// TODO: implement`) in methods that are already implemented. |
| `.claude/agents/meridian-cleanup.md` | 430 | `TODO` | ❌ | - Open-work comments (for example, `// TODO:` or `// FIXME:`) that describe genuine pending tasks — flag them |
| `.claude/skills/meridian-code-review/evals/evals.json` | 166 | `TODO` | ❌ | "prompt": "Review this ViewModel and its paired View code-behind together for MVVM compliance:\n\nFile 1: SymbolsViewModel.cs\n```csharp\nusing System.Collections.ObjectModel;\nusing Meridian.Ui.Services;\nusing Meridian.Contracts;\n\nnamespace Meridian.Wpf.ViewModels;\n\npublic class SymbolsViewModel : BindableBase\n{\n    private readonly ISymbolService _symbolService;\n    private ObservableCollection<SymbolStatus> _symbols = new();\n    private string _searchText = string.Empty;\n    private bool _isLoading;\n\n    public SymbolsViewModel(ISymbolService symbolService)\n    {\n        _symbolService = symbolService;\n        LoadSymbolsCommand = new RelayCommand(async _ => await LoadSymbolsAsync());\n        RemoveSymbolCommand = new RelayCommand(async p => await RemoveSymbolAsync((string)p!));\n    }\n\n    public ObservableCollection<SymbolStatus> Symbols\n    {\n        get => _symbols;\n        private set => SetProperty(ref _symbols, value);\n    }\n\n    public string SearchText\n    {\n        get => _searchText;\n        set\n        {\n            SetProperty(ref _searchText, value);\n            FilterSymbols();\n        }\n    }\n\n    public bool IsLoading\n    {\n        get => _isLoading;\n        private set => SetProperty(ref _isLoading, value);\n    }\n\n    public RelayCommand LoadSymbolsCommand { get; }\n    public RelayCommand RemoveSymbolCommand { get; }\n\n    private async Task LoadSymbolsAsync()\n    {\n        IsLoading = true;\n        var symbols = await _symbolService.GetSymbolsAsync();\n        Symbols = new ObservableCollection<SymbolStatus>(symbols);\n        IsLoading = false;\n    }\n\n    private async Task RemoveSymbolAsync(string symbol)\n    {\n        await _symbolService.RemoveSymbolAsync(symbol);\n        var item = _symbols.FirstOrDefault(s => s.Symbol == symbol);\n        if (item != null) _symbols.Remove(item);\n    }\n\n    private void FilterSymbols()\n    {\n        // TODO: implement filtering\n    }\n}\n```\n\nFile 2: SymbolsPage.xaml.cs\n```csharp\nusing System.Windows.Controls;\nusing Meridian.Wpf.ViewModels;\nusing Meridian.Ui.Services;\n\nnamespace Meridian.Wpf.Views;\n\npublic partial class SymbolsPage : Page\n{\n    private readonly SymbolsViewModel _viewModel;\n\n    public SymbolsPage(ISymbolService symbolService)\n    {\n        InitializeComponent();\n        _viewModel = new SymbolsViewModel(symbolService);\n        DataContext = _viewModel;\n        Loaded += async (_, _) => await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n\n    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)\n    {\n        _viewModel.SearchText = ((TextBox)sender).Text;\n    }\n\n    private async void RemoveButton_Click(object sender, System.Windows.RoutedEventArgs e)\n    {\n        var symbol = (string)((System.Windows.FrameworkElement)sender).Tag;\n        await _viewModel._symbolService.RemoveSymbolAsync(symbol);\n        await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n}\n```", |
| `.codex/agents/meridian-cleanup.toml` | 410 | `TODO` | ❌ | - placeholder implementation comments (for example, `// TODO: implement`) in methods that are already implemented.\r |
| `.codex/agents/meridian-cleanup.toml` | 417 | `TODO` | ❌ | - Open-work comments (for example, `// TODO:` or `// FIXME:`) that describe genuine pending tasks — flag them\r |
| `.github/agents/cleanup-agent.md` | 385 | `TODO` | ❌ | - placeholder implementation comments (for example, `// TODO: implement`) in methods that are already implemented. |
| `.github/agents/cleanup-agent.md` | 393 | `TODO` | ❌ | - Open-work comments (for example, `// TODO:` or `// FIXME:`) that describe genuine pending tasks — flag |
| `AGENTS.md` | 156 | `TODO` | ❌ | TODO: `docs/HELP.md` also includes `--statement-broker` and `--statement-date` examples, but the |
| `AGENTS.md` | 160 | `TODO` | ❌ | TODO: `SecurityMasterCommands` exposes `--security-master-ingest`, but its prerequisites are |
| `AGENTS.md` | 172 | `TODO` | ❌ | TODO: `docs/HELP.md` includes `--package --package-format csv`, but `PackageCommands` currently |
| `AGENTS.md` | 179 | `TODO` | ❌ | TODO: `docs/status/FEATURE_INVENTORY.md` lists `--simulate-execution` as a planned simulation CLI |
| `AGENTS.md` | 392 | `TODO` | ❌ | TODO: `docs/development/desktop-workflow-automation.md` mentions `make desktop-workflow`, |
| `AGENTS.md` | 396 | `TODO` | ❌ | TODO: `docs/development/desktop-testing-guide.md` still references `make desktop-dev-bootstrap`, |
| `AGENTS.md` | 564 | `TODO` | ❌ | TODO: `make doctor-fix` exists, but current `make/diagnostics.mk` says auto-fix is not yet |
| `AGENTS.md` | 568 | `TODO` | ❌ | TODO: `make ai-docs-archive-execute` exists, but it moves stale docs. Run `make ai-docs-archive` |
| `benchmarks/run-bottleneck-benchmarks.sh` | 111 | `NOTE` | ❌ | # Note: --filter is intentionally not added here; each phase below supplies its own |
| `config/appsettings.sample.json` | 369 | `NOTE` | ❌ | // NOTE: This key is a duplicate of the one near the top of this file for documentation purposes. |
| `config/appsettings.sample.json` | 378 | `NOTE` | ❌ | // NOTE: Credentials are resolved from environment variables - do NOT add them here. |
| `docs/architecture/deterministic-canonicalization.md` | 365 | `NOTE` | ❌ | Note: Polygon does not define buyer-initiated codes. Only ~5% of trades carry definitive aggressor inference. The canonicalization layer preserves `Unknown` as a valid canonical value rather than attempting inference. |
| `docs/architecture/domains.md` | 111 | `NOTE` | ❌ | > Note: not every enum member is currently emitted by the three core collectors (`TradeDataCollector`, `MarketDepthCollector`, `QuoteCollector`); several are used by adapters, backfill paths, or the `L3OrderBookCollector`. |
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
| `src/Meridian.Backtesting/Metrics/BacktestMetricsEngine.cs` | 289 | `NOTE` | ❌ | /// NOTE: This is an independent computation over fill events for metric attribution purposes. |
| `src/Meridian.Backtesting/Portfolio/SimulatedPortfolio.cs` | 841 | `NOTE` | ❌ | /// NOTE: This must stay consistent with <c>BacktestMetricsEngine.ComputeRealisedPnl</c>, |
| `src/Meridian.Core/Monitoring/MigrationDiagnostics.cs` | 17 | `NOTE` | ❌ | /// NOTE: This class lives in the Core project (not Application) so that |
| `src/Meridian.Core/Serialization/MarketDataJsonContext.cs` | 171 | `NOTE` | ❌ | /// NOTE: Alpaca payloads use both "T" and "t" keys in the same object. |
| `src/Meridian.Execution/BrokerageServiceRegistration.cs` | 135 | `NOTE` | ❌ | // NOTE: We intentionally use GetRequiredKeyedService here rather than |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 97 | `NOTE` | ❌ | Note: item.ResolutionNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 209 | `NOTE` | ❌ | Note: request.ReviewNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 303 | `NOTE` | ❌ | Note: request.ResolutionNote, |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 494 | `NOTE` | ❌ | Note: hasParams ? null : "Run was started without a captured parameter set.")); |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 505 | `NOTE` | ❌ | Note: hasPortfolio ? null : "No portfolio seam is associated with this run.")); |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 516 | `NOTE` | ❌ | Note: hasLedger ? null : "No ledger reference is associated with this run.")); |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 527 | `NOTE` | ❌ | Note: hasAudit ? null : "No audit reference was captured for this run.")); |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 541 | `NOTE` | ❌ | Note: promo?.ApprovedBy is not null ? $"Approved by {promo.ApprovedBy}." : null)); |
| `src/Meridian.Ui.Services/Services/AdminMaintenanceModels.cs` | 266 | `NOTE` | ❌ | /// Note: Renamed from RetentionPolicy to avoid conflict with Meridian.Ui.Services.RetentionPolicy |
| `src/Meridian.Ui.Services/Services/AdminMaintenanceModels.cs` | 398 | `NOTE` | ❌ | // NOTE: SelfTest*, ErrorCodes*, ShowConfig*, QuickCheck* models are defined in DiagnosticsService.cs |
| `src/Meridian.Ui.Services/Services/ProviderHealthService.cs` | 514 | `NOTE` | ❌ | // NOTE: ProviderComparison is defined in AdvancedAnalyticsModels.cs for cross-provider comparison |
| `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts` | 262 | `NOTE` | ❌ | note: "Streaming quote path is healthy." |
| `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts` | 522 | `NOTE` | ❌ | note: "Paper endpoint returned intermittent quote gaps.", |
| `src/Meridian.Ui/dashboard/src/app.test.tsx` | 294 | `NOTE` | ❌ | note: "Streaming quote path is healthy." |
| `src/Meridian.Ui/dashboard/src/app.test.tsx` | 507 | `NOTE` | ❌ | note: "Paper endpoint returned intermittent quote gaps.", |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.test.tsx` | 16 | `NOTE` | ❌ | note: "Opening sleeve" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.tsx` | 527 | `NOTE` | ❌ | note: draftNote |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.tsx` | 544 | `NOTE` | ❌ | note: draftNote.trim() |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 17 | `NOTE` | ❌ | note: "Opening sleeve" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 25 | `NOTE` | ❌ | note: "" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 40 | `NOTE` | ❌ | note: "Add-on" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 176 | `NOTE` | ❌ | note: "" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 207 | `NOTE` | ❌ | note: "" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.ts` | 341 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.ts` | 349 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.ts` | 557 | `NOTE` | ❌ | note: buildDraftField({ |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 558 | `NOTE` | ❌ | note: "Realtime subscriptions are stable.", |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 570 | `NOTE` | ❌ | note: "One options-chain backfill is waiting on operator review.", |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/service.ts` | 145 | `NOTE` | ❌ | note: alert.note |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/service.ts` | 227 | `NOTE` | ❌ | note: draft.note?.trim() ? draft.note.trim() : null, |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/storage.test.ts` | 25 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/storage.test.ts` | 44 | `NOTE` | ❌ | note: null |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/storage.ts` | 119 | `NOTE` | ❌ | note: asString(raw.note) ?? null, |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/storage.ts` | 154 | `NOTE` | ❌ | note: asString(raw.note) ?? null |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/types.ts` | 11 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/types.ts` | 30 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/screens/covered-call-screen.view-model.ts` | 284 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/covered-call-screen.view-model.ts` | 987 | `NOTE` | ❌ | note: "Covered-call net curve requires the underlying cost basis which is not yet threaded through the API. The chart shows the short-call leg only." |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.security-master.ts` | 80 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.security-master.ts` | 374 | `NOTE` | ❌ | note: "Matches issuer relations and SEC 8-K references." |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.security-master.ts` | 385 | `NOTE` | ❌ | note: "Awaiting final packet annotation from treasury." |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.security-master.ts` | 396 | `NOTE` | ❌ | note: "Retained for longitudinal identifier reconciliation." |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.security-master.ts` | 597 | `NOTE` | ❌ | note: "Matches paying agent notice and treasury schedule." |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.security-master.ts` | 608 | `NOTE` | ❌ | note: "Historical programme amendment retained for evidence." |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.test.tsx` | 27 | `NOTE` | ❌ | note: "Realtime subscriptions are stable.", |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.test.tsx` | 155 | `NOTE` | ❌ | note: "Backfill pressure is elevated.", |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.view-model.test.ts` | 83 | `NOTE` | ❌ | note: "Realtime subscriptions are stable.", |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.view-model.test.ts` | 97 | `NOTE` | ❌ | note: "Configured with paper API keys.", |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.view-model.test.ts` | 522 | `NOTE` | ❌ | note: "Backfill pressure is elevated.", |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.view-model.test.ts` | 820 | `NOTE` | ❌ | note: "Checkpoint delay exceeded the review threshold.", |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.view-model.ts` | 177 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.view-model.ts` | 1115 | `NOTE` | ❌ | note: provider.note, |
| `src/Meridian.Ui/dashboard/src/screens/governance-screen.view-model.test.ts` | 197 | `NOTE` | ❌ | note: "Coupon posted." |
| `src/Meridian.Ui/dashboard/src/screens/governance-screen.view-model.test.ts` | 217 | `NOTE` | ❌ | note: "Expected-versus-actual variance." |
| `src/Meridian.Ui/dashboard/src/screens/governance-screen.view-model.ts` | 229 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/screens/governance-screen.view-model.ts` | 876 | `NOTE` | ❌ | note: "Semi-annual fixed coupon projected from the reference coupon schedule." |
| `src/Meridian.Ui/dashboard/src/screens/governance-screen.view-model.ts` | 896 | `NOTE` | ❌ | note: "Principal paydown carries a small expected-versus-actual variance for operator review." |
| `src/Meridian.Ui/dashboard/src/screens/governance-screen.view-model.ts` | 916 | `NOTE` | ❌ | note: "Final coupon and principal repayment remain pending until trustee schedule confirmation." |
| `src/Meridian.Ui/dashboard/src/screens/governance-screen.view-model.ts` | 938 | `NOTE` | ❌ | note: "Fixture coupon row used by browser workbench tests." |
| `src/Meridian.Ui/dashboard/src/screens/governance-screen.view-model.ts` | 958 | `NOTE` | ❌ | note: "Fixture amortization row keeps schedule selection deterministic." |
| `src/Meridian.Ui/dashboard/src/screens/operator-readiness-console.view-model.test.ts` | 187 | `NOTE` | ❌ | note: "Ready", |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.test.tsx` | 41 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.test.tsx` | 63 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.test.ts` | 67 | `NOTE` | ❌ | note: "  earnings prep  " |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.test.ts` | 92 | `NOTE` | ❌ | note: "   " |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.test.ts` | 182 | `NOTE` | ❌ | note: null |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.test.ts` | 358 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.test.ts` | 380 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.ts` | 13 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.ts` | 21 | `NOTE` | ❌ | note: "" |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.ts` | 61 | `NOTE` | ❌ | note: PriceAlertStaticFormFieldViewModel; |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.ts` | 510 | `NOTE` | ❌ | note: buildPriceAlertStaticFormField(PRICE_ALERT_STATIC_FIELD_CONFIG.note) |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.ts` | 528 | `NOTE` | ❌ | note: form.note.trim() \|\| null |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.ts` | 600 | `NOTE` | ❌ | note: { |
| `src/Meridian.Ui/dashboard/src/types.ts` | 821 | `NOTE` | ❌ | note: string; |
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
