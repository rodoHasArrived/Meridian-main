# Suite cohesion review — toward 4.4

**Baseline:** 27 templates (7 archetypes + 20 production screens), `_ds_bundle.js` at 4.3.
**Question asked:** do the templates add up to one workstation, and does that workstation behave like real software?
**Status:** waves 1–4 shipped — tracks A, B, C, D are closed and E-2/E-3 have their first real loops. What remains is listed at the end.

---

## Shipped — waves 2–4

| Track | What changed |
| --- | --- |
| **B-5 · who is signed in** | `WorkstationTopbar` now renders `SessionControls`' `UserMenu` from `MeridianWorld.OPERATOR` — on all 27 screens, with no per-screen work. The popover carries the name, a `RoleBadge`, and the desk; sign out routes to the gate through the app map. `operator={null}` suppresses it for pre-auth surfaces, and the cohesion checker's new `operator` rule allows that only for `session-start`. |
| **D-1 · preferences that survive a step** | New `_scaffold/prefs.js`. Every `applyChrome()` in the suite (27 screens plus `screen-kit.js` for the strategy seven) now resolves theme, density and environment through `MeridianPrefs` — **tweak seeds, operator wins**: a screen's declared default still shows on a first visit (Ops Wall is a dark wallboard; the asset sheet is light paper), and the moment the operator chooses in the masthead menu, the choice follows them everywhere until reset. `ds-base.js` loads prefs first so there is no flash of the wrong chrome. |
| **E-2 · return loops** | `app-map.js` gains `LOOPS` — eleven object types' declared returns (entry ⇄ break, break ⇄ account, task ⇄ tie-out, position → security, run → strategy, alert → job…) — plus `loops(type, obj)` and `loopActions(type, obj)`. New DS component **`ReturnPath`** renders them. Wired end to end on the flagship loop: `JE-1042` ↔ `CASE-4181` and `JE-1060` ↔ `CASE-4184`, using the break narratives the reconciliation screen already carried. A loop appears only when the object holds the link, so an unresolved break offers no "entry that resolved this". |
| **E-1 · the way back** | `app-map.js` gains `parent` on every drill-in screen plus `trail()`, `backTarget()` and `nameObject()`; `WorkstationTopbar` derives its own breadcrumb from them — **Domain › parent screen › screen › object** — so every screen built on the shared masthead gained a way back with no per-screen work, exactly as B-5 did for identity. Crumbs above the current one navigate; the back chevron appears only when there is real in-suite history or a parent screen, never as a dead affordance. Crumbs drop from the outside in as the bar narrows, so the command field never starves. Three screens teach it their record's name (`nameObject`), so a crumb reads `1100 · Cash & equivalents` or `JE-1060 · Revenue accrual` rather than `Account 1100`. The long-documented but never-implemented `breadcrumb` prop is now real, and is an override rather than a requirement. `SecurityDetail` is the one exception — it carries its own compact record header — but its back link now takes its label from the map, so it calls the registry what everything else does. |
| **E-3 · selection in the URL** | The two screens on the loop now keep their primary selection there: `Reconciliation` reads and writes `?break=`, `AccountingWorkstation` reads and writes `?entry=`. Arriving from a loop opens the right object instead of the first row, and Back / new-tab / deep links keep the operator's place. |

**Two new contract rules** in `check_suite_cohesion.py`: `prefs` (no raw `setAttribute` of a chrome attribute — it overwrites the operator's choice on every render) and `operator` (the masthead identity is suppressible only pre-auth). Nine rules total.

---

---

## Shipped — wave 1, the spine

Three files, one or two lines per screen, no new screens.

| File | What it owns |
| --- | --- |
| `templates/_scaffold/app-map.js` | The one address book. 35 screens under five business domains (Trading · Accounting · Operations · Research · Reporting, Admin pinned to the foot), every `G` chord, `activeFromLocation()`, and `open(type, id)` for 21 object types. A screen's whole nav is `sections: MeridianAppMap.sections("<id>")` + `onSelect: MeridianAppMap.go`. Unknown ids toast instead of silently doing nothing. |
| `templates/_scaffold/world.js` | The one shared fiction. A **ticking** instant (2026-08-12 14:32:08Z, `subscribe`/`tick`), one business date with July as the open close period and Q2 as last closed, one signed-in `OPERATOR` (`r.alvarez` — deliberately an id the seed trails already credit, so past entries read as your own work), canonical `ENTITIES`, `PEOPLE`, `CUSTODIANS`, and feed health derived from the clock so the pulse dot and the clock stop contradicting each other. |
| `components/shell/GlobalSearch.jsx` | The masthead field made real: the current screen's own objects (via `MeridianOperatorLayer.current.resolve`) plus every destination in the map. `ds-base.js` mounts it and binds the topbar button and `/`; screens write nothing. Has its own `@dsCard`, so consuming projects get it too. |

Also changed: `WorkstationTopbar` drives its own clock from the world and reflects real feed health (pass `clock` only to freeze it deliberately); `ds-base.js` loads the spine and warns on an environment outside the mode set rather than rendering a silently-neutral badge; `operator-layer.js` publishes the live layer so system chrome can reach a screen's objects; `screen-kit.js`'s `NAV_SECTIONS` and its fallback route now defer to the map.

**Enforced, not just written** — `scripts/check_suite_cohesion.py`, advisory by default (`--strict` to gate): `env` · `nav` · `map` · `label` · `clock` · `search` · `world`. The `label` rule compares each masthead literal against the screen's app-map label; labels stay literals in the markup rather than `{{ }}` holes, because a hole would delay the paint — so the rule is a comparison, not an interpolation.

---

**Finding.** Individually the screens are deep — guarded flows, proof contracts, action envelopes, data states. Collectively they are **27 dioramas, not one product**. What is missing is not more screen depth; it is the connective tissue an operator uses all day: one address book, one clock, one signed-in person, one set of preferences. Five tracks below, each stated as a gap with evidence from the tree.

---

## A · Navigation is decorative

The rail is the most-repeated element in the suite and it is, on most screens, a picture of a rail.

| | Evidence |
| --- | --- |
| A-1 | **Nine screens render a rail whose `onSelect` is a no-op.** `on-select="{{ noop }}"` — `reconciliation` (matcher view), `ingestion-operations`, `security-master-registry`, `basket-builder`, `trading-desk`. `onSelect: () => {}` — `report-scheduler`, `report-library`. `onNav: () => {}` — `analytical-workbench`, `investment-event-ledger`. Clicking "Dashboard" in any of them does nothing, with no disabled state to say so. |
| A-2 | **Twelve screens have no rail at all** — `charting-workstation`, `ownership-graph`, `alm-review`, `close-management`, `ReconciliationException`, `ops-wall`, `interaction-depth`, `adapter-operations`, `settings-admin` (local section nav instead), `report-pack`, `asset-sheet`, `session-start`. The frame changes shape as you move, so there is no persistent "where am I in the product". |
| A-3 | **Every rail invents its own vocabulary.** Section labels across the tree: Operate · Books · Data · Reporting · Analysis · Workspaces · Strategy · Review · Investment accounting. The portfolio dashboard is `dashboard` in five screens and `portfolio` in `blank-workstation`. No two rails list the same set of destinations, so the product's information architecture is unknowable from any single screen. |
| A-4 | **Cross-screen routing exists in exactly two places.** `_strategy-executor/screen-kit.js` `ROUTES` (the seven strategy screens — the model to copy) and a hand-rolled `if (id === …)` ladder in `alerting-workstation` that reaches sideways through `../../templates/…`. Everything else is a dead end or a hardcoded single destination: `session-start` sends **every** handover and trail object to `Reconciliation.dc.html` regardless of its type. |

**Proposal — `templates/_scaffold/app-map.js`, the one address book.**
One module declaring Meridian's actual IA: `sections` (id · label · icon · `href` · `G` chord) plus `open(type, id, params)` → href for the 17 object types. Every screen becomes two lines:

```js
sections: MeridianAppMap.sections("trading-desk"),   // rail, palette "Go to" rows, G chords
go: MeridianAppMap.go,                               // real navigation, identity carried in the URL
```

Payoff: the rail works everywhere, the palette's "Go to" group is identical on every screen, the twelve rail-less screens can adopt the same rail without inventing one, and `session-start` routes each object to the screen that owns its type. This is the single highest-leverage change in the review — one new file, one or two lines per screen.

---

## B · The chrome does not tell the truth

| | Evidence |
| --- | --- |
| B-1 | **The search field is dead on 27 of 27 screens.** `WorkstationTopbar` renders a full-width button reading *"Search symbols, runs, commands… Ctrl K"*. `onSearch` is passed by **no template in the tree** (zero matches). The most prominent affordance in the chrome does nothing when clicked, on every screen. The palette it advertises exists — but only on keyboard, and only on screens that mounted the operator layer. |
| B-2 | **An environment that does not exist.** `reconciliation` (queue) passes `environment="OPS"`. `Badge` has no `ops` variant, so it falls through to `neutral` — the contract's mandatory Live/Paper/Fixture badge is not satisfied, and `ds-base.js`'s fallback badge (driven by `<html data-environment>`) sits in the same corner saying something different. |
| B-3 | **Two screens ignore their own environment tweak.** `analytical-workbench` and `investment-event-ledger` hardcode `environment="PAPER"` in the topbar while the `environment` prop still moves `<html data-environment>`. Set the tweak to LIVE and you get a LIVE document scope under a PAPER badge — plus the red LIVE inset hairline never fires. |
| B-4 | **Module labels are retyped, and drift.** `reconciliation`'s matcher view is labelled "Accounting" while its queue view — same file — says "Reconciliation". `charting-workstation` is "PlotTool", `ownership-graph` is "Entities" — names that appear nowhere else in the suite, including in the rails that are supposed to link to them. The label belongs to the app map, not to each view. |
| B-5 | **Nobody is signed in.** No screen shows the operator, their role, or their desk — yet `accounting-workstation` has a `role` tweak that silently disables posting, and `session-start` spends a whole screen establishing an identity that no other screen ever mentions. `SessionControls` is exported by the design system and used by **zero** templates. |
| B-6 | **`WorkstationShell` is used by 11 of 27.** The other 16 hand-roll `height:100vh; display:flex` frames. That is why B-1…B-4 drift: there is no one place the chrome is assembled. |

**Proposal.** Wire `onSearch` to the palette in `ds-base.js` (one place, all screens). Fix OPS → the real environment. Take `moduleLabel` from the app map. Mount `SessionControls` in the topbar's status cluster, fed by the shared session. Migrate hand-rolled frames to `WorkstationShell` opportunistically — required only for the screens being touched anyway.

---

## C · The world does not agree with itself

Walk two screens and the fiction breaks.

| | Evidence |
| --- | --- |
| C-1 | **Six different frozen wall clocks.** `14:32:08 UTC` (most), `11:29:42` (analytical-workbench), `10:45:12` (investment-event-ledger), `16:05:41` (close-management), `09:12:05` (alm-review), `11:48:20` (ReconciliationException). Each sits next to a green pulse dot that claims a live feed. A clock that never moves under a "connected" indicator is the fastest way to read as a mock. |
| C-2 | **Three business dates.** `accounting-workstation` closes 2026-Q2 as of Jun 30 · `session-start` signs in on 2026-07-05 · `ReconciliationException` works FY2026 · July, as-of Jul 31 · `security-master-registry` audit trails stop Jun 09. The same operator cannot be in all three. |
| C-3 | **The fund has two names.** "Meridian Fund I LP" (`accounting-workstation/data.js`) vs "Meridian Capital · Fund I" (`ReconciliationException` context bar). Same legal entity, two renderings, in two screens that explicitly hand off to each other. |
| C-4 | **Six actors and no self.** `k.ito`, `r.alvarez`, `E. Torres`, `A. Rodriguez`, `D. Kim`, `M. Alvarez` appear as audit actors. None of them is the person `session-start` signed in. Post a journal entry and the trail credits a stranger. |

**Proposal — `templates/_scaffold/world.js`, one shared fiction.**
`NOW` (one instant, ticking — so the clock and the pulse dot stop contradicting each other), `BUSINESS_DATE`, `PERIOD`, `ENTITIES` (canonical id → label), `OPERATOR` (`{id, name, role, desk}`), `DESK`. Screens read it instead of retyping literals. Seed data keeps its own historical timestamps; only *now*, *the period*, *the entity names*, and *who I am* are centralised.

---

## D · Preferences do not survive a step

`theme`, `density` and `environment` are per-screen props applied in each screen's `applyChrome()`. Navigate from a terminal-density dark Ops Wall to the Trading Desk and you land in default-density light PAPER. Real workstations remember; this one forgets between every click.

**Proposal.** Persist the three to `localStorage` in `ds-base.js` (tweak seeds it, operator choice wins, per-screen default only on first visit). Small change, disproportionate realism payoff — it is the difference between "a set of files" and "an application I'm logged into".

---

## E · Functional gaps that read as unreal

| | Gap |
| --- | --- |
| E-1 | **No way back.** `ObjectTrail` is mounted on three screens. On the other 24 there is no breadcrumb, no back to the parent object, no browser-history integration — you arrive at `SecurityDetail.dc.html?security=…` and the only exit is the URL bar. |
| E-2 | **Handoffs are one-way.** `PRODUCTION_SCREENS.md` documents reconciliation → accounting and close → tie-out, but not the returns: a journal entry cannot open the break that caused it, an account sheet cannot open its own reconciliation. Operators work in loops, not chains. |
| E-3 | **Selection and filter state die on navigation.** URL params exist on a handful (`view`, `security`, `task`, `horizon`, `strategy`, `run`). Elsewhere, filters, sort, selected row and open drawer are component state — so deep links, "open in new tab", and the back button all lose the operator's place. Every screen's *primary* selection should be a URL param. |
| E-4 | **In-flight work is not protected.** `ActionQueue` is per-screen and unmounts on navigation; leaving `basket-builder` mid-commit silently drops the instruction whose whole purpose is to stay visible until it lands. A suite-level queue (sessionStorage-backed) plus a navigation guard would finish the pattern the system already argues for. |

---

## Sequence

| Wave | Ships | Why first |
| --- | --- | --- |
| **1 · Spine** ✓ | `app-map.js` · `world.js` · live search | Every later fix references one of the three. |
| **2 · Chrome truth** ✓ | valid environments · labels from the map · `SessionControls` in the masthead | Cheap, visible, and cleared the contract violations. |
| **3 · Continuity** ✓ | sticky theme/density/environment · the rail on every non-exempt screen · selection in the URL | Made navigation feel like one app rather than 27 documents. |
| **4 · Loops** ◧ | `LOOPS` + `ReturnPath` shipped and wired on entry ⇄ break | The remaining loops are now one data field each. |

## Still open

- **E-2 · the rest of the loops.** The relations are declared for eleven object types; two are wired to real data. Each remaining one is a field on a seed record plus a `ReturnPath` in the inspector — no new machinery.
- **E-4 · in-flight work is unprotected.** `ActionQueue` is still per-screen and unmounts on navigation; leaving `basket-builder` mid-commit drops the instruction whose whole purpose is to stay visible until it lands. Needs a `sessionStorage`-backed suite-level queue and a navigation guard.
- **E-3 · selection in the URL** is done on the two screens the flagship loop touches; the other 25 still hold filters, sort and open drawers in component state.
- **E-1 · object names.** The breadcrumb is on every screen built on `WorkstationTopbar`, but only three screens call `nameObject`; elsewhere an object crumb still reads `Run SRUN-1042`. One line in the screen that loaded the record.
- **E-1 · `SecurityDetail`'s own masthead.** The record page still hand-rolls a header instead of `WorkstationTopbar` — a 28th masthead in a suite that just spent a wave unifying chrome. Folding it onto the shared one would give it the derived crumb and the operator chip for free.
</content>
</invoke>
