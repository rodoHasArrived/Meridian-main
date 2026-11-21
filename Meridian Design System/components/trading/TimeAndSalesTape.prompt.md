The market-wide print tape — every print on the instrument, newest first, beside (not instead of) `FillsFeed`, which shows only the desk's own executions. Tick direction is derived per symbol from the prior print unless a print carries an explicit `dir`; size buckets raise emphasis, and `blockSize` washes and tags block prints. Nothing animates — colour and weight carry the signal.

```jsx
<TimeAndSalesTape
  prints={prints}          // newest first
  priceDecimals={2}
  sizeBuckets={[1000, 5000]}
  blockSize={10000}
  showVenue
  maxHeight={320}
/>
```

Pair it with `DepthLadder` (resting liquidity) and `Blotter`/`FillsFeed` (own orders and fills). Keep the tape's price decimals equal to the ladder's so the two columns read as one instrument.
