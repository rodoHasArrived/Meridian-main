TagInput — chip-list entry for symbol lists, watchlist members, report recipients. Controlled: `value` is a string array. Enter or comma commits the draft; Backspace on an empty draft removes the last chip; blur commits. Duplicates are silently dropped.

```jsx
<TagInput value={symbols} onChange={setSymbols} uppercase placeholder="Add symbol…"
  validate={(t) => /^[A-Z.]{1,8}$/.test(t)} aria-label="Watchlist symbols" />
```
