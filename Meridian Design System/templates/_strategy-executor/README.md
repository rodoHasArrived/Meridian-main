# `_strategy-executor/` — suite-local controller (not a template)

Shared by the Strategy Executor suite — `strategy-formation` (formation + builder views),
`field-formula`, `strategy-runs` (run ledger + backtest + compare + covered-call lab views),
`strategy-governance` — and the `basket-builder` adapter. Load it from a screen's `<helmet>` **after** `ds-base.js`:

```html
<helmet><script src="./ds-base.js"></script><script src="../_strategy-executor/executor-store.js"></script></helmet>
```

`window.MeridianStrategyExecutor` owns:

- **Persistence** — `meridian.strategyExecutor.store.v1` in localStorage, fixture-first.
  Corrupt / unavailable / unsupported-schema storage falls back to a session-only in-memory
  store; `storageNotice()` returns the visible read-only banner content (or null).
- **Identity** — `resolveIdentity()` with precedence URL params → valid local store →
  canonical fixture (`STR-0007`). `withIdentity(href, extra)` carries `strategy`, `run`,
  `runA`, `runB` across relative navigation; `setUrlState({formationStage})` keeps stage in
  the URL. Artifact JSON never enters a URL.
- **The shared draft** — `getStrategy` / `updateStrategy` / `updateWorksheetCell`
  (`validateFormula` is the one formula gate Builder and Field & Formula share) /
  `checkpoint` / `transition` (via `StrategyModel`).
- **Immutable run artifacts** — `runBacktest` snapshots the draft into a new
  `StrategyRunArtifact` (deterministic fixture engine seeded by the proof hash: same inputs →
  same artifact), `listRuns` / `getRun`, `freshnessFor(runId)` derived centrally from the
  current draft hash — never stored.
- **Attestations** — `attest` records version- and proof-bound signatures;
  `attestationState` derives standing/superseded.

Everything financial-model-shaped lives in the design system (`StrategyModel`,
`StrategyRunModel`); this file is only the suite's store, fixture, and URL glue.
