# TODO / FIXME / HACK / NOTE Scan

Total items: **239**

| File | Line | Tag | Linked Issue | Text |
| --- | ---: | --- | :---: | --- |
| `.agents/skills/meridian-code-review/evals/evals.json` | 166 | `TODO` | ❌ | "prompt": "Review this ViewModel and its paired View code-behind together for MVVM compliance:\n\nFile 1: SymbolsViewModel.cs\n```csharp\nusing System.Collections.ObjectModel;\nusing Meridian.Ui.Services;\nusing Meridian.Contracts;\n\nnamespace Meridian.Wpf.ViewModels;\n\npublic class SymbolsViewModel : BindableBase\n{\n    private readonly ISymbolService _symbolService;\n    private ObservableCollection<SymbolStatus> _symbols = new();\n    private string _searchText = string.Empty;\n    private bool _isLoading;\n\n    public SymbolsViewModel(ISymbolService symbolService)\n    {\n        _symbolService = symbolService;\n        LoadSymbolsCommand = new RelayCommand(async _ => await LoadSymbolsAsync());\n        RemoveSymbolCommand = new RelayCommand(async p => await RemoveSymbolAsync((string)p!));\n    }\n\n    public ObservableCollection<SymbolStatus> Symbols\n    {\n        get => _symbols;\n        private set => SetProperty(ref _symbols, value);\n    }\n\n    public string SearchText\n    {\n        get => _searchText;\n        set\n        {\n            SetProperty(ref _searchText, value);\n            FilterSymbols();\n        }\n    }\n\n    public bool IsLoading\n    {\n        get => _isLoading;\n        private set => SetProperty(ref _isLoading, value);\n    }\n\n    public RelayCommand LoadSymbolsCommand { get; }\n    public RelayCommand RemoveSymbolCommand { get; }\n\n    private async Task LoadSymbolsAsync()\n    {\n        IsLoading = true;\n        var symbols = await _symbolService.GetSymbolsAsync();\n        Symbols = new ObservableCollection<SymbolStatus>(symbols);\n        IsLoading = false;\n    }\n\n    private async Task RemoveSymbolAsync(string symbol)\n    {\n        await _symbolService.RemoveSymbolAsync(symbol);\n        var item = _symbols.FirstOrDefault(s => s.Symbol == symbol);\n        if (item != null) _symbols.Remove(item);\n    }\n\n    private void FilterSymbols()\n    {\n        // TODO: implement filtering\n    }\n}\n```\n\nFile 2: SymbolsPage.xaml.cs\n```csharp\nusing System.Windows.Controls;\nusing Meridian.Wpf.ViewModels;\nusing Meridian.Ui.Services;\n\nnamespace Meridian.Wpf.Views;\n\npublic partial class SymbolsPage : Page\n{\n    private readonly SymbolsViewModel _viewModel;\n\n    public SymbolsPage(ISymbolService symbolService)\n    {\n        InitializeComponent();\n        _viewModel = new SymbolsViewModel(symbolService);\n        DataContext = _viewModel;\n        Loaded += async (_, _) => await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n\n    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)\n    {\n        _viewModel.SearchText = ((TextBox)sender).Text;\n    }\n\n    private async void RemoveButton_Click(object sender, System.Windows.RoutedEventArgs e)\n    {\n        var symbol = (string)((System.Windows.FrameworkElement)sender).Tag;\n        await _viewModel._symbolService.RemoveSymbolAsync(symbol);\n        await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n}\n```", |
| `.claude/skills/meridian-code-review/evals/evals.json` | 166 | `TODO` | ❌ | "prompt": "Review this ViewModel and its paired View code-behind together for MVVM compliance:\n\nFile 1: SymbolsViewModel.cs\n```csharp\nusing System.Collections.ObjectModel;\nusing Meridian.Ui.Services;\nusing Meridian.Contracts;\n\nnamespace Meridian.Wpf.ViewModels;\n\npublic class SymbolsViewModel : BindableBase\n{\n    private readonly ISymbolService _symbolService;\n    private ObservableCollection<SymbolStatus> _symbols = new();\n    private string _searchText = string.Empty;\n    private bool _isLoading;\n\n    public SymbolsViewModel(ISymbolService symbolService)\n    {\n        _symbolService = symbolService;\n        LoadSymbolsCommand = new RelayCommand(async _ => await LoadSymbolsAsync());\n        RemoveSymbolCommand = new RelayCommand(async p => await RemoveSymbolAsync((string)p!));\n    }\n\n    public ObservableCollection<SymbolStatus> Symbols\n    {\n        get => _symbols;\n        private set => SetProperty(ref _symbols, value);\n    }\n\n    public string SearchText\n    {\n        get => _searchText;\n        set\n        {\n            SetProperty(ref _searchText, value);\n            FilterSymbols();\n        }\n    }\n\n    public bool IsLoading\n    {\n        get => _isLoading;\n        private set => SetProperty(ref _isLoading, value);\n    }\n\n    public RelayCommand LoadSymbolsCommand { get; }\n    public RelayCommand RemoveSymbolCommand { get; }\n\n    private async Task LoadSymbolsAsync()\n    {\n        IsLoading = true;\n        var symbols = await _symbolService.GetSymbolsAsync();\n        Symbols = new ObservableCollection<SymbolStatus>(symbols);\n        IsLoading = false;\n    }\n\n    private async Task RemoveSymbolAsync(string symbol)\n    {\n        await _symbolService.RemoveSymbolAsync(symbol);\n        var item = _symbols.FirstOrDefault(s => s.Symbol == symbol);\n        if (item != null) _symbols.Remove(item);\n    }\n\n    private void FilterSymbols()\n    {\n        // TODO: implement filtering\n    }\n}\n```\n\nFile 2: SymbolsPage.xaml.cs\n```csharp\nusing System.Windows.Controls;\nusing Meridian.Wpf.ViewModels;\nusing Meridian.Ui.Services;\n\nnamespace Meridian.Wpf.Views;\n\npublic partial class SymbolsPage : Page\n{\n    private readonly SymbolsViewModel _viewModel;\n\n    public SymbolsPage(ISymbolService symbolService)\n    {\n        InitializeComponent();\n        _viewModel = new SymbolsViewModel(symbolService);\n        DataContext = _viewModel;\n        Loaded += async (_, _) => await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n\n    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)\n    {\n        _viewModel.SearchText = ((TextBox)sender).Text;\n    }\n\n    private async void RemoveButton_Click(object sender, System.Windows.RoutedEventArgs e)\n    {\n        var symbol = (string)((System.Windows.FrameworkElement)sender).Tag;\n        await _viewModel._symbolService.RemoveSymbolAsync(symbol);\n        await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n}\n```", |
| `.codex/agents/meridian-cleanup.toml` | 410 | `TODO` | ❌ | - placeholder implementation comments (for example, `// TODO: implement`) in methods that are already implemented.\r |
| `.codex/agents/meridian-cleanup.toml` | 417 | `TODO` | ❌ | - Open-work comments (for example, `// TODO:` or `// FIXME:`) that describe genuine pending tasks — flag them\r |
| `.github/agents/cleanup-agent.md` | 385 | `TODO` | ❌ | - placeholder implementation comments (for example, `// TODO: implement`) in methods that are already implemented. |
| `.github/agents/cleanup-agent.md` | 393 | `TODO` | ❌ | - Open-work comments (for example, `// TODO:` or `// FIXME:`) that describe genuine pending tasks — flag |
| `CLAUDE.md` | 140 | `NOTE` | ❌ | > Note: the out-of-process Chief of Staff (CoS) ADK runtime that previously owned this heavy-duty |
| `Meridian Design System/components/core/ContextMenu.d.ts` | 68 | `NOTE` | ❌ | * NOTE: bundle-internal — like `useToast` / `useTableState`, this lowercase helper is NOT on the |
| `Meridian Design System/components/core/MultiSelect.jsx` | 23 | `NOTE` | ❌ | // Note: caller must provide all IDs; this component only tracks selection state. |
| `Meridian Design System/tests/test_design_system_governance.py` | 28 | `NOTE` | ❌ | # NOTE: this exercises the real, current tree (ROOT), not a synthetic fixture. If this |
| `benchmarks/run-bottleneck-benchmarks.sh` | 111 | `NOTE` | ❌ | # Note: --filter is intentionally not added here; each phase below supplies its own |
| `config/appsettings.sample.json` | 397 | `NOTE` | ❌ | // NOTE: This key is a duplicate of the one near the top of this file for documentation purposes. |
| `config/appsettings.sample.json` | 406 | `NOTE` | ❌ | // NOTE: Credentials are resolved from environment variables - do NOT add them here. |
| `docs/architecture/deterministic-canonicalization.md` | 365 | `NOTE` | ❌ | Note: Polygon does not define buyer-initiated codes. Only ~5% of trades carry definitive aggressor inference. The canonicalization layer preserves `Unknown` as a valid canonical value rather than attempting inference. |
| `docs/architecture/domains.md` | 111 | `NOTE` | ❌ | > Note: not every enum member is currently emitted by the three core collectors (`TradeDataCollector`, `MarketDepthCollector`, `QuoteCollector`); several are used by adapters, backfill paths, or the `L3OrderBookCollector`. |
| `docs/operators/provider-backfill-operations.md` | 89 | `NOTE` | ❌ | > Note: in examples above, use lowercase `dotnet` command. |
| `src/Meridian.Application/Commands/SecurityMasterCommands.cs` | 25 | `NOTE` | ❌ | // NOTE: _importService is null when the Security Master database is not configured at CLI |
| `src/Meridian.Backtesting/Metrics/BacktestMetricsEngine.cs` | 314 | `NOTE` | ❌ | /// NOTE: This is an independent computation over fill events for metric attribution purposes. |
| `src/Meridian.Backtesting/Portfolio/SimulatedPortfolio.cs` | 854 | `NOTE` | ❌ | /// NOTE: This must stay consistent with <c>BacktestMetricsEngine.ComputeRealisedPnl</c>, |
| `src/Meridian.Core/Monitoring/MigrationDiagnostics.cs` | 17 | `NOTE` | ❌ | /// NOTE: This class lives in the Core project (not Application) so that |
| `src/Meridian.Core/Serialization/MarketDataJsonContext.cs` | 176 | `NOTE` | ❌ | /// NOTE: Alpaca payloads use both "T" and "t" keys in the same object. |
| `src/Meridian.Execution/BrokerageServiceRegistration.cs` | 283 | `NOTE` | ❌ | // NOTE: We intentionally use GetRequiredKeyedService here rather than |
| `src/Meridian.Reporting/ReportingGovernanceService.cs` | 100 | `NOTE` | ❌ | note: ReportingGovernanceCanonicalValidation.BuildInitialRunAuditNote(run), |
| `src/Meridian.Reporting/ReportingGovernanceService.cs` | 588 | `NOTE` | ❌ | note: ReportingGovernanceCanonicalValidation.BuildRestatementDraftAuditNote(draftRun), |
| `src/Meridian.Reporting/ReportingGovernanceService.cs` | 617 | `NOTE` | ❌ | note: ReportingGovernanceCanonicalValidation.BuildRestatementApprovalAuditNote(approvedRequest), |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.Casework.cs` | 187 | `NOTE` | ❌ | Note: note, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.Casework.cs` | 267 | `NOTE` | ❌ | Note: request.ReviewNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.Casework.cs` | 283 | `NOTE` | ❌ | Note: request.ResolutionNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.Casework.cs` | 888 | `NOTE` | ❌ | Note: request.Note, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.Casework.cs` | 946 | `NOTE` | ❌ | Note: request.Note, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.Casework.cs` | 976 | `NOTE` | ❌ | Note: command.Note, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.Casework.cs` | 1626 | `NOTE` | ❌ | Note: command.Note, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 260 | `NOTE` | ❌ | Note: exactReplay |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 303 | `NOTE` | ❌ | Note: created.ResolutionNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 427 | `NOTE` | ❌ | Note: $"Re-keyed reconciliation case from superseded break id '{previousBreakId}' to '{migrated.BreakId}' after a statement fingerprint-input change.", |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 552 | `NOTE` | ❌ | Note: normalized.ResolutionNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 606 | `NOTE` | ❌ | Note: "Reconciliation case deleted from the active queue; audit evidence remains retained.", |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 733 | `NOTE` | ❌ | Note: request.ReviewNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 762 | `NOTE` | ❌ | Note: request.ReviewNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 982 | `NOTE` | ❌ | Note: request.ResolutionNote, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 1376 | `NOTE` | ❌ | Note: request.Note, |
| `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` | 1418 | `NOTE` | ❌ | Note: request.Note, |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 1221 | `NOTE` | ❌ | Note: hasParams ? null : "Run was started without a captured parameter set.")); |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 1232 | `NOTE` | ❌ | Note: hasPortfolio ? null : "No portfolio seam is associated with this run.")); |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 1243 | `NOTE` | ❌ | Note: hasLedger ? null : "No ledger reference is associated with this run.")); |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 1254 | `NOTE` | ❌ | Note: hasAudit ? null : "No audit reference was captured for this run.")); |
| `src/Meridian.Strategies/Services/StrategyRunReadService.cs` | 1268 | `NOTE` | ❌ | Note: promo?.ApprovedBy is not null ? $"Approved by {promo.ApprovedBy}." : null)); |
| `src/Meridian.Ui.Services/Services/AdminMaintenanceModels.cs` | 266 | `NOTE` | ❌ | /// Note: Renamed from RetentionPolicy to avoid conflict with Meridian.Ui.Services.RetentionPolicy |
| `src/Meridian.Ui.Services/Services/AdminMaintenanceModels.cs` | 398 | `NOTE` | ❌ | // NOTE: SelfTest*, ErrorCodes*, ShowConfig*, QuickCheck* models are defined in DiagnosticsService.cs |
| `src/Meridian.Ui.Services/Services/ProviderHealthService.cs` | 476 | `NOTE` | ❌ | // NOTE: ProviderComparison is defined in AdvancedAnalyticsModels.cs for cross-provider comparison |
| `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs` | 33 | `NOTE` | ❌ | // NOTE: GET /schedules, GET /schedules/{id}, POST /schedules, POST /schedules/{id}/enable, |
| `src/Meridian.Ui.Shared/Endpoints/ArchiveMaintenanceEndpoints.cs` | 116 | `NOTE` | ❌ | // NOTE: POST /schedules/{id}/enable and POST /schedules/{id}/disable are registered |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.DataProviders.cs` | 136 | `NOTE` | ❌ | Note: note, |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.StatementCaseworkAuthority.cs` | 110 | `NOTE` | ❌ | Note: request.ResolutionNote, |
| `src/Meridian.Ui.Shared/Services/MarginControlCenterReadService.cs` | 103 | `NOTE` | ❌ | Note: request.Note.Trim(), |
| `src/Meridian.Ui.Shared/Services/ReportPackDeliveryService.cs` | 819 | `NOTE` | ❌ | Note: NormalizeOptional(target.Note) ?? $"Scheduled delivery for {normalizedTemplateId}.", |
| `src/Meridian.Ui.Shared/Services/ReportPackRunReadService.cs` | 2044 | `NOTE` | ❌ | Note: NormalizeOptional(target.Note), |
| `src/Meridian.Ui.Shared/Services/ReportPackRunReadService.cs` | 2098 | `NOTE` | ❌ | Note: NormalizeOptional(target.Note), |
| `src/Meridian.Ui.Shared/Services/StatementReconciliationCaseworkHandoffService.cs` | 144 | `NOTE` | ❌ | Note: request.Note, |
| `src/Meridian.Ui.Shared/Services/StatementReconciliationCaseworkHandoffService.cs` | 299 | `NOTE` | ❌ | Note: "Clear the durable statement casework evidence-handoff obligation.", |
| `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts` | 387 | `NOTE` | ❌ | note: "Heartbeat delayed" |
| `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts` | 455 | `NOTE` | ❌ | note: "Credential verification failed." |
| `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts` | 852 | `NOTE` | ❌ | note: "Streaming quote path is healthy." |
| `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts` | 927 | `NOTE` | ❌ | note: "Streaming quote path is healthy." |
| `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts` | 1382 | `NOTE` | ❌ | note: "Paper endpoint returned intermittent quote gaps.", |
| `src/Meridian.Ui/dashboard/src/app.test.tsx` | 250 | `NOTE` | ❌ | note: "Paper endpoint returned intermittent quote gaps.", |
| `src/Meridian.Ui/dashboard/src/app.test.tsx` | 583 | `NOTE` | ❌ | note: "Credential check failed" |
| `src/Meridian.Ui/dashboard/src/app.test.tsx` | 844 | `NOTE` | ❌ | note: "Paper endpoint returned intermittent quote gaps.", |
| `src/Meridian.Ui/dashboard/src/components/data/skeleton.tsx` | 5 | `NOTE` | ❌ | // NOTE: a second, Tailwind-based Skeleton family lives in `components/ui/skeleton.tsx` |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.test.tsx` | 23 | `NOTE` | ❌ | note: "Opening sleeve" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.test.tsx` | 151 | `NOTE` | ❌ | note: "operator note" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.tsx` | 598 | `NOTE` | ❌ | note: draftNote |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.tsx` | 615 | `NOTE` | ❌ | note: draftNote.trim() |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 17 | `NOTE` | ❌ | note: "Opening sleeve" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 25 | `NOTE` | ❌ | note: "" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 40 | `NOTE` | ❌ | note: "Add-on" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 176 | `NOTE` | ❌ | note: "" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.test.ts` | 207 | `NOTE` | ❌ | note: "" |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.ts` | 510 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.ts` | 518 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.view-model.ts` | 726 | `NOTE` | ❌ | note: buildDraftField({ |
| `src/Meridian.Ui/dashboard/src/components/ui/skeleton.tsx` | 3 | `NOTE` | ❌ | // NOTE: a second, CSS-in-JS Skeleton family lives in `components/data/skeleton.tsx` as |
| `src/Meridian.Ui/dashboard/src/lib/api.trading.test.ts` | 923 | `NOTE` | ❌ | note: "Promote ready rows", |
| `src/Meridian.Ui/dashboard/src/lib/api.trading.test.ts` | 933 | `NOTE` | ❌ | note: "Reviewed" |
| `src/Meridian.Ui/dashboard/src/lib/daily-control-tower.test.ts` | 75 | `NOTE` | ❌ | note: "Paper endpoint returned intermittent quote gaps.", |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 674 | `NOTE` | ❌ | note: "Realtime subscriptions are stable.", |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 688 | `NOTE` | ❌ | note: "One options-chain backfill is waiting on operator review.", |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 1340 | `NOTE` | ❌ | note: "Board portal delivery." |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 1346 | `NOTE` | ❌ | note: "Investor email-link delivery." |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 1368 | `NOTE` | ❌ | note: "Board portal delivery.", |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 1392 | `NOTE` | ❌ | note: "Investor email-link delivery.", |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 1497 | `NOTE` | ❌ | note: "pricing-correction" |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 2201 | `NOTE` | ❌ | note: "Fixture handoff retained after identity review.", |
| `src/Meridian.Ui/dashboard/src/lib/notification-center/merge.test.ts` | 47 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/service.ts` | 147 | `NOTE` | ❌ | note: alert.note |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/service.ts` | 249 | `NOTE` | ❌ | note: draft.note?.trim() ? draft.note.trim() : null, |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/storage.test.ts` | 31 | `NOTE` | ❌ | note: null, |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/storage.test.ts` | 50 | `NOTE` | ❌ | note: null |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/storage.ts` | 119 | `NOTE` | ❌ | note: asString(raw.note) ?? null, |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/storage.ts` | 154 | `NOTE` | ❌ | note: asString(raw.note) ?? null |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/types.ts` | 11 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/lib/price-alerts/types.ts` | 30 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/lib/security-schedule-dev-fixtures.ts` | 29 | `NOTE` | ❌ | note: "Semi-annual fixed coupon projected from the reference coupon schedule." |
| `src/Meridian.Ui/dashboard/src/lib/security-schedule-dev-fixtures.ts` | 49 | `NOTE` | ❌ | note: "Principal paydown carries a small expected-versus-actual variance for operator review." |
| `src/Meridian.Ui/dashboard/src/lib/security-schedule-dev-fixtures.ts` | 69 | `NOTE` | ❌ | note: "Final coupon and principal repayment remain pending until trustee schedule confirmation." |
| `src/Meridian.Ui/dashboard/src/lib/security-schedule-dev-fixtures.ts` | 91 | `NOTE` | ❌ | note: "Validation coupon row used by browser workbench checks." |
| `src/Meridian.Ui/dashboard/src/lib/security-schedule-dev-fixtures.ts` | 111 | `NOTE` | ❌ | note: "Validation amortization row keeps schedule selection consistent." |
| `src/Meridian.Ui/dashboard/src/lib/view-state-envelope.test.ts` | 21 | `NOTE` | ❌ | note: "Fónd Δ — ünïcode" |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.test.ts` | 516 | `NOTE` | ❌ | note: "Coupon posted." |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.test.ts` | 536 | `NOTE` | ❌ | note: "Expected-versus-actual variance." |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.test.ts` | 2103 | `NOTE` | ❌ | note: "Expected-versus-actual variance." |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts` | 916 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts` | 6059 | `NOTE` | ❌ | note: event.sourceReason ?? (event.isCurrentProjection ? "Current schedule projection." : null) |
| `src/Meridian.Ui/dashboard/src/screens/covered-call-screen.view-model.ts` | 304 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/covered-call-screen.view-model.ts` | 1111 | `NOTE` | ❌ | note: "Covered-call net curve requires the underlying cost basis which is not yet threaded through the API. The chart shows the short-call leg only." |
| `src/Meridian.Ui/dashboard/src/screens/daily-control-tower-screen.test.tsx` | 76 | `NOTE` | ❌ | note: "Paper endpoint returned intermittent quote gaps.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 128 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 141 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 437 | `NOTE` | ❌ | note: "Matches issuer relations and SEC 8-K references. Amount amended from $2.75 at board confirmation.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 459 | `NOTE` | ❌ | note: "Awaiting final packet annotation from treasury.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 480 | `NOTE` | ❌ | note: "Retained for longitudinal identifier reconciliation.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 501 | `NOTE` | ❌ | note: "Issuer withdrew the distribution before record date; retained for audit evidence.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 713 | `NOTE` | ❌ | note: "Matches paying agent notice and treasury schedule.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 736 | `NOTE` | ❌ | note: "Historical programme amendment retained for evidence.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.security-master.ts` | 883 | `NOTE` | ❌ | note: seed.note, |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.test.tsx` | 41 | `NOTE` | ❌ | note: "Realtime subscriptions are stable.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.test.tsx` | 541 | `NOTE` | ❌ | note: "Backfill pressure is elevated.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.view-model.test.ts` | 125 | `NOTE` | ❌ | note: "Realtime subscriptions are stable.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.view-model.test.ts` | 141 | `NOTE` | ❌ | note: "Configured with paper API keys.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.view-model.test.ts` | 1570 | `NOTE` | ❌ | note: "Backfill pressure is elevated.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.view-model.test.ts` | 2080 | `NOTE` | ❌ | note: "Checkpoint delay exceeded the review threshold.", |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.view-model.ts` | 393 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/screens/data-screen.view-model.ts` | 2384 | `NOTE` | ❌ | note: recommendedActionText ?? providerRecord?.note ?? "No operator action reported", |
| `src/Meridian.Ui/dashboard/src/screens/margin-control-center-screen.test.tsx` | 109 | `NOTE` | ❌ | note: "Reviewed provider statement and position contributions.", |
| `src/Meridian.Ui/dashboard/src/screens/margin-control-center-screen.test.tsx` | 135 | `NOTE` | ❌ | note: "Reviewed provider statement and position contributions." |
| `src/Meridian.Ui/dashboard/src/screens/operations-record-release-screen.test.tsx` | 44 | `NOTE` | ❌ | note: "Ready" |
| `src/Meridian.Ui/dashboard/src/screens/operations-record-release-screen.view-model.test.ts` | 129 | `NOTE` | ❌ | note: "Ready" |
| `src/Meridian.Ui/dashboard/src/screens/operator-readiness-console.view-model.test.ts` | 192 | `NOTE` | ❌ | note: "Ready", |
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
| `src/Meridian.Ui/dashboard/src/screens/report-run-governance-screen.test.tsx` | 531 | `NOTE` | ❌ | note: "Certified run created.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.schedule-view-model.ts` | 205 | `NOTE` | ❌ | note: plan.note, |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 724 | `NOTE` | ❌ | note: "Built-in template catalog" |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 732 | `NOTE` | ❌ | note: "Controller approved investor statement baseline." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 818 | `NOTE` | ❌ | note: "Starter target" |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 1356 | `NOTE` | ❌ | note: "Board package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 2166 | `NOTE` | ❌ | note: "Added exposure columns." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3465 | `NOTE` | ❌ | note: "Email link pack." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3641 | `NOTE` | ❌ | note: "Email link pack." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3682 | `NOTE` | ❌ | note: "Board portal pack." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3690 | `NOTE` | ❌ | note: "Email link archive." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3752 | `NOTE` | ❌ | note: "Board portal pack." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3760 | `NOTE` | ❌ | note: "Email link archive." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3810 | `NOTE` | ❌ | note: "Board package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3816 | `NOTE` | ❌ | note: "Investor package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3888 | `NOTE` | ❌ | note: "Board package.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3965 | `NOTE` | ❌ | note: "Investor package.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 3989 | `NOTE` | ❌ | note: "Delivered after approval.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4234 | `NOTE` | ❌ | note: "Board portal package cleared." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4564 | `NOTE` | ❌ | note: "Board package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4604 | `NOTE` | ❌ | note: "Board package.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4645 | `NOTE` | ❌ | note: "Delivered after approval.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4717 | `NOTE` | ❌ | note: "Board package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4739 | `NOTE` | ❌ | note: "Board package.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4785 | `NOTE` | ❌ | note: "Board package.", |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.test.tsx` | 4987 | `NOTE` | ❌ | note: "pricing-correction" |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.tsx` | 2770 | `NOTE` | ❌ | note: note \|\| null |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.tsx` | 2783 | `NOTE` | ❌ | note: draft.deliveryNote, |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.view-model.test.ts` | 203 | `NOTE` | ❌ | note: "pricing-correction" |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.view-model.test.ts` | 488 | `NOTE` | ❌ | note: "Starter target" |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.view-model.test.ts` | 619 | `NOTE` | ❌ | note: "Board package." |
| `src/Meridian.Ui/dashboard/src/screens/reporting-screen.view-model.ts` | 524 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1664 | `NOTE` | ❌ | note: "Reviewed from the Settings Provider Connection Center runtime evidence panel." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1672 | `NOTE` | ❌ | note: "Marked from the Settings Provider Connection Center for replay after mapping changes." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1682 | `NOTE` | ❌ | note: "Ignored from the Settings Provider Connection Center after operator review." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1804 | `NOTE` | ❌ | note: "Approved after identity review.", |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1904 | `NOTE` | ❌ | note: "Reviewed from the Settings Provider Connection Center runtime evidence panel." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 1989 | `NOTE` | ❌ | note: "Approved from the Settings Provider Connection Center promotion readiness panel.", |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 2007 | `NOTE` | ❌ | note: "Reviewed from the Settings Provider Connection Center runtime evidence panel." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 2057 | `NOTE` | ❌ | note: "Marked from the Settings Provider Connection Center for replay after mapping changes." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.test.tsx` | 2078 | `NOTE` | ❌ | note: "Ignored from the Settings Provider Connection Center after operator review." |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.tsx` | 1474 | `NOTE` | ❌ | note: "Approved from the Settings Provider Connection Center promotion readiness panel.", |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.tsx` | 1601 | `NOTE` | ❌ | note: providerRuntimeQuarantineActionNote(action) |
| `src/Meridian.Ui/dashboard/src/screens/w4-acceptance-parity.test.ts` | 429 | `NOTE` | ❌ | note: "Close evidence reviewed." |
| `src/Meridian.Ui/dashboard/src/screens/w4-acceptance-parity.test.ts` | 437 | `NOTE` | ❌ | note: "Published to investor portal." |
| `src/Meridian.Ui/dashboard/src/types/reporting-governance.ts` | 124 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/types/workstation-3.ts` | 1066 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/types/workstation-4.ts` | 193 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Ui/dashboard/src/types/workstation-4.ts` | 819 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/types/workstation-4.ts` | 1175 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/types/workstation-4.ts` | 1269 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Ui/dashboard/src/types/workstation-4.ts` | 1610 | `NOTE` | ❌ | note: string \| null; |
| `src/Meridian.Wpf/GlobalUsings.cs` | 7 | `NOTE` | ❌ | // NOTE: Type aliases and Contracts namespaces are NOT re-defined here because |
| `src/Meridian.Wpf/ViewModels/SecurityPassportEditorViewModel.cs` | 262 | `NOTE` | ❌ | Note: null, |
| `tests/Meridian.QuantScript.Tests/workers/quant-script/Meridian.Core.xml` | 3203 | `NOTE` | ❌ | NOTE: This class lives in the Core project (not Application) so that |
| `tests/Meridian.QuantScript.Tests/workers/quant-script/Meridian.Core.xml` | 4997 | `NOTE` | ❌ | NOTE: Alpaca payloads use both "T" and "t" keys in the same object. |
| `tests/Meridian.Tests/Application/Backfill/BackfillWorkerServiceTests.cs` | 28 | `NOTE` | ❌ | // NOTE: Using null! because validation throws before dependencies are accessed |
| `tests/Meridian.Tests/Application/Backfill/BackfillWorkerServiceTests.cs` | 56 | `NOTE` | ❌ | // NOTE: Using null! because validation throws before dependencies are accessed |
| `tests/Meridian.Tests/Application/Backfill/BackfillWorkerServiceTests.cs` | 86 | `NOTE` | ❌ | // NOTE: Using null! dependencies - we only verify that ArgumentOutOfRangeException is not thrown |
| `tests/Meridian.Tests/Application/Pipeline/FSharpEventValidatorTests.cs` | 72 | `NOTE` | ❌ | // Note: Trade.ctor only checks Price > 0, so $2,000,000 is constructible. |
| `tests/Meridian.Tests/DataIntegration/Monitoring/DataQuality/DataFreshnessSlaMonitorTests.cs` | 525 | `NOTE` | ❌ | // NOTE: Actual result depends on current time, so we check the logic is working |
| `tests/Meridian.Tests/Integration/EndpointTests/PilotAcceptanceHarnessTests.cs` | 1632 | `NOTE` | ❌ | note: "Canonical pilot run created from the certified renderer manifest."); |
| `tests/Meridian.Tests/Integration/EndpointTests/PilotAcceptanceHarnessTests.cs` | 1645 | `NOTE` | ❌ | note: "Certified renderer output reconciliation started."); |
| `tests/Meridian.Tests/Integration/EndpointTests/PilotAcceptanceHarnessTests.cs` | 1658 | `NOTE` | ❌ | note: "Exact certified renderer output was retained."); |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 2102 | `NOTE` | ❌ | Note: "Ready for review."); |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 2128 | `NOTE` | ❌ | Note: "Submit through gate.", |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 2173 | `NOTE` | ❌ | Note: "Retry after a crashed submission.", |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 2215 | `NOTE` | ❌ | Note: "Attempt to claim someone else's submitted workflow.", |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 2240 | `NOTE` | ❌ | Note: "Submit.", |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 2265 | `NOTE` | ❌ | Note: "Submit.", |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 2289 | `NOTE` | ❌ | Note: "Submit through gate.", |
| `tests/Meridian.Tests/SecurityMaster/Workbench/SecurityMasterWorkbenchCommandServiceTests.cs` | 3339 | `NOTE` | ❌ | Note: "Submit while an edit is mid-flight.", |
| `tests/Meridian.Tests/Storage/StorageChecksumServiceTests.cs` | 99 | `NOTE` | ❌ | // NOTE: File.WriteAllTextAsync uses UTF-8 with BOM by default on some platforms, |
| `tests/Meridian.Tests/Strategies/ReconciliationBreakQueueRepositoryTests.cs` | 868 | `NOTE` | ❌ | Note: "Automation suggested reopened-case resolution.", |
| `tests/Meridian.Tests/Ui/EvidenceWorkflowFabricTests.cs` | 5504 | `NOTE` | ❌ | Note: "Delivered after approval.", |
| `tests/Meridian.Tests/Ui/FundOpsCloseLaneScenarioTests.cs` | 418 | `NOTE` | ❌ | // Note: Status derives as ApprovalPending once all four gates are clean, which is expected |
| `tests/Meridian.Tests/Ui/ReportPackWorkflowServiceTests.cs` | 2056 | `NOTE` | ❌ | Note: "Delivered after restatement.", |
| `tests/Meridian.Tests/Ui/SecurityMasterWorkbenchEndpointsTests.cs` | 274 | `NOTE` | ❌ | Note: "ready", |
| `tests/Meridian.Tests/Ui/StatementReconciliationCaseworkHandoffTests.cs` | 611 | `NOTE` | ❌ | Note: "Corrected statement row retained.", |
| `tests/Meridian.Tests/Ui/StatementReconciliationCaseworkHandoffTests.cs` | 1059 | `NOTE` | ❌ | Note: "Statement variance reviewed.", |
| `tests/Meridian.Tests/Ui/WorkstationFinancialRecordExplorerEndpointTests.cs` | 209 | `NOTE` | ❌ | Note: "Board pack delivered with retained evidence graph.", |
| `tests/Meridian.Tests/Ui/WorkstationStatementCaseworkAuthorityEndpointTests.cs` | 130 | `NOTE` | ❌ | Note: request.ResolutionNote, |
| `tests/Meridian.Ui.Tests/Services/DiagnosticsServiceTests.cs` | 9 | `NOTE` | ❌ | /// Note: The service methods require a running backend (ApiClientService), |
| `tests/Meridian.Ui.Tests/Services/ScheduledMaintenanceServiceTests.cs` | 110 | `NOTE` | ❌ | // NOTE: since this is a singleton shared across tests, if StartScheduler was |
| `tests/Meridian.Ui.Tests/Services/StorageAnalyticsServiceTests.cs` | 9 | `NOTE` | ❌ | /// Note: Full analytics calculation requires file I/O, so these tests |
| `tests/Meridian.Wpf.Tests/Services/OfflineTrackingPersistenceServiceTests.cs` | 27 | `NOTE` | ❌ | // NOTE: Singleton state may persist across tests. |
| `tests/Meridian.Wpf.Tests/Services/PendingOperationsQueueServiceTests.cs` | 30 | `NOTE` | ❌ | // NOTE: This may not be false if other tests have run InitializeAsync. |
| `tools/actionlint/docs/config.md` | 14 | `NOTE` | ❌ | Note: If you're using [Super-Linter][], the file should be placed in a different directory. Please check the project's document. |
