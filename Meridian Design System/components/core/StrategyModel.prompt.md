Pure identity/lifecycle/formation contract for the `strategy` object — `STRATEGY_LIFECYCLE` state graph, the six guarded `STRATEGY_FORMATION_STEPS` (Mandate · Thesis · Universe · Portfolio rules · Risk & governance · Review), canonical SHA-256 hashing, stage validation, checkpoint snapshots, and transitions. No rendering, no persistence.

```jsx
const { StrategyModel } = window.MeridianDesignSystem_205d17;
const draft = StrategyModel.createDraft({ name: "Bond Carry & Roll" });
StrategyModel.stageDisposition(draft, "riskGovernance"); // "blocked" | "attention" | "complete"
const acked = StrategyModel.acknowledgeReview(draft);    // binds review to formationHash(draft)
const { strategy, snapshot } = StrategyModel.checkpoint(acked);
StrategyModel.applyTransition(strategy, "validating", { actor: "j.okafor" });
```

Non-obvious: gates are recomputed from CURRENT values — visiting a stage proves nothing, and any edit after `acknowledgeReview` makes review `blocked` again (`acknowledgment-stale`), because acknowledgment stores `formationHash`. `materialHash` excludes narrative fields (thesis wording, description) so a copy edit never falsely stales a run proof; universe, rules, worksheet cells, limits, and benchmark are all material. Missing transitions return `{ok:false, issue}` — they never throw.
