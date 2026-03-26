# TODO / FIXME / NOTE Scan

Total items: **187**

| File | Line | Tag | Linked Issue | Text |
|---|---:|---|:---:|---|
| `.claude/agents/meridian-blueprint.md` | 194 | `NOTE` | ❌ | markup. If no UI surface, note "N/A — backend feature only." |
| `.claude/agents/meridian-blueprint.md` | 321 | `NOTE` | ❌ | a one-line note. |
| `.claude/agents/meridian-cleanup.md` | 180 | `TODO` | ❌ | - Remove commented-out `InitializeComponent()` calls and leftover TODO comments |
| `.claude/agents/meridian-cleanup.md` | 193 | `NOTE` | ❌ | - Business logic in code-behind — flag it as a note but do not move it (that |
| `.claude/agents/meridian-cleanup.md` | 365 | `TODO` | ❌ | `_logger.LogWarning("TODO: implement")`. |
| `.claude/agents/meridian-cleanup.md` | 422 | `TODO` | ❌ | - `// TODO: implement` in methods that are already implemented. |
| `.claude/agents/meridian-cleanup.md` | 429 | `TODO` | ❌ | - `// TODO:` or `// FIXME:` comments that describe genuine open work items — flag them |
| `.claude/agents/meridian-cleanup.md` | 474 | `NOTE` | ❌ | - **No new features** — cleanup only; if something is missing, note it but do |
| `.claude/agents/meridian-cleanup.md` | 478 | `NOTE` | ❌ | note instead |
| `.claude/agents/meridian-docs.md` | 241 | `NOTE` | ❌ | - **No code changes** — documentation only; if code is wrong, note it but do not fix it |
| `.claude/skills/meridian-blueprint/SKILL.md` | 283 | `NOTE` | ❌ | surface, note "N/A — backend feature only." |
| `.claude/skills/meridian-blueprint/SKILL.md` | 422 | `NOTE` | ❌ | one-line note. |
| `.claude/skills/meridian-code-review/agents/grader.md` | 25 | `NOTE` | ❌ | Read the full transcript. Note the input code, what review steps were taken, and the final output. |
| `.claude/skills/meridian-code-review/eval-viewer/generate_review.py` | 306 | `NOTE` | ❌ | print("Note: lsof not found, cannot check if port is in use", file=sys.stderr) |
| `.claude/skills/meridian-code-review/evals/evals.json` | 166 | `TODO` | ❌ | "prompt": "Review this ViewModel and its paired View code-behind together for MVVM compliance:\n\nFile 1: SymbolsViewModel.cs\n```csharp\nusing System.Collections.ObjectModel;\nusing Meridian.Ui.Services;\nusing Meridian.Contracts;\n\nnamespace Meridian.Wpf.ViewModels;\n\npublic class SymbolsViewModel : BindableBase\n{\n    private readonly ISymbolService _symbolService;\n    private ObservableCollection<SymbolStatus> _symbols = new();\n    private string _searchText = string.Empty;\n    private bool _isLoading;\n\n    public SymbolsViewModel(ISymbolService symbolService)\n    {\n        _symbolService = symbolService;\n        LoadSymbolsCommand = new RelayCommand(async _ => await LoadSymbolsAsync());\n        RemoveSymbolCommand = new RelayCommand(async p => await RemoveSymbolAsync((string)p!));\n    }\n\n    public ObservableCollection<SymbolStatus> Symbols\n    {\n        get => _symbols;\n        private set => SetProperty(ref _symbols, value);\n    }\n\n    public string SearchText\n    {\n        get => _searchText;\n        set\n        {\n            SetProperty(ref _searchText, value);\n            FilterSymbols();\n        }\n    }\n\n    public bool IsLoading\n    {\n        get => _isLoading;\n        private set => SetProperty(ref _isLoading, value);\n    }\n\n    public RelayCommand LoadSymbolsCommand { get; }\n    public RelayCommand RemoveSymbolCommand { get; }\n\n    private async Task LoadSymbolsAsync()\n    {\n        IsLoading = true;\n        var symbols = await _symbolService.GetSymbolsAsync();\n        Symbols = new ObservableCollection<SymbolStatus>(symbols);\n        IsLoading = false;\n    }\n\n    private async Task RemoveSymbolAsync(string symbol)\n    {\n        await _symbolService.RemoveSymbolAsync(symbol);\n        var item = _symbols.FirstOrDefault(s => s.Symbol == symbol);\n        if (item != null) _symbols.Remove(item);\n    }\n\n    private void FilterSymbols()\n    {\n        // TODO: implement filtering\n    }\n}\n```\n\nFile 2: SymbolsPage.xaml.cs\n```csharp\nusing System.Windows.Controls;\nusing Meridian.Wpf.ViewModels;\nusing Meridian.Ui.Services;\n\nnamespace Meridian.Wpf.Views;\n\npublic partial class SymbolsPage : Page\n{\n    private readonly SymbolsViewModel _viewModel;\n\n    public SymbolsPage(ISymbolService symbolService)\n    {\n        InitializeComponent();\n        _viewModel = new SymbolsViewModel(symbolService);\n        DataContext = _viewModel;\n        Loaded += async (_, _) => await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n\n    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)\n    {\n        _viewModel.SearchText = ((TextBox)sender).Text;\n    }\n\n    private async void RemoveButton_Click(object sender, System.Windows.RoutedEventArgs e)\n    {\n        var symbol = (string)((System.Windows.FrameworkElement)sender).Tag;\n        await _viewModel._symbolService.RemoveSymbolAsync(symbol);\n        await _viewModel.LoadSymbolsCommand.Execute(null);\n    }\n}\n```", |
| `.claude/skills/meridian-code-review/scripts/aggregate_benchmark.py` | 332 | `NOTE` | ❌ | for note in benchmark["notes"]: |
| `.claude/skills/meridian-code-review/scripts/aggregate_benchmark.py` | 333 | `NOTE` | ❌ | lines.append(f"- {note}") |
| `.github/agents/adr-generator.agent.md` | 135 | `NOTE` | ❌ | - Note any migration steps required |
| `.github/agents/cleanup-agent.md` | 162 | `TODO` | ❌ | - Remove commented-out `InitializeComponent()` calls and leftover TODO tombstones. |
| `.github/agents/cleanup-agent.md` | 170 | `NOTE` | ❌ | - Business logic in code-behind — flag it as a note but do not move it. |
| `.github/agents/cleanup-agent.md` | 327 | `TODO` | ❌ | `_logger.LogWarning("TODO: implement")`. |
| `.github/agents/cleanup-agent.md` | 385 | `TODO` | ❌ | - `// TODO: implement` in methods that are already implemented. |
| `.github/agents/cleanup-agent.md` | 393 | `TODO` | ❌ | - `// TODO:` or `// FIXME:` comments that describe genuine open work items — flag |
| `.github/agents/cleanup-agent.md` | 436 | `NOTE` | ❌ | - **No new features** — cleanup only; if something is missing, note it but do not add it. |
| `.github/agents/cleanup-agent.md` | 438 | `NOTE` | ❌ | - **No ViewModel extraction** — flag it as a note; full MVVM refactors belong in code review. |
| `.github/agents/documentation-agent.md` | 294 | `TODO` | ❌ | │       │   ├── create-todo-issues.py |
| `.github/agents/documentation-agent.md` | 678 | `TODO` | ❌ | │   │   └── TODO.md |
| `.github/agents/performance-agent.md` | 248 | `NOTE` | ❌ | - **No new features** — if a performance win requires a new feature, note it but defer it. |
| `.github/workflows/README.md` | 11 | `TODO` | ❌ | \| `documentation.yml` \| `docs-comprehensive.yml`, `docs-auto-update.yml`, `docs-structure-sync.yml`, `ai-instructions-sync.yml`, `todo-automation.yml`, `docs-check.yml` \| AI documentation quality review, AI TODO triage \| |
| `.github/workflows/README.md` | 136 | `TODO` | ❌ | - **Purpose**: Centralized documentation quality checks, generation, AI instruction sync, and TODO tracking |
| `.github/workflows/README.md` | 137 | `TODO` | ❌ | - **Replaces**: `docs-comprehensive.yml`, `docs-auto-update.yml`, `docs-structure-sync.yml`, `ai-instructions-sync.yml`, `todo-automation.yml` |
| `.github/workflows/README.md` | 140 | `TODO` | ❌ | - **Push/PR/Schedule triggers**: Runs documentation generation, validation, and TODO scanning jobs |
| `.github/workflows/README.md` | 148 | `TODO` | ❌ | - TODO/FIXME/HACK/NOTE comment scanning with documentation generation |
| `.github/workflows/README.md` | 150 | `TODO` | ❌ | - **AI**: Documentation quality review, TODO triage recommendations |
| `.github/workflows/README.md` | 319 | `TODO` | ❌ | \| `documentation.yml` \| Doc Quality Review, TODO Triage \| Completeness/accuracy assessment, TODO prioritization \| |
| `.github/workflows/desktop-builds.yml` | 9 | `NOTE` | ❌ | # NOTE: UWP/WinUI 3 application has been removed. WPF is the sole desktop client. |
| `.github/workflows/nightly.yml` | 214 | `NOTE` | ❌ | 4) If only certain platforms failed, note platform-specific issues |
| `.github/workflows/prompt-generation.yml` | 269 | `NOTE` | ❌ | - name: Note skipped AI review |
| `.github/workflows/prompt-generation.yml` | 473 | `NOTE` | ❌ | - name: Note Copilot trigger status |
| `.github/workflows/scheduled-maintenance.yml` | 311 | `TODO` | ❌ | echo "### TODO/FIXME/HACK Markers" >> $GITHUB_STEP_SUMMARY |
| `.github/workflows/scheduled-maintenance.yml` | 314 | `TODO` | ❌ | TODO_COUNT=$(grep -rn "TODO" src/ tests/ --include="*.cs" --include="*.fs" 2>/dev/null \| wc -l) |
| `.github/workflows/scheduled-maintenance.yml` | 315 | `FIXME` | ❌ | FIXME_COUNT=$(grep -rn "FIXME" src/ tests/ --include="*.cs" --include="*.fs" 2>/dev/null \| wc -l) |
| `.github/workflows/scheduled-maintenance.yml` | 324 | `TODO` | ❌ | echo "\| TODO \| $TODO_COUNT \|" >> $GITHUB_STEP_SUMMARY |
| `.github/workflows/scheduled-maintenance.yml` | 325 | `FIXME` | ❌ | echo "\| FIXME \| $FIXME_COUNT \|" >> $GITHUB_STEP_SUMMARY |
| `.github/workflows/scheduled-maintenance.yml` | 330 | `FIXME` | ❌ | echo "<details><summary>FIXME locations (click to expand)</summary>" >> $GITHUB_STEP_SUMMARY |
| `.github/workflows/scheduled-maintenance.yml` | 333 | `FIXME` | ❌ | grep -rn "FIXME" src/ tests/ --include="*.cs" --include="*.fs" 2>/dev/null \| head -25 >> $GITHUB_STEP_SUMMARY |
| `.github/workflows/test-matrix.yml` | 5 | `NOTE` | ❌ | # NOTE: This workflow intentionally does NOT use reusable-dotnet-build.yml because it needs |
| `.github/workflows/validate-workflows.yml` | 206 | `NOTE` | ❌ | echo "Note: Ensure cron schedules are distributed to avoid rate limits" |
| `CLAUDE.md` | 100 | `NOTE` | ❌ | **Note:** Always use `/p:EnableWindowsTargeting=true` on non-Windows systems to avoid NETSDK1100 errors. |
| `CLAUDE.md` | 383 | `TODO` | ❌ | │       │   ├── create-todo-issues.py |
| `CLAUDE.md` | 767 | `TODO` | ❌ | │   │   └── TODO.md |
| `README.md` | 497 | `TODO` | ❌ | │       │   ├── create-todo-issues.py |
| `README.md` | 871 | `TODO` | ❌ | │   │   ├── TODO.md |
| `benchmarks/run-bottleneck-benchmarks.sh` | 111 | `NOTE` | ❌ | # Note: --filter is intentionally not added here; each phase below supplies its own |
| `build/scripts/run/start-collector.ps1` | 109 | `NOTE` | ❌ | if ($depth -gt 0) { Write-Host "[NOTE] L2 depth requires provider depth entitlements for venues." } |
| `build/scripts/run/start-collector.sh` | 114 | `NOTE` | ❌ | print("[NOTE] L2 depth requires provider depth subscription for venues.") |
| `config/appsettings.sample.json` | 364 | `NOTE` | ❌ | // NOTE: Credentials are resolved from environment variables - do NOT add them here. |
| `config/appsettings.sample.json` | 484 | `NOTE` | ❌ | //   4. Note the port (default 7497 for TWS paper, 7496 for live) |
| `docs/HELP.md` | 422 | `NOTE` | ❌ | **Note:** Environment variables override values in `appsettings.json`. |
| `docs/HELP.md` | 450 | `NOTE` | ❌ | - Note the Socket Port (default: 7497 for TWS, 4001 for Gateway) |
| `docs/ai/ai-known-errors.md` | 239 | `NOTE` | ❌ | - **Note**: This issue regressed multiple times (1e2ea1d, 5756479, 1802ea9, bf67ed5, e920c34) when using workarounds. The structural fix eliminates the problem at the API design level. |
| `docs/ai/claude/CLAUDE.actions.md` | 23 | `TODO` | ❌ | \| Documentation \| `documentation.yml` \| Push/PRs (docs/source), weekly, issues, manual \| Doc generation, structure sync, TODO scan \| |
| `docs/ai/claude/CLAUDE.actions.md` | 92 | `TODO` | ❌ | - **Documentation** - AI documentation quality review, AI TODO triage |
| `docs/ai/claude/CLAUDE.api.md` | 12 | `NOTE` | ❌ | **Implementation Note:** 300 route constants in `UiApiRoutes.cs` across 38 endpoint files. Core endpoints (status, health, config, backfill) are fully functional. A small number of advanced endpoints may return stub responses or 501 Not Implemented. |
| `docs/ai/claude/CLAUDE.api.md` | 221 | `TODO` | ❌ | \| `documentation.yml` \| Documentation generation, AI instruction sync, TODO scanning \| |
| `docs/ai/claude/CLAUDE.structure.md` | 160 | `TODO` | ❌ | │       │   ├── create-todo-issues.py |
| `docs/ai/claude/CLAUDE.structure.md` | 473 | `TODO` | ❌ | │   │   └── TODO.md |
| `docs/ai/copilot/instructions.md` | 5 | `NOTE` | ❌ | > **Note:** For comprehensive project context, see [CLAUDE.md](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) in the repository root. For the master AI resource index, see [docs/ai/README.md](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/README.md). |
| `docs/ai/copilot/instructions.md` | 368 | `TODO` | ❌ | │       │   ├── create-todo-issues.py |
| `docs/ai/copilot/instructions.md` | 752 | `TODO` | ❌ | │   │   └── TODO.md |
| `docs/ai/copilot/instructions.md` | 2812 | `TODO` | ❌ | - `documentation.yml` — Doc generation, TODO scanning, AI error intake |
| `docs/architecture/deterministic-canonicalization.md` | 365 | `NOTE` | ❌ | Note: Polygon does not define buyer-initiated codes. Only ~5% of trades carry definitive aggressor inference. The canonicalization layer preserves `Unknown` as a valid canonical value rather than attempting inference. |
| `docs/architecture/domains.md` | 111 | `NOTE` | ❌ | > Note: not every enum member is currently emitted by the three core collectors (`TradeDataCollector`, `MarketDepthCollector`, `QuoteCollector`); several are used by adapters, backfill paths, or the `L3OrderBookCollector`. |
| `docs/audits/BACKTEST_ENGINE_CODE_REVIEW_2026_03_25.md` | 150 | `NOTE` | ❌ | **Fix:** Add a note to the XML doc comment explaining the open/close midpoint convention and when to use `BarMidpointFillModel` vs. `OrderBookFillModel`. Consider offering `(bar.High + bar.Low) / 2m` as an alternative mode. |
| `docs/development/documentation-contribution-guide.md` | 84 | `TODO` | ❌ | ├── status/                    # Project status, roadmap, TODO |
| `docs/development/documentation-contribution-guide.md` | 154 | `TODO` | ❌ | \| Project roadmap, changelog, TODO, feature inventory \| `status/` \| |
| `docs/development/expanding-scripts.md` | 11 | `TODO` | ❌ | add-todos.py                  # TODO item creator (NEW) |
| `docs/development/expanding-scripts.md` | 12 | `TODO` | ❌ | scan-todos.py                 # TODO scanning (enhanced) |
| `docs/development/expanding-scripts.md` | 30 | `TODO` | ❌ | ### TODO Item Creator (`add-todos.py`) |
| `docs/development/expanding-scripts.md` | 32 | `TODO` | ❌ | Interactive tool to help developers add well-formatted TODO comments with proper metadata. |
| `docs/development/expanding-scripts.md` | 50 | `TODO` | ❌ | - Interactive prompts for TODO details |
| `docs/development/expanding-scripts.md` | 119 | `TODO` | ❌ | ## Enhanced TODO Scanner |
| `docs/development/expanding-scripts.md` | 124 | `TODO` | ❌ | - **Assignee detection** - Recognizes @username in TODO comments |
| `docs/development/expanding-scripts.md` | 131 | `TODO` | ❌ | python3 build/scripts/docs/scan-todos.py --output docs/status/TODO.md |
| `docs/development/refactor-map.md` | 169 | `NOTE` | ❌ | > **Note:** The UWP desktop application has been fully removed from the codebase. WPF is the sole desktop client. |
| `docs/development/repository-organization-guide.md` | 356 | `TODO` | ❌ | │   ├── TODO.md                 # auto-generated task marker tracking |
| `docs/development/repository-organization-guide.md` | 681 | `FIXME` | ❌ | grep -rE "FIXME:\|HACK:" src/ tests/ |
| `docs/docfx/api/index.md` | 62 | `NOTE` | ❌ | > **Note:** The source code must build successfully before DocFX can extract XML documentation. |
| `docs/evaluations/high-impact-improvement-brainstorm-2026-03.md` | 76 | `NOTE` | ❌ | > **Note:** Ratings above reflect the initial assessment (2026-03-01). Follow-up |
| `docs/evaluations/high-value-low-cost-improvements-brainstorm.md` | 58 | `NOTE` | ❌ | **Status (2026-03-15):** `ConfigurationPipeline.cs` detects when both `DataSource` and `DataSources` are set and logs: `"Both 'DataSource' and 'DataSources' are set. 'DataSources' takes precedence."` A note was also added to `appsettings.sample.json`. |
| `docs/evaluations/high-value-low-cost-improvements-brainstorm.md` | 62 | `NOTE` | ❌ | **Improvement:** At config load time, if both `DataSource` and `DataSources` are populated, log a structured warning: `"Both 'DataSource' and 'DataSources' are set. 'DataSources' takes precedence. Remove 'DataSource' to silence this warning."` Add a note to `appsettings.sample.json`. |
| `docs/evaluations/high-value-low-cost-improvements-brainstorm.md` | 670 | `NOTE` | ❌ | Note: IB would add L2 depth but requires TWS running |
| `docs/evaluations/nautilus-inspired-restructuring-proposal.md` | 185 | `TODO` | ❌ | // TODO: Add provider-specific dependencies (HttpClient, config, etc.) |
| `docs/evaluations/nautilus-inspired-restructuring-proposal.md` | 187 | `TODO` | ❌ | public bool IsEnabled => true; // TODO: Wire to configuration |
| `docs/evaluations/nautilus-inspired-restructuring-proposal.md` | 190 | `TODO` | ❌ | => throw new NotImplementedException("TODO: Implement connection logic"); |
| `docs/evaluations/nautilus-inspired-restructuring-proposal.md` | 193 | `TODO` | ❌ | => throw new NotImplementedException("TODO: Implement disconnection logic"); |
| `docs/evaluations/quant-script-blueprint-brainstorm.md` | 275 | `NOTE` | ❌ | **`decimal` vs `double` note.** `NumericSeries` uses `decimal` to preserve the precision of price data sourced from `PriceSeries` (which is already `decimal`). Libraries such as `Skender.Stock.Indicators` accept `double` inputs; callers that bridge to such libraries must explicitly cast (`(double)value`) at the adapter boundary rather than silently losing precision inside the core series type. |
| `docs/operations/operator-runbook.md` | 202 | `NOTE` | ❌ | - note: L2 depth requires provider depth entitlements |
| `docs/operations/portable-data-packager.md` | 303 | `NOTE` | ❌ | 2. **Document filters**: Note any symbols, dates, or types that were excluded |
| `docs/plans/l3-inference-implementation-plan.md` | 180 | `NOTE` | ❌ | > **Note:** Providers that supply L2 depth data include Interactive Brokers, Polygon, NYSE, and StockSharp. Providers that supply only daily OHLCV bars (e.g. Stooq, Yahoo Finance) are **not sufficient** for queue inference — depth tick data is required. |
| `docs/plans/quant-script-environment-blueprint.md` | 1193 | `TODO` | ❌ | - [ ] `Api/PortfolioBuilder.cs` + `PortfolioResult.cs` (`EfficientFrontier` returns equal-weight stub + `// TODO` comment) |
| `docs/plans/ufl-direct-lending-target-state-v2.md` | 417 | `NOTE` | ❌ | \| AssessFee of feeType:FeeType * amount:decimal * effectiveDate:DateOnly * note:string option |
| `docs/plans/ufl-direct-lending-target-state-v2.md` | 432 | `NOTE` | ❌ | \| FeeAssessed of feeType:FeeType * amount:decimal * effectiveDate:DateOnly * note:string option |
| `docs/plans/ufl-treasury-bill-target-state-v2.md` | 105 | `NOTE` | ❌ | 5. Future note and bond packages should reuse the same government-security patterns where possible. |
| `docs/providers/interactive-brokers-free-equity-reference.md` | 254 | `NOTE` | ❌ | Note: It is important to understand the concept of market data lines since it has an impact not only on the live real time requests but also for requesting market depth and real time bars. |
| `docs/providers/interactive-brokers-free-equity-reference.md` | 265 | `NOTE` | ❌ | - Note: BID_ASK requests count as **two** requests |
| `docs/reference/data-dictionary.md` | 724 | `NOTE` | ❌ | **Note:** Aligns with Interactive Brokers conventions. |
| `docs/reference/data-uniformity.md` | 5 | `NOTE` | ❌ | This note expands on the data-quality goals for the collector so downstream users receive a uniform, analysis-ready tape regardless of provider quirks. |
| `docs/status/EVALUATIONS_AND_AUDITS.md` | 394 | `NOTE` | ❌ | - Historical note: the original audit flagged generated docs as stale, but `docs/generated/` has since been refreshed and expanded |
| `docs/status/IMPROVEMENTS.md` | 1344 | `TODO` | ❌ | - **[TODO.md](TODO.md)** — Auto-generated task marker tracking from code comments |
| `docs/status/README.md` | 30 | `TODO` | ❌ | \| [TODO.md](TODO.md) \| Auto-generated TODO tracking from source comments \| |
| `docs/status/docs-automation-summary.json` | 25 | `TODO` | ❌ | "docs/status/TODO.md", |
| `docs/status/docs-automation-summary.json` | 27 | `TODO` | ❌ | "docs/status/todo-scan-results.json" |
| `docs/status/docs-automation-summary.json` | 32 | `TODO` | ❌ | "output_file": "docs/status/TODO.md", |
| `docs/status/docs-automation-summary.md` | 9 | `TODO` | ❌ | \| `scan-todos` \| `success` \| `175.615` \| `docs/status/TODO.md` \| |
| `docs/status/example-validation.md` | 148 | `TODO` | ❌ | \| `docs\status\TODO.md` \| 1 \| |
| `docs/status/health-dashboard.md` | 23 | `TODO` | ❌ | \| TODO/FIXME markers \| 309 \| |
| `docs/status/health-dashboard.md` | 33 | `TODO` | ❌ | \| TODO density \| 15 pts \| Lower density of TODO/FIXME markers \| |
| `docs/status/rules-report.md` | 431 | `TODO` | ❌ | \| `docs\status\TODO.md` \| No hardcoded API keys in docs \| error \| |
| `docs/status/rules-report.md` | 432 | `TODO` | ❌ | \| `docs\status\TODO.md` \| No hardcoded localhost URLs in docs \| info \| |
| `src/Meridian.Application/Config/ConfigValidationHelper.cs` | 123 | `TODO` | ❌ | var placeholders = new[] { "__SET_ME__", "YOUR_", "REPLACE_", "ENTER_", "INSERT_", "TODO" }; |
| `src/Meridian.Application/Config/ConfigurationPipeline.cs` | 295 | `TODO` | ❌ | ["__SET_ME__", "your-key-here", "your-secret-here", "REPLACE_ME", "ENTER_YOUR", "INSERT_YOUR", "TODO", "xxx"]; |
| `src/Meridian.Application/Config/Credentials/CredentialTestingService.cs` | 377 | `NOTE` | ❌ | if (content.Contains("\"Note\":")) |
| `src/Meridian.Application/Config/Credentials/ProviderCredentialResolver.cs` | 195 | `TODO` | ❌ | "TODO" or |
| `src/Meridian.Application/Config/IConfigValidator.cs` | 165 | `TODO` | ❌ | "TODO", "xxx", "change-me", "placeholder" |
| `src/Meridian.Application/DirectLending/InMemoryDirectLendingService.Workflows.cs` | 99 | `NOTE` | ❌ | AppendEvent(stored, "loan.fee-assessed", request.EffectiveDate, new { loanId, request.FeeType, request.Amount, request.EffectiveDate, request.Note }, metadata); |
| `src/Meridian.Application/DirectLending/InMemoryDirectLendingService.Workflows.cs` | 100 | `NOTE` | ❌ | GetList(_feeBalances, loanId).Add(new FeeBalanceDto(Guid.NewGuid(), loanId, request.FeeType, request.EffectiveDate, request.Amount, request.Amount, stored.History[^1].EventId, request.Note, DateTimeOffset.UtcNow)); |
| `src/Meridian.Application/DirectLending/PostgresDirectLendingCommandService.cs` | 397 | `NOTE` | ❌ | request.Note |
| `src/Meridian.Application/DirectLending/PostgresDirectLendingCommandService.cs` | 420 | `NOTE` | ❌ | request.Note) |
| `src/Meridian.Application/Http/HtmlTemplates.cs` | 149 | `NOTE` | ❌ | <p><strong>Note:</strong> External templates not found. Using minimal fallback UI.</p> |
| `src/Meridian.Application/Http/HtmlTemplates.cs` | 242 | `NOTE` | ❌ | <p><strong>Note:</strong> External templates not found. Using minimal fallback UI.</p> |
| `src/Meridian.Application/Services/GovernanceExceptionService.cs` | 111 | `NOTE` | ❌ | /// <summary>Marks an exception as resolved with an optional closing note.</summary> |
| `src/Meridian.Application/Wizard/Steps/ConfigureDataSourceStep.cs` | 155 | `NOTE` | ❌ | _output.WriteLine("  Note: IB requires TWS or IB Gateway to be running.\n"); |
| `src/Meridian.Contracts/DirectLending/DirectLendingWorkflowDtos.cs` | 29 | `NOTE` | ❌ | string? Note); |
| `src/Meridian.Contracts/DirectLending/DirectLendingWorkflowDtos.cs` | 70 | `NOTE` | ❌ | string? Note, |
| `src/Meridian.Core/Monitoring/MigrationDiagnostics.cs` | 17 | `NOTE` | ❌ | /// NOTE: This class lives in the Core project (not Application) so that |
| `src/Meridian.Core/Serialization/MarketDataJsonContext.cs` | 178 | `NOTE` | ❌ | /// NOTE: Alpaca payloads use both "T" and "t" keys in the same object. |
| `src/Meridian.Execution/BrokerageServiceRegistration.cs` | 138 | `NOTE` | ❌ | // NOTE: We intentionally use GetRequiredKeyedService here rather than |
| `src/Meridian.FSharp/Domain/SecMasterDomain.fs` | 98 | `NOTE` | ❌ | \| Note |
| `src/Meridian.Infrastructure/Adapters/AlphaVantage/AlphaVantageHistoricalDataProvider.cs` | 87 | `NOTE` | ❌ | return !json.Contains("Note") && !json.Contains("Thank you for using Alpha Vantage"); |
| `src/Meridian.Infrastructure/Adapters/AlphaVantage/AlphaVantageHistoricalDataProvider.cs` | 241 | `NOTE` | ❌ | return json.Contains("\"Note\"") \|\| json.Contains("Thank you for using Alpha Vantage"); |
| `src/Meridian.Storage/DirectLending/DirectLendingPersistenceBatch.cs` | 28 | `NOTE` | ❌ | string? Note); |
| `src/Meridian.Storage/DirectLending/Migrations/005_direct_lending_operations.sql` | 54 | `NOTE` | ❌ | note                     text, |
| `src/Meridian.Storage/DirectLending/Migrations/005_direct_lending_workflows.sql` | 64 | `NOTE` | ❌ | note text, |
| `src/Meridian.Storage/DirectLending/PostgresDirectLendingStateStore.Operations.cs` | 109 | `NOTE` | ❌ | note, |
| `src/Meridian.Storage/DirectLending/PostgresDirectLendingStateStore.cs` | 971 | `NOTE` | ❌ | note, |
| `src/Meridian.Storage/DirectLending/PostgresDirectLendingStateStore.cs` | 981 | `NOTE` | ❌ | @note, |
| `src/Meridian.Storage/DirectLending/PostgresDirectLendingStateStore.cs` | 991 | `NOTE` | ❌ | insert.Parameters.AddWithValue("note", (object?)row.Note ?? DBNull.Value); |
| `src/Meridian.Storage/Packaging/PortableDataPackager.Scripts.Sql.cs` | 31 | `NOTE` | ❌ | sb.AppendLine("-- Note: For JSONL files, use PostgreSQL's COPY with JSON processing"); |
| `src/Meridian.Ui.Services/Services/AdminMaintenanceModels.cs` | 275 | `NOTE` | ❌ | /// Note: Renamed from RetentionPolicy to avoid conflict with Meridian.Ui.Services.RetentionPolicy |
| `src/Meridian.Ui.Services/Services/AdminMaintenanceModels.cs` | 411 | `NOTE` | ❌ | // NOTE: SelfTest*, ErrorCodes*, ShowConfig*, QuickCheck* models are defined in DiagnosticsService.cs |
| `src/Meridian.Ui.Services/Services/AnalysisExportWizardService.cs` | 816 | `NOTE` | ❌ | // For now, fall back to CSV with a note |
| `src/Meridian.Ui.Services/Services/AnalysisExportWizardService.cs` | 825 | `NOTE` | ❌ | "Note: Full Parquet export requires Apache.Arrow library.\n" + |
| `src/Meridian.Ui.Services/Services/AnalysisExportWizardService.cs` | 850 | `NOTE` | ❌ | // Add a note file explaining the Excel fallback |
| `src/Meridian.Ui.Services/Services/AnalysisExportWizardService.cs` | 856 | `NOTE` | ❌ | "Note: Full Excel (.xlsx) export requires the EPPlus library.\n" + |
| `src/Meridian.Ui.Services/Services/AnalysisExportWizardService.cs` | 892 | `NOTE` | ❌ | "Note: Full HDF5 (.h5) export requires the h5py library in Python.\n" + |
| `src/Meridian.Ui.Services/Services/AnalysisExportWizardService.cs` | 986 | `NOTE` | ❌ | "Note: Full QuantConnect Lean format export requires specific data structure.\n" + |
| `src/Meridian.Ui.Services/Services/ProviderHealthService.cs` | 516 | `NOTE` | ❌ | // NOTE: ProviderComparison is defined in AdvancedAnalyticsModels.cs for cross-provider comparison |
| `src/Meridian.Ui.Services/Services/StorageOptimizationAdvisorService.cs` | 806 | `NOTE` | ❌ | // Fallback: Note that tier migration requires storage configuration |
| `src/Meridian.Ui.Services/Services/StorageOptimizationAdvisorService.cs` | 1043 | `NOTE` | ❌ | // Fallback: Note that tier migration requires storage configuration |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | 931 | `NOTE` | ❌ | new { provider = "Interactive Brokers", status = "Healthy", capability = "Execution + fills", latency = "23ms p50", note = "Paper session heartbeat and ingress are healthy." }, |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | 932 | `NOTE` | ❌ | new { provider = "Polygon", status = "Healthy", capability = "Streaming equities", latency = "18ms p50", note = "Quote and trade subscriptions are within expected envelope." }, |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | 933 | `NOTE` | ❌ | new { provider = "Databento", status = "Warning", capability = "Historical replay", latency = "74ms p50", note = "Backfill queue is elevated for options symbols." } |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | 961 | `NOTE` | ❌ | new { provider = "Interactive Brokers", status = "Healthy", capability = "Execution + fills", latency = "21ms p50", note = "Paper adapter routing is available." }, |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | 962 | `NOTE` | ❌ | new { provider = "Polygon", status = "Healthy", capability = "Streaming equities", latency = "16ms p50", note = "Realtime subscriptions are steady." }, |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | 963 | `NOTE` | ❌ | new { provider = "Databento", status = "Warning", capability = "Historical replay", latency = "69ms p50", note = "Replay queue is elevated but within tolerance." } |
| `src/Meridian.Ui.Shared/Services/BackfillCoordinator.cs` | 55 | `NOTE` | ❌ | /// <para><b>Migration Note:</b> This class wraps the core implementation from |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.test.tsx` | 20 | `NOTE` | ❌ | note: "Realtime subscriptions are stable." |
| `src/Meridian.Ui/dashboard/src/screens/data-operations-screen.tsx` | 211 | `NOTE` | ❌ | <div className="mt-3 text-sm leading-6 text-foreground">{provider.note}</div> |
| `src/Meridian.Ui/dashboard/src/types.ts` | 115 | `NOTE` | ❌ | note: string; |
| `src/Meridian.Wpf/GlobalUsings.cs` | 7 | `NOTE` | ❌ | // NOTE: Type aliases and Contracts namespaces are NOT re-defined here because |
| `src/Meridian.Wpf/Services/ContextMenuService.cs` | 120 | `NOTE` | ❌ | "Add Note", "\uE70B", |
| `tests/Meridian.Tests/Application/Backfill/AdditionalProviderContractTests.cs` | 637 | `NOTE` | ❌ | "Note": "Thank you for using Alpha Vantage! Our standard API call frequency is 5 calls per minute and 500 calls per day. Please visit https://www.alphavantage.co/premium/ if you would like to target a higher API call frequency." |
| `tests/Meridian.Tests/Application/Backfill/BackfillWorkerServiceTests.cs` | 28 | `NOTE` | ❌ | // NOTE: Using null! because validation throws before dependencies are accessed |
| `tests/Meridian.Tests/Application/Backfill/BackfillWorkerServiceTests.cs` | 55 | `NOTE` | ❌ | // NOTE: Using null! because validation throws before dependencies are accessed |
| `tests/Meridian.Tests/Application/Backfill/BackfillWorkerServiceTests.cs` | 84 | `NOTE` | ❌ | // NOTE: Using null! dependencies - we only verify that ArgumentOutOfRangeException is not thrown |
| `tests/Meridian.Tests/Application/Monitoring/DataQuality/DataFreshnessSlaMonitorTests.cs` | 525 | `NOTE` | ❌ | // NOTE: Actual result depends on current time, so we check the logic is working |
| `tests/Meridian.Tests/Application/Pipeline/FSharpEventValidatorTests.cs` | 72 | `NOTE` | ❌ | // Note: Trade.ctor only checks Price > 0, so $2,000,000 is constructible. |
| `tests/Meridian.Tests/Infrastructure/Providers/IBRuntimeGuidanceTests.cs` | 46 | `NOTE` | ❌ | client.ProviderNotes.Should().Contain(note => note.Contains("interactive-brokers-setup.md")); |
| `tests/Meridian.Tests/Infrastructure/Providers/IBRuntimeGuidanceTests.cs` | 55 | `NOTE` | ❌ | provider.ProviderNotes.Should().Contain(note => note.Contains("build-ibapi-smoke.ps1")); |
| `tests/Meridian.Tests/Storage/AnalysisExportServiceTests.cs` | 281 | `NOTE` | ❌ | new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "TEST", Note = "Price < 100 & Volume > 50" } |
| `tests/Meridian.Tests/Storage/StorageChecksumServiceTests.cs` | 121 | `NOTE` | ❌ | // NOTE: File.WriteAllTextAsync uses UTF-8 with BOM by default on some platforms, |
| `tests/Meridian.Ui.Tests/Services/DiagnosticsServiceTests.cs` | 9 | `NOTE` | ❌ | /// Note: The service methods require a running backend (ApiClientService), |
| `tests/Meridian.Ui.Tests/Services/ScheduledMaintenanceServiceTests.cs` | 85 | `NOTE` | ❌ | // NOTE: since this is a singleton shared across tests, if StartScheduler was |
| `tests/Meridian.Ui.Tests/Services/StorageAnalyticsServiceTests.cs` | 9 | `NOTE` | ❌ | /// Note: Full analytics calculation requires file I/O, so these tests |
| `tests/Meridian.Wpf.Tests/Services/OfflineTrackingPersistenceServiceTests.cs` | 27 | `NOTE` | ❌ | // NOTE: Singleton state may persist across tests. |
| `tests/Meridian.Wpf.Tests/Services/PendingOperationsQueueServiceTests.cs` | 30 | `NOTE` | ❌ | // NOTE: This may not be false if other tests have run InitializeAsync. |
