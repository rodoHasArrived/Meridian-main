Layered, orthogonally-routed provenance and dependency graphs — sources → transformations → models → derived fields → consumers. Stable node order, labels outside nodes, committed selection, and a text outline of every node and relationship.

```jsx
<LineageGraph nodes={nodes} edges={edges} selectedId={node} onSelect={setNode} height={340} />
```

Pair the selection with `EvidenceInspector`/`ProvenanceTrace` so clicking a node fills the rail with owner, freshness, quality, and consumers. Never use a force-directed layout for ownership or lineage — direction is the message. When the topology stops being readable, filter to a neighborhood or cluster by stage; shrinking labels is not a density strategy. For legal ownership percentages use `OwnershipGraph`; for step-by-step process state use `EventTimeline` or `GateRail`.
