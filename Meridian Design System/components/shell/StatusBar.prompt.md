Near-black footer status bar (`#171A1F`) mirroring the desktop StatusBar — a row of mono telemetry fields divided by hairlines. Always-on operational state at the bottom of the workstation.

```jsx
<StatusBar items={[
  { status: "ok", label: "Connected", value: "IBKR · Polygon" },
  { label: "Sync", value: "00:00:04 ago" },
  { label: "Rows", value: "412,008" },
  { status: "ok", value: "12ms", label: "Latency", push: true },
]} />
```

`status` adds a leading dot (`ok` green / `warn` amber / `err` red); `push` right-aligns from that item. Values are mono tabular.
