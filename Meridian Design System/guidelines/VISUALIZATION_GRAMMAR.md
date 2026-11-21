# Meridian — Visualization & Analytical Grammar

**v3.1 · the law for every chart, table, graph, and analytical workspace in Meridian.**

Meridian visualization is an **operational evidence system**, not a chart gallery. Every visualization
must answer four questions without hover, without a detached legend, and without explanatory prose:

1. **What changed?**
2. **Compared with what?**
3. **How certain and current is it?**
4. **What should the operator inspect or do next?**

> The default view carries the analytical claim. If it needs a tooltip to be intelligible, it is not done.

## 3.1 compound figure contract

`ChartFrame` remains the plot primitive. `AnalyticalFigure` is the consequential analytical container and should own the complete public evidence contract:

1. question or analytical claim;
2. title, context, and unit;
3. actual, comparison, benchmark, scenario, estimate, and target roles;
4. as-of time, source, condition, and coverage;
5. main plot;
6. selected-observation readout;
7. one primary annotation;
8. method note or caveat;
9. nonvisual table, summary, or relationship outline;
10. export or copy-link actions supplied by the consuming application.

A figure that omits source, state, or fallback is not complete merely because the marks render.

### Which of the three

| | Role | Reach for it when |
| --- | --- | --- |
| `ChartFrame` | Axis / plot chrome | You are drawing marks yourself and need real-pixel axes, gridlines, rails |
| `Figure` | Parametric plot | You need a ladder, plate, distribution or trace and would otherwise write a new named chart |
| `AnalyticalFigure` | Published contract | The figure is consequential — it needs a question, unit, as-of, source, condition, readout, method, fallback |

`AnalyticalFigure` wraps either of the other two, or a domain chart. The domain ladders and matrices
(`CashFlowLadder`, `MaturityLadder`, `DurationContribution`, `ComparisonPlate`, …) stay in their own
right: each states a bucket definition, a comparison mode, or per-point envelopes that `Figure` does
not model. Folding them into `Figure` is 4.1 · D-01.

### New domain visualizations

| Need | Component | Contract |
| --- | --- | --- |
| Coverage, population, quality, workflow, or scenario matrix | `AnalyticalField` | every mark maps to an observation; keyboard selection and textual summary |
| Asset inflow versus liability outflow by timing bucket | `CashFlowLadder` | shared scale, visible net position, no dual axes |
| Exposure or obligation by maturity | `MaturityLadder` | primary and comparison series on one baseline and domain |
| Portfolio contribution by security or sector | `DurationContribution` | ranked, directly labeled, committed selection |
| Close-control topology | `ControlDependencyGraph` | stable layered layout, orthogonal edges, blocker semantics, outline fallback |

These components use SVG/DOM for semantics and annotation. Do not mount hundreds of substantial SVG figures: summarize repeated populations, virtualize adjacent tables, and move to Canvas only when mark count—not fashion—requires it.

### Context, selection, and continuity

- Read entity, basis, period, currency, scenario, and freshness from the workspace context rather than duplicating labels in local state.
- A selected mark should synchronize through `LinkedSelectionProvider`; hover remains preview.
- Place live or refreshable evidence inside `ContinuityFrame`. Last-known-good data remains visible through stale, partial, delayed, offline, or reconnecting states.
- Saved URLs preserve committed selection and active analytical configuration; they do not preserve hover or animation frame.

---

## 1. Classify the job before choosing a chart

| Analytical job | Default artifact | Meridian component | Avoid |
| --- | --- | --- | --- |
| Compare categories | Dot plot, aligned bars, table with in-cell bars | `BarChart`, `DataGrid` | Pie charts, isolated KPI tiles |
| Compare repeated entities | Standardized plates / small multiples | `PlateGrid` | Independently scaled mini charts |
| Change over time | Line, step, event-annotated trace | `TraceChart`, `EquityCurve` | Smoothed decorative areas |
| Distribution | Histogram, interval plot | `Histogram`, `IntervalBar` | Mean-only summaries |
| Uncertainty | Interval bands, quantile bars | `TraceChart band`, `IntervalBar` | A single deterministic line |
| Relationship | Scatter, matrix, facets | `ScatterChart`, `CorrelationHeatmap` | Dual axes |
| Composition | Ordered bars, treemap | `BarChart`, `Treemap` | Multi-slice donuts |
| Hierarchy / dependency | Layered graph + outline + inspector | `LineageGraph`, `OwnershipGraph` | Force-directed business workflows |
| Operational state | Focal status view + diagnostics | `ReadinessPanel`, `GateRail` | Equal-weight dashboard grids |
| Reconcile & resolve | Table-first workspace + evidence rail | `CaseQueue` + `EvidenceInspector` | Chart-first layouts |

Familiarity is not a reason. A chart type is accepted only when it strengthens the required comparison.

---

## 2. Reading path

Every explanatory or operational view is read in this order, and is therefore *built* in this order:

1. **Claim** — `ClaimHeader`. A testable statement, not a topic label.
   Weak: `US Treasury yield curve` · Meridian: `The belly steepened while the long end remained anchored.`
2. **Immediate evidence** — the dominant chart, table, or graph (60–75% of desktop working width).
3. **Comparison context** — prior period, benchmark, peer, or scenario.
4. **State and caveat** — freshness, uncertainty, missingness, method, source (`DataCondition`, `ConnectionState`).
5. **Selected detail** — `EvidenceInspector`.
6. **Action** — filter, compare, investigate, resolve, save, export.

Rewrite the claim when the default selection or filter changes it. A workspace carries at most four
structural roles: primary viewport · evidence inspector · comparison band · command/status layer.
Do not add a panel because the grid has room.

---

## 3. Series-role ledger

Color is assigned by **role**, never by chart instance. The tokens are in `tokens/visualization.css`
and are resolved for you by `roleStyle()` inside every chart component — you never pass a hex.

| Role | Token | Line | Marker | Meaning |
| --- | --- | --- | --- | --- |
| Actual / current | `--series-actual` (graphite; paper in dark) | 2.25px solid | filled | Primary observed evidence |
| Prior period | `--series-prior` (slate) | 1.5px solid | filled | Historical comparison |
| Benchmark | `--series-benchmark` (neutral) | 1.5px dashed `4 4` | none | External reference |
| Scenario | `--series-scenario` (terracotta) | 1.5px dashed `7 5` | open | Model- or user-defined alternate |
| Target / threshold | `--series-target` | 1px dotted `2 4` | none | Goal, limit, policy boundary |
| Estimate | `--series-estimated` | dashed `5 4` + `Est.` label | open | Model-derived or incomplete |
| Selected | role treatment + datum rail | enlarged | — | Committed selection |

**Terracotta is brand and scenario. It is never the default negative or error color** — losses use
`--series-negative`. Positive, negative, warning, and exception encodings always carry a redundant
sign, marker, dash, or text label so they survive grayscale and color-vision deficiency.

Legacy aliases (`--chart-primary`, `--chart-compare-1/2`, `--chart-scenario`) now resolve *through*
the ledger, so existing charts inherit the roles without change.

---

## 4. Direct labels, not legends

- Directly label up to four important series, at the endpoint or latest observation.
- Labels keep a surface-colored halo (`--viz-halo`) and de-collide deterministically.
- Essential values are visible without hover.
- An embedded key is allowed for >4 series, toggled categories, matrices, and heat scales.
- A shared legend parked away from the charts it serves is prohibited.

## 5. Scales, axes, units

- Bars and columns start at zero unless the mark is an explicitly labelled deviation.
- Comparison plates share one domain and one tick set — `PlateGrid` enforces this.
- Zero and policy lines are stronger than gridlines (`--viz-stroke-zero`).
- 4–6 major ticks, round values, no minor ticks unless precise reading needs them.
- Units are declared once per axis or column heading, never repeated on every label.
- Log, indexed, normalized, or transformed axes are labelled as such. No dual axes without a
  documented reason and a static fallback.

### Financial notation

| Context | Notation |
| --- | --- |
| Analytical negative | `−1.23%` |
| Accounting negative | `(1.23)` |
| Basis points | `+32 bp` |
| Currency table | ISO code in the heading |
| Large approximate | `$28.4M` (exact value in detail) |
| Estimated | `4.35% Est.` |
| Stale | `4.35% · Stale 18m` |
| Not applicable | `—` |
| Missing required | `Missing` |

`DataConditionValue` and `Money.formatMoney` produce all of these — do not hand-format.

---

## 6. Comparison plates

A plate is a reusable unit governed by a geometry contract: identical plot dimensions, domain, tick
positions, title and metadata placement, padding, annotation locations, footer metric, and responsive
collapse. A plate set is sortable without changing internal scales. A selected plate gets a rail and
tint — it is never resized. Past ~12 plates, add search, sort, pagination, clustering, or a table plus
one focused plate. Never shrink type to fit more.

## 7. Tables

Tables remain the default instrument for exact multi-column comparison. Requirements: sticky headers,
tabular numerals, units in headings, sortable/resizable columns, saved column sets, keyboard row
navigation, persistent selection, distinct zero / missing / not-applicable / stale / estimated states,
source and freshness access, and a focused-detail mode on small screens rather than column crushing.
In-cell graphics (bars, dots, sparklines, mini intervals) are for scanning, not decoration. Zebra
striping is not a default — use grouping, rules, hover, and a selected-row datum rail.

## 8. Uncertainty vs. data condition

Two different claims, two different treatments, never merged:

- **Observed-data condition** — freshness, source, quality, missingness → `DataCondition`.
- **Model uncertainty** — the range of plausible outputs under assumptions → `TraceChart band`,
  `IntervalBar`, `Histogram` with percentile lines.

Label the probability level of every interval. Opacity alone never states a probability, and
uncertainty never hides inside a tooltip.

## 9. The eleven data conditions

`confirmed · provisional · estimated · stale · overridden · missing · not applicable · pending ·
exception · reconciled · restricted`

Each condition carries (1) a text label, (2) a marker or line treatment, (3) a surface or structural
treatment when high priority, and (4) a reason available in the evidence inspector. **A blank cell is
never a data condition.** See `guidelines/STATES_AND_ANTIPATTERNS.md` for the per-surface contract.

## 10. Live, delayed, partial, offline, error

`ConnectionState` states the condition next to the evidence it describes:

| Condition | Required behavior |
| --- | --- |
| Live | Show update age and cadence near the evidence |
| Delayed | Keep the last data; show delay and expected cadence |
| Partial | Show what exists and mark the missing series or periods |
| Offline | Show cached data, an offline badge, and a reconnect action |
| Error | Preserve context; say what failed and what remains valid |
| Reconnecting | Keep the last view; show progress without replacing evidence |

**A view that has rendered valid data never reverts to a loading skeleton.** For bursty streams:
aggregate before rendering, use ring buffers, downsample while preserving extrema, lower update
frequency under pressure, and preserve alert and selection semantics across refresh.

## 11. Interaction states

`Default → Preview → Selected → Expanded → Loading → Degraded (stale/partial/offline/error) → Reset`.

**Hover is preview. Selection is commitment.** Touch: first tap previews, an explicit action commits
when density demands it; dense marks get enlarged invisible hit areas (`--viz-hit-area`); nearest-item
or step-through navigation replaces pixel-precise targeting; chart gestures never trap page scroll.
Every view has an obvious reset.

## 12. Persistence

| Scope | Contents |
| --- | --- |
| URL | filters, visible series, range, scenario, selected datum or node, tab, zoom/brush, saved-view id |
| Local preference | theme, density, reduced motion |
| Saved workspace | capsule layout, durable analytical config, table columns, comparison set |
| Ephemeral (never persisted) | hover, pointer position, drag in progress, animation frame, uncommitted tooltip |

The URL codec must parse, validate, normalize, serialize in stable order, and migrate or safely reject
old schemas.

## 13. Graphs

Ownership, lineage, accounting, and workflow graphs are directed with a dominant flow, so the default
layout is **layered**, not force-directed: stable node order between refreshes, orthogonal routing,
real node dimensions in the layout, edge labels outside nodes, explicit overlap removal, and a selected
path emphasized without dimming context into illegibility. Every graph ships a text outline of nodes
and relationships, a selected-node inspector, reset/fit, and URL-backed selection. When topology gets
dense: filter to a neighborhood, cluster by stage, offer a matrix, or facet — never shrink labels or
fade edges.

## 14. Responsive contract

Desktop and mobile are sibling states, not one compressed into the other.

- **Desktop** — dominant viewport + persistent inspector; plates below or beside; controls adjacent to
  the evidence they change.
- **Mobile portrait** — claim and main visualization **before** settings; inspector becomes an inline
  disclosure or bottom sheet; plates stack; the graph shows the selected neighborhood, not a shrunken
  topology; hover becomes tap/focus; Apply/Cancel/Reset return the user to the affected visualization.
- **Mobile landscape** — only when a wide curve, table, or graph materially improves comprehension.

## 15. Accessibility

Insight title · direct labels or adjacent key · values visible without hover · accessible name and
description · data table or structured outline · keyboard selection and reset · redundant line/marker/
status encoding · reduced-motion behavior · export-ready source, date, and caveat. `TraceChart` ships
the table fallback, `LineageGraph` ships the outline — leave them on.

## 16. Renderers

| Surface | Renderer | Fallback |
| --- | --- | --- |
| Annotated curves, distributions | SVG/DOM | Static SVG/PDF + table |
| Repeated dense sparklines | Canvas2D | Summary values + selected SVG |
| Layered lineage graph | SVG + layered layout | Outline + static graph |
| Very large relational exploration | Canvas/WebGL | Filtered neighborhood + matrix |
| High-density heatmap | Canvas2D | Table + aggregated matrix |
| Report figures | SVG/PDF | High-resolution PNG |

Budgets: ≤12 simultaneous SVG chart instances per workstation; ≤~2,000 visible SVG marks per primary
view; Canvas above ~5,000 simple marks; WebGL only when scale or picking produces analytical value.

## 17. Motion

Four verbs only — **reflow** (marks move after sort/filter/scenario), **trace** (source-to-output path
in causal order), **reconcile** (two states converge to matched), **focus** (selected evidence sharpens,
context remains). Timing from the foundation tokens (`--viz-reflow`, `--viz-trace`). No bounce,
overshoot, shimmer, or particles. Reduced motion shows the final state immediately.

## 18. Annotation

One **primary** annotation per focal view — the turning point, difference, breach, or consequence.
Secondary annotations are optional and explain method, event timing, or caveat. Treatments: point
callout, range bracket, reference line, event marker, threshold band, short aligned text. Annotations
must survive export and responsive adaptation; important explanation never lives only in a tooltip.

---

## 19. Production QA gate

Ship only when every answer is yes.

**Analytical integrity** — 1. Does the title state a claim? 2. Is the strongest encoding on the main
comparison? 3. Are scale, baseline, units, aggregation, and smoothing trustworthy? 4. Are uncertainty,
missingness, and sample support visible when material? 5. Are the series roles consistent?

**Composition** — 6. Is one evidence surface dominant? 7. Are controls adjacent to what they change?
8. Are labels attached to marks? 9. Do plates share geometry? 10. Does it work as a static screenshot?

**State & provenance** — 11. Are the eleven conditions distinct? 12. Are source and as-of visible?
13. Can a selected value be traced to its evidence? 14. Does last-known-good survive degradation?

**Interaction & persistence** — 15. Is preview separate from commitment? 16. Is there a reset and an
escape path? 17. Does the URL restore the same analysis? 18. Are touch and keyboard paths complete?

**Accessibility & export** — 19. Is the claim available without color, hover, motion, or pointer
precision? 20. Is there a table or outline fallback? 21. Do exports preserve title, caveat, source, and
selected state? 22. Has grayscale and color-deficiency resilience been reviewed?

---

## 20. Prohibited deviations

- Equal-weight dashboard tiles replacing a dominant viewport.
- A detached legend as the only series key.
- Hover-only essential values.
- Independently scaled comparison plates.
- Force-directed layout for directional ownership or lineage.
- Brand terracotta reused as the default loss or error color.
- Blank cells standing for missing or not-applicable data.
- Model uncertainty and observed-data condition sharing one treatment.
- Mobile settings placed before the main evidence.
- Solving graph or plate density by shrinking labels.

**Reference implementation:** `templates/analytical-workbench/` (rates workbench) and
`templates/investment-event-ledger/` (event-first investment accounting).
