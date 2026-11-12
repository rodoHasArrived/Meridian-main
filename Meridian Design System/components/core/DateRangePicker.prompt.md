Start/end date field with a two-month calendar popover. The named button opens with Enter, Space, or ArrowDown. First selection sets the start (clearing any prior end); second selection completes an ordered range, closes, and restores focus, including when the second day is earlier.

```jsx
<DateRangePicker label="Backtest window" start={from} end={to}
  onChange={({ start, end }) => { setFrom(start); setTo(end); }} />
```

Dates between start and end wash blue (`--blue-a10`); the start/end cells themselves are solid accent-filled. `onChange` always fires with the full `{ start, end }` pair, even mid-selection (`end: null`).

Values are civil-date strings (`YYYY-MM-DD`) and preserve their day across timezones. Arrow keys, Home/End, and PageUp/PageDown move the active day; Shift+PageUp/PageDown change year. Escape closes only the calendar. Supply a label or an external `id`/ARIA label; standard button descriptions and disabled state are forwarded.
