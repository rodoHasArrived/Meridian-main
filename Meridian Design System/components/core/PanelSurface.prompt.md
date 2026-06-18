White paper card mirroring `CardStyle` — 1px hairline border, 8px radius, a whisper of shadow (`0 1px 1px / .08`). Pad with 20px (`--pad-card`).

```jsx
<PanelSurface style={{ padding: 20 }}>…</PanelSurface>
<PanelSurface raised style={{ padding: 18 }}>…</PanelSurface>
```

`raised` = off-white `#FAFBFC` (metric tiles, inspector rails); `elevated` = slightly deeper shadow; `flat` = no shadow. Depth comes from borders, never gradients.
