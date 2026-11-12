/**
 * StrategyModel — identity, lifecycle, and formation contract of the `strategy` object (the
 * seventeenth ObjectModel entry). Pure data + pure functions; nothing here renders, fetches,
 * or persists. Wire shapes: `guidelines/ENTITY_SCHEMAS.md` (Strategy · StrategyFormationSnapshot).
 *
 * Invalidation is structural, not procedural: every stage gate is `validateStage(draft, stage)`
 * over CURRENT field values, review acknowledgment binds to `formationHash(draft)`, and run
 * proofs bind to `materialHash(draft)` — so any edit invalidates dependent stages, review, and
 * proof freshness without a listener anywhere.
 *
 * @example
 * const { StrategyModel, STRATEGY_FORMATION_STEPS } = window.MeridianDesignSystem_205d17;
 * const draft = StrategyModel.createDraft({ name: "Bond Carry & Roll" });
 * StrategyModel.stageDisposition(draft, "mandate");        // "blocked" until fields are filled
 * const acked = StrategyModel.acknowledgeReview(filled);   // binds review to this exact draft
 * const { strategy, snapshot } = StrategyModel.checkpoint(acked); // version bump + frozen record
 */

export type StrategyLifecycleState =
  | "draft" | "validating" | "blocked" | "ready-for-review" | "in-review"
  | "approved" | "changes-requested" | "active" | "suspended" | "superseded" | "retired";

export interface StrategyLifecycleMeta {
  id: StrategyLifecycleState;
  label: string;
  /** Canonical Status severity: ready · review · action · blocked · info. */
  severity: "ready" | "review" | "action" | "blocked" | "info";
  /** Legal next states. Empty = terminal. */
  next: StrategyLifecycleState[];
}
/** The state graph: draft → validating → blocked/ready-for-review → in-review →
 *  approved/changes-requested → active/suspended → superseded/retired. */
export declare const STRATEGY_LIFECYCLE: StrategyLifecycleMeta[];

export type StrategyFormationStageId =
  | "mandate" | "thesis" | "universe" | "portfolioRules" | "riskGovernance" | "review";
export interface StrategyFormationStep {
  id: StrategyFormationStageId;
  label: string;
  /** One-line operator description. */
  blurb: string;
  /** Draft paths the stage owns — the fields its gate reads. */
  fields: string[];
}
/** The six guarded stages, in order: Mandate · Thesis · Universe · Portfolio rules ·
 *  Risk & governance · Review. */
export declare const STRATEGY_FORMATION_STEPS: StrategyFormationStep[];

export interface StrategyIssue {
  stage: StrategyFormationStageId;
  code: string;
  severity: "blocking" | "warning";
  message: string;
  /** Draft path the issue points at, when field-scoped. */
  path: string | null;
}

export interface StrategyDraft {
  id: string;
  version: number;
  lifecycle: StrategyLifecycleState;
  name: string;
  description?: string;
  mandate: { objective: string; benchmark: string; capitalBand: { min: number | null; max: number | null }; horizon: string; owner?: string };
  thesis: { statement: string; edge: string; decay: string; capacity?: string };
  universe: { datasetId: string; assetClass: string; filters: { field: string; op: string; value: unknown }[]; minAdv: number | null; exclusions?: string[] };
  portfolioRules: { entry: string; exit: string; sizing: string; maxPositions: number | null; rebalance: string; worksheet: { ref: string; formula: string; status: "ok" | "error"; message?: string }[] };
  riskGovernance: { maxDrawdownPct: number | null; grossExposureMax: number | null; perNameMax: number | null; stopPolicy?: string; reviewCadence: string; approvers: string[] };
  review: { acknowledged: boolean; acknowledgedHash: string | null; notes?: string | null; acknowledgedAt?: number; acknowledgedBy?: string | null };
  history: { from: StrategyLifecycleState; to: StrategyLifecycleState; at: number; actor: string | null; note: string | null }[];
  createdAt: number;
  updatedAt: number;
}

/** Frozen formation record — see `guidelines/ENTITY_SCHEMAS.md › StrategyFormationSnapshot`. */
export interface StrategyFormationSnapshot {
  schema: "meridian.strategy.formation/1";
  strategyId: string | null;
  strategyVersion: number | null;
  capturedAt: number | string;
  fields: Pick<StrategyDraft, "name" | "mandate" | "thesis" | "universe" | "portfolioRules" | "riskGovernance">;
  review: { acknowledged: boolean; acknowledgedHash: string | null; notes: string | null };
  /** SHA-256 of every formation field (canonical form) — review freshness. */
  formationHash: string;
  /** SHA-256 of material inputs only (universe, rules, worksheet, limits, benchmark) — proof freshness. */
  materialHash: string;
}

/** Bundle carrier: `const { StrategyModel } = window.<Namespace>`. */
export declare const StrategyModel: {
  LIFECYCLE: StrategyLifecycleMeta[];
  STEPS: StrategyFormationStep[];
  lifecycleMeta(id: string): StrategyLifecycleMeta;
  canTransition(from: string, to: string): boolean;
  nextStates(from: string): StrategyLifecycleState[];
  isTerminal(id: string): boolean;
  /** Pure transition with history append; `{ok:false, issue}` on an illegal edge. */
  applyTransition(strategy: StrategyDraft, to: StrategyLifecycleState, ctx?: { at?: number; actor?: string; note?: string }): { ok: true; strategy: StrategyDraft } | { ok: false; issue: { code: string; severity: "blocking"; message: string } };
  /** A complete empty draft — every section present so gates evaluate without null checks. */
  createDraft(seed?: Partial<StrategyDraft>): StrategyDraft;
  /** Recompute one stage's gate from current values. Pure; call as often as you like. */
  validateStage(draft: StrategyDraft, stage: StrategyFormationStageId): StrategyIssue[];
  /** `complete` · `attention` (warnings only) · `blocked` (any blocking issue). */
  stageDisposition(draft: StrategyDraft, stage: StrategyFormationStageId): "complete" | "attention" | "blocked";
  formationIssues(draft: StrategyDraft): StrategyIssue[];
  formationReady(draft: StrategyDraft): boolean;
  /** Deterministic JSON (mjcf/1): sorted keys, dropped undefined, arrays in order. */
  canonicalize(value: unknown): string;
  /** Synchronous, dependency-free SHA-256 → lowercase hex. */
  sha256Hex(input: string | Uint8Array): string;
  /** Hash of ALL formation fields — binds review acknowledgment to one exact draft. */
  formationHash(draft: StrategyDraft): string;
  /** Hash of MATERIAL inputs only — what a run's proof binds to. Narrative excluded. */
  materialHash(draft: StrategyDraft): string;
  /** Freeze the current draft as a StrategyFormationSnapshot. */
  snapshot(draft: StrategyDraft, opts?: { at?: number | string }): StrategyFormationSnapshot;
  /** Version-bump + the snapshot recording the checkpoint. */
  checkpoint(draft: StrategyDraft, opts?: { at?: number }): { strategy: StrategyDraft; snapshot: StrategyFormationSnapshot };
  /** Bind acknowledgment to the exact current draft; any later edit makes it stale. */
  acknowledgeReview(draft: StrategyDraft, opts?: { at?: number; actor?: string; notes?: string }): StrategyDraft;
  deepFreeze<T>(value: T): T;
  get(obj: unknown, path: string): unknown;
};
