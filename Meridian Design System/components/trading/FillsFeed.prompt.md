FillsFeed — streaming execution tape, newest first. Mono rows: time-of-day, symbol, washed side, qty @ price. Deliberately quiet — no per-row animation. Prepend new fills to the array.

```jsx
<FillsFeed maxHeight={300} fills={[
  { id: "F-88", time: t, symbol: "AAPL", side: "Buy", qty: "100", price: "201.1200" },
]} />
```
