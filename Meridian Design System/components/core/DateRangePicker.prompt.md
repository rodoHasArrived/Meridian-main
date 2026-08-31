Start/end date range picker with a two-month calendar popover. First click sets the start (clearing any prior end); second click sets the end and closes.

```jsx
<DateRangePicker label="Backtest window" start={from} end={to}
  onChange={({ start, end }) => { setFrom(start); setTo(end); }} />
```

Dates between start and end wash blue (`--blue-a10`); the start/end cells themselves are solid accent-filled. `onChange` always fires with the full `{ start, end }` pair, even mid-selection (`end: null`).
