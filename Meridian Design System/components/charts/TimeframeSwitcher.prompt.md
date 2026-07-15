Two chart-toolbar time controls.

**TimeframeSwitcher** — mono segmented resolution picker (1m · 5m · 15m · 1h · 1D · 1W by default). One active value, radio semantics.

**AsOfControl** — the session clock. Every analysis surface states its time basis explicitly: **Live** (green dot, ticking UTC stamp, "Freeze") ⇄ **As-of** (amber AS-OF chip, frozen UTC datetime input, "Return to live").

```jsx
<TimeframeSwitcher value={tf} onChange={setTf} />
<TimeframeSwitcher options={["1D", "1W", "1M", "1Y"]} defaultValue="1D" />
<AsOfControl onChange={({ mode, asOf }) => reprice(mode === "live" ? null : asOf)} />
```

`AsOfControl` emits `{ mode, asOf }` — `asOf` is `null` while live, a Date when frozen. Wire it to your data layer so the whole surface reprices against the frozen instant. Put both in the toolbar of any charting or coverage view; don't leave the time basis implicit — an operator must always know whether they're looking at live or as-of data.
