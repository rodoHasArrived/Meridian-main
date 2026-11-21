The calendar-date authority on the namespace — the sibling of `Format`. `core/localDate.js` has owned the rule since 4.1 (a trade, settlement or accounting date is a date, not a moment, and never goes through `toISOString()` — rule C-02), but only components could import it; `LocalDate` puts the module where templates and consumer code can reach it, and `LocalDateText` renders one as a `<time>`.

```jsx
const { LocalDate, LocalDateText } = window.MeridianDesignSystem_205d17;
LocalDate.dayOfInstant(fill.ts)               // "2026-09-08" — the operator's calendar day
LocalDate.utcDayOfInstant(bar.ts)             // UTC session bucket — say so at the call site
LocalDate.addDays(LocalDate.fromISO(d), 3)    // arithmetic on a date, not a moment
<LocalDateText value="2026-06-30" format="long" />   // <time dateTime="2026-06-30">30 June 2026</time>
```

Reach for it whenever a date becomes text or a key: trade and settlement dates, period ends, calendar buckets, `?date=` params. `Timestamp` is for instants; this is for days. `utcDayOfInstant` exists for sources defined on the UTC calendar — use it deliberately and state it, never as the default.
