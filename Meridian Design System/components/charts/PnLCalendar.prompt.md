PnLCalendar — month-grid daily P&L heat view. Monday-first, UTC dates. Cells wash green/red at alpha-10 (alpha-20 past half the month's max magnitude); values are mono with explicit signs; the footer double-rules the month total, statement-style.

```jsx
<PnLCalendar month="2026-06" values={{ "2026-06-01": 1240.5, "2026-06-02": -3180, "2026-06-03": 0 }} />
<PnLCalendar month="2026-06" values={dailyPnl} valueFormat={(v) => (v>0?"+":"") + v.toFixed(1) + " bps"} />
```
