# The operating language

**Status: normative. Release 3.2.0.**

Meridian's operating concepts — *this entity, in this period, on this basis; this evidence, as of
this moment; this one thing I have selected; this value, against that reference* — were prose in
the guidelines and hand-rolled in every screen. Release 3.1.0 made them components. Release 3.2.0
hardened those components. This document is the law for **using** them.

An operator moving between the trading desk, the close calendar, and the reconciliation matcher
should not have to relearn where scope lives, what "selected" means, or how a screen admits that
its data is old. Six primitives carry those answers, identically, everywhere.

---

## The vocabulary

| The operator's question | The primitive | What it replaces |
| --- | --- | --- |
| Where am I working? | `WorkspaceContextBar` | Per-panel period, currency, and basis pickers that silently disagree |
| What is the one thing I mean? | `LinkedSelectionProvider` | A `selectedIndex` in each screen's local state |
| What is this value measured against? | `Datum` | An axis the reader must interpolate against by eye |
| Is this evidence current? | `ContinuityFrame` | Spinners, blanked panels, and staleness nobody declares |
| Is this figure complete? | `AnalyticalFigure` | A plot with a title and no unit, source, or condition |
| What is a workspace module? | `Capsule` | Ad-hoc panels of arbitrary height and inconsistent anatomy |

Six words. Every Meridian screen is spoken in some subset of them.

---

## Rules of use

### `WorkspaceContextBar` — scope is declared once, at the top

**Rule.** A screen that reads data scoped by entity, book, basis, period, as-of, currency, or
scenario declares that scope in one bar directly under the chrome, and every panel below inherits
it from `WorkspaceContextProvider`. No panel carries its own period picker.

**Test.** Point at any number on the screen and ask "as of when, in what currency, on what basis?"
If the answer is not in the context bar, the screen is lying by omission.

**Anti-pattern.** A period selector in a card header. A currency toggle in a table toolbar. Two
panels showing the same measure under different as-ofs with nothing to say so.

**3.2 note.** The bar measures itself and folds low-priority facets into a `More +N` disclosure —
drop order scenario → currency → book → as-of → period. Entity, data condition, and a locked period
never collapse. Do not add breakpoints around it.

### `LinkedSelectionProvider` — selection is committed, named, and shared

**Rule.** When two or more surfaces respond to "the thing the operator picked", that selection lives
in a provider, not in the screen. Hover previews stay local to the renderer; click, Enter, and Space
commit; Escape clears.

**Test.** Can you delete the screen's own selection state entirely? If a `useState` still holds a
row index that another panel reads, the coordination is fake.

**Anti-pattern.** Passing `selectedId` down four levels. Two panels each keeping their own idea of
the selected security. Hover changing a chart.

**3.2 note.** Name every provider with `scope` when a screen hosts more than one linked group
(`"position"` and `"lot"` are independent selections, not one nested one). `multiple` plus `items`
gives ⌘/Ctrl-click toggling and Shift-click range extension. `back()` walks committed history —
offer it wherever a drill-in loses the operator's place.

### `Datum` — a reference the reader does not have to compute

**Rule.** Any value that is judged against something — a target, a zero, a prior close, an embargo
window, the current time, the committed selection — carries that reference as a datum inside the
plot. The reader never estimates a threshold from an axis.

**Test.** Does the figure answer "is this good?" without the reader doing arithmetic?

**Anti-pattern.** Terracotta as a default datum color — it is the brand and scenario signal, not a
generic rule. A datum for model uncertainty; that is `IntervalBar`. A datum for observed-data
condition; that is `DataCondition`.

**3.2 note.** Use `svg` inside a plot's own `<svg>` so the datum shares its coordinate space and
prints with it. `to` makes a range band for spans — embargo windows, feed gaps, tolerance corridors.
Wrap crowded sets in `DatumGroup`, which measures and assigns label lanes so labels stack instead of
overprinting.

### `ContinuityFrame` — degraded evidence stays visible, marked, and locked

**Rule.** A consequential surface never blanks and never spins in place of the last known good
answer. It shows the retained evidence, names the condition, states the age, and locks writes.

**Test.** Pull the feed. Does the operator still see the last confirmed number, know it is old, know
how old, and find themselves unable to act on it by accident?

**Anti-pattern.** A skeleton where a number was. A stale value styled identically to a live one. A
toast that scrolls away as the only notice of an offline provider.

**3.2 note.** Pass `since` and the frame runs its own clock — 1s cadence under a minute, 15s under
an hour, 60s beyond. `dim="auto"` fades retained content on stale, offline, and error, the same
states already locked read-only. Supply `onRetry` wherever recovery is an operator action rather
than a wait.

### `AnalyticalFigure` — a figure is complete or it is not published

**Rule.** Consequential analysis ships inside a figure that carries the analytical question, unit,
as-of, source, data condition, a selected-observation readout, the method caveat, and a nonvisual
fallback. A plot without these is a decoration.

**Test.** Screenshot the figure and paste it into an email with no surrounding text. Can the
recipient tell what it measures, when, from where, and how much to trust it?

**Anti-pattern.** A chart whose only label is its title. Essential values reachable only by hover.
A complex plot with no exact-values fallback.

**3.2 note.** `variant="small-multiple"` compacts the figure for tiling and drops the per-panel
question and fallback, because `AnalyticalFigureGrid` declares them once for the set — and the grid
states the **shared scale** explicitly, or prints the standing warning that scales differ.
`variant="print"` flattens to paper and expands the exact-value table.

### `Capsule` — a workspace module has fixed anatomy

**Rule.** Modules on a composed workspace are capsules: header with identity, context line, and
honest state; scrolling body; optional footer for provenance. Heights come from the `xs / sm / md /
lg` ladder so a grid of modules aligns without hand-tuned pixels.

**Test.** Do two modules on the same row share a baseline and a header height? Does each one say
what it is scoped to and whether its data is ready?

**Anti-pattern.** A bare `PanelSurface` with a hand-built header. Arbitrary heights. A module that
does not declare its state.

**Known gap.** `movable` and `resizable` currently render affordances that do not move or resize
anything. Do not use them in production screens until they are wired.

---

## Restraint over coverage

**A screen adopts the primitives its job needs, and no more.** Six-for-six on every screen would be
consistency theater: a sign-in gate has no analytical scope to declare, and a printable report pack
has nothing to select.

Reach for a primitive when the screen actually asks its question:

| Archetype | Nearly always | Often | Rarely |
| --- | --- | --- | --- |
| **Start** (sign-in, blank shell) | — | `ContinuityFrame` for provider reachability | the rest |
| **Monitor** (dashboards, desks, ops) | `WorkspaceContextBar`, `ContinuityFrame`, `Capsule` | `LinkedSelectionProvider`, `Datum` | `AnalyticalFigure` outside the main plot |
| **Investigate** (workbench, accounting, recon) | all six | — | — |
| **Compose** (builders, baskets, worksheets) | `WorkspaceContextBar`, `LinkedSelectionProvider`, `Capsule` | `Datum` on preview plots | `ContinuityFrame` |
| **Report** (packs, statements) | `AnalyticalFigure` (`variant="print"`) | `Datum` | selection, capsules |
| **Admin** (settings, governance) | — | `Capsule`, `ContinuityFrame` on integration state | `Datum`, `AnalyticalFigure` |

If a screen uses a primitive in only one place and gains nothing from the shared model, take it out.
A primitive used once is a dependency, not a language.

---

## The screen checklist

Before a screen is considered adopted:

1. **Scope** is declared once, at the top, and nothing below re-asks for it.
2. **Selection** state has been deleted from the screen and lives in a named provider.
3. Every judged value has its **reference** in the plot, not in the reader's head.
4. Every consequential surface has an answer for **feed loss** that is not a spinner.
5. Every published figure passes the **paste-into-an-email** test.
6. Modules share **anatomy and height**.
7. The screen uses **no primitive it does not need**.

---

Reference compositions: `../docs/COORDINATED_COMPONENTS.md`. Component-by-component API:
`../components/<domain>/<Name>.d.ts`. Reach-for-this guidance: `PATTERNS.md`. The analytical law the
figure rules descend from: `VISUALIZATION_GRAMMAR.md`.
