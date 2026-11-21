PnLCalendar — month-grid daily P&L view. Monday-first, UTC dates. Default: green/red alpha-10 heat wash (alpha-20 past half the month's max magnitude) with signed mono values. Sheet style: `heat={false}` (no wash — the value alone carries the sign color) plus `bars` per-day intraday micro-bars around a dashed midline. Footer double-rules the month total, statement-style.

```jsx
<PnLCalendar month="2026-06" values={{ "2026-06-01": 1240.5, "2026-06-02": -3180 }} />
<PnLCalendar month="2026-06" values={dailyPnl} bars={intradayPnl} heat={false} cellHeight={56} />
```
