Searchable single-select — one value out of a list long enough that a `Select` would be a scroll hunt (counterparties, symbols, books, GL accounts). Type to filter (the matched run is marked), ↑↓ to navigate, Enter to commit, Escape to close, Home/End and PageUp/PageDown to move in bulk. Field geometry matches `Input`, so the two align in a form row.

```jsx
<Combobox label="Counterparty" options={counterparties} value={cp} onChange={setCp} clearable />
```

Options are plain strings or objects — `{ value, label, meta, group, disabled }`. `meta` right-aligns secondary text (a count, an exchange, a last price), `group` draws a small-caps heading above the first option of each run (keep grouped options adjacent), `disabled` makes an option unselectable and skips it during arrow navigation.

Form use: `required` adds the asterisk and `aria-required`; `error` renders the message, reddens the border and wires `aria-invalid`/`aria-describedby` — same contract as `Input`, so `FormField` treats them alike.

```jsx
<Combobox
  label="Settlement account"
  options={accounts}
  value={acct}
  onChange={setAcct}
  required
  error={touched && !acct ? "Required" : undefined}
/>
```

Server-side filtering: pass `onQueryChange` to fetch as the user types and feed the results back through `options`; pass `filter` to replace the default substring test (fuzzy, ticker-prefix, match-on-`meta`). For a combobox whose options are *always* remote, reach for `AsyncCombobox` instead — it owns the request lifecycle, debounce and in-flight states.

The menu opens on click, ↓, or typing — **not** on bare focus, so tabbing through a form does not pop menus open. Set `openOnFocus` if a screen wants the old behavior.
