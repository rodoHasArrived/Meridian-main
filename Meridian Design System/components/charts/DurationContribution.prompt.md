What drives aggregate duration — ranked by contribution, with the inputs that produced it kept in view.

```jsx
<DurationContribution
  items={sectors}
  groupBy="Sector"
  durationDefinition="Effective duration"
  total={5.42}
  comparisonLabel="Benchmark"
  asOf="July 31, 2026"
  onSelect={row => select(row)}
  onOpenTrace={row => openTrace(row)}
/>
```

**Three quantities, never interchanged.** Weight (percent of value), instrument duration (years), and contribution (weight × duration) are distinct and all three stay on the row. A high-duration position with a small weight and a low-duration position with a large weight can contribute equally — that is the point of the view, and it is only legible when all three are visible. Spread duration and key-rate duration are separate measures: state which one is in force via `durationDefinition`, shown on the face.

**Zero datum and offsets.** Contributions diverge around a visible zero, so offsetting exposures read as offsets rather than as small bars. Negative contributions take the secondary series color and the sign is written out.

**Tie out to the total.** Pass `total` and the component states whether contributions tie to it; where they do not, the residual is named in the footnote rather than quietly absorbed. Rows missing a weight or a duration render *Missing* — never zero — and are counted in that same footnote.

**Ranking.** Rank by contribution (default), by difference from benchmark, by weight, or by name. Ranking never changes units or scale, so the reading is stable as the sort changes. Benchmark values must come from compatible methodology; the component cannot verify that for you.

**Scenarios are scenarios.** `scenario` labels the run as a scenario in the header, the units line, and the table caption. Do not present a scenario as a forecast.

**Fallback.** The table carries entity, weight, duration, contribution, comparison, difference, and condition, and takes over on narrow screens. Selection is keyboard-operable throughout — arrows move, Enter and Space commit — and is meant to drive a linked inspector, the underlying positions, or `CalculationTrace` via `onOpenTrace`.

Unavailable-input safeguard: explicit missing/not-applicable/restricted conditions or
nonfinite required amounts replace the populated view with a stated unavailable
result. Restricted row values, labels, titles and table totals are not emitted, and
no partial totals or percentages are presented. Empty declared buckets still
represent an observed empty bucket at zero. The caller must recover usable data
before the ordinary view returns; UI redaction does not replace server permissions.
