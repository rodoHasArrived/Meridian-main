# Meridian Browser Workstation — UI/UX Refinement Audit

**Date:** 2026-07-11
**Scope:** `src/Meridian.Ui/dashboard/` — the active browser-based operator
workstation (React 18.3.1, Vite 8, TypeScript 5.7, Tailwind 3.4, token-driven
design system).
**Method:** Static audit across four dimensions — accessibility, styling
consistency, performance, and UX state-handling — corroborated by direct
inspection of configs (`tailwind.config.ts`, `vite.config.ts`, `eslint.config.mjs`),
the design-system contract test, and the app shell/router.

> **This is a proposal, not an applied change.** No component behavior was
> modified. The findings below are prioritized refinements with file paths and
> implementation notes so they can be scoped and picked up individually. Line
> numbers reflect the tree at audit time and should be re-confirmed before edits.

---

## Headline verdict

**The workstation is in genuinely good shape.** The fundamentals people usually
expect to find broken here are engineered well:

- **Design-system discipline.** Zero Tailwind literal color classes
  (`text-red-500`-style) and zero `dark:` variants anywhere in `src` — theming is
  entirely token-driven CSS, enforced by `src/design-system-contract.test.ts`.
- **Performance.** All ~36 screens are `React.lazy` route-split with a `Suspense`
  skeleton fallback and a `RouteErrorBoundary` (`src/app.tsx:96-131, 537-538`).
  Dense tables use a centralized windowing policy
  (`src/lib/dense-table-virtualization.ts`) targeting 50k-row tapes at 60fps.
  Polling effects all clean up their intervals.
- **State handling.** A consistent lifecycle — `useRequestLifecycle` →
  per-workspace error isolation via `Promise.allSettled` in `useWorkstationData`
  → `describeApiError` → shell/inline banners → `Button` `busy`/`disabledReason`
  → `EmptyState`. Mutating actions have model-quality pending/success/error
  feedback.
- **Accessibility.** Icon-only buttons carry `aria-label`, colored status is
  always paired with text, images are `alt=""`/`aria-hidden`, and Dialog/Drawer/
  Sheet/command-palette all implement focus trap + restore + Escape + `aria-modal`.
  `jest-axe` is wired globally and used in 18 test files.
- **Dependencies.** `npm audit` reports **0 vulnerabilities**. Pins are current;
  React is a deliberate stay on 18.x. `lucide-react` (0.468 → 1.x) is the only
  meaningfully behind package and is icon-only/low-risk.

The refinements below are polish on a solid base. There are **no high-severity
defects** and no security findings.

---

## Prioritized refinements

Ordered by impact-to-effort. Each is independently shippable.

### 1. Fix dark-mode rendering of form primitives (highest impact) — Medium impact / Low–Med effort

**Problem.** ~43 hardcoded arbitrary-hex Tailwind classes remain in
`src/components/ui/` primitives — light-mode values that never flip under
`data-theme="dark"`. A `dark`-themed input/select/date-picker keeps a light
`#F3F6F9` fill while its `bg-card`/`text-foreground` neighbors flip, producing a
visibly broken control. This is a real rendering bug, not a purity nit.

Representative sites:
- `components/ui/input.tsx:49` `bg-[#F3F6F9]`, `:56` `hover:border-[#ADB8C4]`
- `components/ui/select.tsx:33,40`
- `components/ui/date-picker.tsx:37,82,83,94,103` (5×), `date-range-picker.tsx` (5×)
- `stepper.tsx` (3×), `checkbox.tsx` (3×), `number-input.tsx` (3×), plus
  `text-area`, `combobox`, `card`, `radio-group`, `sheet`, `drawer`, `gauge`,
  `progress`, `accordion`, `segmented-control`, `kbd`, `panel-surface`,
  `context-menu`, `file-upload`, `dialog.view-model.ts`.

**Implementation notes.** Map the literals to existing tokens that already have
dark equivalents in `styles/index.css`: `bg-[#F3F6F9]` → `bg-muted`/`bg-input`,
`hover:bg-[#EAEEF3]` → `hover:bg-accent`, `hover:border-[#ADB8C4]` →
`hover:border-[hsl(var(--border-hover))]`. Behavior-preserving in light mode;
fixes dark mode for free.

**Related, do together — latent config trap.** `tailwind.config.ts` sets
`darkMode: ["class"]`, but the runtime toggle writes a `data-theme` **attribute**
(`lib/theme.ts:39`), never a `.dark` class. It is inert only because there are 0
`dark:` variants today; the first `dark:` utility anyone writes will silently not
respond to the toggle. Fix: `darkMode: ["selector", ':root[data-theme="dark"]']`.

### 2. Stop masking backend failures as "not found" in detail screens — Medium impact / Low–Med effort

**Problem.** A subset of self-fetching detail screens `.catch` a failed load by
setting data to `null`/`[]` with no error branch, so an API/network outage is
indistinguishable from a genuinely missing record — and offers no retry:

- `screens/journal-entry-detail-screen.tsx:60-66` → renders "Journal entry not found"
- `screens/asset-detail-screen.tsx:237-242` → renders "Asset not found"
- `screens/trial-balance-screen.tsx:81-84` → silently blanks the journal-evidence panel
- `screens/finance-standard-pages-screen.tsx:278-281`, `report-run-parameters-screen.tsx:106-110`,
  `portfolio-screen.tsx:334-337` → silent empties

**Implementation notes.** The repo already has the right primitive —
`components/ui/async-region.tsx` (`AsyncRegion` + `RegionErrorState`/`onRetry`) —
but it is adopted by exactly **one** screen. Migrate these self-fetchers onto it,
or minimally add an `error` state that renders `StatusBanner tone="danger"` with a
retry. Screens that already do this correctly and can serve as the template:
`cash-ladder-screen.tsx:147-151`, `accounting-screen.tsx:990-994`,
`settings-screen.tsx:1076-1082`.

### 3. Close the accessibility gaps in Popover and form error text — Medium impact / Low effort

**Problem.** Two focused a11y defects on otherwise-clean primitives:
- `components/ui/popover.tsx:104-116` renders `role="dialog"` but has **no**
  `aria-modal`, no focus move-in on open, and no focus restore to the anchor on
  close — an element announced as a dialog that never receives or returns focus.
  Fix: move focus into the panel on open and restore to `anchorEl` on close, or
  drop `role="dialog"` and render as a labelled non-modal group.
- `components/ui/form.tsx:65` (`FormRow`) and `components/ui/checkbox.tsx:57`
  render error/hint text with **no `id`** and don't wire `aria-describedby` on the
  control, so screen readers don't announce the error on focus. `FormRow`'s
  `error=` prop is used across 16 files. Fix: `useId()` the error `<p>`/`<span>`
  and pass `aria-describedby`/`aria-invalid` to the field. The correct plumbing
  already exists in `field-support.tsx` (`joinDescribedByIds`) — it just isn't
  used by these two.

**Coverage note.** 12 screens have no `jest-axe` assertion at all (e.g.
`trial-balance`, `cash-ladder`, `journal-entry-detail`, `evidence-workbench`,
`operations-continuity`). Add a one-line axe smoke test each, mirroring
`screens/portfolio-screen.a11y.test.tsx`, to prevent regressions.

### 4. Make rail sub-navigation actually focus its target section — Medium impact / Med effort

**Problem.** Two competing navigation systems don't meet. Rail sub-items are
**path** routes (Trading → Orders/Positions/Risk = `/trading/orders…`,
`lib/workspace.ts:20-22`) that fall through to the workspace catch-all
(`/trading/*` → `TradingScreen`, `app.tsx:556`), but the screen's own task
navigation is **hash**-based (`resolveTradingTaskViewId` reads
`window.location.hash` only, `trading-screen.tsx:175-186`). Net effect: clicking
"Orders"/"Positions"/"Risk" highlights the sub-item as active but the content
stays on the default "Overview" view. Same for other path sub-items resolving to a
catch-all workspace screen (e.g. Strategy → Promotions/Lab).

**Implementation notes.** Either point the rail sub-items at the hash anchors the
screens actually consume, or have the screens derive the active task view from
`pathname` as well as `hash` (extend `resolveTradingTaskViewId` and
`app-shell.route-focus.ts:normalizeHashTarget`). Pick one nav contract and make
both sides honor it.

### 5. Split the two oversized screens and add a vendor chunk — Low impact / Med effort

**Problem (maintainability + caching).**
- `screens/settings-screen.tsx` (7,505 lines) and `screens/accounting-screen.tsx`
  (6,580 lines) are extreme single-file components. Mitigated by the repo's habit
  of pushing logic into sibling `*.view-model.ts` files, but they remain large
  lazy chunks and hard-to-review surfaces. Consider splitting each into per-tab
  lazy sub-routes so each tab is its own chunk.
- `vite.config.ts` (build block) sets no `rollupOptions.output.manualChunks`, so
  shared vendor deps (`react`, `react-dom`, `react-router-dom`, `lucide-react`)
  aren't isolated into a stable long-cached vendor chunk, and no
  `chunkSizeWarningLimit` is set. Add a `vendor` manualChunk.

**Minor perf nit (optional).** `app.tsx` recreates several shell handlers
(`handleWorkflowPresetUsed:224`, `handleSetOperatingScope:298`,
`handleRestoreLayout:312`) each render, so memoized masthead/status children still
reconcile on every data tick. Wrap in `useCallback` if profiling shows it matters.

---

## Housekeeping / lower priority

- **Tokenize the repeated menu shadow.** `shadow-[0_2px_6px_rgba(0,0,0,0.18)]` is
  hardcoded in 11 overlay components and is currently *asserted* by
  `design-system-contract.test.ts:488`. Add a `--shadow-menu` token +
  `boxShadow.menu`, replace the 11 literals, and update the contract test.
- **Hardcoded command-palette scrim.** `command-palette.tsx:209`
  `background: "rgba(14, 17, 19, 0.32)"` won't adapt to theme — use
  `hsl(var(--background) / 0.32)` or a `--ws-scrim` token.
- **Hand-synced dark palette.** `styles/index.css` intentionally duplicates the
  full dark palette across the `@media (prefers-color-scheme: dark)` and
  `:root[data-theme="dark"]` blocks (~90 lines) that must be kept in lockstep by
  hand. A shared custom-property block would remove the drift risk.

---

## What was checked and found clean (no action)

- Route-level code splitting, `Suspense`/error-boundary wrapping.
- Dense-table virtualization policy and its live consumers.
- Memoization of heavy derived data (487 `useMemo`/`useCallback`/`memo` uses).
- Interval/polling cleanup.
- Icon-only button labelling, color-only status signaling, image `alt`/`aria-hidden`.
- Dialog/Drawer/Sheet/command-palette focus trap + restore + Escape + `aria-modal`.
- Empty-state and loading-state consistency (`EmptyState`, skeleton fallbacks).
- Mutating-action feedback (`busy`/`busyLabel`/`disabledReason`, toasts).
- Dependency vulnerabilities (`npm audit`: 0).
