# Meridian AI Recent Changes

> Auto-generated on 2026-11-06T22:07:36Z by `build/scripts/docs/generate-ai-navigation.py`. Do not edit manually.

Rolling source-file activity for the last 14 days.

| File | Subsystem | Last modified | Commit | Summary | Touches |
|---|---|---|---|---|---|
| `src/Meridian.Wpf/ViewModels/SecurityMasterViewModel.cs` | Desktop and UI Workflows | 2026-11-06T19:11:01+00:00 | `4e6151df` | fix(wpf): keep SecurityMasterViewModel within its file-size cap | 14 |
| `src/Meridian.Contracts/README.md` | Host and Composition | 2026-11-06T18:32:01+00:00 | `8b0e25ea` | docs(source): document conflict categories and refresh reviewed hashes | 38 |
| `src/Meridian.Wpf/README.md` | Desktop and UI Workflows | 2026-11-06T18:32:01+00:00 | `8b0e25ea` | docs(source): document conflict categories and refresh reviewed hashes | 19 |
| `src/Meridian.Execution/README.md` | Execution, Risk, and Strategies | 2026-11-06T18:25:31+00:00 | `4cf61eca` | Compute session TCA commission rates on the multiplied principal | 17 |
| `src/Meridian.Execution/Services/SessionTcaReporter.cs` | Execution, Risk, and Strategies | 2026-11-06T18:25:31+00:00 | `4cf61eca` | Compute session TCA commission rates on the multiplied principal | 7 |
| `src/Meridian.Execution/PaperTradingGateway.cs` | Execution, Risk, and Strategies | 2026-11-06T18:11:29+00:00 | `cd0070f4` | Scale paper notional-based costs by the resolved contract multiplier | 8 |
| `src/Meridian.Execution/Adapters/PaperTradingGateway.cs` | Execution, Risk, and Strategies | 2026-11-06T18:11:29+00:00 | `cd0070f4` | Scale paper notional-based costs by the resolved contract multiplier | 7 |
| `src/Meridian.Execution/PaperMatching/PaperTradingCostModel.cs` | Execution, Risk, and Strategies | 2026-11-06T18:11:29+00:00 | `cd0070f4` | Scale paper notional-based costs by the resolved contract multiplier | 7 |
| `src/Meridian.Strategies/README.md` | Execution, Risk, and Strategies | 2026-11-06T17:56:13+00:00 | `dcece77e` | Scale strategy-run cash flows by the booked contract multiplier | 9 |
| `src/Meridian.Strategies/Live/LiveRunMetricsTracker.cs` | Execution, Risk, and Strategies | 2026-11-06T17:56:13+00:00 | `dcece77e` | Scale strategy-run cash flows by the booked contract multiplier | 7 |
| `src/Meridian.Strategies/Live/LiveStrategyRunSession.cs` | Execution, Risk, and Strategies | 2026-11-06T17:56:13+00:00 | `dcece77e` | Scale strategy-run cash flows by the booked contract multiplier | 7 |
| `src/Meridian.Contracts/SecurityMaster/SecurityMasterConflictFieldCategories.cs` | Host and Composition | 2026-11-06T17:56:07+00:00 | `6400667a` | fix(wpf): route Security Master conflicts by declared field names | 1 |
| `src/Meridian.Ui.Shared/README.md` | Desktop and UI Workflows | 2026-11-06T17:45:03+00:00 | `2025b3f5` | Derive banking action origin from the principal; multiply margin notionals | 28 |
| `src/Meridian.Ui.Shared/Endpoints/BankingEndpoints.cs` | Desktop and UI Workflows | 2026-11-06T17:45:03+00:00 | `2025b3f5` | Derive banking action origin from the principal; multiply margin notionals | 9 |
| `src/Meridian.Execution/Margin/PortfolioMarginModel.cs` | Execution, Risk, and Strategies | 2026-11-06T17:45:03+00:00 | `2025b3f5` | Derive banking action origin from the principal; multiply margin notionals | 8 |
| `src/Meridian.Execution/Margin/RegTMarginModel.cs` | Execution, Risk, and Strategies | 2026-11-06T17:45:03+00:00 | `2025b3f5` | Derive banking action origin from the principal; multiply margin notionals | 8 |
| `src/Meridian.Execution/Margin/MarginPricing.cs` | Execution, Risk, and Strategies | 2026-11-06T17:45:03+00:00 | `2025b3f5` | Derive banking action origin from the principal; multiply margin notionals | 2 |
| `src/Meridian.Execution/OrderManagement/OrderFillProcessor.cs` | Execution, Risk, and Strategies | 2026-11-06T17:28:43+00:00 | `057d0ef4` | Stamp the OMS-resolved contract multiplier on the replayable fill report | 8 |
| `src/Meridian.Execution/Services/PaperTradingPortfolio.cs` | Execution, Risk, and Strategies | 2026-11-06T17:11:01+00:00 | `dfc72366` | Address review: multiplier handoff, sub-1 multipliers, MMF search, ingestion transitions | 11 |
| `src/Meridian.Ui.Shared/Endpoints/IngestionJobEndpoints.cs` | Desktop and UI Workflows | 2026-11-06T17:11:01+00:00 | `dfc72366` | Address review: multiplier handoff, sub-1 multipliers, MMF search, ingestion transitions | 8 |
| `src/Meridian.Ui.Shared/Endpoints/MoneyMarketFundEndpoints.cs` | Desktop and UI Workflows | 2026-11-06T17:11:01+00:00 | `dfc72366` | Address review: multiplier handoff, sub-1 multipliers, MMF search, ingestion transitions | 8 |
| `src/Meridian.Execution/Events/TradeExecutedEvent.cs` | Execution, Risk, and Strategies | 2026-11-06T17:11:01+00:00 | `dfc72366` | Address review: multiplier handoff, sub-1 multipliers, MMF search, ingestion transitions | 7 |
| `src/Meridian.Ui/dashboard/README.md` | Desktop and UI Workflows | 2026-11-06T16:27:46+00:00 | `2f930f77` | Address review: record queue visits only from the queue; Settings keeps fresh provider counts | 27 |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.tsx` | Desktop and UI Workflows | 2026-11-06T16:27:46+00:00 | `2f930f77` | Address review: record queue visits only from the queue; Settings keeps fresh provider counts | 19 |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.test.tsx` | Desktop and UI Workflows | 2026-11-06T16:27:46+00:00 | `2f930f77` | Address review: record queue visits only from the queue; Settings keeps fresh provider counts | 12 |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.tsx` | Desktop and UI Workflows | 2026-11-06T16:27:46+00:00 | `2f930f77` | Address review: record queue visits only from the queue; Settings keeps fresh provider counts | 12 |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.view-model.test.ts` | Desktop and UI Workflows | 2026-11-06T16:27:46+00:00 | `2f930f77` | Address review: record queue visits only from the queue; Settings keeps fresh provider counts | 9 |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.view-model.ts` | Desktop and UI Workflows | 2026-11-06T16:27:46+00:00 | `2f930f77` | Address review: record queue visits only from the queue; Settings keeps fresh provider counts | 8 |
| `src/Meridian.Ui/dashboard/src/lib/cross-screen-counts.ts` | Desktop and UI Workflows | 2026-11-06T16:27:46+00:00 | `2f930f77` | Address review: record queue visits only from the queue; Settings keeps fresh provider counts | 4 |
| `src/Meridian.Ui/dashboard/src/lib/since-last-visit.ts` | Desktop and UI Workflows | 2026-11-06T16:27:46+00:00 | `2f930f77` | Address review: record queue visits only from the queue; Settings keeps fresh provider counts | 4 |
| `src/Meridian.Ui/dashboard/src/lib/since-last-visit.test.ts` | Desktop and UI Workflows | 2026-11-06T16:27:46+00:00 | `2f930f77` | Address review: record queue visits only from the queue; Settings keeps fresh provider counts | 3 |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.reconciliation-workbench.test.tsx` | Desktop and UI Workflows | 2026-11-06T16:08:02+00:00 | `3b276d86` | Address review: one bulk action at a time, from the keyboard too | 7 |
| `src/Meridian.Ui/dashboard/src/screens/reconciliation-bulk-triage-bar.tsx` | Desktop and UI Workflows | 2026-11-06T16:08:02+00:00 | `3b276d86` | Address review: one bulk action at a time, from the keyboard too | 4 |
| `src/Meridian.Ui/dashboard/src/app.tsx` | Desktop and UI Workflows | 2026-11-06T16:00:29+00:00 | `180e9667` | Address review: scope-bound bulk updates and late default scopes | 16 |
| `src/Meridian.Ui/dashboard/src/app.test.tsx` | Desktop and UI Workflows | 2026-11-06T16:00:29+00:00 | `180e9667` | Address review: scope-bound bulk updates and late default scopes | 12 |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts` | Desktop and UI Workflows | 2026-11-06T16:00:29+00:00 | `180e9667` | Address review: scope-bound bulk updates and late default scopes | 12 |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.reconciliation-workbench.tsx` | Desktop and UI Workflows | 2026-11-06T16:00:29+00:00 | `180e9667` | Address review: scope-bound bulk updates and late default scopes | 10 |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.reconciliation-continuity.test.ts` | Desktop and UI Workflows | 2026-11-06T16:00:29+00:00 | `180e9667` | Address review: scope-bound bulk updates and late default scopes | 4 |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.linked-context.ts` | Desktop and UI Workflows | 2026-11-06T15:47:17+00:00 | `a28de56c` | Address review: withheld break counts, StrictMode default scope, stale dry runs, shared-tab visits | 8 |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.workflow-continuity.ts` | Desktop and UI Workflows | 2026-11-06T15:47:17+00:00 | `a28de56c` | Address review: withheld break counts, StrictMode default scope, stale dry runs, shared-tab visits | 8 |

