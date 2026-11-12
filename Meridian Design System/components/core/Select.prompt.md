Native-backed single select — labeled and keyboard-accessible. Options are `{value,label}`, bare strings, or `"---"` for a divider.

```jsx
<Select label="Venue" value={venue} onChange={setVenue}
  options={[{ value: "XNAS", label: "Nasdaq" }, { value: "ARCX", label: "NYSE Arca" }]} />
```

For search-as-you-type over long lists use `Combobox`; for multi-pick use `MultiSelect`. Keep option labels short and scannable.

Keyboard: focus the trigger with Tab; ArrowDown/ArrowUp opens and focuses an enabled option. Arrows cycle enabled options, Home/End reach their boundaries, Enter/Space requests selection and returns focus, Escape closes without changing value, and Tab closes while leaving the control. Separators and disabled options are skipped. `label` or standard ARIA naming props name the trigger; supplied id/descriptions pass through for FormRow/FormField composition.
