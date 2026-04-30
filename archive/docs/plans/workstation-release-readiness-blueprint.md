# Workstation Release Readiness Blueprint

**Owner:** Core Team
**Audience:** Product, Architecture, UI, API, and Platform contributors
**Last Updated:** 2026-04-09
**Status:** Historical blueprint — pre desktop-only transition

> **Historical note (2026-04-09):** This blueprint describes the retired standalone browser workstation (`src/Meridian.Ui`, React `/workstation`, and browser bootstrap routes). Keep it for sequencing/history only; any remaining workstation delivery work should be re-scoped to WPF and the retained desktop-local API surface.

---

## Summary

This blueprint turns the current workstation shell into a release-ready operator experience by locking three product decisions:

- Trading v1 ships with constrained write actions in the first release.
- Delivery priority remains `Trading -> Data Operations -> Governance`.
- Workspace payloads are prefetched at shell load for fast workspace switching, with heavier detail fetched lazily by subroute.

The goal is to move Meridian from a mostly observational dashboard to a safe operator cockpit without widening scope into full live-routing parity. The existing web shell, workstation endpoints, route model, and typed Trading/Research payloads stay in place and are extended rather than replaced.

---

## Scope

### In scope

- Web workstation UI under `src/Meridian.Ui/dashboard`.
- Workstation API payloads under `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`.
- Trading v1 operational controls for paper-mode-first workflows.
- Data Operations MVP slices after Trading.
- Governance read-only operational slices after Data Operations.
- Shell prefetch strategy for workspace bootstrap data.
- Release hardening for loading, authorization, confirmation, telemetry, and audit feedback.
- Sprint board with implementation stories and acceptance criteria.

### Out of scope

- WPF parity work.
- Full live broker rollout and unrestricted live-write operations.
- New engine research features unrelated to workstation release readiness.
- Deep Governance polish before Trading and Data Operations are usable.

### Decisions captured from product direction

1. Trading v1 includes write actions in first release.
2. Governance does not move ahead of Data Operations polish.
3. Workspace payloads are prefetched on shell load.

### Assumptions

- Trading write actions target paper mode first, with live mode gated behind explicit authorization and later rollout.
- Existing workstation endpoints remain the preferred integration path.
- Current shell bootstrap already proves the prefetch pattern with `session`, `research`, and `trading`; the same pattern will expand to Data Operations and Governance summaries.

---

## Architecture

### Current grounded state

The release plan builds on these existing implementation anchors:

- `src/Meridian.Ui/dashboard/src/app.tsx` already owns the shell, route resolution, and top-level workspace rendering.
- `src/Meridian.Ui/dashboard/src/hooks/use-workstation-data.ts` already prefetches `session`, `research`, and `trading` on shell load.
- `src/Meridian.Ui/dashboard/src/lib/api.ts` already centralizes typed workstation fetches.
- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` already exposes `/session`, `/research`, `/trading`, and `/governance`, plus governance-related read routes.
- `src/Meridian.Ui/dashboard/src/components/meridian/command-palette.tsx` is still limited to workspace jumps and two research-only quick actions.
- `src/Meridian.Ui/dashboard/src/screens/workspace-placeholder.tsx` is still used for Data Operations and Governance.

### Release-ready target

#### 1. Trading becomes an operator cockpit, not just a monitor

Trading v1 should ship these panels:

- positions
- open orders
- fills
- P&L snapshot
- brokerage wiring status
- risk summary and guardrails

Trading v1 should also ship these constrained write actions:

- cancel single order
- cancel all open orders
- close position
- pause strategy

Each write path must include:

- explicit confirmation
- disabled busy state while request is in flight
- success and failure feedback at action scope
- authorization check before action is shown or enabled
- audit and telemetry event emission
- idempotent API behavior where feasible

`flatten positions` and broader live controls remain follow-on unless they can meet the same safety bar without delaying release.

#### 2. Data Operations is the second release slice

After Trading, Data Operations should replace its placeholder with thin operational views:

- provider health
- active backfill queue
- recent export jobs

These should remain operationally read-heavy at first, with scoped actions only where the backend already supports safe retries or cancellations.

#### 3. Governance is real, but follows Data Operations

Governance should ship as a usable read-first slice in the same release wave, but without taking polish priority ahead of Data Operations. The first Governance release should expose:

- reconciliation runs list and detail
- ledger summary
- trial balance
- journal view
- security master search/detail

#### 4. Shell prefetch becomes the default workspace loading model

The workstation shell should prefetch summary payloads for all four workspaces on initial load:

- `/api/workstation/session`
- `/api/workstation/research`
- `/api/workstation/trading`
- `/api/workstation/data-operations`
- `/api/workstation/governance`

Heavier panels should continue to lazy-load below the workspace route, for example:

- Trading action dialogs and order detail
- Governance journal filters and security history
- Data Operations backfill detail and export detail

This keeps first navigation responsive without forcing the shell to load every large detail dataset up front.

#### 5. Command palette and route model must match the IA

The existing top-level route model should stay stable, but the shell should add subroutes and matching command palette entries:

- `/trading/orders`
- `/trading/positions`
- `/trading/risk`
- `/data-operations/providers`
- `/data-operations/backfills`
- `/data-operations/exports`
- `/governance/ledger`
- `/governance/reconciliation`
- `/governance/security-master`

Release criterion: every primary workflow reachable from navigation must also be reachable from the command palette.

---

## Interfaces and Models

### Web client surface

Extend `src/Meridian.Ui/dashboard/src/lib/api.ts` toward a single workstation client surface:

```ts
export function getSession(): Promise<SessionInfo>;
export function getResearchWorkspace(): Promise<ResearchWorkspaceResponse>;
export function getTradingWorkspace(): Promise<TradingWorkspaceResponse>;
export function getDataOperationsWorkspace(): Promise<DataOperationsWorkspaceResponse>;
export function getGovernanceWorkspace(): Promise<GovernanceWorkspaceResponse>;
export function cancelTradingOrder(orderId: string): Promise<TradingActionResult>;
export function cancelAllTradingOrders(scope: TradingActionScope): Promise<TradingActionResult>;
export function closeTradingPosition(positionId: string): Promise<TradingActionResult>;
export function pauseTradingStrategy(strategyRunId: string): Promise<TradingActionResult>;
```

### Hook strategy

Split shell bootstrap from detail fetching:

- `useWorkstationBootstrapData()` for shell-level prefetch of summary payloads.
- workspace hooks for detail screens and subroutes:
  - `useTradingWorkspace()`
  - `useDataOperationsWorkspace()`
  - `useGovernanceWorkspace()`

The current `useWorkstationData()` can be evolved into the bootstrap hook rather than discarded.

### Additive contracts

Add these DTO groups in `src/Meridian.Ui/dashboard/src/types.ts` and matching workstation contracts on the server side:

```ts
interface TradingActionResult {
  actionId: string;
  status: "Accepted" | "Completed" | "Rejected" | "Failed";
  message: string;
  occurredAt: string;
}

interface DataOperationsWorkspaceResponse {
  providerHealth: ProviderHealthRecord[];
  backfills: BackfillJobRecord[];
  exports: ExportJobRecord[];
}

interface GovernanceWorkspaceResponse {
  reconciliationRuns: ReconciliationRunSummary[];
  ledgerSummary: LedgerSummary | null;
  trialBalance: LedgerTrialBalanceLine[];
  journalPreview: LedgerJournalLine[];
  securityCoverage: SecurityMasterWorkstationDto[];
}
```

### API direction

Add or extend workstation endpoints to support:

- Data Operations summary payload
- Trading write-action endpoints
- Governance summary payload optimized for shell prefetch

Trading write endpoints should return action results that support:

- immediate UI confirmation
- audit correlation
- telemetry correlation
- safe retry messaging

---

## Data Flow

### Shell bootstrap

1. User loads `/workstation`.
2. Shell renders navigation and header immediately.
3. Bootstrap hook prefetches session plus all workspace summary payloads.
4. Shell stores successful payloads in shared client state.
5. User navigation across top-level workspaces reads from prefetched state with no blocking spinner unless data is stale or missing.

### Trading write action flow

1. Operator opens Trading workspace.
2. UI displays prefetched orders, positions, fills, risk, and brokerage state.
3. Operator chooses `cancel`, `close`, or `pause`.
4. UI shows confirmation with action-specific risk copy.
5. UI submits action request and disables duplicate submission.
6. API validates authorization, environment, and target state.
7. API executes action or rejects it with a structured reason.
8. UI shows action result and refreshes affected Trading panels.
9. Audit and telemetry capture action intent, result, actor, and timestamp.

### Detail fetch model

1. User enters a subroute like `/governance/security-master`.
2. Screen loads from summary payload first where possible.
3. Screen fetches heavier detail only for the selected record or filtered view.
4. Errors stay localized to the panel or table rather than failing the full workstation shell.

---

## Edge Cases and Risks

- Trading write actions can shift the release from dashboard semantics to operator semantics.
  Mitigation: keep actions narrow, confirmed, authorized, audited, and observable.

- Shell prefetch can become expensive as workspace summaries grow.
  Mitigation: prefetch summaries only; push details and histories behind subroute fetches.

- A partial command palette will make the IA look less complete than the underlying routes.
  Mitigation: treat command coverage as a release gate, not a nice-to-have.

- Governance can sprawl into a large accounting UI before core operator workflows are stable.
  Mitigation: keep Governance read-first and prioritize Data Operations polish ahead of Governance depth.

- Trading action APIs can fail in ways that leave the UI ambiguous.
  Mitigation: return structured action results, refresh affected slices after every mutation, and display correlated audit identifiers when available.

---

## Release Gates

1. All four workspaces render real content, not generic placeholders.
2. Trading v1 exposes positions, orders, fills, P&L, risk, and brokerage state.
3. Trading v1 includes `cancel order`, `cancel all open orders`, `close position`, and `pause strategy` behind confirmation and authorization.
4. Data Operations exposes provider health, backfills, and exports as real workstation slices.
5. Governance exposes reconciliation, ledger, and security master read surfaces.
6. Shell bootstrap prefetched summaries cover all four workspaces.
7. Every primary workflow has both a route and a command palette entry.
8. Write actions emit audit and telemetry data and have end-to-end test coverage.

---

## Sprint Board

### Sprint 1: Shell and Trading foundation

**Story 1.1: Expand shell bootstrap to all workspaces**
Acceptance criteria:

- `useWorkstationData()` or its successor prefetches session, research, trading, Data Operations, and Governance summary payloads.
- Shell bootstrap failure does not blank already successful workspace summaries.
- Top-level workspace switching is instant after bootstrap success.
- Bootstrap tests cover mixed-success and full-failure cases.

**Story 1.2: Add route and command palette coverage for all primary workflows**
Acceptance criteria:

- Trading, Data Operations, and Governance subroutes exist for the primary release workflows.
- Command palette includes matching deep links for each primary workflow.
- No quick action routes to `/` as a placeholder for non-research workflows.
- Navigation smoke tests cover one route and one command per workspace.

**Story 1.3: Replace Data Operations and Governance placeholders with MVP screens**
Acceptance criteria:

- `/data-operations` renders provider health, backfill, and export summaries.
- `/governance` renders reconciliation, ledger, and security preview content.
- Placeholder copy is removed from release routes.
- Empty and error states are panel-scoped and retryable.

### Sprint 2: Trading write-path release slice

**Story 2.1: Implement Trading action endpoints and typed client wrappers**
Acceptance criteria:

- API routes exist for cancel order, cancel all open orders, close position, and pause strategy.
- Typed client functions exist in the dashboard API layer.
- Action responses return a structured action result.
- Contract tests verify success, validation failure, and unauthorized access behavior.

**Story 2.2: Add Trading action UX with confirmations and safe busy states**
Acceptance criteria:

- Orders and positions tables expose contextual action affordances.
- Every action requires an explicit confirmation step.
- Double submission is prevented while a request is in flight.
- Success and failure are shown inline without forcing a full page reload.

**Story 2.3: Wire audit and telemetry for Trading mutations**
Acceptance criteria:

- Each Trading write action logs actor, action, target, timestamp, and outcome.
- UI action results expose enough metadata to correlate with backend audit records.
- Failed actions produce actionable user feedback rather than generic errors.
- Automated tests verify audit emission for successful and rejected actions.

### Sprint 3: Release hardening and operator confidence

**Story 3.1: Finish workspace-level resilience behavior**
Acceptance criteria:

- Prefetched summaries can be refreshed without dropping the shell.
- Panel-level retries exist for Trading, Data Operations, and Governance.
- Last successful payload remains visible during transient refresh failures.
- Error copy is specific to the failed workspace or action.

**Story 3.2: Add end-to-end operator smoke coverage**
Acceptance criteria:

- Automated smoke flow opens the shell and visits all four workspaces.
- Command palette executes at least one action per workspace.
- Trading smoke covers at least one write action in a test-safe environment.
- No route returns 404 and no shell-breaking console errors occur.

**Story 3.3: Release review checklist and sign-off pack**
Acceptance criteria:

- Release checklist exists for Trading write controls, Data Operations readiness, and Governance minimum slice coverage.
- Roles and permissions are verified for write-action visibility.
- Audit evidence and telemetry dashboards are linked from the release checklist.
- Product and engineering sign-off criteria are documented in the release notes package.

---

## Test Plan

### Web UI

- `pnpm --dir src/Meridian.Ui/dashboard test`
- Add tests for:
  - shell prefetch bootstrap behavior
  - workspace route resolution
  - command palette deep links
  - Trading confirmation and mutation UX
  - panel-scoped loading, error, and retry handling

### API and contracts

- `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true`
- Add contract tests for:
  - Data Operations and Governance workstation payloads
  - Trading action endpoint success and failure paths
  - authorization failures on Trading mutations

### Smoke and release validation

- Open `/workstation`
- Navigate to each workspace and one subroute per workspace
- Execute one command palette action per workspace
- Execute at least one Trading write action in paper mode
- Verify audit result and UI state refresh after the mutation

---

## Open Questions

1. Should `cancel all open orders` be available globally in Trading v1 or only within strategy-scoped panels?
2. Should `close position` operate at symbol level only in v1, leaving basket flattening for a later release?
3. Should Governance shell prefetch include full trial balance lines, or only a compact ledger summary with lazy detail loading?
