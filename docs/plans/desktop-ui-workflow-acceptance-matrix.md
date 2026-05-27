# Desktop UI Workflow Acceptance Matrix

**Last Updated:** 2026-05-27
**Owner lane:** Workstation Shell and UX
**Scope:** `src/Meridian.Wpf`, `src/Meridian.Ui.Services`, `src/Meridian.Ui.Shared`, with `src/Meridian` host and shared workstation endpoints as integration edges
**Status:** Active acceptance matrix for desktop work inside Waves 2-4

## Purpose

This matrix turns the active desktop and shared UI workflow goal into release criteria. WPF work is accepted only when it closes an operator scenario in the Wave 2, Wave 3, or Wave 4 path and consumes shared services, read models, or workstation endpoints before composing the desktop UI.

The target lanes are:

| Lane | Wave | Acceptance focus |
| --- | --- | --- |
| Lane A | W2 | Trading cockpit reliability: paper-session persistence, replay recovery, promotion readiness, and actionable operator work items. |
| Lane B | W3 | Run -> portfolio -> ledger continuity: shared run context, portfolio/account posture, ledger/cash-flow evidence, and reconciliation handoff continuity. |
| Lane C | W4 | Reconciliation/governance close flow: durable casework, sign-off visibility, close/report workflow posture, and audit/result traceability. |

## Acceptance Rules

1. Desktop business behavior must start in shared contracts, shared services, shared read models, or shared workstation endpoints. WPF may compose and present that behavior, but it must not become the only source of wave-critical state.
2. Every WPF change must name the lane, scenario, shared API/read-model check, and focused WPF test it advances.
3. A desktop-browser mismatch against a shared endpoint, DTO, route target, readiness tone, blocker reason, or work-item action is a release blocker for the workflow claim.
4. Support evidence is not an exit claim. A page, hero, route, or queue affordance counts only when the matching happy path, blocker path, and recovery path have evidence.
5. Fixture/demo payloads can support local development, but they cannot satisfy wave acceptance unless the matrix row explicitly marks them as deterministic harness evidence.

## Scenario Matrix

| Lane | Scenario | Operator outcome | Shared-first contract check | Desktop evidence | Browser/parity check | Release blocker if missing |
| --- | --- | --- | --- | --- | --- | --- |
| A: W2 cockpit | Happy path: restore an active paper session, verify replay, inspect cockpit readiness, and route to the next review action. | Operator can confirm the paper workflow is restart-safe and ready for paper operation from shared evidence. | `GET /api/workstation/trading/readiness`, `GET /api/workstation/operator/inbox`, execution replay endpoints, and `PilotAcceptanceHarnessTests` `PaperPromotion` / `PaperSession` gates. | `TradingWorkspaceShellViewModelTests`, `TradingWorkspaceShellPageTests`, `MainPageUiWorkflowTests`, plus focused shared tests such as `Wave2PaperTradingCockpitAcceptanceTests` and `TradingOperatorReadinessServiceTests`. | Browser Trading cockpit and `/trading/readiness` must show the same readiness posture, UTC evidence timing, and next action. | WPF shows ready while replay, DK1 trust, brokerage sync, promotion, or operator-inbox evidence is stale or blocked. |
| A: W2 cockpit | Blocker path: stale replay, execution-control block, promotion-review gap, brokerage-sync blocker, or security/reconciliation blocker is present. | Operator sees a concrete reason, owner/source when available, and a route to the workbench that can clear the blocker. | Shared `TradingOperatorReadinessDto` work items and `/api/workstation/operator/inbox` route metadata, including account-scoped `fundAccountId` when applicable. | `MainPageViewModel` / queue behavior through `MainPageUiWorkflowTests`, `WorkspaceShellContextServiceTests`, `TradingWorkspaceShellViewModelTests`, and `Wave2OperatorInboxAcceptanceTests`. | Browser readiness summary and Operator Readiness Console must use the same work-item route/action semantics. | Direct action exists only in WPF copy, toast text, or page-local logic instead of a shared work item. |
| A: W2 cockpit | Recovery path: after replay verification or blocker remediation, readiness and queue state refresh without preserving stale blocked state. | Operator can rerun the verification/remediation and see readiness downgrade or recover consistently. | Replay audit metadata, readiness recomputation, and inbox refresh from the shared endpoint path. | `TradingWorkspaceShellViewModelTests`, `TradingWorkspaceShellPageTests`, and `Wave2PaperTradingCockpitAcceptanceTests` recovery cases. | Browser Trading refresh must recover to the same effective level and action list. | A stale in-memory WPF state disagrees with the latest shared readiness or inbox payload. |
| B: W3 continuity | Happy path: open a retained run/session and follow it into portfolio, ledger, cash-flow, and reconciliation context. | Operator can explain how the run changed portfolio and accounting state without switching mental models. | `StrategyRunReadService`, `StrategyRunContinuityService`, `PortfolioReadService`, `LedgerReadService`, `CashFlowProjectionService`, reconciliation projections, and `PilotAcceptanceHarnessTests` `PortfolioLedgerReview` gate. | `StrategyRunPortfolioViewModelTests`, `AccountPortfolioViewModelTests`, `AggregatePortfolioViewModelTests`, `CashFlowViewModelTests`, `FundLedgerViewModelTests`. | Browser Strategy, Portfolio, Accounting, and Reporting routes must agree on selected run/session identity and blocker state. | Desktop derives run/portfolio/ledger meaning from page-local state that cannot be reproduced through shared services. |
| B: W3 continuity | Blocker path: missing run context, brokerage/account sync stale state, ledger-count mismatch, missing cash-flow rows, or reconciliation gap. | Operator sees the missing evidence, impact, and next route instead of a blank grid or silent partial view. | Shared continuity warnings, account-sync readiness DTOs, reconciliation read models, and compatibility tests such as `LedgerReconciliationContractCompatibilityTests`. | `FundAccountsViewModelTests`, `CashFlowViewModelTests`, `FundLedgerViewModelTests`, `AccountPortfolioViewModelTests`, `WorkspaceShellContextServiceTests`. | Browser Portfolio/Accounting should expose the same gap and not silently mark the workflow ready. | WPF hides a continuity gap behind an empty state that looks successful. |
| B: W3 continuity | Recovery path: account sync, run context restore, ledger reconciliation, or filter reset makes retained evidence visible again. | Operator can recover without reloading unrelated workflow state or losing selected context. | Shared account endpoints, `BrokeragePortfolioSyncService`, workstation endpoint DTO compatibility, and `PilotAcceptanceHarnessTests` `Reconciliation` gate. | `BrokeragePortfolioSyncServiceTests`, `FundAccountsViewModelTests`, `FundLedgerViewModelTests`, `StrategyRunContinuityServiceTests`. | Browser and WPF must route brokerage-sync work to the same account-scoped destination and use the same readiness reason. | Recovery exists in one client only or depends on a desktop-only business rule. |
| C: W4 governance | Happy path: review reconciliation casework, sign-off posture, close-lane readiness, and report-pack evidence from one governed context. | Operator can trace issue -> decision -> audit/result view for close/report work. | Operations-continuity endpoints, reconciliation case services, report-pack validation, evidence packet/graph links, and `PilotAcceptanceHarnessTests` `GovernedReportPack` gate. | `FundLedgerViewModelTests`, `FundReconciliationWorkbenchServiceTests`, `FundAccountsViewModelTests`, `OperationsContinuityDtoContractTests`, `GovernanceWorkspaceShellPageTests`. | Browser Accounting/Reporting/Evidence Workbench must expose the same case status, sign-off state, and report-pack blocker semantics. | Desktop presents close/report readiness without durable case status, approval/sign-off, or evidence links. |
| C: W4 governance | Blocker path: unresolved reconciliation case, missing tolerance/sign-off metadata, approval gap, report-pack validation failure, or broken evidence chain. | Operator sees severity, owner/role, required sign-off, decision state, and next action. | `ReconciliationCaseService`, `OperationsContinuityWorkflowService`, `ReportPackValidationService`, and reconciliation calibration/readiness endpoints. | `FundLedgerViewModelTests`, `FundReconciliationWorkbenchServiceTests`, `OperationsContinuityDtoContractTests`, `WorkspaceShellContextServiceTests`. | Browser case queue and report-pack task panels must agree with WPF on the blocker reason and route target. | WPF emits governance-ready language from preview data or endpoint-local fallbacks. |
| C: W4 governance | Recovery path: case decision, approval/sign-off, or report-pack regeneration updates queue, close posture, and audit trail. | Operator can resume close/report work and audit who changed what, when, and why. | Durable operations-continuity timeline, reconciliation case audit history, report-pack lifecycle metadata, and evidence graph references. | `OperationsContinuityWorkflowServiceTests`, `OperationsContinuityPostgresRoundTripTests`, `ReconciliationCaseServiceTests`, plus WPF view-model tests for the consuming surface. | Browser and WPF should agree after refresh on case state, approval posture, and report-pack readiness. | Desktop cannot reconstruct the recovery path after restart or refresh. |

## Milestone Evidence

| Milestone | Required green evidence | Required docs update |
| --- | --- | --- |
| Milestone 1: W2 desktop cockpit acceptance | `Wave2PaperTradingCockpitAcceptanceTests`, `Wave2OperatorInboxAcceptanceTests`, `TradingOperatorReadinessServiceTests`, the focused WPF Trading/MainPage tests touched by the slice, and `PilotAcceptanceHarnessTests` with W2 blockers reflected in `TrustedData`, `PaperPromotion`, or `PaperSession`. | Update the relevant W2 row in `wave-implementation-checklists.md`, then link any produced evidence packet from `ROADMAP.md` or a status evidence doc. |
| Milestone 2: W3 continuity workflows | Focused shared continuity tests, focused WPF Portfolio/Accounting/CashFlow/FundAccounts/FundLedger tests touched by the slice, and `PilotAcceptanceHarnessTests` with W3 blockers reflected in `ResearchRun`, `RunComparison`, `PortfolioLedgerReview`, or `Reconciliation`. | Update W3 checklist blockers and any affected shared-contract or source README notes when DTO/read-model behavior changes. |
| Milestone 3: W4 governance/close baseline | Focused reconciliation/casework/report-pack/operations-continuity tests, focused WPF consuming-surface tests, and `PilotAcceptanceHarnessTests` with W4 blockers reflected in `Reconciliation` or `GovernedReportPack`. | Update W4 checklist blockers and the nearest governance/status evidence doc before claiming close/report readiness progress. |

## Current Support Evidence

- 2026-05-27: Lane C blocker-path support evidence now includes selected Fund Ledger reconciliation-break lifecycle and sign-off posture projected from shared break queue rows into `FundLedgerViewModel`/`FundLedgerPage`. Focused validation: `dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~FundLedgerViewModelTests|FullyQualifiedName~FundReconciliationWorkbenchServiceTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:UseSharedCompilation=false -maxcpucount:1 --logger "console;verbosity=normal"` (9 passed). This is W4 support evidence only; durable close/casework acceptance still requires the operations-continuity and browser-parity gates in the matrix.
- 2026-05-27: Lane B blocker-path support evidence now includes Run Cash Flow continuity posture projected from `StrategyRunContinuityService` into `CashFlowViewModel`/`RunCashFlowPage`. Focused validation: `dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~CashFlowViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:UseSharedCompilation=false -maxcpucount:1 --logger "console;verbosity=normal"` (15 passed). This is W3 support evidence only; Milestone 2 still requires the broader run -> portfolio -> ledger -> reconciliation happy/blocker/recovery proof and browser parity.

## Validation Command Patterns

Use the narrowest command that covers the changed scenario. Typical proof lanes are:

```powershell
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~Wave2PaperTradingCockpitAcceptanceTests|FullyQualifiedName~Wave2OperatorInboxAcceptanceTests|FullyQualifiedName~TradingOperatorReadinessServiceTests" --logger "console;verbosity=normal"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PilotAcceptanceHarnessTests" --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~TradingWorkspaceShellViewModelTests|FullyQualifiedName~TradingWorkspaceShellPageTests|FullyQualifiedName~MainPageUiWorkflowTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~FundLedgerViewModelTests|FullyQualifiedName~FundAccountsViewModelTests|FullyQualifiedName~CashFlowViewModelTests|FullyQualifiedName~AccountPortfolioViewModelTests" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
```

Broaden only when shared DTOs, endpoint behavior, route metadata, source README contracts, or both browser and desktop consumers changed.

## Release Blocker Checklist

Before accepting any desktop workflow slice, confirm:

- The scenario names one W2, W3, or W4 acceptance row above.
- Shared endpoint/read-model behavior exists or is intentionally unchanged.
- WPF presents shared state and does not fork business logic.
- Browser parity is either verified or the mismatch is logged as a blocker.
- The matching pilot-readiness stage is green or has a blocker with owner, expected evidence, and target follow-up.
- The status/planning docs identify the evidence packet or explicitly say the claim is support evidence only.
