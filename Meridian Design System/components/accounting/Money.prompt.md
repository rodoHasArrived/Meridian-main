Carrier object for the shared money arithmetic — the same functions every accounting component uses internally, so figures you compute agree to the cent with what the tables render. Reach it like `TableHooks`:

```jsx
const { allocateAmount, roundMoney, convertAmount, formatBps } = window.MeridianDesignSystem_205d17.Money;

allocateAmount(10000, [50, 30, 20]);       // [5000, 3000, 2000] — always sums exactly
allocateAmount(100, [1, 1, 1]);            // [33.34, 33.33, 33.33] — no lost cent
roundMoney(2.675, 2);                      // 2.68 — banker's (half-even), the books default
convertAmount(84210.5, 1.0842);            // EUR→USD at 1.0842, half-even rounded
```

Non-obvious: `allocateAmount` distributes remainder cents to the largest fractional parts (deterministic, stable), and falls back to an equal split when all weights are 0. `roundMoney` defaults to **half-even**, not schoolbook rounding — pass `"half-up"` for invoice convention. Rendering stays `AmountCell`'s job; use `formatMoney` only where a raw string is unavoidable (tooltips, exports).

Money.toNumber is a tolerant display parser. It must not validate posting, proof, or required source amounts. Financial components use strict complete numeric availability checks before arithmetic; malformed prefixes or exponent corruption cannot become proof inputs.

`Money.getDefaultLocale()` and `Money.setDefaultLocale(locale)` access the shared formatter locale. This is mutable application-wide state; restore the prior locale after a scoped override. Display parsing remains tolerant and must not be used as a financial proof validator.
