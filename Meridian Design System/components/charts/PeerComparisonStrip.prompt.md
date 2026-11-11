Use `PeerComparisonStrip` to locate one subject within a peer population across
several metrics. Each row owns its domain; identical marker positions across rows
mean relative placement, not identical financial quantities.

```jsx
<PeerComparisonStrip subjectLabel="Synthetic fund"
  ariaLabel="Synthetic fund relative profile"
  metrics={[{ id: "fee", label: "Fee (bp)", value: 42,
    peers: [30, 40, 50, 60], higherIsBetter: false,
    fmt: value => value.toFixed(0) + " bp", note: "Same fee basis" }]}
  caption="Synthetic comparable funds; one reporting period and share-class basis." />
```

Declare population, period, units, exclusions, and comparability. Set
`higherIsBetter` only when that judgment is defensible; otherwise omit directional
semantics and consider hiding rank. Ranking is one plus the count of strictly
better peers, so ties share a rank. The subject is added to the displayed
population count. A provided median overrides the computed peer median.

Nonfinite peer inputs are excluded from the domain, median, and population count.
Missing or nonfinite subjects render a dash and no rank. Explicit finite ordered
domains are retained; ensure they contain the values because marker positions
are not clamped. Explain the cause of missingness in the surrounding workflow.
Empty peers yield no median or rank. The strip owns no loading, stale-data,
filtering, or drilldown workflow.

Supply `ariaLabel` to emit the subject/median/rank table, including every finite
peer value. `showRank={false}` also removes rank from that table. Peer marks are
not keyboard controls; expose related evidence in an adjacent inspector.
