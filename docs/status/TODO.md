# TODO / FIXME / HACK / NOTE Scan

Total items: **212**

| File | Line | Tag | Linked Issue | Text |
| --- | ---: | --- | :---: | --- |
| `.agents/skills/meridian-code-review/evals/evals.json` | 166 | `TODO` | ❌ | "prompt": "Review this ViewModel and its paired View code-behind together for MVVM compliance:\n\nFile 1: SymbolsViewModel.cs\n```csharp\nusing System.Collections.ObjectModel;\nusing Meridian.Ui.Services;\nusing Meridian.Contracts;\n\nnamespace Meridian.Wpf.ViewModels;\n\npublic class SymbolsViewModel : BindableBase\n{\n    private readonly ISymbolService _symbolService;\n    private ObservableCollection<SymbolStatus> _symbols = new();\n    private string _searchText = string.Empty;\n    private bool _isLoading;\n\n    public SymbolsViewModel(ISymbolService symbolService)\n    {\n        _symbolService = symbolService;\n        LoadSymbolsCommand = new RelayCommand(async _ => await LoadSymbolsAsync());\n        RemoveSymbolCommand = new RelayCommand(async p => await RemoveSymbolAsync((string)p!));\n    }\n\n    public ObservableCollection<SymbolStatus> Symbols\n    {\n        get => _symbols;\n        private set => SetProperty(ref _symbols, value);\n    }\n\n    public string SearchText\n    {\n        get => _searchText;\n        set\n        {\n            SetProperty(ref _searchText, value);\n            FilterSymbols();\n        }\n    }\n\n    public bool IsLoading\n    {\n        get => _isLoading;\n        private set => SetProperty(ref _isLoading, value);\n    }\n\n    public RelayCommand LoadSymbolsCommand { get; }\n    public RelayCommand RemoveSymbolCommand { get; }\n\n    private async Task LoadSymbolsAsync()\n    {\n        IsLoading = true;\n        var symbols = await _symbolService.GetSymbolsAsync();\n        Symbols = new ObservableCollection<SymbolStatus>(symbols);\n        IsLoading = false;\n    }\n\n    private async Task RemoveSymbolAsync(string symbol)\n    {\n        await _symbolService.RemoveSymbolAsync(symbol);\n        var item = _symbols.FirstOrDefault(s => s.Symbol == symbol);\n        if (item != null) _symbols.Remove(item);\n    }\n\n    private void FilterSymbols()\n    {\n        // TODO: implement filtering\n    }\n}\n```\n\nFile 2: SymbolsPage.xaml.cs\n```csharp\nusing System.Windows.Controls;\nusing Meridian.Wpf.ViewModels;\nusing Meridian.Ui.Services;\n\nnamespace Meridian.Wpf.Views;\n\npublic partial class SymbolsPage : Page\n{\n    private readonly SymbolsViewModel _viewModel;\n\n    public SymbolsPage(ISymbolService symbolService)\n    {\n        InitializeComponent();\n        _viewModel = new SymbolsViewModel(symbolService);\n        DataContext = _viewModel;\n        Loaded += async (_, _) => await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n\n    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)\n    {\n        _viewModel.SearchText = ((TextBox)sender).Text;\n    }\n\n    private async void RemoveButton_Click(object sender, System.Windows.RoutedEventArgs e)\n    {\n        var symbol = (string)((System.Windows.FrameworkElement)sender).Tag;\n        await _viewModel._symbolService.RemoveSymbolAsync(symbol);\n        await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n}\n```", |
| `.claude/skills/meridian-code-review/evals/evals.json` | 166 | `TODO` | ❌ | "prompt": "Review this ViewModel and its paired View code-behind together for MVVM compliance:\n\nFile 1: SymbolsViewModel.cs\n```csharp\nusing System.Collections.ObjectModel;\nusing Meridian.Ui.Services;\nusing Meridian.Contracts;\n\nnamespace Meridian.Wpf.ViewModels;\n\npublic class SymbolsViewModel : BindableBase\n{\n    private readonly ISymbolService _symbolService;\n    private ObservableCollection<SymbolStatus> _symbols = new();\n    private string _searchText = string.Empty;\n    private bool _isLoading;\n\n    public SymbolsViewModel(ISymbolService symbolService)\n    {\n        _symbolService = symbolService;\n        LoadSymbolsCommand = new RelayCommand(async _ => await LoadSymbolsAsync());\n        RemoveSymbolCommand = new RelayCommand(async p => await RemoveSymbolAsync((string)p!));\n    }\n\n    public ObservableCollection<SymbolStatus> Symbols\n    {\n        get => _symbols;\n        private set => SetProperty(ref _symbols, value);\n    }\n\n    public string SearchText\n    {\n        get => _searchText;\n        set\n        {\n            SetProperty(ref _searchText, value);\n            FilterSymbols();\n        }\n    }\n\n    public bool IsLoading\n    {\n        get => _isLoading;\n        private set => SetProperty(ref _isLoading, value);\n    }\n\n    public RelayCommand LoadSymbolsCommand { get; }\n    public RelayCommand RemoveSymbolCommand { get; }\n\n    private async Task LoadSymbolsAsync()\n    {\n        IsLoading = true;\n        var symbols = await _symbolService.GetSymbolsAsync();\n        Symbols = new ObservableCollection<SymbolStatus>(symbols);\n        IsLoading = false;\n    }\n\n    private async Task RemoveSymbolAsync(string symbol)\n    {\n        await _symbolService.RemoveSymbolAsync(symbol);\n        var item = _symbols.FirstOrDefault(s => s.Symbol == symbol);\n        if (item != null) _symbols.Remove(item);\n    }\n\n    private void FilterSymbols()\n    {\n        // TODO: implement filtering\n    }\n}\n```\n\nFile 2: SymbolsPage.xaml.cs\n```csharp\nusing System.Windows.Controls;\nusing Meridian.Wpf.ViewModels;\nusing Meridian.Ui.Services;\n\nnamespace Meridian.Wpf.Views;\n\npublic partial class SymbolsPage : Page\n{\n    private readonly SymbolsViewModel _viewModel;\n\n    public SymbolsPage(ISymbolService symbolService)\n    {\n        InitializeComponent();\n        _viewModel = new SymbolsViewModel(symbolService);\n        DataContext = _viewModel;\n        Loaded += async (_, _) => await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n\n    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)\n    {\n        _viewModel.SearchText = ((TextBox)sender).Text;\n    }\n\n    private async void RemoveButton_Click(object sender, System.Windows.RoutedEventArgs e)\n    {\n        var symbol = (string)((System.Windows.FrameworkElement)sender).Tag;\n        await _viewModel._symbolService.RemoveSymbolAsync(symbol);\n        await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n}\n```", |
| `.codex/agents/meridian-cleanup.toml` | 410 | `TODO` | ❌ | - placeholder implementation comments (for example, `// TODO: implement`) in methods that are already implemented.\r |
| `.codex/agents/meridian-cleanup.toml` | 417 | `TODO` | ❌ | - Open-work comments (for example, `// TODO:` or `// FIXME:`) that describe genuine pending tasks — flag them\r |
| `.github/agents/cleanup-agent.md` | 385 | `TODO` | ❌ | - placeholder implementation comments (for example, `// TODO: implement`) in methods that are already implemented. |
| `.github/agents/cleanup-agent.md` | 393 | `TODO` | ❌ | - Open-work comments (for example, `// TODO:` or `// FIXME:`) that describe genuine pending tasks — flag |
| `Meridian Design System/_ds_bundle.js` | 2754 | `NOTE` | ❌ | // Note: caller must provide all IDs; this component only tracks selection state. |
| `Meridian Design System/components/core/MultiSelect.jsx` | 22 | `NOTE` | ❌ | // Note: caller must provide all IDs; this component only tracks selection state. |
| `benchmarks/run-bottleneck-benchmarks.sh` | 111 | `NOTE` | ❌ | # Note: --filter is intentionally not added here; each phase below supplies its own |
| `config/appsettings.sample.json` | 387 | `NOTE` | ❌ | // NOTE: This key is a duplicate of the one near the top of this file for documentation purposes. |
| `config/appsettings.sample.json` | 396 | `NOTE` | ❌ | // NOTE: Credentials are resolved from environment variables - do NOT add them here. |
| `docs/architecture/deterministic-canonicalization.md` | 365 | `NOTE` | ❌ | Note: Polygon does not define buyer-initiated codes. Only ~5% of trades carry definitive aggressor inference. The canonicalization layer preserves `Unknown` as a valid canonical value rather than attempting inference. |
| `docs/architecture/domains.md` | 111 | `NOTE` | ❌ | > Note: not every enum member is currently emitted by the three core collectors (`TradeDataCollector`, `MarketDepthCollector`, `QuoteCollector`); several are used by adapters, backfill paths, or the `L3OrderBookCollector`. |
| `docs/operators/provider-backfill-operations.md` | 89 | `NOTE` | ❌ | > Note: in examples above, use lowercase `dotnet` command. |
| `src/Meridian.Application/Commands/SecurityMasterCommands.cs` | 25 | `NOTE` | ❌ | // NOTE: _importService is null when the Security Master database is not configured at CLI |
| `src/Meridian.Backtesting/Metrics/BacktestMetricsEngine.cs` | 289 | `NOTE` | ❌ | /// NOTE: This is an independent computation over fill events for metric attribution purposes. |
| `src/Meridian.Backtesting/Portfolio/SimulatedPortfolio.cs` | 841 | `NOTE` | ❌ | /// NOTE: This must stay consistent with <c>BacktestMetricsEngine.ComputeRealisedPnl</c>, |
| `src/Meridian.Core/Monitoring/MigrationDiagnostics.cs` | 17 | `NOTE` | ❌ | /// NOTE: This class lives in the Core project (not Application) so that |
| `src/Meridian.Core/Serialization/MarketDataJsonContext.cs` | 173 | `NOTE` | ❌ | /// NOTE: Alpaca payloads use both "T" and "t" keys in the same object. |
| `src/Meridian.Execution/BrokerageServiceRegistration.cs` | 141 | `NOTE` | ❌ | // NOTE: We intentionally use GetRequiredKeyedService here rather than |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 133 | `NOTE` | ❌ | Note: created.ResolutionNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 255 | `NOTE` | ❌ | Note: request.ReviewNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 282 | `NOTE` | ❌ | Note: request.ReviewNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 399 | `NOTE` | ❌ | Note: request.ResolutionNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 528 | `NOTE` | ❌ | Note: request.Note, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 552 | `NOTE` | ❌ | Note: request.Note, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 574 | `NOTE` | ❌ | Note: request.Note, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 784 | `NOTE` | ❌ | Note: note, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 1213 | `NOTE` | ❌ | Note: command.Note, |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 735 | `NOTE` | ❌ | Note: hasParams ? null : "Run was started without a captured parameter set.")); |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 746 | `NOTE` | ❌ | Note: hasPortfolio ? null : "No portfolio seam is associated with this run.")); |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 757 | `NOTE` | ❌ | Note: hasLedger ? null : "No ledger reference is associated with this run.")); |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 768 | `NOTE` | ❌ | Note: hasAudit ? null : "No audit reference was captured for this run.")); |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 782 | `NOTE` | ❌ | Note: promo?.ApprovedBy is not null ? $"Approved by {promo.ApprovedBy}." : null)); |
| `src/Meridian.Ui.Services/Services/AdminMaintenanceModels.cs` | 266 | `NOTE` | ❌ | /// Note: Renamed from RetentionPolicy to avoid conflict with Meridian.Ui.Services.RetentionPolicy |
| `src/Meridian.Ui.Services/Services/AdminMaintenanceModels.cs` | 398 | `NOTE` | ❌ | // NOTE: SelfTest*, ErrorCodes*, ShowConfig*, QuickCheck* models are defined in DiagnosticsService.cs |
| `src/Meridian.Ui.Services/Services/ProviderHealthService.cs` | 530 | `NOTE` | ❌ | // NOTE: ProviderComparison is defined in AdvancedAnalyticsModels.cs for cross-provider comparison |
| `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs` | 32 | `NOTE` | ❌ | // NOTE: GET /schedules, GET /schedules/{id}, POST /schedules, POST /schedules/{id}/enable, |
| `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs` | 117 | `NOTE` | ❌ | // NOTE: POST /schedules/{id}/enable and POST /schedules/{id}/disable are registered |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | 4334 | `NOTE` | ❌ | note: "Paper adapter routing is available.", |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | 4346 | `NOTE` | ❌ | note: "Realtime subscriptions are steady.", |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | 4358 | `NOTE` | ❌ | note: "Replay queue is elevated but within tolerance.", |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | 4470 | `NOTE` | ❌ | Note: note, |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | 4553 | `NOTE` | ❌ | Note: note, |
| `src/Meridian.Ui.Shared/Services/ProviderLedgerReconciliationService.cs` | 1974 | `NOTE` | ❌ | Note: "Provider-ledger reconciliation break signed off.", |
| `src/Meridian.Ui.Shared/Services/ReportPackDeliveryService.cs` | 240 | `NOTE` | ❌ | Note: NormalizeNullable(target.Note) ?? $"Scheduled delivery for {normalizedTemplateId}.", |
| `src/Meridian.Ui.Shared/Services/ReportPackRunReadService.cs` | 2047 | `NOTE` | ❌ | Note: NormalizeOptional(target.Note), |
| `src/Meridian.Ui.Shared/Services/ReportPackRunReadService.cs` | 2101 | `NOTE` | ❌ | Note: NormalizeOptional(target.Note), |
| `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts` | 367 | `NOTE` | ❌ | note: "Heartbeat delayed" |
| `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts` | 718 | `NOTE` | ❌ | note: "Streaming quote path is healthy." |
| `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts` | 793 | `NOTE` | ❌ | note: "Streaming quote path is healthy." |
| `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts` | 1250 | `NOTE` | ❌ | note: "Paper endpoint returned intermittent quote gaps.", |
| `src/Meridian.Ui/dashboard/src/app.test.tsx` | 221 | `NOTE` | ❌ | note: "Paper endpoint returned intermittent quote gaps.", |
| `src/Meridian.Ui/dashboard/src/app.test.tsx` | 379 | `NOTE` | ❌ | note: "Credential check failed" |
| `src/Meridian.Ui/dashboard/src/app.test.tsx` | 629 | `NOTE` | ❌ | note: "Paper endpoint returned intermittent quote gaps.", |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.test.tsx` | 17 | `NOTE` | ❌ | note: "Opening sleeve" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.tsx` | 568 | `NOTE` | ❌ | note: draftNote |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.tsx` | 585 | `NOTE` | ❌ | note: draftNote.trim() |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 17 | `NOTE` | ❌ | note: "Opening sleeve" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 25 | `NOTE` | ❌ | note: "" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 40 | `NOTE` | ❌ | note: "Add-on" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 176 | `NOTE` | ❌ | note: "" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 207 | `NOTE` | ❌ | note: "" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.ts` | 510 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.ts` | 518 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.ts` | 726 | `NOTE` | ❌ | note: buildDraftField({ |
| `src/Meridian.Ui/dashboard/src/lib/api.trading.test.ts` | 896 | `NOTE` | ❌ | note: "Promote ready rows", |
| `src/Meridian.Ui/dashboard/src/lib/api.trading.test.ts` | 906 | `NOTE` | ❌ | note: "Reviewed" |
| `src/Meridian.Ui/dashboard/src/lib/daily-control-tower.test.ts` | 75 | `NOTE` | ❌ | note: "Paper endpoint returned intermittent quote gaps.", |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 682 | `NOTE` | ❌ | note: "Realtime subscriptions are stable.", |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 696 | `NOTE` | ❌ | note: "One options-chain backfill is waiting on operator review.", |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 1348 | `NOTE` | ❌ | note: "Board portal delivery." |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 1354 | `NOTE` | ❌ | note: "Investor email-link delivery." |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 1376 | `NOTE` | ❌ | note: "Board portal delivery.", |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 1400 | `NOTE` | ❌ | note: "Investor email-link delivery.", |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 1505 | `NOTE` | ❌ | note: "pricing-correction" |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 2191 | `NOTE` | ❌ | note: "Fixture handoff retained after identity review.", |
| `src/Meridian.Ui/dashboard/src/lib/notification-center/merge.test.ts` | 47 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/service.ts` | 146 | `NOTE` | ❌ | note: alert.note |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/service.ts` | 248 | `NOTE` | ❌ | note: draft.note?.trim() ? draft.note.trim() : null, |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/storage.test.ts` | 31 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/storage.test.ts` | 50 | `NOTE` | ❌ | note: null |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/storage.ts` | 119 | `NOTE` | ❌ | note: asString(raw.note) ?? null, |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/storage.ts` | 154 | `NOTE` | ❌ | note: asString(raw.note) ?? null |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/types.ts` | 11 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/types.ts` | 30 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/lib/view-state-envelope.test.ts` | 21 | `NOTE` | ❌ | note: "Fónd Δ — ünïcode" |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.test.ts` | 491 | `NOTE` | ❌ | note: "Coupon posted." |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.test.ts` | 511 | `NOTE` | ❌ | note: "Expected-versus-actual variance." |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.test.ts` | 1994 | `NOTE` | ❌ | note: "Expected-versus-actual variance." |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts` | 906 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts` | 2922 | `NOTE` | ❌ | note: "Semi-annual fixed coupon projected from the reference coupon schedule." |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts` | 2942 | `NOTE` | ❌ | note: "Principal paydown carries a small expected-versus-actual variance for operator review." |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts` | 2962 | `NOTE` | ❌ | note: "Final coupon and principal repayment remain pending until trustee schedule confirmation." |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts` | 2984 | `NOTE` | ❌ | note: "Validation coupon row used by browser workbench checks." |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts` | 3004 | `NOTE` | ❌ | note: "Validation amortization row keeps schedule selection consistent." |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts` | 6070 | `NOTE` | ❌ | note: event.sourceReason ?? (event.isCurrentProjection ? "Current schedule projection." : null) |
| `src/Meridian.Ui/dashboard/src/screens/covered-call-screen.view-model.ts` | 287 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/covered-call-screen.view-model.ts` | 990 | `NOTE` | ❌ | note: "Covered-call net curve requires the underlying cost basis which is not yet threaded through the API. The chart shows the short-call leg only." |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 127 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 140 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 436 | `NOTE` | ❌ | note: "Matches issuer relations and SEC 8-K references. Amount amended from $2.75 at board confirmation.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 458 | `NOTE` | ❌ | note: "Awaiting final packet annotation from treasury.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 479 | `NOTE` | ❌ | note: "Retained for longitudinal identifier reconciliation.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 500 | `NOTE` | ❌ | note: "Issuer withdrew the distribution before record date; retained for audit evidence.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 712 | `NOTE` | ❌ | note: "Matches paying agent notice and treasury schedule.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 735 | `NOTE` | ❌ | note: "Historical programme amendment retained for evidence.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 883 | `NOTE` | ❌ | note: seed.note, |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.test.tsx` | 40 | `NOTE` | ❌ | note: "Realtime subscriptions are stable.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.test.tsx` | 439 | `NOTE` | ❌ | note: "Backfill pressure is elevated.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.view-model.test.ts` | 121 | `NOTE` | ❌ | note: "Realtime subscriptions are stable.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.view-model.test.ts` | 137 | `NOTE` | ❌ | note: "Configured with paper API keys.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.view-model.test.ts` | 1219 | `NOTE` | ❌ | note: "Backfill pressure is elevated.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.view-model.test.ts` | 1729 | `NOTE` | ❌ | note: "Checkpoint delay exceeded the review threshold.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.view-model.ts` | 263 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.view-model.ts` | 2366 | `NOTE` | ❌ | note: recommendedActionText ?? providerRecord?.note ?? "No operator action reported", |
| `src/Meridian.Ui/dashboard/src/screens/operations-record-release-screen.view-model.test.ts` | 123 | `NOTE` | ❌ | note: "Ready" |
| `src/Meridian.Ui/dashboard/src/screens/operator-readiness-console.view-model.test.ts` | 191 | `NOTE` | ❌ | note: "Ready", |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.test.tsx` | 41 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.test.tsx` | 63 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.test.ts` | 67 | `NOTE` | ❌ | note: "  earnings prep  " |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.test.ts` | 92 | `NOTE` | ❌ | note: "   " |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.test.ts` | 182 | `NOTE` | ❌ | note: null |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.test.ts` | 358 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.test.ts` | 380 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.ts` | 14 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.ts` | 22 | `NOTE` | ❌ | note: "" |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.ts` | 62 | `NOTE` | ❌ | note: PriceAlertStaticFormFieldViewModel; |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.ts` | 511 | `NOTE` | ❌ | note: buildPriceAlertStaticFormField(PRICE_ALERT_STATIC_FIELD_CONFIG.note) |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.ts` | 529 | `NOTE` | ❌ | note: form.note.trim() \|\| null |
| `src/Meridian.Ui/dashboard/src/screens/price-alerts-screen.view-model.ts` | 601 | `NOTE` | ❌ | note: { |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 697 | `NOTE` | ❌ | note: "Built-in template catalog" |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 705 | `NOTE` | ❌ | note: "Controller approved investor statement baseline." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 1278 | `NOTE` | ❌ | note: "Board package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 2361 | `NOTE` | ❌ | note: "Added exposure columns." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3527 | `NOTE` | ❌ | note: "Approved custom exposure report-writer pack." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3810 | `NOTE` | ❌ | note: "Email link pack." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3891 | `NOTE` | ❌ | note: "Email link pack." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3930 | `NOTE` | ❌ | note: "Board portal pack." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3936 | `NOTE` | ❌ | note: "Email link archive." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3981 | `NOTE` | ❌ | note: "Board portal pack." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3987 | `NOTE` | ❌ | note: "Email link archive." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4023 | `NOTE` | ❌ | note: "Board package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4029 | `NOTE` | ❌ | note: "Investor package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4051 | `NOTE` | ❌ | note: "Board package.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4128 | `NOTE` | ❌ | note: "Investor package.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4152 | `NOTE` | ❌ | note: "Delivered after approval.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4397 | `NOTE` | ❌ | note: "Board portal package cleared." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4711 | `NOTE` | ❌ | note: "Board package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4751 | `NOTE` | ❌ | note: "Board package.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4793 | `NOTE` | ❌ | note: "Delivered after approval.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4832 | `NOTE` | ❌ | note: "Delivery failure recorded from Reporting workspace for Board reporting committee.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4851 | `NOTE` | ❌ | note: "Delivery failure recorded from Reporting workspace for Board reporting committee.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4899 | `NOTE` | ❌ | note: "Board package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4921 | `NOTE` | ❌ | note: "Board package.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4967 | `NOTE` | ❌ | note: "Board package.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 5023 | `NOTE` | ❌ | note: "Board package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 5066 | `NOTE` | ❌ | note: "Board package.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 5219 | `NOTE` | ❌ | note: "pricing-correction" |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.tsx` | 2967 | `NOTE` | ❌ | note: note \|\| null |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.tsx` | 2978 | `NOTE` | ❌ | note: draft.deliveryNote, |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.tsx` | 3725 | `NOTE` | ❌ | note: "Delivered from browser Reporting workspace.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.tsx` | 3748 | `NOTE` | ❌ | note: "Published from browser Reporting workspace." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.tsx` | 3847 | `NOTE` | ❌ | note: `Delivery failure recorded from Reporting workspace for ${attempt.recipient}.`, |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.view-model.test.ts` | 195 | `NOTE` | ❌ | note: "pricing-correction" |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.view-model.test.ts` | 528 | `NOTE` | ❌ | note: "Board package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.view-model.ts` | 384 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.view-model.ts` | 1853 | `NOTE` | ❌ | note: plan.note, |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1329 | `NOTE` | ❌ | note: "Reviewed from the Settings Provider Connection Center runtime evidence panel." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1337 | `NOTE` | ❌ | note: "Marked from the Settings Provider Connection Center for replay after mapping changes." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1347 | `NOTE` | ❌ | note: "Ignored from the Settings Provider Connection Center after operator review." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1469 | `NOTE` | ❌ | note: "Approved after identity review.", |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1569 | `NOTE` | ❌ | note: "Reviewed from the Settings Provider Connection Center runtime evidence panel." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1651 | `NOTE` | ❌ | note: "Approved from the Settings Provider Connection Center promotion readiness panel.", |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1668 | `NOTE` | ❌ | note: "Reviewed from the Settings Provider Connection Center runtime evidence panel." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1715 | `NOTE` | ❌ | note: "Marked from the Settings Provider Connection Center for replay after mapping changes." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1735 | `NOTE` | ❌ | note: "Ignored from the Settings Provider Connection Center after operator review." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.tsx` | 1514 | `NOTE` | ❌ | note: "Approved from the Settings Provider Connection Center promotion readiness panel.", |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.tsx` | 1641 | `NOTE` | ❌ | note: providerRuntimeQuarantineActionNote(action) |
| `src/Meridian.Ui/dashboard/src/screens/w4-acceptance-parity.test.ts` | 429 | `NOTE` | ❌ | note: "Close evidence reviewed." |
| `src/Meridian.Ui/dashboard/src/screens/w4-acceptance-parity.test.ts` | 437 | `NOTE` | ❌ | note: "Published to investor portal." |
| `src/Meridian.Ui/dashboard/src/types.ts` | 3975 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/types.ts` | 5120 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/types.ts` | 5473 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/types.ts` | 5522 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/types.ts` | 5719 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Wpf/GlobalUsings.cs` | 7 | `NOTE` | ❌ | // NOTE: Type aliases and Contracts namespaces are NOT re-defined here because |
| `src/Meridian.Wpf/ViewModels/SecurityPassportEditorViewModel.cs` | 262 | `NOTE` | ❌ | Note: null, |
| `tests/Meridian.Tests/Application/Backfill/BackfillWorkerServiceTests.cs` | 28 | `NOTE` | ❌ | // NOTE: Using null! because validation throws before dependencies are accessed |
| `tests/Meridian.Tests/Application/Backfill/BackfillWorkerServiceTests.cs` | 56 | `NOTE` | ❌ | // NOTE: Using null! because validation throws before dependencies are accessed |
| `tests/Meridian.Tests/Application/Backfill/BackfillWorkerServiceTests.cs` | 86 | `NOTE` | ❌ | // NOTE: Using null! dependencies - we only verify that ArgumentOutOfRangeException is not thrown |
| `tests/Meridian.Tests/Application/Pipeline/FSharpEventValidatorTests.cs` | 72 | `NOTE` | ❌ | // Note: Trade.ctor only checks Price > 0, so $2,000,000 is constructible. |
| `tests/Meridian.Tests/DataIntegration/Monitoring/DataQuality/DataFreshnessSlaMonitorTests.cs` | 525 | `NOTE` | ❌ | // NOTE: Actual result depends on current time, so we check the logic is working |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 227 | `NOTE` | ❌ | Note: "Ready for review."); |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 253 | `NOTE` | ❌ | Note: "Submit through gate.", |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 282 | `NOTE` | ❌ | Note: "Submit.", |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 307 | `NOTE` | ❌ | Note: "Submit.", |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 331 | `NOTE` | ❌ | Note: "Submit through gate.", |
| `tests/Meridian.Tests/Storage/StorageChecksumServiceTests.cs` | 121 | `NOTE` | ❌ | // NOTE: File.WriteAllTextAsync uses UTF-8 with BOM by default on some platforms, |
| `tests/Meridian.Tests/Strategies/ReconciliationBreakQueueRepositoryTests.cs` | 374 | `NOTE` | ❌ | Note: "Automation suggested reopened-case resolution.", |
| `tests/Meridian.Tests/Ui/EvidenceWorkflowFabricTests.cs` | 4098 | `NOTE` | ❌ | Note: "Delivered after approval.", |
| `tests/Meridian.Tests/Ui/FundOpsCloseLaneScenarioTests.cs` | 418 | `NOTE` | ❌ | // Note: Status derives as ApprovalPending once all four gates are clean, which is expected |
| `tests/Meridian.Tests/Ui/ReportPackWorkflowServiceTests.cs` | 112 | `NOTE` | ❌ | Note: "Approved by controller.", |
| `tests/Meridian.Tests/Ui/ReportPackWorkflowServiceTests.cs` | 1757 | `NOTE` | ❌ | Note: "Delivered after restatement.", |
| `tests/Meridian.Tests/Ui/SecurityMasterWorkbenchEndpointsTests.cs` | 274 | `NOTE` | ❌ | Note: "ready", |
| `tests/Meridian.Tests/Ui/WorkstationFinancialRecordExplorerEndpointTests.cs` | 179 | `NOTE` | ❌ | Note: "Board pack delivered with retained evidence graph.", |
| `tests/Meridian.Ui.Tests/Services/DiagnosticsServiceTests.cs` | 9 | `NOTE` | ❌ | /// Note: The service methods require a running backend (ApiClientService), |
| `tests/Meridian.Ui.Tests/Services/ScheduledMaintenanceServiceTests.cs` | 85 | `NOTE` | ❌ | // NOTE: since this is a singleton shared across tests, if StartScheduler was |
| `tests/Meridian.Ui.Tests/Services/StorageAnalyticsServiceTests.cs` | 9 | `NOTE` | ❌ | /// Note: Full analytics calculation requires file I/O, so these tests |
| `tests/Meridian.Wpf.Tests/Services/OfflineTrackingPersistenceServiceTests.cs` | 27 | `NOTE` | ❌ | // NOTE: Singleton state may persist across tests. |
| `tests/Meridian.Wpf.Tests/Services/PendingOperationsQueueServiceTests.cs` | 30 | `NOTE` | ❌ | // NOTE: This may not be false if other tests have run InitializeAsync. |
| `tools/actionlint/docs/config.md` | 14 | `NOTE` | ❌ | Note: If you're using [Super-Linter][], the file should be placed in a different directory. Please check the project's document. |
