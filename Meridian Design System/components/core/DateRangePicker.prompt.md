Start/end date selection over two months — P&L periods, report windows, audit ranges, backtest spans.

```jsx
<DateRangePicker label="Period" start={range.start} end={range.end} onChange={setRange} />
```

`onChange` fires with the whole `{ start, end }` on every change, including the intermediate state after the first click where `end` is still `null` — render that as an in-progress range rather than an error.

**Local calendar dates throughout** — ISO `YYYY-MM-DD` in and out, with no timezone round-trip. Store the strings.

**Presets are the primary path.** Today, Last 7d, Last 30d, MTD, QTD and YTD sit beside the calendar because that is how a desk actually picks a period; the grid is for the exception. Replace them with `presets` when a product has its own vocabulary (prior close, since inception, current accounting period), or drop them with `showPresets={false}`.

Two clicks make a range, and the span shades live from the anchor as the pointer or keyboard moves, so the selection is legible before it's committed. Clicking a completed range starts a new one instead of silently extending the old. Within the day grids, arrows move a day, PageUp/PageDown a month, Shift+PageUp/PageDown a year, Home/End the week, and Enter or Space picks. Tab and Shift+Tab traverse presets, month controls, active days and footer actions without selecting a date or closing the calendar. Buttons retain their native Enter/Space activation. Escape closes from any calendar control and returns focus to the range field.

```jsx
<DateRangePicker
  label="Reporting period"
  start={start} end={end} onChange={setRange}
  max={today}
  disabledDate={(iso, d) => d.getDay() === 0 || d.getDay() === 6}
  presets={[
    { id: "cur", label: "Current period", start: period.start, end: period.end },
    { id: "prior", label: "Prior period", start: prior.start, end: prior.end },
  ]}
/>
```

Month navigation retains button focus and moves the keyboard-active day with the displayed months, clamped to `min`/`max`, so the grid remains reachable by Tab/Shift+Tab. `months={1}` for narrow rails (the second month also hides itself under 720px). For a single date use `DatePicker`; for a named fiscal period use `AccountingPeriodSelector`.
