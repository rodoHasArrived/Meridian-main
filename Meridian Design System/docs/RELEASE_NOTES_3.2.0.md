# Release 3.2.0 — Hardening the operating language

**11 August 2026 · minor · strictly additive**

Release 3.1.0 shipped six primitives that made Meridian's conceptual model executable:
`Datum`, `Capsule`, `WorkspaceContextBar`, `LinkedSelectionProvider`, `ContinuityFrame`, and
`AnalyticalFigure`. They shipped with contracts, prompt briefs, and specimen cards — and no screen
used them. **Release 3.2.0 is the first half of closing that gap:** the primitives are hardened to
the point where a production screen can adopt them without working around them. The screen adoption
itself follows in 3.3.

No existing prop, default, or export was changed or removed. A 3.1.0 consumer upgrades untouched.

---

## Added

**`LinkedSelectionProvider` — coordination that scales past one group per screen.**
- `scope` names a provider; `useLinkedSelection("name")` reaches it past a nearer one, so a screen
  can host several independent linked groups (a position group and a lot group) without nesting
  confusion or prop drilling.
- `multiple`, `max`, and `items` add additive selection: `toggle`, `add`, `remove`, `selectAll`, and
  `selectRange` for Shift-click extension. `LinkedSelectionTarget` reads ⌘/Ctrl and Shift itself.
- `history`, `canGoBack`, and `back()` walk committed selections. `historyLimit` defaults to 12.
- New API surface on the hook: `selections`, `selectedIds`, `count`, `scope`, `multiple`, `items`.
  The 3.1.0 surface (`selection`, `selectedId`, `select`, `clear`, `isSelected`) is unchanged and
  still correct for single-selection consumers.

**`Datum` — references that live in the plot.**
- `svg` renders the datum as an SVG `<g>` of `<line>`/`<rect>`/`<text>` for use inside a plot's own
  `<svg>`, sharing its coordinate space and printing with it.
- `to` turns a datum into a range band — alpha-10 wash with hairline edges — for embargo windows,
  feed gaps, and tolerance corridors. Model uncertainty still belongs to `IntervalBar`.
- `DatumGroup` (new export) measures its container, estimates label widths, and assigns collision
  lanes so crowded labels stack instead of overprinting. No-op until it has a width.

**`ContinuityFrame` — continuity that runs itself.**
- `since` derives and ticks the age with an adaptive cadence (1s under a minute, 15s under an hour,
  60s beyond), so hosts no longer run their own clock. An explicit `age` still wins.
- `dim` fades retained content; `"auto"` (the default) applies to stale, offline, and error — the
  same states already locked read-only — with a `Retained` corner mark and a pointer-events lock.
- `onRetry`, `retryLabel`, and `retrying` render a recovery control in the status row.
- `retainedLabel` customizes or removes the corner mark.

**`WorkspaceContextBar` — responsive without breakpoints.**
- `collapse="auto"` (default) measures the bar and folds low-priority facets into a `More +N`
  disclosure as width runs out. Drop order: scenario → currency → book → as-of → period. Entity,
  data condition, and a locked period never collapse.
- `collapse="never"` preserves the 3.1.0 horizontal-scroll behavior exactly; `collapse="always"`
  holds a fixed header height regardless of entity-name length.
- `minVisible` and `overflowLabel` tune the fold. Print restores every facet and drops the
  disclosure.

**`AnalyticalFigure` — figures that tile and figures that print.**
- `variant="small-multiple"` compacts head, readout, and foot for tiling, dropping the per-panel
  question and exact-values disclosure.
- `variant="print"` flattens to paper white, expands the exact-values table, and guards against
  page breaks inside the figure.
- `AnalyticalFigureGrid` (new export) tiles small multiples under one declared `sharedScale`,
  `source`, and `note` — and prints the standing warning when no shared scale is declared.
- A `@media print` layer now applies to every figure regardless of variant.

**Specimen cards.** Four new cards exercise the hardened states:
`core/reference-and-continuity` (SVG datums, range bands, lane stacking, ticking age, retained lock
and retry) · `data/linked-selection-coordination` (two named scopes, multi-select, history) ·
`charts/analytical-figure-grid` (small multiples under a shared scale, print variant) ·
`shell/workspace-context-collapse` (priority folding at four widths).

**Guidelines.** `guidelines/OPERATING_LANGUAGE.md` is new and normative: the vocabulary, a rule /
test / anti-pattern block per primitive, the restraint clause, an adoption matrix by archetype, and
the seven-point screen checklist that 3.3 will be graded against.

---

## Changed

- Retained content in `ContinuityFrame` is now dimmed and marked on stale, offline, and error. These
  states were already locked read-only in 3.1.0; the treatment makes the lock legible rather than
  changing which states are locked.
- `AnalyticalFigure` gained a variant class on its root element (`mds-figure--standard` by default).
  Consumers targeting `.mds-figure` are unaffected.

## Compatibility

Strictly additive. Two new exports (`DatumGroup`, `AnalyticalFigureGrid`) bring the namespace to 225
public symbols. No migration is required and `docs/UPGRADING.md` has no 3.2.0 entry.

## Known gaps carried forward

- `Capsule`'s `movable` and `resizable` render affordances that do not move or resize anything. They
  were deliberately left out of this release's scope; `OPERATING_LANGUAGE.md` marks them
  do-not-use until wired.
- No template consumes the six primitives yet. That is release 3.3.

## Next — 3.3, adoption

All 28 templates recomposed against `OPERATING_LANGUAGE.md`, screen by screen, applying only the
primitives each screen's job needs.
