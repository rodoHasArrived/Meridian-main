Native-backed single select — labeled and keyboard-accessible. Options are `{value,label}`, bare strings, or `"---"` for a divider.

```jsx
<Select label="Venue" value={venue} onChange={setVenue}
  options={[{ value: "XNAS", label: "Nasdaq" }, { value: "ARCX", label: "NYSE Arca" }]} />
```

For search-as-you-type over long lists use `Combobox`; for multi-pick use `MultiSelect`. Keep option labels short and scannable.

A11y (3.5): a fully named select-only combobox — trigger `role="combobox"` named by label AND current value, `aria-controls` wired to the listbox, `ariaLabel` fallback for unlabeled instances.
