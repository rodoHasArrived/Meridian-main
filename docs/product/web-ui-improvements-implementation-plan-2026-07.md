# Browser Workstation UI Improvements — Implementation Plan (2026-07)

> **Status:** Proposed · **Created:** 2026-07-02 · **Owner lane:** Workstation Shell and UX
> **Source:** [Browser Workstation UI Improvements Brainstorm (2026-07)](web-ui-improvements-brainstorm-2026-07.md)
> **Depth:** phased implementation plan (all nine brainstorm ideas), code-ready anchors, no code in this change

This plan sequences the nine brainstorm ideas into seven phases. Each phase names the files to
touch, the contracts to add, the test plan, and the narrowest validation command. Facts below
(seams, existing precedents, gaps) were verified against current source on 2026-07-02.

## Phase Order and Rationale

| Phase | Ideas | Effort | Why this order |
|-------|-------|--------|----------------|
| 0 | Conventions baseline | S | Shared rules every later phase relies on |
| 1 | #2 Freshness chips | S | Days of work, immediate trust payoff, defines the status vocabulary |
| 2 | #3 Palette verbs | S/M | Keyboard-first quick win on shipped palette foundation |
| 3 | #4 Deep links + saved views | S/M | Collaboration quick win; defines the view-state envelope Phases 5–6 reuse |
| 4 | #1 Live data spine (SSE) | L | The platform bet; lands behind a polling fallback |
| 5 | #5 Notification center | M | Push-fed from day one once Phase 4 lands |
| 6 | #6 Sticky scope · #7 Virtualized grids · #8 Chart sync · #9 Pop-outs | M each | Institutional-scale and analyst-depth workhorses; #9 sequenced last so pop-outs share the Phase 4 stream |

---

## Phase 0 — Conventions Baseline (S)

No feature work; confirm and document the rules each later phase must follow.

- **Persisted client state:** localStorage keys use the `meridian.workstation.<feature>.vN`
  pattern with an in-payload `version` discriminant and a defensive normalize-on-read pass.
  Template: `src/Meridian.Ui/dashboard/src/lib/sql-workbench-storage.ts`
  (`SQL_WORKBENCH_STORAGE_KEY = "meridian.workstation.dataQuery.v1"`).
- **New API routes:** constants in `src/Meridian.Contracts/Api/UiApiRoutes.cs` (PascalCase,
  area-prefixed), mirrored to TypeScript via `build/scripts/generate-ui-api-routes-ts.py` into
  `src/Meridian.Ui/dashboard/src/lib/ui-api-routes.generated.ts`. Endpoints live in a
  `WorkstationEndpoints` partial (`src/Meridian.Ui.Shared/Endpoints/`), mapped inside the
  `/api/workstation` group (`MapGroup(...).RequireWorkstationTenantScope()`), resolving services
  per-request via `context.RequestServices.GetService<T>()` and returning 503 when unregistered.
- **Endpoint tests:** in-proc TestServer harness in `tests/Meridian.Tests/Ui/` — pattern at
  `tests/Meridian.Tests/Ui/WorkflowLibraryEndpointTests.cs:95-114` (`WebApplication.CreateBuilder`
  → `UseTestServer()` → register services → `MapWorkstationEndpoints` → `GetTestClient()`),
  xUnit + FluentAssertions, camelCase JSON with `JsonStringEnumConverter`.
- **Client fetches:** exported wrappers `apiGetJson<T>` / `apiPostJson<T>` in
  `src/Meridian.Ui/dashboard/src/lib/api.ts`; mutations attach the CSRF header via
  `buildMutationHeaders()` (`mdc-csrf` cookie → `X-CSRF-Token`).

**Checklist**
- [x] Conventions above linked from the first implementation PR description.

---

## Phase 1 — Freshness Chips (idea #2, S)

**Goal:** every high-frequency card answers "is this number current?" at a glance.

- **New primitive:** `FreshnessChip` in `src/Meridian.Ui/dashboard/src/components/ui/`
  (paired `.view-model.ts` + tests, matching sibling primitives). Props: `lastSucceededAt`,
  `staleBudgetMs`, `errorMessage?`, `live?` (Phase 4 flips this on). States: fresh (quiet),
  aging (amber past budget), failing (red, hover shows error + retry metadata), live (green dot).
- **Hook gap to close first:** `src/Meridian.Ui/dashboard/src/hooks/use-request-lifecycle.ts`
  tracks `settledAt` on ANY settle (succeed/fail/abort), not success only. Add an additive
  success-only `lastSucceededAt: string | null` to `RequestLifecycleStatus`, set inside
  `succeed()`. Backoff metadata (`RequestBackoffMetadata`) already exists for the hover detail.
- **Adoption pass (in order of refresh frequency):** live quotes header
  (`screens/live-quotes-screen.tsx`), watchlist (`screens/watchlist-screen.tsx`), portfolio
  summary (`screens/portfolio-screen.tsx`), close cockpit
  (`screens/accounting-screen.close-cockpit-panels.tsx`), reporting run status
  (`screens/reporting-screen.run-status-modules.tsx`). Keep it off static reference data.
- **Staleness budgets:** per-surface constants beside each screen's poll interval constant
  (e.g. next to `LIVE_QUOTES_POLL_INTERVAL_MS`), roughly 2× the poll interval.
- **Tests:** view-model unit tests for state transitions (fresh → aging → failing), plus one
  adoption assertion per screen test file.
- **Validation:** `npm --prefix src/Meridian.Ui/dashboard run test`

**Checklist**
- [x] `lastSucceededAt` added to `use-request-lifecycle.ts` with tests.
- [x] `FreshnessChip` primitive + view-model + tests.
- [x] Adopted on the five surfaces above.

---

## Phase 2 — Command Palette Verbs (idea #3, S/M)

**Goal:** the palette runs actions, not just navigation.

- **Current state:** every `CommandPaletteItem` resolves to a `route`
  (`command-palette.view-model.ts`, kinds `focus | entity | workspace | route | workflow |
  preset`; static routes in `LOCAL_ROUTE_COMMANDS`). No client-side "run a function" concept.
- **New contract:** add kind `"action"` and a `CommandPaletteActionProvider` interface
  (`id`, `verbLabel`, `keywords`, `run(): Promise<void>`, `confirmLabel?`). A registry module
  (`command-palette.actions.ts`) collects providers; `buildCommandPaletteViewModel` merges them
  into the existing `COMMAND_KIND_ORDER` flow. Items with `confirmLabel` render an inline
  confirm row (no modal) before `run()` fires; results surface via the existing `useToast` API.
- **First providers (delegating to existing surfaces, no new business logic):**
  - workflow preset pin / unpin (`POST /api/workstation/workflows/presets/{id}/pin`), built at shell
    level from the preset library;
  - reporting export run (`screens/reporting-screen.exports-runner.tsx` command path), registered by
    the reporting screen while mounted with confirm required.
  - *Amended at implementation:* close-calendar item creation was dropped as a palette verb — it
    requires a six-field form, which is a navigation target rather than a verb; it remains reachable
    through existing workflow route items.
- **Governance rule (enforced by convention + review):** approval-gated or destructive mutations
  are never palette-registered; providers must delegate to existing view-model commands.
- **Tests:** extend `command-palette.view-model.test.ts` (action merge, ordering, confirm flow)
  and `command-palette.test.tsx` (keyboard: Enter on action item, Escape cancels confirm).
- **Validation:** `npm --prefix src/Meridian.Ui/dashboard run test`

**Checklist**
- [x] `action` kind + provider registry + inline confirm.
- [x] Launch providers wired to existing POST surfaces (preset pin/unpin + reporting export run;
      close-calendar dropped as form-shaped).
- [x] Palette keyboard tests cover the action path.

---

## Phase 3 — Copy-Link Deep Links + Saved Views (idea #4, S/M)

**Goal:** any operator view is shareable as a URL; named views persist server-side.

- **View-state envelope (the primitive Phases 5–6 reuse):** versioned, compact-serialized
  `{ v: 1, scope, screen, viewState }` in a new `lib/view-state-envelope.ts`. Generalizes the
  proven URL-scope pattern in `app-shell.operating-scope.ts`
  (`appendOperatingScopeToRoute` / `readOperatingScopeFromSearch` /
  `removeOperatingScopeFromSearch`). Screen-local `viewState` starts with `useTableState`'s
  persisted shape (`query`, `sortBy`, `filters`) plus active task mode and selected row id.
- **Copy-link affordance:** `workspace-header.tsx` currently renders only the refresh button in
  its `workstation-actions` container — add an `actions?: ReactNode` prop, then a `CopyLink`
  button that serializes the current envelope to the clipboard with a toast confirmation.
- **Hydration:** on route load, screens hydrate `viewState` after permission checks — role-gated
  task modes re-verify via the existing role permission catalog rather than trusting the link.
- **Saved views:** extend the existing preset payload handled by `WorkflowPresetService` /
  `FileWorkflowPresetStore` (`{dataRoot}/workstation/workflows/workflow-presets.json`, atomic
  writes via `AtomicFileWriter`, versioned `WorkflowPresetSnapshot`) with an optional
  `viewStateEnvelope` field — no new store. Saved views then surface through the existing preset
  items in the palette and workflow continuity dock. *Implementation note:* the field landed as an
  additive nullable property on snapshot v1 (no v2 bump — additive nullable is bidirectionally
  compatible under System.Text.Json Web defaults), capped at 4096 chars server-side.
- **Tests:** envelope round-trip + version-tolerance unit tests; `WorkflowPresetService` payload
  extension tests in `tests/Meridian.Tests/Ui/WorkflowLibraryEndpointTests.cs` style.
- **Validation:** `npm --prefix src/Meridian.Ui/dashboard run test` and
  `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true --filter FullyQualifiedName~WorkflowLibraryEndpointTests`

**Checklist**
- [x] `view-state-envelope.ts` with round-trip tests (table-state helpers shipped as the Phases 5–6
      contract; no screen consumer yet since `useTableState` is currently unconsumed).
- [x] `workspace-header.tsx` `actions` slot + Copy-link button (+ Save-view dialog).
- [x] Preset payload extension + server tests.

---

## Phase 4 — Live Data Spine (idea #1, L)

**Goal:** one SSE channel replaces per-screen polling, with polling kept as fallback.

- **Server precedent:** `GET /api/events/stream`
  (`src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:205-245`) already writes
  `text/event-stream` from a 2-second polling loop. Step 1 reuses exactly that shape:
  `GET /api/workstation/stream?topics=…` in a new `WorkstationEndpoints.Stream.cs` partial,
  poll-bridging the same read models the REST endpoints use, emitting named SSE events per topic
  (`quotes:{symbol}`, `workspace:{key}`, `inbox`, `report-run:{id}`) with heartbeat comments.
  Route constant `WorkstationStream` in `UiApiRoutes.cs`, TS mirror regenerated.
- **Step 2 (separately reviewable):** true push fan-out. `EventPipeline`
  (`src/Meridian.Application/Pipeline/EventPipeline.cs`) exposes no subscribe hook — flow is
  `Channel<TracedMarketEvent>` → consumer → `_sink.AppendAsync`. Add an opt-in broadcast seam
  (bounded, drop-oldest, created via `EventPipelinePolicy.*.CreateChannel<T>()`) either as an
  `IStorageSink` decorator or a host-level event bus, feeding topic buffers the SSE loop drains.
  Backpressure policy: coalesce per topic (latest snapshot wins) — never block the hot path.
- **Auth:** SSE is GET-only under existing session-cookie auth + `RequireWorkstationTenantScope`;
  no CSRF header needed. Cap concurrent streams per session; close on session expiry.
- **Client:** new `hooks/use-workstation-stream.ts` sharing one `EventSource` across the app
  (module-level ref-counted subscription map). `lib/api.ts` has no streaming today — keep the
  EventSource wiring in the hook, not the api module. Integration point: the pollers in
  `use-workstation-data.ts` (`window.setInterval` ~line 987),
  `live-quotes-screen.view-model.ts:659`, `watchlist-screen.view-model.ts:542`, and
  `lib/price-alerts/service.ts:211` subscribe to topics and **suspend their intervals while the
  stream is healthy**, resuming on stream error — fallback stays first-class.
- **Freshness integration:** stream-fed surfaces flip `FreshnessChip` to `live`.
- **Tests:** endpoint test asserting content type, topic filtering, and heartbeat via the
  TestServer harness; client hook tests with a stubbed EventSource (connect, dispatch, error →
  fallback resume).
- **Validation:** `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true --filter FullyQualifiedName~WorkstationStream` and
  `npm --prefix src/Meridian.Ui/dashboard run test`

**Checklist**
- [ ] `WorkstationEndpoints.Stream.cs` poll-bridge SSE endpoint + route constant + TS mirror.
- [ ] `use-workstation-stream.ts` shared EventSource hook with fallback semantics.
- [ ] Poller suspension wired on the four polling sites.
- [ ] Step-2 fan-out seam (separate PR, hot-path review required).

---

## Phase 5 — Shell Notification Center (idea #5, M)

**Goal:** transient toasts become a durable, deep-linked inbox.

- **Envelope:** common notification shape (`id`, `severity`, `timestamp`, `title`, `link`
  (Phase 3 envelope URL), `source`, `read`). Merges three feeds:
  - operator inbox: `OperatorInboxDto` from `BuildOperatorInboxAsync`
    (`WorkstationEndpoints.cs:3594`) — already deduped by `WorkItemId` and priority-scored;
  - price alerts: `lib/price-alerts/service.ts` events;
  - workflow-continuity/system events from the app-shell view models.
  Dedup between the inbox screen and the center is by shared `WorkItemId`.
- **UI:** masthead bell with unread count in `app.tsx`'s `workstation-masthead` (beside the
  existing `PriceAlertsBell`, which it replaces), opening a right-side `Sheet`
  (`components/ui/sheet.tsx`) with severity grouping and per-item deep links. Every toast shown
  via `useToast` for these feeds is also written to the center — nothing is toast-only.
  Low-severity events default to inbox-silent (no toast).
- **Read state:** net-new small file-backed store following the `FileWorkflowPresetStore`
  template (snapshot record, `SemaphoreSlim`, `AtomicFileWriter`, STJ source-gen) — the only
  mutable inbox store today (`InMemoryOperatorInboxService`) is `INonProductionOnlyService`.
  Endpoints: `GET/POST /api/workstation/notifications/read-state` in a new partial.
- **Delivery:** polls on the workspace cadence until Phase 4; then subscribes to the `inbox` topic.
- **Tests:** merge/dedup/severity unit tests; read-state endpoint tests; bell + sheet interaction
  tests.
- **Validation:** `npm --prefix src/Meridian.Ui/dashboard run test` and
  `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true --filter FullyQualifiedName~NotificationReadState`

**Checklist**
- [ ] Notification envelope + three-feed merge with dedup tests.
- [ ] Bell + sheet UI; toasts mirrored into the center.
- [ ] File-backed read-state store + endpoints + tests.

---

## Phase 6 — Scale and Depth Workhorses (ideas #6–#9, M each, parallelizable)

### 6a. Editable, sticky operating scope (#6)
- Scope already appends to palette and nav routes (`materializeCommandRoute` in
  `command-palette.view-model.ts`; `workspace-nav.view-model.ts`) and clears via
  `removeOperatingScopeFromSearch` in `app.tsx`. Extend with: a scope **picker** (entity-search
  backed, reachable from the `WorkflowContinuityDock` chips and a palette action), persistence
  under `meridian.workstation.operatingScope.v1`, and app-shell route-change interception so
  scope params survive navigation. Screens that ignore a dimension render it dimmed with an
  explicit "not filtered by fund" hint. Migrate workspace-by-workspace: Portfolio → Accounting →
  Reporting → Trading.

### 6b. Virtualized dense grids (#7)
- `DenseDataTable` (`components/meridian/ui-kit-primitives.tsx`) soft-caps via
  `maxVisibleRows`/`rows.slice` + "show all" — not windowing. Add real row virtualization inside
  the component behind the `dense-row-detail-accessibility.tsx` contract: stable row heights,
  `aria-rowcount`/`aria-rowindex`, focus surviving unmount, Home/End/type-ahead addressing the
  virtual set, Escape-back-to-row scrolling the row into the window. Keep detail in the adjacent
  `DenseRowDetailPanel` (contract already prescribes this; avoids variable-height rows).
  First adopters: trial balance, journal entries, reconciliation breaks, financial record explorer.

### 6c. Linked chart crosshair + evidence drill (#8)
- Charts (`components/charts/`) are stateless SVG: `crosshairIndex` prop flows IN, no events flow
  OUT. Add optional `onCrosshairChange(index)` / `onPointActivate(index)` props to `CandleChart`
  and `EquityCurve`, plus a per-screen `ChartSyncContext` provider (hovered timestamp + selected
  range, timestamp-normalized across differing x-domains). Point activation feeds the screen's
  existing `*.linked-context.ts` / `*.evidence-timeline.ts` modules. Scope the context per screen
  — never global.

### 6d. Pop-out companion panes (#9)
- Every route currently renders inside masthead + shell chrome in `app.tsx` — add an early
  chrome-less branch (`/panes/watchlist`, `/panes/live-quotes`) returning a blank layout before
  the shell markup. Cross-window state via a same-origin `BroadcastChannel` bridge (scope,
  appearance, and — after Phase 4 — fanned-out stream data so children don't open their own
  EventSource). Open with `noopener`; because `window.open` with `noopener` returns `null` even
  on success, never infer popup-blocker state from the return value — always render a persistent
  visible fallback link (`target="_blank" rel="noopener noreferrer"`). Pane components must be
  shell-independent; session expiry propagates over the channel.

**Validation for all of Phase 6:** `npm --prefix src/Meridian.Ui/dashboard run test`; grid work
additionally re-runs the a11y suites (`*-screen.a11y.test.tsx`,
`dense-row-detail-accessibility.test.tsx`).

**Checklist**
- [ ] 6a scope picker + persistence + Portfolio migration.
- [ ] 6b virtualization inside `DenseDataTable` with a11y contract tests green.
- [ ] 6c chart callback props + `ChartSyncContext` + one evidence-drill screen.
- [ ] 6d chrome-less pane branch + BroadcastChannel bridge + popup fallback link.

---

## Cross-Cutting Risks

- **One envelope, three consumers:** Phases 3, 5, and 6a must share the versioned view-state
  envelope; design it in Phase 3 and treat its schema as a contract.
- **Hot-path safety (Phase 4 step 2):** any `EventPipeline` fan-out must be bounded, non-blocking,
  and use `EventPipelinePolicy` channels — reviewed against the durability guardrails.
- **A11y regression surface (6b):** virtualization inside `DenseDataTable` touches the shared
  keyboard contract; the dense-row a11y tests are the merge gate.
- **Notification fatigue (Phase 5):** severity defaults reviewed with the simulated user panel
  before enabling toasts for new feeds.
