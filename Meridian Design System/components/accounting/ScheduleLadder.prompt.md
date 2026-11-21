The factor / rate schedule for a security screen: one continuous period ladder with two named lanes — **expected**, modelled from the security's terms, and **actual**, received from a source. Past periods carry a tolerance-aware variance; future periods carry expected only and read *Not yet delivered*.

```jsx
<ScheduleLadder
  title="Amortization factor schedule" kind="Factor" object="Pool 3138WA · CUSIP 3138WAQP2"
  asOf="2026-08-12" properties={security.properties} requires="amortizes"
  notation="factor" tolerance={0.000001}
  expectedLabel="Expected" expectedSource="Modelled from prospectus"
  actualLabel="Actual" actualSource="Agency factor file"
  basis="Scheduled principal at 100 PSA, prospectus §4" rows={factorRows}
  onSelectRow={setPeriod} onOpenTrace={openTrace} />
```

**Applicability is a property test, never an asset-class label.** `requires` names the property that makes the schedule real — `"amortizes"`, `["resets","indexSource"]`, `{any:["sinkingFund","callSchedule"]}`, or a predicate. A hybrid ARM MBS then gets the factor lane *and* the rate lane with no special case; `if (type === "MBS")` breaks on the next instrument you onboard. Inapplicable schedules render nothing by default (`hideWhenNotApplicable={false}` states the absence instead). `ScheduleLadderSet` takes the security's properties plus a declared list of schedules and renders exactly the applicable ones — that is the component to drop on the security screen.

**Name both lanes on the face.** `expectedSource` and `actualSource` are not decoration: two unlabelled lanes invite the reader to assume one is truth. Expected is a model output; say what it was modelled from (`basis` restates it in the footer).

**Most rows are the future.** A row is projected when its ISO `date` is after `asOf` (or when `projected` is set), and a single rule row marks the boundary. Projected rows never resolve to agreement — they show expected, the delivering source, and `actualDue`. Delivered rows delegate to `VarianceCell`, so `pending`, `not-comparable`, `missing-actual`, and the ten data conditions all keep their normal meaning, and the tally counts only what actually arrived.

**One question per ladder.** It answers *will the next payment match our terms*. Yield, price, and cash projections belong to `IncomeSchedule` (accrual mechanics, single lane) and the analytics panels — adding them here makes the panel uncheckable. Tolerance is stated on the face: absolute for factors (`0.000001`), relative for cash amounts, and the near-tolerance band resolves to Review rather than agreement.
