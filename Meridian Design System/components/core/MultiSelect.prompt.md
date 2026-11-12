Pick **several** values from a list — books, desks, strategies, counterparties, tags. Chips in the field, a checkbox listbox below, type to filter.

```jsx
const [books, setBooks] = useState([]);
<MultiSelect label="Books" options={bookOptions} value={books} onChange={setBooks} />
```

Options are strings or `{ value, label, meta, group, disabled }` — the same shape `Combobox` takes. `value` is an array in pick order; `onChange` hands back the full next array.

**Bulk actions act on what is currently filtered.** Type "EUR", hit "Select matches", and you get exactly those — that pairing is the reason the search box and the bulk row sit together. `maxSelected` caps the selection (further options go unselectable and the count shows `n / max`); `maxChips` controls how many chips show before collapsing to `+N`.

Keyboard: ↑↓ move, Enter toggles the active option, Backspace with an empty query removes the last chip, Escape closes.

```jsx
<MultiSelect
  label="Counterparties"
  options={counterparties}
  value={cps}
  onChange={setCps}
  maxSelected={8}
  required
  error={submitted && !cps.length ? "Pick at least one" : undefined}
/>
```

**Choosing between the pickers:** one value from a short list → `Select`; one value from a long or remote list → `Combobox` / `AsyncCombobox`; several values → `MultiSelect`. To act on selected *table rows* (delete, export, reassign) you want `BulkActionBar` — that is a toolbar over a table, not a form field.
