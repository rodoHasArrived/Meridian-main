# Meridian AI Recent Changes

> Auto-generated on 2026-08-15T12:47:37Z by `build/scripts/docs/generate-ai-navigation.py`. Do not edit manually.

Rolling source-file activity for the last 14 days.

| File | Subsystem | Last modified | Commit | Summary | Touches |
|---|---|---|---|---|---|
| `src/Meridian.Storage/Archival/AtomicFileWriter.cs` | Providers and Storage | 2026-08-12T14:47:39+00:00 | `e8823fbcc` | Write persisted OAuth tokens owner-only | 4 |
| `src/Meridian.Application/Config/Credentials/OAuthTokenRefreshService.cs` | Host and Composition | 2026-08-12T14:47:39+00:00 | `e8823fbcc` | Write persisted OAuth tokens owner-only | 3 |
| `src/Meridian.DataIntegration/Credentials/FileProviderCredentialStore.cs` | Unmapped | 2026-08-12T04:17:31+00:00 | `d6bb9d45f` | Create the provider credential vault key with owner-only permissions | 3 |
| `src/Meridian.Application/README.md` | Host and Composition | 2026-08-11T18:36:49+00:00 | `8ae514295` | Give provider-integration field transforms a single owner | 6 |
| `src/Meridian.Application/Integrations/ProviderIntegrationDryRunService.cs` | Host and Composition | 2026-08-11T18:36:49+00:00 | `8ae514295` | Give provider-integration field transforms a single owner | 3 |
| `src/Meridian.Application/Integrations/ProviderIntegrationQuarantineReplayService.cs` | Host and Composition | 2026-08-11T18:36:49+00:00 | `8ae514295` | Give provider-integration field transforms a single owner | 3 |
| `src/Meridian.Application/Integrations/ProviderIntegrationRestDryRunService.cs` | Host and Composition | 2026-08-11T18:36:49+00:00 | `8ae514295` | Give provider-integration field transforms a single owner | 3 |
| `src/Meridian.Application/Integrations/ProviderIntegrationFieldTransforms.cs` | Host and Composition | 2026-08-11T18:36:49+00:00 | `8ae514295` | Give provider-integration field transforms a single owner | 1 |
| `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.Dimensions.cs` | Desktop and UI Workflows | 2026-08-11T17:59:58+00:00 | `1ec2e63f3` | Keep LedgerEndpoints.cs at its file-size ratchet cap | 4 |
| `src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.cs` | Desktop and UI Workflows | 2026-08-11T17:59:58+00:00 | `1ec2e63f3` | Keep LedgerEndpoints.cs at its file-size ratchet cap | 4 |
| `src/Meridian.Contracts/README.md` | Host and Composition | 2026-08-11T17:54:42+00:00 | `69881b684` | Give ledger GL dimensions a single owner | 6 |
| `src/Meridian.FinancialOperations/AccountingClose/AccountingReportPackageService.cs` | Unmapped | 2026-08-11T17:54:42+00:00 | `69881b684` | Give ledger GL dimensions a single owner | 3 |
| `src/Meridian.Reporting/ReportGenerationService.cs` | Unmapped | 2026-08-11T17:54:42+00:00 | `69881b684` | Give ledger GL dimensions a single owner | 3 |
| `src/Meridian.Storage/Ledger/PostgresLedgerBookService.cs` | Providers and Storage | 2026-08-11T17:54:42+00:00 | `69881b684` | Give ledger GL dimensions a single owner | 3 |
| `src/Meridian.Contracts/Ledger/LedgerDimensionTags.cs` | Host and Composition | 2026-08-11T17:54:42+00:00 | `69881b684` | Give ledger GL dimensions a single owner | 1 |
| `src/Meridian.Core/Config/ConfigTemplateGenerator.cs` | Host and Composition | 2026-08-11T17:07:11+00:00 | `aa767fb17` | Stop treating a missing backfill opt-in as a passing one | 9 |
| `src/Meridian.Core/Config/ConfigEnvironmentOverride.cs` | Host and Composition | 2026-08-11T16:12:55+00:00 | `e1cfef1b2` | Stop MDC_SYMBOLS re-enabling depth the template turned off | 4 |
| `src/Meridian.Ui/dashboard/README.md` | Desktop and UI Workflows | 2026-08-11T15:59:53+00:00 | `cb1743c3e` | Stop the Docker template requesting depth no advertised provider serves | 9 |
| `src/Meridian.Ui/dashboard/src/screens/quant-lab-screen.formulas-tab.test.tsx` | Desktop and UI Workflows | 2026-08-11T09:56:20+00:00 | `6daeb9360` | Mirror the runtime secret predicate, fix a vacuous test, and regenerate nav last | 5 |
| `src/Meridian.Mcp/Tools/ConventionTools.cs` | MCP Integration | 2026-08-11T09:42:23+00:00 | `01652ac36` | Fix the blocking-async detector's false positives and its blind spot | 3 |
| `src/Meridian.Wpf/README.md` | Desktop and UI Workflows | 2026-08-11T09:39:23+00:00 | `80440650a` | Address Codex review: overflow parity and a real producer | 6 |
| `src/Meridian.Wpf/Services/StrategyWorkspaceShellPresentationService.cs` | Desktop and UI Workflows | 2026-08-11T09:39:23+00:00 | `80440650a` | Address Codex review: overflow parity and a real producer | 3 |
| `src/Meridian.Wpf/Views/WorkspaceCommandBarControl.xaml.cs` | Desktop and UI Workflows | 2026-08-11T09:39:23+00:00 | `80440650a` | Address Codex review: overflow parity and a real producer | 3 |
| `src/Meridian.Ui/dashboard/src/screens/quant-lab-screen.tsx` | Desktop and UI Workflows | 2026-08-11T09:36:32+00:00 | `9b6f29142` | Stop advertising Yahoo as a real-time source and align the guard with the converter | 5 |
| `src/Meridian.Wpf/Models/WorkspaceShellChromeModels.cs` | Desktop and UI Workflows | 2026-08-11T09:23:31+00:00 | `1e14cf1e0` | Separate disabled reasons from descriptions in WPF command bars | 3 |
| `src/Meridian.Wpf/Views/WorkspaceCommandBarControl.xaml` | Desktop and UI Workflows | 2026-08-11T09:23:31+00:00 | `1e14cf1e0` | Separate disabled reasons from descriptions in WPF command bars | 3 |
| `src/Meridian.Wpf/Workstation/Controls/WorkstationCommandBarControl.xaml` | Desktop and UI Workflows | 2026-08-11T09:23:31+00:00 | `1e14cf1e0` | Separate disabled reasons from descriptions in WPF command bars | 3 |
| `src/Meridian.Wpf/Workstation/Controls/WorkstationCommandBarControl.xaml.cs` | Desktop and UI Workflows | 2026-08-11T09:23:31+00:00 | `1e14cf1e0` | Separate disabled reasons from descriptions in WPF command bars | 3 |
| `src/Meridian.Wpf/Workstation/Models/WorkstationPresentationModels.cs` | Desktop and UI Workflows | 2026-08-11T09:23:31+00:00 | `1e14cf1e0` | Separate disabled reasons from descriptions in WPF command bars | 3 |
| `src/Meridian.Ui/dashboard/src/screens/strategy-designer-screen.tsx` | Desktop and UI Workflows | 2026-08-11T09:18:32+00:00 | `21cd5ebc9` | Fix the god-file ratchet breach and make the disabled reasons reachable | 6 |
| `src/Meridian.Ui/dashboard/src/screens/strategy-designer-screen.view-model.ts` | Desktop and UI Workflows | 2026-08-11T09:18:32+00:00 | `21cd5ebc9` | Fix the god-file ratchet breach and make the disabled reasons reachable | 4 |
| `src/Meridian.Ui/dashboard/src/screens/strategy-designer-screen.test.tsx` | Desktop and UI Workflows | 2026-08-11T09:18:32+00:00 | `21cd5ebc9` | Fix the god-file ratchet breach and make the disabled reasons reachable | 3 |
| `src/Meridian.Ui/dashboard/src/screens/strategy-designer-screen.copy.ts` | Desktop and UI Workflows | 2026-08-11T09:18:32+00:00 | `21cd5ebc9` | Fix the god-file ratchet breach and make the disabled reasons reachable | 1 |
| `src/Meridian.Ui/dashboard/src/screens/strategy-designer-screen.view-model.test.ts` | Desktop and UI Workflows | 2026-08-11T09:02:26+00:00 | `0dca8544a` | Wire the sample-config guard into CI and make the designer tell one story | 3 |
| `src/Meridian.Ui/dashboard/src/lib/api-errors.test.ts` | Desktop and UI Workflows | 2026-08-11T08:16:24+00:00 | `a291b4e26` | Implement the first remediation items: unwired-route guard, error detail, devcontainer durability, honest designer buttons | 4 |
| `src/Meridian.Ui/dashboard/src/lib/api-errors.ts` | Desktop and UI Workflows | 2026-08-11T08:16:24+00:00 | `a291b4e26` | Implement the first remediation items: unwired-route guard, error detail, devcontainer durability, honest designer buttons | 4 |
| `src/Meridian.Ui/dashboard/src/lib/workspace.test.ts` | Desktop and UI Workflows | 2026-08-11T08:16:24+00:00 | `a291b4e26` | Implement the first remediation items: unwired-route guard, error detail, devcontainer durability, honest designer buttons | 4 |
| `src/Meridian.Ui/dashboard/src/lib/workspace.ts` | Desktop and UI Workflows | 2026-08-11T08:16:24+00:00 | `a291b4e26` | Implement the first remediation items: unwired-route guard, error detail, devcontainer durability, honest designer buttons | 4 |
| `src/Meridian.Ui/dashboard/src/components/meridian/command-palette.view-model.test.ts` | Desktop and UI Workflows | 2026-08-11T08:16:24+00:00 | `a291b4e26` | Implement the first remediation items: unwired-route guard, error detail, devcontainer durability, honest designer buttons | 3 |
| `src/Meridian.Ui/dashboard/src/components/meridian/workspace-nav.view-model.test.ts` | Desktop and UI Workflows | 2026-08-11T08:16:24+00:00 | `a291b4e26` | Implement the first remediation items: unwired-route guard, error detail, devcontainer durability, honest designer buttons | 3 |

