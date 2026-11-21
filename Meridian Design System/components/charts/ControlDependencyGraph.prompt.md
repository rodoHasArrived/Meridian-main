A stable layered graph for close controls, upstream dependencies, blockers, and downstream sign-offs. Supply explicit layers; the component uses orthogonal routes and a complete text outline.

```jsx
<ControlDependencyGraph nodes={controls} edges={dependencies}
  selectedId={selectedId} onSelect={selectControl}/>
```

Use `blocked` on an edge only when the relationship itself prevents progress. Avoid decorative radial or force-directed layouts for operational dependencies.
