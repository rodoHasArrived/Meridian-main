# Browser Workstation UI Improvements — Brainstorm (2026-07)

> **Mode:** Domain-Focused / UX — the request targets one surface (the browser workstation in
> `src/Meridian.Ui/dashboard/`), so ideas are grounded in that codebase's actual shell, screens,
> data layer, and design-system primitives.
>
> **Session date:** 2026-07-02
> **Grounding:** `src/Meridian.Ui/dashboard/src/` (app shell, screens, hooks, charts, design
> primitives), `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`, prior brainstorm ledger.

## What already exists (and is therefore not proposed here)

The workstation already has a light/dark/system appearance toggle (`src/lib/theme.ts`),
per-table filter/sort/query persistence with CSV export (`src/components/data/use-table-state.ts`),
a command palette with entity search (`src/components/meridian/command-palette.entity-search.ts`),
a dense-row keyboard/ARIA accessibility contract (`dense-row-detail-accessibility.tsx`),
a trust strip, evidence timelines, operator-focus and workflow-continuity modules per screen, and
a URL-derived operating-scope strip (`src/app-shell.operating-scope.ts`). The ideas below extend
those foundations rather than duplicating them.

## Ideas at a Glance

| # | Idea | Effort | Audience | Impact | Depends On |
|---|------|--------|----------|--------|------------|
| 1 | Live data spine (SSE push channel) | L | H/Q/I | High | — |
| 2 | Freshness chips — standardized "as of" staleness vocabulary | S | H/Q/I | Med | — (amplified by #1) |
| 3 | Command palette verbs (actions, not just navigation) | S/M | H/Q/I | High | — |
| 4 | Copy-link deep links and saved views | S/M | I | High | — |
| 5 | Shell notification center (toasts → inbox) | M | H/Q/I | Med-High | amplified by #1 |
| 6 | Editable, sticky operating scope | M | I | High | — |
| 7 | Virtualized dense grids | M | I | Med-High | — |
| 8 | Linked chart crosshair + evidence drill | S/M | H/Q | Med | — |
| 9 | Pop-out companion panes (multi-monitor) | M | H/I | Med | amplified by #1 |

Effort: **S** = days, **M** = 1–2 weeks, **L** = 1+ month. Audience: **H** = hobbyist,
**Q** = academic, **I** = institutional.

---

## 1. Live data spine — one SSE push channel replacing per-screen polling

Every live surface in the workstation polls today: the workspace refresh loop in
`src/hooks/use-workstation-data.ts` (`window.setInterval` around line 987), live quotes
(`live-quotes-screen.view-model.ts:659`), the watchlist (`watchlist-screen.view-model.ts:542`),
price alerts (`lib/price-alerts/service.ts:211`), and reporting run status
(`reporting-screen.tsx:294`). Each screen invented its own interval, so freshness varies by screen,
tab-idle browsers hammer the API anyway, and nothing feels "live" the way the desktop shell does.

The improvement: a single `GET /api/workstation/stream` Server-Sent Events endpoint on
`WorkstationEndpoints` (`src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`), fed from the
host's `EventPipeline` and read-model services, with topic-scoped subscriptions
(`quotes:AAPL`, `workspace:trading`, `inbox`, `report-run:{id}`). Client-side, a
`useWorkstationStream(topic)` hook that shares one `EventSource` across the app, dispatches into
the same view-model state the pollers populate today, and **falls back to the existing polling
when the stream is unavailable** — so the change is additive and degradable. SSE (not WebSocket)
keeps it plain HTTP, proxy-friendly, and trivially compatible with the existing auth/session model.

The user moment: a quote in the watchlist ticks the instant the host sees it; a reporting run
flips from *Running* to *Delivered* without waiting out a poll interval; the trust strip's data
posture updates in real time. Tradeoffs: server-side fan-out and per-topic subscription
bookkeeping are genuinely new host complexity; backpressure policy (coalesce vs. drop) must be
explicit; and the polling fallback has to remain first-class or degraded environments break. This
is the platform bet of the session — ideas 2, 5, and 9 all get dramatically better once it lands.

## 2. Freshness chips — a standardized "as of" and staleness vocabulary

Poll-based screens can silently show minutes-old data with no visual signal. The workstation
already has the pieces: `use-request-lifecycle.ts` knows when each fetch last succeeded, and the
trust strip (`app-shell.trust-strip.ts`) already communicates source/mode posture at shell level.
What's missing is a **card-level** vocabulary.

Add a small `FreshnessChip` primitive to `src/components/ui/`: renders "as of 14:32:05", turns
amber past a per-surface staleness budget, red when a refresh is failing (wired to the lifecycle
hook's error state), with hover detail (source endpoint, last attempt, next retry). Adopt it on
the highest-frequency cards first: live quotes header, watchlist, portfolio summary, close
cockpit, reporting run status. When idea #1 lands, the same chip flips to a green "live" dot —
the vocabulary carries over unchanged.

Effort is small because it's one primitive plus adoption passes, and it directly serves the
"status at a glance" principle: an operator should know in two seconds whether a number is
current, aging, or broken. Tradeoff: staleness budgets must be chosen per surface (a trial
balance aging 60s is fine; a live quote isn't), and over-adoption turns it into noise — keep it
off static reference data.

## 3. Command palette verbs — actions, not just navigation

The command palette (`src/components/meridian/command-palette.tsx` + `entity-search.ts`) already
does fuzzy entity search and navigation. The extension: an **action provider registry** so screens
contribute verbs, making the palette the keyboard-first front door to operator workflows —
"Run report: Monthly NAV pack", "Open journal entry JE-2041", "Start backfill: AAPL 30d",
"Pin preset: EOD close", "Approve: pending policy rule".

Implementation shape: a `PaletteActionProvider` interface (id, verb label, keywords, `run()`,
optional confirmation) registered from each screen's view-model where the mutation logic already
lives — the reporting exports runner, close-calendar item creation, workflow preset pin/used
endpoints (`/workflows/presets/{id}/pin`) are all existing POST surfaces. Actions that mutate
state show an inline confirm row in the palette itself rather than a modal, and destructive or
governed actions simply aren't registered (they stay behind their approval-gated screens).

The user moment: `Ctrl+K`, type "run mon", Enter, and the report run starts with a toast linking
to run status — no navigation. This is the Bloomberg command-line pattern adapted to the existing
palette, and it's the best effort-to-delight ratio in this list. Tradeoffs: action registration
must not resurrect business logic in the palette (providers should delegate to existing
view-model commands), and governance boundaries need an explicit "never palette-exposed" rule.

## 4. Copy-link deep links and saved views

`useTableState` already persists filters/sort to `localStorage` — but that state is trapped on one
operator's machine. Operators coordinating a close or chasing a reconciliation break need to hand
a colleague *this exact view*: same screen, same fund, same filters, same selected row.

The extension: serialize per-screen view state (active task mode, filters, selection, scope) into
a compact URL query/hash form, add a "Copy link" affordance in the workspace header
(`workspace-header.tsx`), and hydrate on load. The operating-scope module already reads
`symbol`/`fundAccountId`/`runId`/`provider`/window params from the URL — this generalizes that
pattern to screen-local state. A second layer, **saved views**, reuses the existing workflow
preset endpoints (`/workflows/presets` GET/POST/pin in `WorkstationEndpoints.cs`) to store named
view-state payloads server-side, so "Month-end breaks — Fund A" appears in the palette and the
workflow continuity dock for the whole team.

The user moment: paste a link in chat, teammate opens it, and lands on the same reconciliation
row with the same evidence panel open. Tradeoffs: view-state schemas need versioning or old links
rot; URL length forces a compact encoding; and anything security-scoped (role-gated task modes)
must re-check permissions on hydrate rather than trusting the link.

## 5. Shell notification center — from transient toasts to a durable inbox

Today, signal arrival is fragmented: price alerts fire from a local polling service
(`lib/price-alerts/service.ts`), the operator inbox is a workspace fetch
(`WorkstationOperatorInbox` endpoint), workflow-continuity events render per screen, and toasts
vanish. An operator who steps away misses things.

The improvement: a bell in the masthead with an unread count, opening a right-side sheet (the
`Sheet` primitive exists in `src/components/ui/`) that merges three feeds — price alerts, operator
inbox items, and system/workflow events — into one time-ordered list with read state, severity
grouping, and per-item deep links (each entry navigates via idea #4's link format). Toasts become
the *transient* face of the same entries, so nothing is toast-only. Read state persists via a
small workstation endpoint next to the inbox read model; until idea #1 lands the center polls on
the existing workspace cadence, and afterward it becomes push-fed for free.

The user moment: return from lunch, one glance at the bell — "3 unread: price alert AAPL, recon
break assigned to you, report pack delivered." Tradeoffs: merging heterogeneous feeds needs a
common envelope (id, severity, timestamp, link, source); dedup between the inbox screen and the
center must be by shared id or operators see doubles; and notification fatigue is real — default
to inbox-silent for low-severity events.

## 6. Editable, sticky operating scope

`app-shell.operating-scope.ts` already *displays* scope parsed from the URL (symbol, fund account,
run, provider, window) and many endpoints already accept the matching filters
(`fundAccountId`/`entity` on portfolio summary, trading readiness, close calendar…). But scope is
read-only and evaporates on navigation: an operator working Fund A re-selects Fund A on every
screen.

The extension: make the scope strip an **editable, sticky context bar**. Click it (or `Ctrl+K`
"scope: Fund A") to open a picker backed by the entity search index; the chosen scope writes to
the URL (keeping today's deep-link semantics), persists to `localStorage`, and **survives
workspace navigation** — the app shell appends scope params to route changes, and each workspace
view-model reads scope from one shared context instead of screen-local state. Screens that can't
honor a scope dimension show it dimmed with an explicit "not filtered by fund" hint rather than
silently ignoring it.

The user moment: pick "Fund A · as of Jun 30" once, then walk Trading → Portfolio → Accounting →
Reporting and every number is already filtered; the scope chip is always visible, so there's no
ambiguity about *what* you're looking at. This is the multi-fund operator's single biggest
friction remover. Tradeoffs: migrating screens from local filter state to shared scope is a
many-file touch (do it workspace-by-workspace); "sticky vs. explicit" needs care so operators are
never *surprised* by an inherited filter — the always-visible chip plus a one-click clear is the
mitigation.

## 7. Virtualized dense grids

Journal entries, trial balance, reconciliation breaks, and the financial record explorer render
full row sets into the DOM. At fund-ops scale (tens of thousands of journal lines) initial render
and interaction latency will degrade exactly on the screens institutional users live in.

The improvement: row virtualization inside `DenseDataTable` behind the existing dense-row
accessibility contract (`dense-row-detail-accessibility.tsx`) — windowed rendering with stable
row heights, plus `aria-rowcount`/`aria-rowindex` so screen readers still perceive the full set.
The contract is the hard part and the reason to do this centrally once rather than per screen:
Home/End and type-ahead must address the *virtual* set, focus must survive rows unmounting, and
Escape-back-to-row must scroll the row back into the window. Pair it with a column chooser and a
"rows: comfortable/dense" toggle stored via `useTableState`'s existing persistence.

The user moment: the trial balance with 40k lines opens instantly and scrolls at 60fps; keyboard
navigation behaves identically to today. Tradeoffs: variable-height rows (expanded detail inline)
complicate windowing — keep detail in the adjacent panel as the contract already prescribes;
virtualization also breaks browser find-in-page, which is worth an explicit in-grid search
affordance (already provided by `useTableState.query`).

## 8. Linked chart crosshair + evidence drill

The chart library (`src/components/charts/`: `CandleChart`, `EquityCurve`, `DrawdownChart`,
`DepthChart`, `Histogram`) renders side-by-side on strategy and quant-lab screens, but each chart
is an island: no shared time cursor, and no path from "this point looks wrong" to the evidence
behind it.

The improvement: a lightweight `ChartSyncContext` (hovered timestamp + selected range) that
sibling charts subscribe to, drawing a synchronized crosshair and shared time-range brush; and a
point-level click action that feeds the screen's existing linked-context/evidence-timeline modules
(`*.linked-context.ts`, `*.evidence-timeline.ts`) — click the equity-curve drawdown trough and the
evidence panel shows the fills, journal impacts, or data-quality events at that timestamp, with a
"Copy link" (idea #4) capturing chart + cursor position.

The user moment: hover the equity curve and the drawdown chart's crosshair tracks it; click an
anomalous candle and the evidence rail answers "what happened here?" without leaving the screen.
This is the analyst-facing counterpart to the ops-facing ideas above, and it deepens the
workstation's core differentiator — numbers that carry their evidence. Tradeoffs: crosshair sync
across charts with different x-domains needs explicit timestamp normalization; keep the sync
context per-screen, not global, or unrelated charts start twitching.

## 9. Pop-out companion panes (multi-monitor)

Operators monitoring for hours want the watchlist or live quotes on a second monitor while working
the close cockpit on the first. The browser can do this without any mobile-lane violation:
`window.open` a chrome-less companion route (`/panes/watchlist`, `/panes/live-quotes`) rendering
just that pane, with a `BroadcastChannel` bridge sharing scope, appearance, and — once idea #1
lands — a single SSE connection's data fanned out to child windows so each pop-out doesn't open
its own stream.

The user moment: a ⧉ pop-out button in the watchlist header; the pane opens as a slim, dense
window that remembers its size/position (`localStorage`), tracks the main window's operating
scope, and closes gracefully when the session ends. Tradeoffs: a second render surface means the
pane components must be shell-independent (no masthead/nav assumptions); auth/session expiry has
to propagate to children; and without #1 each pop-out polls independently, so sequencing this
after the live spine is the cheaper path.

---

## Synthesis

**Platform bet:** the live data spine (#1). It converts every polling surface at once, makes the
notification center (#5) push-fed, lets pop-outs (#9) share one stream, and upgrades freshness
chips (#2) from "how stale" to "live". It's also the largest single item — which is why it should
be *preceded* by the quick wins, not block them.

**Highest leverage-to-effort:** command palette verbs (#3) and copy-link deep links (#4). Both
build directly on shipped foundations (palette entity search, URL operating scope, preset
endpoints) and each makes the other better — actions can target saved views, links can invoke the
palette's targets.

**Cross-cutting theme:** three ideas (#4, #5, #6) converge on one primitive — a versioned,
serializable "where am I and what am I looking at" state envelope. Designing that envelope once
(scope + screen + view state + selection) and letting deep links, notifications, and the sticky
scope bar all speak it prevents three incompatible encodings.

**Suggested sequencing:**
1. Freshness chips (#2) — days, immediate trust payoff, defines the status vocabulary.
2. Palette verbs (#3) and deep links (#4) — the keyboard-first and collaboration quick wins.
3. Live data spine (#1) — the structural investment, landed behind polling fallback.
4. Notification center (#5) — now push-fed from day one.
5. Virtualized grids (#7) and sticky scope (#6) — the institutional-scale workhorses.
6. Chart sync (#8), then pop-outs (#9) — analyst depth and multi-monitor polish.

**Competitive signals:** Bloomberg's enduring UX moats are the command line (every workflow is
typeable) and Launchpad (composable multi-window panes) — ideas #3 and #9 adapt both patterns to
the existing palette and browser windowing without new surface area. Databento's developer
ergonomics center on streaming-first delivery, which validates SSE push (#1) as table stakes for
anything calling itself live. OpenBB shows the ceiling of poll-based web dashboards — Meridian's
self-hosted host process is precisely what lets it do better with a first-party push channel.
