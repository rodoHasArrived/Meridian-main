# Blueprint — Report Writer Debounced Live Auto-Preview

**Idea source:** 2026-07-13 report-builder UX brainstorm (idea #2, highest-leverage pick).
**Surface:** Browser workstation report builder, `src/Meridian.Ui/dashboard/`.
**Depth mode:** full.

Today every mutation in the report-writer designer — token drop, filter edit, chart toggle,
formula keystroke — calls `clearWriterPreview`, which blanks the rendered preview until the
operator manually clicks **Preview** again. The render endpoint is fast, sample-sized, and
idempotent; the operator is doing the debouncing by hand. This blueprint replaces
clear-on-edit with a stale-mark + debounced auto-render loop so the preview stays on screen,
dims while out of date, and refreshes itself.

No server-side changes are required. `renderReportTemplate` (`src/Meridian.Ui/dashboard/src/lib/api.ts`)
already accepts `ApiRequestOptions { signal?: AbortSignal }`, and the render endpoint is
stateless per request.

---

## Scope

**In Scope:**
- Debounced (~600 ms) automatic re-render of a designer grid's preview after any draft
  mutation, per grid, in `useReportingReportWriter`
  (`src/Meridian.Ui/dashboard/src/screens/reporting-screen.report-writer.tsx`).
- Stale-state presentation: the last good preview stays visible, dimmed, with an
  "updating…" status pill and `aria-live` announcement instead of vanishing.
- In-flight cancellation via `AbortController` plus a per-grid request sequence guard so
  only the newest response is applied.
- Per-grid **Auto preview** toggle (session-scoped, default on) and retention of the manual
  **Preview** button as the explicit "render now" path.
- Quiet per-grid error handling for auto renders (inline, non-modal), with error display
  held while the operator is still typing in a free-text input (custom formula expression,
  custom dataset rows).
- Payload guard: auto-render is suspended (manual-only, with a hint) when the selected
  dataset payload exceeds a row threshold.

**Out of Scope:**
- Draft persistence / resume (brainstorm idea #3) — separate blueprint.
- Multi-filter builder, chart column pickers, formula workbench (ideas #4–#6).
- Any change to `ReportWriterGridEngine`, `Meridian.Ui.Shared` endpoints, or DTO contracts.
- WPF desktop parity. The behavior is client-side over the shared
  `renderReportTemplate` seam; a matching WPF implementation is a later
  `W8-WPF-PARITY-001` follow-on and inherits nothing from this change except the pattern.
- Persisting the auto-preview toggle across sessions (belongs with idea #3's persistence
  layer).

**Assumptions:**
- Fixture preview profiles produce 4 rows and custom datasets are capped at 100 rows
  (`parseReportWriterCustomDatasetRows`), so a debounced render is cheap. Retained dataset
  sources (`source:` profiles) ship their sample rows in the request body — the payload
  guard below exists because their size is not statically bounded in the UI.
- The dashboard test stack is Vitest + @testing-library/react under jsdom
  (`npm --prefix src/Meridian.Ui/dashboard run test`).
- The repo-native debounce precedent is the covered-call chain preview
  (`covered-call-screen.view-model.ts`, `chainAbortRef`/`chainDebounceRef` +
  `AbortError` swallow). This design mirrors it deliberately.

---

## Architectural Overview

### Context Diagram

```
ReportingScreen (reporting-screen.tsx)
  └── useReportingReportWriter (reporting-screen.report-writer.tsx)
        │  draft state: zones / settings / formulas / chart / format rules / custom dataset
        │
        │  mutation ──► markWriterPreviewStale(grid)          [was: clearWriterPreview]
        │                    │ phase := "stale"
        │                    ▼
        │              useReportWriterAutoPreview             [NEW: reporting-screen.report-writer-auto-preview.ts]
        │                    │ per-grid debounce timer (600 ms)
        │                    │ per-grid AbortController + sequence counter
        │                    ▼
        │              executeWriterPreviewRender(grid, {source})   [extracted from previewWriterGrid]
        │                    │ buildRenderRequest(...)   (unchanged, reporting-screen.report-writer-helpers.ts)
        │                    ▼
        │              renderReportTemplate(request, { signal })    (lib/api.ts, unchanged)
        │                    ▼
        │              writerPreviewByGridId / writerPreviousPreviewByGridId rotation (unchanged)
        │
        └── ReportWriterDesignerGrid ──► ReportWriterPreviewTable
              stale dimming + "updating…" pill + auto-preview toggle + quiet error row
```

### Design Decisions

- **Decision:** Mark previews stale instead of clearing them; keep the last good render on
  screen during re-render.
  **Alternatives considered:** Keep clear-on-edit and only add an auto re-render (preview
  still flickers to blank); optimistic client-side re-aggregation (duplicates
  `ReportWriterGridEngine` logic in TypeScript).
  **Rationale:** The diff panel (`buildReportWriterGridDiff`) and lineage trace only have
  value when there is a stable "before" on screen. Clearing destroys the operator's visual
  anchor and is the root cause of the current friction.
  **Consequences:** The preview can momentarily show data that no longer matches the draft;
  the dimming + pill must make that state unmistakable.

- **Decision:** Per-grid debounce/abort/sequence state lives in a new extracted hook,
  `useReportWriterAutoPreview`, in a new file
  `reporting-screen.report-writer-auto-preview.ts`, consumed by `useReportingReportWriter`.
  **Alternatives considered:** Inline the timers in `useReportingReportWriter`
  (`reporting-screen.report-writer.tsx` is already 1,874 lines); a module-level singleton
  controller (harder to test, leaks across screen mounts).
  **Rationale:** Matches the file-per-concern layout already used by the reporting screen
  (`reporting-screen.report-writer-helpers.ts`, `reporting-screen.workflow-continuity.ts`)
  and keeps the timer/cancellation logic unit-testable in isolation.
  **Consequences:** One new file, one new seam; the main hook change is mostly call-site
  substitution.

- **Decision:** Auto-render outcomes never touch the global `writerPreviewStatus`
  command-status panel; they update a per-grid quiet state instead. The loud
  `ReportingCommandStatusView` panel remains reserved for the manual Preview button.
  **Alternatives considered:** Route auto renders through the existing single-slot
  `writerPreviewStatus` (one grid's background render would overwrite another grid's
  manual result and spam the status panel on every keystroke burst).
  **Rationale:** With auto-preview, multiple grids can render concurrently; a single global
  status slot cannot represent that, and ambient renders should not shout.
  **Consequences:** Two presentation paths for the same operation; mitigated by both paths
  sharing `executeWriterPreviewRender`.

- **Decision:** Auto-render only arms after a grid has been touched (a mutation or a manual
  preview). Mounting the report-builder task mode never fires a render storm across all
  designer grids.
  **Alternatives considered:** Render every grid on mount (N templates × 1 request each,
  wasted work for grids the operator never opens).
  **Rationale:** Stale-marking is inherently mutation-driven; mount-time previews were never
  part of the existing behavior.
  **Consequences:** A grid shows no preview until first touched — identical to today.

- **Decision:** Suppress *display* of auto-render errors while the operator's focus is
  inside that grid's free-text inputs (custom formula expression, custom dataset textarea);
  renders still run, the last good preview stays, and the held error surfaces on blur if
  still current.
  **Alternatives considered:** Skip auto-rendering entirely while focused (preview goes
  stale for the whole typing session, losing the liveness the feature exists for);
  client-side expression pre-validation gating (belongs to brainstorm idea #6, the formula
  workbench).
  **Rationale:** A half-typed `{marketVal` will legitimately fail to render; flashing an
  error per keystroke trains operators to ignore errors.
  **Consequences:** Needs a lightweight per-grid "editing session" flag wired to
  focus/blur of exactly two inputs.

- **Decision:** Guard payload size: when the resolved dataset rows for the render request
  exceed `REPORT_WRITER_AUTO_PREVIEW_MAX_DATASET_ROWS` (200), auto-render is suspended for
  that grid and the pill reads "Auto preview paused — large dataset. Use Preview."
  **Alternatives considered:** No guard (repeatedly POSTing large retained-source row
  arrays on every debounce); a longer debounce for large payloads (still chatty).
  **Rationale:** Custom rows are already capped at 100; retained `source:` profiles are not
  statically bounded in the client.
  **Consequences:** One more phase to present; threshold is a named constant, trivial to
  tune.

---

## Interface & API Contracts

All new contracts are TypeScript (browser workstation lane). No C#, F#, REST, or
configuration-schema changes.

### New module — `src/Meridian.Ui/dashboard/src/screens/reporting-screen.report-writer-auto-preview.ts`

```ts
/** Debounce window between the last draft mutation and the automatic re-render. */
export const REPORT_WRITER_AUTO_PREVIEW_DEBOUNCE_MS = 600;

/** Above this many dataset rows, auto-render suspends and the manual Preview button is required. */
export const REPORT_WRITER_AUTO_PREVIEW_MAX_DATASET_ROWS = 200;

/** Presentation phase of a designer grid's preview. */
export type ReportWriterPreviewPhase =
  | "idle"       // never rendered, no pending work
  | "stale"      // last render visible but out of date; debounce timer armed (or auto off / paused)
  | "rendering"  // request in flight
  | "ready"      // preview matches the current draft
  | "error";     // last auto render failed; last good preview (if any) still shown

/** Quiet, per-grid error from an automatic render. Never routed to the global command status. */
export interface ReportWriterQuietPreviewError {
  summary: string;
  details: string[];
}

export interface UseReportWriterAutoPreviewOptions {
  debounceMs?: number; // default REPORT_WRITER_AUTO_PREVIEW_DEBOUNCE_MS; injectable for tests
  /** Whether auto preview is enabled for this grid (toggle + payload guard already applied). */
  canAutoRender: (gridId: string) => boolean;
  /** Executes one render. MUST honor the signal and throw AbortError when aborted. */
  render: (gridId: string, signal: AbortSignal) => Promise<void>;
}

export interface ReportWriterAutoPreviewController {
  /** Called on every draft mutation. (Re)arms the per-grid debounce timer when allowed. */
  schedule(gridId: string): void;
  /** Cancels the pending timer and aborts any in-flight auto render for the grid. */
  cancel(gridId: string): void;
  /** Cancels everything (unmount / task-mode exit). */
  cancelAll(): void;
  /** True when a timer is armed or a render is in flight for the grid. */
  isPending(gridId: string): boolean;
}

export function useReportWriterAutoPreview(
  options: UseReportWriterAutoPreviewOptions
): ReportWriterAutoPreviewController;
```

Internals contract (what the implementation must guarantee):

- One timer, one `AbortController`, and one monotonically increasing sequence number per
  `gridId`, held in `useRef<Map<string, ...>>` — never in React state (re-render churn).
- `schedule` clears any existing timer for the grid before arming a new one (rapid edits
  collapse to a single render).
- Starting a render aborts the previous controller for the grid, increments the sequence,
  and captures the value; a completed render's effects are applied only if its captured
  sequence still equals the grid's current sequence (belt-and-braces over abort).
- `AbortError` rejections are swallowed silently (mirrors
  `covered-call-screen.view-model.ts` line ~1581).
- All timers/controllers are disposed on unmount via the hook's cleanup effect.

### Modified hook — `useReportingReportWriter` (same file, `reporting-screen.report-writer.tsx`)

No existing return member is removed or changes signature. **Not a breaking change.**
New state and members:

```ts
// New per-grid state (all Record<string, ...> keyed by grid.id, matching existing slices):
//   writerPreviewPhaseByGridId:      Record<string, ReportWriterPreviewPhase>
//   writerAutoPreviewByGridId:       Record<string, boolean>        // absent key = true (default on)
//   writerQuietPreviewErrorByGridId: Record<string, ReportWriterQuietPreviewError | null>
//   writerEditingGridId:             string | null                  // free-text edit session

// New return members:
getWriterPreviewPhase(grid: ReportingWriterGridRow): ReportWriterPreviewPhase;
isWriterAutoPreviewEnabled(grid: ReportingWriterGridRow): boolean;
setWriterAutoPreviewEnabled(grid: ReportingWriterGridRow, enabled: boolean): void;
getWriterQuietPreviewError(grid: ReportingWriterGridRow): ReportWriterQuietPreviewError | null;
/** Focus/blur session for free-text inputs; holds quiet-error display while active. */
setWriterEditSession(grid: ReportingWriterGridRow, active: boolean): void;
```

Internal refactor (private to the hook):

```ts
/** Shared render path. Extracted from the body of previewWriterGrid. */
async function executeWriterPreviewRender(
  grid: ReportingWriterGridRow,
  options: { signal?: AbortSignal; source: "manual" | "auto" }
): Promise<void>;
// - source === "manual": sets writerPreviewStatus (existing loud behavior, verbatim).
// - source === "auto":   sets phase rendering→ready, rotates previous/current preview,
//                        writes quiet error on failure; never touches writerPreviewStatus.
// - Both: identical request building (buildRenderRequest), custom-dataset parse,
//         retained-dataset resolution, and preview rotation.

/** Replaces every `setWriterPreviewByGridId((c) => clearWriterPreview(c, grid.id))` call site. */
function markWriterPreviewStale(grid: ReportingWriterGridRow): void;
// phase := "stale"; keeps the current render; calls autoPreview.schedule(grid.id).
```

Call-site inventory to convert to `markWriterPreviewStale` (11 sites in the current file):
`updateWriterDraftSetting`, `updateWriterCustomFormula`, `updateWriterCustomDataset`,
`updateWriterChartDraft`, `addWriterFormatRule`, `removeWriterFormatRule`,
`updateWriterFormatRule`, `handleWriterZoneDrop`, `removeWriterZoneToken`,
`moveWriterZoneToken`. The eleventh, `resetWriterGrid`, instead calls
`autoPreview.cancel(grid.id)` and *does* clear the preview (a reset returns to the
untouched baseline, where no preview is correct).

`previewWriterGrid` (manual button) becomes: `autoPreview.cancel(grid.id)` →
`executeWriterPreviewRender(grid, { source: "manual" })`. The existing
`previewingWriterDraftId` single-slot gate continues to apply to manual renders only.

### Modified component props — `ReportWriterDesignerGridProps` (same file)

```ts
// Additions (additive only):
previewPhase: ReportWriterPreviewPhase;
autoPreviewEnabled: boolean;
onAutoPreviewToggle: (grid: ReportingWriterGridRow, enabled: boolean) => void;
quietPreviewError: ReportWriterQuietPreviewError | null;
onEditSessionChange: (grid: ReportingWriterGridRow, active: boolean) => void;
```

`ReportWriterPreviewTable` gains `phase` and `quietError` props.

---

## Component Design

### useReportWriterAutoPreview

**File:** `src/Meridian.Ui/dashboard/src/screens/reporting-screen.report-writer-auto-preview.ts`
**Type:** React hook returning a stable `ReportWriterAutoPreviewController` (memoized once).
**Dependencies:** none beyond React; `render` and `canAutoRender` callbacks are read through
refs so the controller identity never changes across renders (same stabilization concern
documented in `covered-call-screen.view-model.ts` for its services memo).

**Responsibilities:**
- Own per-grid debounce timers (`window.setTimeout` ids), `AbortController`s, and sequence
  counters in `useRef<Map<string, ...>>`.
- Collapse rapid `schedule` calls into one render per quiet window.
- Guarantee at most one in-flight auto render per grid; newest always wins.
- Dispose everything on unmount.

**Concurrency model:** All state is main-thread; races are between the debounce timer,
manual preview, and response arrival. Ordering is enforced by (a) aborting the previous
controller before each new render and (b) the sequence check before applying results.

**Error handling:** `AbortError` → silent. Any other rejection propagates to the `render`
callback's own handler (the hook does not interpret render failures; `executeWriterPreviewRender`
owns quiet-error state).

### useReportingReportWriter (modified)

**File:** `src/Meridian.Ui/dashboard/src/screens/reporting-screen.report-writer.tsx`
**New wiring:**

```ts
const autoPreview = useReportWriterAutoPreview({
  canAutoRender: (gridId) => resolveAutoRenderEligibility(gridId),  // toggle && payload guard && grid still present
  render: (gridId, signal) => {
    const grid = findWriterGrid(gridId);            // from the grids passed to the hook's consumers
    return grid ? executeWriterPreviewRender(grid, { signal, source: "auto" }) : Promise.resolve();
  }
});
```

`resolveAutoRenderEligibility` composes: `writerAutoPreviewByGridId[gridId] !== false`
AND resolved dataset rows length ≤ `REPORT_WRITER_AUTO_PREVIEW_MAX_DATASET_ROWS`
(via `resolveReportWriterDatasetSource` / parsed custom rows — both already available in
the hook). When ineligible, `markWriterPreviewStale` still sets phase `"stale"` so the pill
communicates that the preview is out of date and manual render is needed.

**Quiet-error hold:** `executeWriterPreviewRender` (auto path) on failure writes
`writerQuietPreviewErrorByGridId[grid.id]` and sets phase `"error"` — but the *component*
only displays the error row when `writerEditingGridId !== grid.id`. `setWriterEditSession(grid, false)`
triggers no re-render logic beyond clearing the flag; the already-stored error becomes
visible naturally.

### ReportWriterDesignerGrid / ReportWriterPreviewTable (modified)

Presentation only; see UI Design below. The Auto-preview toggle reuses the existing
`Checkbox` component (already imported for the inline-chart enable). The stale pill reuses
`Badge` variants (`warning` for stale/paused, `outline` for rendering).

---

## Data Flow

### Edit → auto re-render (happy path)

1. Operator drops the `sector` field token onto Rows (`handleWriterZoneDrop`).
2. Zone state updates; `markWriterPreviewStale(grid)` sets phase `"stale"` — the existing
   preview table remains rendered, now dimmed, pill shows "Preview out of date · updating…".
3. `autoPreview.schedule(grid.id)` arms a 600 ms timer (clearing any prior timer).
4. Operator drops `pnl` onto Metrics 300 ms later → steps 2–3 repeat; timer restarts.
5. Timer fires. Eligibility check passes. Previous controller (none) aborted; sequence → n+1;
   phase `"rendering"`; `executeWriterPreviewRender(grid, { signal, source: "auto" })` builds
   the request via the unchanged `buildRenderRequest` and calls
   `renderReportTemplate(request, { signal })`.
6. Response arrives; sequence still current. Current preview rotates into
   `writerPreviousPreviewByGridId`, new render into `writerPreviewByGridId` (existing
   rotation, unchanged) — the "Changes since previous preview" diff now reflects the edit
   automatically. Phase `"ready"`, quiet error cleared, pill disappears, dimming lifts.

### Edit while a render is in flight (supersession path)

1. Steps 1–5 above; a render for sequence n is in flight.
2. Operator changes the filter operator. `markWriterPreviewStale` + `schedule` re-arm the timer.
3. Timer fires: controller for sequence n is aborted (`AbortError` swallowed), sequence → n+1,
   new request issued.
4. If the aborted request had already resolved into a queued microtask, its sequence check
   (n ≠ n+1) discards it. Only the n+1 result is applied.

### Auto render fails while typing a formula (error-hold path)

1. Operator focuses the custom-formula expression input → `setWriterEditSession(grid, true)`.
2. Types `{marketVal` — mutation marks stale, debounce fires, server render fails
   (unknown field).
3. Auto path stores the quiet error, phase `"error"`; last good preview remains on screen.
   Because the edit session is active, the error row is *not* displayed; the pill shows
   "Preview out of date".
4. Operator finishes typing `{marketValue}` — next debounce cycle succeeds; quiet error is
   cleared before it was ever shown.
5. Alternative ending: operator blurs the input with the expression still broken →
   `setWriterEditSession(grid, false)` → the stored quiet error row becomes visible under
   the preview ("Auto preview failed: unknown field 'marketVal'…" + details), with the last
   good preview still rendered above it. The global command-status panel is untouched.

### Manual Preview / toggle-off path

1. Auto toggle off: mutations still set phase `"stale"` (pill: "Preview out of date — auto
   preview off"), but `canAutoRender` fails and no timer work happens — behavior degrades
   exactly to today's manual flow, minus the preview-blanking.
2. Manual **Preview** click: `autoPreview.cancel(grid.id)` (kills pending timer/in-flight
   auto render), then the existing manual path runs verbatim, including the loud
   `writerPreviewStatus` command status.

---

## UI Design (browser workstation — TSX, not XAML)

*N/A for XAML — this is a browser-lane feature; the equivalent section below describes the
React component structure.*

### ReportWriterPreviewTable header (modified)

```
Live preview row (existing flex header)
 ├── left:  "Live preview" eyebrow + "{title} · N rows" (existing)
 ├── right: Badge {preview.kind} (existing)
 └── NEW status pill, role="status" aria-live="polite", placed left of the kind badge:
       phase "stale"      → Badge variant=warning  "Preview out of date · updating…"
       phase "stale"+off  → Badge variant=warning  "Preview out of date — auto preview off"
       phase "stale"+paused → Badge variant=warning "Auto preview paused — large dataset"
       phase "rendering"  → Badge variant=outline  "Rendering…"
       phase "ready"|"idle"|"error" → no pill
```

### Preview body dimming

Wrap the existing table + chart container in:
`className={cn("transition-opacity", (phase === "stale" || phase === "rendering") && "opacity-60")}`
plus `aria-busy={phase === "rendering"}` on the container. The diff `<details>`, lineage,
data dictionary, and validation blocks are inside the wrapper and dim with it.

### Quiet error row (new, below the table, above lineage)

Rendered only when `quietPreviewError !== null` and the grid is not in an edit session:
a small `text-warning` row — `Auto preview failed: {summary}` with `details` in a
collapsed `<details>`. A "Retry" ghost button calls the manual `onPreview`.

### Actions row (modified)

```
[✓] Auto preview        [ Eye  Preview ]  [ PencilLine  Save draft ]
```

The checkbox (existing `Checkbox` component) sits left of the buttons with label
"Auto preview" and `aria-label` \`Toggle auto preview for ${grid.title}\`. The Preview
button is unchanged; its `busy` state continues to bind to the *manual* gate only, so
background auto renders never lock the button.

### Edit-session wiring

`onFocus`/`onBlur` on exactly two inputs — the custom-formula expression `Input` and the
custom-dataset `textarea` — call `onEditSessionChange(grid, true|false)`.

---

## Test Plan

**Principle:** Mock at the API module boundary (`vi.mock("@/lib/api")`, the established
pattern in `reporting-screen.test.tsx`); drive time with `vi.useFakeTimers()`; assert on
rendered DOM via @testing-library. The controller hook gets isolated tests with injected
`debounceMs`.

### Unit tests — `reporting-screen.report-writer-auto-preview.test.ts` (new)

| Test name | What it verifies | Setup / notes |
|---|---|---|
| schedule_ArmsTimer_RendersOnceAfterDebounce | one `render` call after `debounceMs` | fake timers, spy render |
| schedule_RapidCalls_CollapseToSingleRender | 5 schedules in 300 ms → 1 render | advance timers between calls |
| schedule_WhileInFlight_AbortsPreviousController | prior signal aborted before new render starts | render returns deferred promise |
| staleResponse_SequenceMismatch_IsDiscarded | older deferred resolving after newer applies nothing | resolve out of order |
| canAutoRenderFalse_TimerNeverFires | no render when eligibility callback returns false | toggle-off simulation |
| cancel_ClearsTimerAndAbortsInFlight | pending + in-flight both dead after cancel | |
| unmount_CancelsAll | no timer callbacks fire post-unmount | renderHook unmount |
| abortError_IsSwallowed | AbortError rejection does not surface | render rejects with AbortError |

### Unit tests — `useReportingReportWriter` additions (extend existing coverage or new `reporting-screen.report-writer.test.tsx`)

| Test name | What it verifies |
|---|---|
| tokenDrop_KeepsPreviewVisible_SetsStalePhase | preview no longer cleared on mutation; phase = stale |
| debounceElapsed_AutoRender_RotatesPreviousPreview | diff "before" = pre-edit render after auto render |
| autoRenderError_SetsQuietError_LeavesGlobalStatusUntouched | `writerPreviewStatus` unchanged; quiet error stored |
| editSessionActive_HoldsQuietErrorDisplay_ShowsOnBlur | error hidden while editing, visible after blur |
| manualPreview_CancelsPendingAutoRender | timer killed; manual path sets loud status |
| resetWriterGrid_ClearsPreviewAndCancelsAuto | reset returns to baseline, no orphan timer |
| largeRetainedDataset_SuspendsAutoRender | rows > threshold → stale+paused, no request |
| mount_NoUntouchedGridRenders | zero render calls before first mutation |

### Component tests — `ReportWriterDesignerGrid` (same new test file)

| Test name | What it verifies |
|---|---|
| stalePhase_ShowsPillAndDimsPreview | pill text + `opacity-60` class + `role="status"` |
| autoPreviewToggle_DispatchesAndAnnounces | checkbox fires `onAutoPreviewToggle`, aria-label present |
| quietErrorRow_RendersWithRetry | retry button invokes `onPreview` |

### Accessibility — extend `reporting-screen.a11y.test.tsx`

| Test name | What it verifies |
|---|---|
| autoPreviewStatusRegion_IsPoliteLiveRegion | pill container has `role="status"`/`aria-live="polite"` and `aria-busy` toggles |

### Integration test (deferred)

| Test name | What it verifies |
|---|---|
| reportBuilderTaskMode_EndToEndAutoPreview (deferred) | full screen: edit → debounce → rendered rows update; covered adequately by hook + component layers for this sprint |

### Test infrastructure needed

- None new. `vi.useFakeTimers`, deferred-promise helpers, and the `@/lib/api` mock pattern
  all exist in the dashboard suite.

---

## Implementation Checklist

**Estimated effort:** Low–Medium (~3–4 days including tests).
**Suggested branch name:** `feature/report-writer-auto-preview` (or continue on
`claude/report-builder-ui-improvements-5hjzpj`).
**Suggested PR sequence:** single PR; the change is cohesive and additive.

### Phase 1: Auto-preview controller
- [ ] Create `reporting-screen.report-writer-auto-preview.ts` with the constants, types,
      and `useReportWriterAutoPreview` per the contracts above.
- [ ] Write the 8 controller unit tests; green with fake timers.

### Phase 2: Hook integration
- [ ] Extract `executeWriterPreviewRender(grid, { signal, source })` from
      `previewWriterGrid`, preserving the manual path byte-for-byte (loud status, gate).
- [ ] Add the four new state slices (`phase`, `autoEnabled`, `quietError`, `editingGridId`)
      and the five new return members.
- [ ] Implement `markWriterPreviewStale` + payload-guard eligibility; convert the 10
      mutation call sites; special-case `resetWriterGrid` (cancel + clear).
- [ ] Thread `renderReportTemplate`'s `signal` through the auto path.

### Phase 3: Presentation
- [ ] `ReportWriterPreviewTable`: status pill, dimming wrapper, `aria-busy`, quiet error
      row with Retry.
- [ ] `ReportWriterDesignerGrid`: Auto-preview checkbox in the actions row; focus/blur
      wiring on the formula input and custom-dataset textarea; new props.
- [ ] `reporting-screen.tsx`: pass the new props at the `ReportWriterDesignerGrid` call
      site (lines ~1929–1957).

### Phase 4: Tests
- [ ] Hook-integration and component tests from the plan (11 tests).
- [ ] A11y live-region test.
- [ ] `npm --prefix src/Meridian.Ui/dashboard run test` green.

### Phase 5: Wrap-up
- [ ] `npm --prefix src/Meridian.Ui/dashboard run build` (workstation assets build clean).
- [ ] No ADR impact (client-side only; no contract, storage, or pipeline changes) — verify
      and note in PR body.
- [ ] Update the 2026-07-13 brainstorm ledger entry's `document_updated` field to this file.
- [ ] PR review checklist: no `.Result`/`.Wait()` equivalents (no floating promises —
      `void` only at event-handler boundaries), abort paths covered, existing manual-path
      behavior verified unchanged.

---

## Open Questions

| # | Question | Owner | Impact if unresolved |
|---|---------|-------|---------------------|
| 1 | Should the auto-preview toggle default to **on** for all operators immediately, or ship default-off behind one release of soak? | Product | If default-on surprises operators on large retained datasets despite the payload guard, trust in the builder drops; default-off delays the feature's value. Blueprint assumes **on**. |
| 2 | Is 600 ms the right debounce for drag-heavy editing (multiple drops in quick succession)? | Implementer | Too short → chatty renders; too long → feels dead. Constant is named and test-injectable; tune during review with real templates. |
| 3 | Should the payload guard threshold (200 rows) instead be a byte-size estimate of the serialized request? | Implementer | Row count is a proxy; very wide rows could still produce heavy payloads. Acceptable for v1. |

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Render endpoint receives materially more traffic once previews are ambient | Med | Low | Debounce + per-grid single-flight + mutation-gated arming + payload guard; endpoint is stateless and sample-sized |
| Stale response applied out of order corrupts the diff "before/after" rotation | Low | Med | AbortController **and** sequence check; explicit out-of-order unit test |
| Dimmed-but-visible stale preview mistaken for current data during a demo/review | Med | Med | High-contrast warning pill with `aria-live`, dimming, and pill text naming the state explicitly |
| Timer leaks across task-mode switches or unmount | Low | Low | `cancelAll` in hook cleanup; dedicated unmount test |
| Manual-path regression while extracting `executeWriterPreviewRender` | Low | High | Manual path preserved verbatim behind `source: "manual"`; existing `reporting-screen.test.tsx` coverage plus the manual-cancels-auto test |
