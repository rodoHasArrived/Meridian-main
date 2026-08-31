Evidence surface for raw process output — backfill jobs, quality scans, strategy runs. Mono stream with UTC time-of-day per line, level-count filter chips, and a follow-tail that pauses automatically when the operator scrolls up (resumes at bottom). Warn/error lines carry an alpha-10 wash. It never summarizes — the raw line *is* the evidence.

```jsx
<LogTail title="backfill-7742 · Polygon daily" height={260} entries={[
  { ts: t0,       level: "info",  source: "fetch",  text: "XNAS window 2026-06-01..06-30 · 21 sessions" },
  { ts: t0 + 900, level: "warn",  source: "verify", text: "AAPL 2026-06-19 short session · 390 → 210 bars" },
  { ts: t0 + 2100,level: "error", source: "write",  text: "parquet flush failed · retrying (1/3)" },
]} />
```

Prepend/append to `entries` to stream. Keep `follow` on for live runs. Don't paraphrase log text into friendly copy — operators trust the verbatim line. Pair with `EventTimeline` when you need the who/what/when audit view rather than the raw tape.
