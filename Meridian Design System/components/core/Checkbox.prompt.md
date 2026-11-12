Checkbox and Toggle — the two boolean controls. `Checkbox` for settings and multi-select lists (supports a `hint` line); `Toggle` for immediate on/off state. Both are controlled via `checked` + `onChange(next)`.

```jsx
<Checkbox checked={live} onChange={setLive} label="Live data" hint="Streams NBBO quotes" />
<Toggle checked={paused} onChange={setPaused} label="Pause alerts" />
```

Use Toggle when flipping it takes effect immediately; use Checkbox when the choice is submitted with a form or drives row selection.

Toggle is a native button with switch semantics: Tab focuses it; Space or Enter requests the next checked state; disabled switches cannot activate. Its label supplies the accessible name, or provide aria-label for a visually unlabelled switch. Both controls provide a minimum 24px-high hit area and a visible keyboard focus indicator.
