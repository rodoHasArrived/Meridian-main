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

Supply `ariaLabel` to name the group and emit the table of subject, median,
rank and every peer value. Each rail with peers is a button: its name states the
metric, subject value, median, rank and peer count, and Enter, Space or a click
opens the peer values in ascending order beneath the row; Escape closes them.
Pass `peerLabels` so peers read as names instead of positions. No value is
disclosed through a hover title. Set `peerDetail={false}` only where the strip
repeats a table that already lists the peers. Evidence behind a peer value still
belongs in an adjacent inspector.
