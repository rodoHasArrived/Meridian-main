Pure carrier for the immutable StrategyRunArtifact v1 — canonical SHA-256 input hashes, proof verification, current-draft freshness, one-time metric derivation from raw series + trades, finalization (deep-freeze), and legacy `Run` adapters. Five independent axes: environment · run lifecycle · validation disposition · proof integrity · freshness.

```jsx
const { StrategyRunModel } = window.MeridianDesignSystem_205d17;
let run = StrategyRunModel.createRun(draft, { env: "FIXTURE", window: { start: "2024-01-02", end: "2026-06-30" } });
const artifact = StrategyRunModel.completeRun(StrategyRunModel.startRun(run), { series: { equity, benchmark }, trades });
artifact.metrics.cagr;                                  // ACT/365.2425 — or null + issue
StrategyRunModel.freshness(artifact, currentDraft);     // { state: "stale", changed: ["universe"] }
StrategyRunModel.verifyProof(artifact).state;           // "verified" | "mismatch"
```

Non-obvious: `completeRun` deep-freezes — historical artifacts cannot change; stale/current is *derived* by comparing `proof.materialHash` to `materialHash(currentDraft)`, never stored. Missing evidence yields `null` **plus** a validation issue (fees absent ≠ 0). `fromLegacyRun` keeps the old `metrics` map under `reported` and leaves derived `metrics` null — legacy figures render as provisional, never as derived. `normalizeEnv("FIXTURE")` → `"fixture"` (uppercase template props stay compatible).
