YieldCurve — term-structure line chart (yield vs tenor) for rates and bond surfaces (Basket Builder, fixed-income research). Tenors position on a sqrt-of-years x-scale so the money-market end doesn't crush. The primary curve draws in navy data ink (`--chart-primary`); `overlays` adds prior-period curves that recede down the series ladder (`--chart-compare-1/2`) and `scenario: true` overlays render dashed terracotta (`--chart-scenario`) so a shock can never read as observed data. An optional tenor-pair spread readout ("2Y–10Y") prints in bp, red with an inversion note when negative. Flat, token-driven.

```jsx
<YieldCurve points={TODAY} label="Today" spread={{ a: "2Y", b: "10Y" }}
  overlays={[
    { points: ONE_MONTH_AGO, label: "1M ago" },
    { points: THREE_MONTHS_AGO, label: "3M ago" },
    { points: SHOCKED, label: "Scenario: +25bp bull steepen", scenario: true },
  ]} />
```
