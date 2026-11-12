# UX depth & extension proposal — toward 4.1

**Status:** proposal · tracks **A**, **B-1**, **D-1/D-2** and **F-1** now built · **Date:** 2026-08-15 · **Baseline:** 3.5.0 (268 exports · 26 templates)

> **Shipped 2026-09-01 (4.2.0, archetype pass).** Track **B-1** — `ActionQueue` + `ActionQueueChip`
> (operations), adopted by the three writing archetypes (`basket-builder`, `settings-admin`,
> `report-pack`). Track **D-2** — `CommandPalette` gained async object resolution, verbs on a row and
> session-scoped recency ranking. Track **D-1** — the `?` sheet is now generated from the same
> bindings the palette lists (`templates/_scaffold/operator-layer.js`); all seven archetypes mount
> palette + hotkeys. Track **A** widened: `ObjectTrail` on `dashboard-workstation`,
> `analytical-workbench`, `basket-builder`; `WorkspaceLayout` on `analytical-workbench` — both
> **session-scoped** by decision. B-2, B-3, C, D-3, E and F-2..4 remain carried; the 20 production
> screens are the next adoption wave.

> **Shipped since this was written.** Track A in full — `WorkspaceLayout` (shell), `ObjectTrail` (core),
> `HandoverBrief` (operations), law in `guidelines/SESSION_CONTINUITY.md`, adopted by `templates/session-start`
> (arrival) and `templates/adapter-operations` (layout + trail). Track F-1 — `AdapterDiagnostics`
> (operations), law in `guidelines/ADAPTER_SEMANTICS.md`, reference screen `templates/adapter-operations`,
> closing `CONTRACT_AUDIT.md` §A-5 / §D-2. F-2, F-3 and F-4 remain carried.

Meridian's *decision* layer is finished work: guarded flows, reversibility classes, evidence and
consequence rails, twelve time roles, ten data conditions, value envelopes. What is thin is the layer
**around** the decision — how an operator arrives, resumes, keeps attention, submits, and returns.
Every item below names the gap as evidence, not opinion.

Evidence base: `docs/adoption-census-4.0.json` (167 of 268 exports adopted by a template),
`docs/CONTRACT_AUDIT.md` §D (unbuilt domains), `tests/BROWSER_TEST_PLAN.md` (deferred a11y run).

---

## A. Session & workspace continuity — *the largest true absence*

The system composes a screen well and remembers nothing about the operator who left it.

| # | Add | Operator question | Gap evidence |
|---|---|---|---|
| A-1 ✓ | **`WorkspaceLayout`** (shell) — named, persisted, shareable pane layouts per screen; pop a region into its own window with `LinkedSelectionProvider` intact | "Put my ops wall back the way I left it — on three monitors." | `SplitPane` is pane-local, `SavedViews` is table-local, `ColumnManager` persists columns. No screen-level layout identity exists. |
| A-2 ✓ | **`ObjectTrail`** (core) — recents · pinned · back/forward across the 17 object types, keyed by object ref **and** version | "Take me back to the case I was on before the close call." | `Breadcrumb` has zero template adoption; `ObjectModel` defines the types but nothing records traversal. |
| A-3 ✓ | **`HandoverBrief`** (operations) — what changed on the objects I own since my last session, what breached while I was out, what is mine now | "I was off for four hours. What moved?" | Nothing in 268 exports addresses the returning or incoming operator. `EventTimeline` is per-object, not per-person. |

Law to extend: `guidelines/WORKSTATION_HIERARCHY.md` (add a *session identity* clause),
`guidelines/TEMPLATE_CONTRACT.md` (layout/trail restoration as a template obligation).

## B. Action in flight — the write path has no state

`DecisionFlow` ends at commit; `ToastProvider` fires and forgets; `ConnectionState` and
`ContinuityFrame` govern **reads** only. Between "operator pressed Post" and "the ledger agrees"
there is currently no surface.

- **B-1 `ActionQueue`** (operations) — one tray of in-flight consequential actions with an explicit
  ladder: `submitted → acknowledged → confirmed | rejected | timed out`, each carrying its
  idempotency key, elapsed clock, and retry path. Serves order submission, posting runs, backfills,
  report delivery — four domains that each hand-roll a spinner today.
- ~~**B-2 `OptimisticCell`**~~ (data) — **built 4.2**: `OptimisticProvider` ledger · `OptimisticCell` · `OptimisticRowBar`. A pending value renders as **provisional** until confirmed, a rejection reverts *with the reason attached*, and a confirmed edit stays revertible — cell or row batch — under a stated reason recorded as its own write. Adopted on `security-master-registry` (inspector fields) and `ingestion-operations` (queue priority).
- **B-3** Extend `VersionConflictNotice` to the queue: a conflict discovered *after* submission is a
  different event from one discovered while editing, and today only the latter has a surface.

## C. Attention & escalation — subscribing where the number is

- **C-1 `SubscriptionChip`** (operations) — "watch this" on any `ValueEnvelope`, `GateRail` lane, or
  case row, producing a rule the alerting workstation owns. Today an operator who distrusts a figure
  must leave the screen and rebuild it as an alert rule from scratch.
- **C-2** Adopt **`NotificationCenter`** (currently zero templates) as the standing inbox for C-1,
  with per-object mute and a severity floor.
- **C-3 `EscalationPath`** (operations) — on breach: who is next, at what time, through which channel.
  `SlaChip` states posture; nothing states the path out of it.

## D. Keyboard & motor economy — built, unadopted

`HotkeysProvider` appears in 4 of 26 templates. `ContextMenu`, `MultiSelect`, `SavedViews`,
`ColumnChooser`, `VirtualizedList` and `Popover` appear in none.

- **D-1** A `ShortcutHelp` overlay (`?`) generated from the registered hotkey scopes — a keyboard map
  that cannot drift from the code, replacing the static `keyboard-map.card.html`.
- **D-2** Deepen `CommandPalette`: async object resolution (type an order id, a symbol, a case
  number), recency/frequency ranking, screen-scoped vs. global mode, and verbs on a result row
  (*open · pin · attest*). Today `commands` is a static array — it cannot answer "find object X".
- **D-3** `templates/interaction-depth/` — one reference screen that exercises the whole unadopted
  interaction cluster (right-click, bulk edit, saved views, column chooser, 50k-row virtualization,
  keyboard-only path) so the behaviours have a normative source, the way `analytical-workbench`
  is the source for the visualization grammar.

## E. Reading depth — annotation is entirely missing

Review in Meridian is attestation-only: an operator can sign a figure but cannot mark one up.

- ~~**E-1 `AnnotationLayer`**~~ (operations) — **built 4.2**: `AnnotationProvider` · `AnnotationAnchor` (cell · point · line · header) · `AnnotationPanel`. Versioned with the object; the operator picks **follow** (stale on a later version, re-affirmable) or **pin** at write time. Adopted on `security-master-registry` (record header), `reconciliation` (statement + ledger lines), `trading-desk` (positions), `accounting-workstation` (account sheet).
- ~~**E-2**~~ Comparison by selection — **built 4.2** as `SelectionCompare` (data): `CompareToggle` on a row, `CompareTray`, `CompareSheet` → `PlateGrid` with one shared domain. Adopted on `strategy-runs` (run ledger → equity paths).
- **D-3** — **built 4.2** as `templates/interaction-depth`, the normative reference exercising all three with the 50k-row grid, right-click, bulk edit, saved views and a keyboard-only path.

## F. Coverage debt worth closing (from the contract audit)

1. ~~**OMS adapter diagnostics**~~ — **built 4.1** as `AdapterDiagnostics` + `guidelines/ADAPTER_SEMANTICS.md` + `templates/adapter-operations`: posture, retry schedule, verbatim error, queue age, key rotation.
2. **Operations-continuity approvals** — submit/approve/reject over `GateRail` + `CloseCalendar` (§D-1).
3. **Trading depth** — `OptionChainTable` and `TimeAndSalesTape` ship with no screen exercising them.
4. **Accessibility close-out** — *partially addressed.* A static audit of the 4.1 surfaces shipped as
   `tests/A11Y_STATIC_AUDIT_4.1.md` (nine defects found and fixed, including a `hidden` region with
   no keyboard route back), and `tests/BROWSER_TEST_PLAN.md` gained a §4.1 matrix. The
   browser/NVDA run itself is **still deferred**: announcement verbosity, live-region timing and
   focus after a region collapses cannot be settled by reading source.

---

## Sequencing

| Wave | Content | Why first |
|---|---|---|
| 1 | A-1 · A-2 · B-1 | Continuity and in-flight state are the two absences an operator meets every session. |
| 2 | D-1 · D-2 · D-3 | Turns 6 built-but-unadopted primitives into a normative interaction reference. |
| 3 | C-1 · C-2 · C-3 · A-3 | Attention layer, once subscriptions have an inbox to land in. |
| 4 | E-1 · E-2 · F | Annotation and coverage debt. |

Each wave stays strictly additive: no changed defaults, no retired exports, one new guideline per
wave at most.
