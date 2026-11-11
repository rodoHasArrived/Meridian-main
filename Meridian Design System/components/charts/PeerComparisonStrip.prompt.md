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

Validate finite peer inputs and ensure explicit domains contain the values;
marker positions are not clamped. Missing subject values render a dash, but the
caller must explain missingness rather than presenting a computed rank as useful.
Empty peers yield no median or rank. The strip owns no loading, stale-data,
filtering, or drilldown workflow.

Supply `ariaLabel` to emit the subject, median, individual peer values, and rank
table. Peer marks use native titles and are not keyboard controls; the table
exposes the same formatted individual values without requiring hover. Put source
evidence and any supported drilldown in an accessible adjacent inspector.
