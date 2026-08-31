Time and change primitives — the content rules as components. `Timestamp` renders UTC mono time ("2026-06-09 20:00:00Z", `format="time"` → "20:00:04Z", `format="relative"` → auto-ticking "6m ago"; full UTC always on hover). `Delta` renders signed change with the explicit-sign rule: `+1.84%`, `-4118.22`, `±0.00`.

```jsx
<Timestamp value={run.completedAt} />
<Timestamp value={lastSeen} format="relative" />
<Delta value={1.84} suffix="%" />
<Delta value={dd} tone="down" />   // tone override for inverted metrics (drawdown)
```

Use these instead of hand-formatting — hand-rolled time/delta strings are the most common content-rule violations. Never local timezones; never unsigned deltas.
