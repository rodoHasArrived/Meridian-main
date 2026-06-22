Inline status banner — alpha-10 fill with a 4px solid semantic left-accent border and dim title. For run results, data health, and session notices.

```jsx
<StatusBanner tone="success" title="Backfill complete" detail="412,008 bars · 0 gaps" />
<StatusBanner tone="danger" title="Provider offline" detail="Polygon last seen 14:02:11Z" />
```

Tones: `success | warning | danger | info`. Title is terse sentence case; detail carries mono evidence.
