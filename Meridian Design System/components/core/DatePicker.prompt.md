Single date field with a calendar popover — trade dates, settlement dates, effective dates, as-of dates.

```jsx
<DatePicker label="Effective date" value={date} onChange={setDate} />
```

**Dates are local calendar dates, never instants.** An ISO `YYYY-MM-DD` goes in and the same string comes out, with no timezone round-trip — a date entered as the 26th is the 26th for every operator, in every timezone. Pass and store the ISO string; never a `Date` or an epoch.

The field is **typeable** — operators paste dates far more often than they click them. ISO (`2026-08-26`), `26 Aug 2026`, and `8/26/2026` all parse; an unparseable or out-of-range entry snaps back to the last good value on blur. The calendar is a real ARIA grid: arrows move a day, PageUp/PageDown a month, Shift+PageUp/PageDown a year, Home/End the week, Enter selects, Escape closes and returns focus to the field.

Constrain the range with `min`/`max`, and individual days with `disabledDate` — the hook for weekends, market holidays and closed accounting periods:

```jsx
<DatePicker
  label="Settlement date"
  value={settle}
  onChange={setSettle}
  min={tradeDate}
  disabledDate={(iso, d) => d.getDay() === 0 || d.getDay() === 6 || holidays.has(iso)}
  required
  error={touched && !settle ? "Required" : undefined}
/>
```

`error`/`required` behave exactly as on `Input` and `Combobox`, so `FormField` treats all three alike. `weekStartsOn={1}` for Monday-start desks. For a start/end pair use `DateRangePicker`; for a fiscal period use `AccountingPeriodSelector`.
