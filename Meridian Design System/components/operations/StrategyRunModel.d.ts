/**
 * StrategyRunModel — pure carrier for the immutable StrategyRunArtifact v1
 * (`guidelines/ENTITY_SCHEMAS.md`). Owns canonical SHA-256 input hashing, proof integrity,
 * freshness against the current draft, one-time metric derivation from raw series + trades,
 * finalization (deep-freeze), and legacy `Run` adapters.
 *
 * Five INDEPENDENT axes — none derived from another, none stored where it can drift:
 * environment (live/paper/fixture) · run lifecycle (queued→running→complete/failed/cancelled) ·
 * validation disposition (pass/pass-with-issues/fail) · proof integrity (verified/unverified/
 * mismatch, recomputed by `verifyProof`) · current-draft freshness (current/stale/unknown,
 * derived by `freshness(artifact, draft)` and NEVER persisted).
 *
 * Missing evidence produces `null` plus a validation issue — never a fixture value.
 *
 * @example
 * const { StrategyRunModel } = window.MeridianDesignSystem_205d17;
 * let run = StrategyRunModel.createRun(draft, { env: "FIXTURE", window: { start: "2024-01-02", end: "2026-06-30" } });
 * run = StrategyRunModel.startRun(run);
 * const artifact = StrategyRunModel.completeRun(run, { series: { equity, benchmark }, trades });
 * StrategyRunModel.freshness(artifact, draft).state;   // "current" until a material edit
 * StrategyRunModel.verifyProof(artifact).state;        // "verified"
 */

import type { StrategyDraft, StrategyFormationSnapshot } from "../core/StrategyModel";

export interface StrategyRunAxes {
  environment: ["live", "paper", "fixture"];
  lifecycle: ["queued", "running", "complete", "failed", "cancelled"];
  validation: ["pass", "pass-with-issues", "fail"];
  proof: ["verified", "unverified", "mismatch"];
  freshness: ["current", "stale", "unknown"];
}
/** The five independent axes and their vocabularies. Frozen. */
export declare const STRATEGY_RUN_AXES: StrategyRunAxes;

export interface RunIssue { code: string; severity: "blocking" | "warning"; message: string }
export interface SeriesPoint { t: number | string; v: number }
export interface RunTrade {
  id?: string; symbol: string; side: "Buy" | "Sell" | "Short" | string;
  qty: number; entryAt: number | string; entryPx: number;
  exitAt?: number | string | null; exitPx?: number | null;
  /** Absent ≠ zero: when no trade records fees, derived fees are null + an issue. */
  fees?: number | null;
}

export interface StrategyRunMetrics {
  totalReturn: number | null;
  benchmarkReturn: number | null;
  excessReturn: number | null;
  /** ACT/365.2425 on actual elapsed days. */
  cagr: number | null;
  /** Rolling-peak, close-to-close; a negative fraction (−0.183 = −18.3%). */
  maxDrawdown: number | null;
  /** Declared method: arithmetic daily · rf 0 · sample sd · annualized √252. */
  sharpe: number | null;
  winRate: number | null;
  avgHoldingDays: number | null;
  /** Gross traded notional / mean equity, annualized ACT/365.2425. */
  turnover: number | null;
  /** Sum of recorded per-trade fees; null (with issue) when none are recorded. */
  fees: number | null;
}

/** Immutable run record — see `guidelines/ENTITY_SCHEMAS.md › StrategyRunArtifact v1`. */
export interface StrategyRunArtifact {
  schema: "meridian.strategy.run-artifact/1";
  id: string;
  strategyId: string | null;
  strategyVersion: number | null;
  env: "live" | "paper" | "fixture";
  lifecycle: "queued" | "running" | "complete" | "failed" | "cancelled";
  requestedAt: number | string | null;
  startedAt: number | string | null;
  completedAt: number | string | null;
  request: { window: { start: string | number; end: string | number } | null; dataset: { id: string; label?: string } | null; engine: { name: string; version: string | null }; costModel: unknown | null };
  inputs: { snapshot: StrategyFormationSnapshot | null };
  proof: { algo: "sha256"; canonical: "mjcf/1"; materialHash: string; inputHash: string; hashedAt: number | string } | null;
  series: { equity: SeriesPoint[]; benchmark?: SeriesPoint[] } | null;
  trades: RunTrade[] | null;
  metrics: StrategyRunMetrics | null;
  /** The declared derivation method per metric — shipped with the numbers, not documentation. */
  methods: Record<string, string> | null;
  validation: { disposition: "pass" | "pass-with-issues" | "fail" | null; issues: RunIssue[] };
  /** Present on records adapted from the legacy Run shape; shown as provisional, never merged. */
  reported?: Record<string, number | string> | null;
  legacy?: boolean;
  error: string | null;
}

/** Legacy wire shape from `guidelines/ENTITY_SCHEMAS.md › Run`. */
export interface LegacyRun {
  id: string; kind: string; startedAt: number | string; completedAt?: number | string;
  status: string; progress?: number; metrics?: Record<string, number | string>;
  env?: string; strategyId?: string; strategyVersion?: number;
}

/** Bundle carrier: `const { StrategyRunModel } = window.<Namespace>`. */
export declare const StrategyRunModel: {
  AXES: StrategyRunAxes;
  /** Case-insensitive; uppercase template environment props remain compatible. Unknown → null. */
  normalizeEnv(env: unknown): "live" | "paper" | "fixture" | null;
  /** Snapshot + hashes are taken at creation — later draft edits never reach an existing run. */
  createRun(draft: StrategyDraft, opts?: { env?: string; window?: { start: string | number; end: string | number }; dataset?: { id: string; label?: string }; engine?: { name: string; version: string | null }; costModel?: unknown; id?: string; at?: number }): StrategyRunArtifact;
  startRun(run: StrategyRunArtifact, at?: number): StrategyRunArtifact;
  /** Attach raw evidence, derive metrics ONCE, resolve the disposition, deep-freeze. */
  completeRun(run: StrategyRunArtifact, evidence: { series?: { equity: SeriesPoint[]; benchmark?: SeriesPoint[] }; trades?: RunTrade[]; at?: number }): Readonly<StrategyRunArtifact>;
  failRun(run: StrategyRunArtifact, error: unknown, at?: number): Readonly<StrategyRunArtifact>;
  cancelRun(run: StrategyRunArtifact, at?: number): Readonly<StrategyRunArtifact>;
  /** Total, deterministic. Missing evidence → null metric + issue; never a fixture value. */
  deriveMetrics(artifact: Partial<StrategyRunArtifact>): { metrics: StrategyRunMetrics; methods: Record<string, string>; issues: RunIssue[] };
  /** Recompute both hashes from the artifact's own stored inputs. */
  verifyProof(artifact: StrategyRunArtifact): { state: "verified" | "mismatch" | "unverified"; expected?: { materialHash: string; inputHash: string }; actual?: { materialHash: string; inputHash: string }; reason?: string };
  /** Freshness against the CURRENT draft — derived, never stored. `changed` names the stale
   *  sections: benchmark · universe · portfolioRules · limits. */
  freshness(artifact: StrategyRunArtifact | null, draft: StrategyDraft | null): { state: "current" | "stale" | "unknown"; changed: string[]; artifactHash: string | null; draftHash: string | null };
  /** Legacy Run → artifact-shaped record; reported metrics stay separate as provisional. */
  fromLegacyRun(run: LegacyRun): StrategyRunArtifact;
  /** Artifact → legacy Run for run-history ledgers and StatusBanner. */
  toLegacyRun(artifact: StrategyRunArtifact): LegacyRun;
  deepFreeze<T>(value: T): T;
};
