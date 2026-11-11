// Meridian StrategyRunModel — the pure carrier for the immutable StrategyRunArtifact v1
// (guidelines/ENTITY_SCHEMAS.md). It owns canonical input hashing, proof integrity, freshness
// against the current draft, one-time metric derivation from raw series + trades, finalization
// (deep-freeze), and adapters to/from the legacy Run shape. Environment, run lifecycle,
// validation disposition, proof integrity, and current-draft freshness are INDEPENDENT axes —
// none is ever derived from another. Missing evidence produces null plus a validation issue,
// never a fixture value.
import { StrategyModel } from "../core/StrategyModel";

const { canonicalize, sha256Hex, materialHash } = StrategyModel;
const DAY = 86400000, YEAR = 365.2425;

/** The five independent axes and their vocabularies. */
export const STRATEGY_RUN_AXES = Object.freeze({
  environment: ["live", "paper", "fixture"],
  lifecycle: ["queued", "running", "complete", "failed", "cancelled"],
  validation: ["pass", "pass-with-issues", "fail"],
  proof: ["verified", "unverified", "mismatch"],
  freshness: ["current", "stale", "unknown"],
});

function normalizeEnv(env) { const e = String(env || "").toLowerCase(); return STRATEGY_RUN_AXES.environment.includes(e) ? e : null; }
function iss(code, severity, message) { return { code, severity, message }; }
const toMs = (t) => (typeof t === "number" ? t : Date.parse(t));
function deepFreeze(v, visited = new WeakSet()) { if (v && typeof v === "object" && !visited.has(v)) { visited.add(v); Object.keys(v).forEach((k) => deepFreeze(v[k], visited)); Object.freeze(v); } return v; }
const clone = (v) => (v === undefined ? null : JSON.parse(JSON.stringify(v)));

/** New run request from the current draft. Snapshot + hashes are taken HERE — later draft edits
 *  never reach into an existing run. */
function createRun(draft, opts = {}) {
  const snapshot = StrategyModel.snapshot(draft, { at: opts.at });
  const request = { window: clone(opts.window) || null, dataset: clone(opts.dataset) || (draft.universe ? { id: draft.universe.datasetId } : null), engine: clone(opts.engine) || { name: "meridian-backtest", version: "1" }, costModel: clone(opts.costModel) || null };
  const env = normalizeEnv(opts.env);
  return {
    schema: "meridian.strategy.run-artifact/1",
    id: opts.id || "RUN-" + Date.now().toString(36).toUpperCase() + Math.random().toString(36).slice(2, 5).toUpperCase(),
    strategyId: draft.id ?? null, strategyVersion: draft.version ?? null,
    env: env || "fixture",
    lifecycle: "queued", requestedAt: opts.at ?? Date.now(), startedAt: null, completedAt: null,
    request,
    inputs: { snapshot },
    proof: { algo: "sha256", canonical: "mjcf/1", materialHash: snapshot.materialHash, inputHash: sha256Hex(canonicalize({ materialHash: snapshot.materialHash, request })), hashedAt: opts.at ?? Date.now() },
    series: null, trades: null, metrics: null, methods: null,
    validation: { disposition: null, issues: env ? [] : [iss("env-unsupported", "warning", "Environment '" + String(opts.env) + "' is not live/paper/fixture — recorded as fixture.")] },
    error: null,
  };
}
function startRun(run, at) { return { ...run, lifecycle: "running", startedAt: at ?? Date.now() }; }
function failRun(run, error, at) { return deepFreeze({ ...run, lifecycle: "failed", completedAt: at ?? Date.now(), error: String(error || "Run failed"), validation: { disposition: "fail", issues: [...run.validation.issues, iss("run-failed", "blocking", String(error || "Run failed"))] } }); }
function cancelRun(run, at) { return deepFreeze({ ...run, lifecycle: "cancelled", completedAt: at ?? Date.now() }); }

/** Attach raw evidence, derive metrics ONCE, resolve the validation disposition, freeze.
 *  `evidence`: `{ series: { equity, benchmark? }, trades, at? }`. Returns the immutable artifact. */
function completeRun(run, evidence = {}) {
  const series = clone(evidence.series) || null, trades = clone(evidence.trades) || null;
  const staged = { ...run, series, trades, lifecycle: "complete", completedAt: evidence.at ?? Date.now() };
  const { metrics, methods, issues } = deriveMetrics(staged);
  const all = [...run.validation.issues, ...issues];
  const disposition = all.some((i) => i.severity === "blocking") ? "fail" : all.length ? "pass-with-issues" : "pass";
  return deepFreeze({ ...staged, metrics, methods, validation: { disposition, issues: all } });
}

// ---- metric derivation -----------------------------------------------------------------------
function points(list) {
  if (!Array.isArray(list)) return null;
  const p = list.map((d) => ({ t: toMs(d.t ?? d.time ?? d.date), v: Number(d.v ?? d.value ?? d.equity) })).filter((d) => Number.isFinite(d.t) && Number.isFinite(d.v)).sort((a, b) => a.t - b.t);
  return p.length ? p : null;
}
/** Derive every metric from the artifact's raw series and trades. Deterministic and total:
 *  missing evidence → null value + a validation issue; never a fixture number. */
export function deriveMetrics(artifact = {}) {
  const issues = [];
  const eq = points(artifact.series && artifact.series.equity);
  const bench = points(artifact.series && artifact.series.benchmark);
  const trades = Array.isArray(artifact.trades) ? artifact.trades : null;
  const m = { totalReturn: null, benchmarkReturn: null, excessReturn: null, cagr: null, maxDrawdown: null, sharpe: null, winRate: null, avgHoldingDays: null, turnover: null, fees: null };
  const methods = { returns: "close-to-close on the artifact equity series", cagr: "ACT/365.2425 on actual elapsed days", maxDrawdown: "rolling-peak, close-to-close, reported as a negative fraction", sharpe: "arithmetic daily returns \u00b7 rf 0 \u00b7 sample sd \u00b7 annualized \u221a252", winRate: "closed trades, net of recorded fees", turnover: "gross traded notional / mean equity, annualized ACT/365.2425", fees: "sum of per-trade recorded fees" };

  if (!eq || eq.length < 2) issues.push(iss("equity-series-missing", "blocking", "No usable equity series — every series-derived metric is null."));
  else if (eq.some((p) => p.v <= 0)) issues.push(iss("equity-series-invalid", "blocking", "Equity series contains non-positive values — series-derived metrics are null."));
  else {
    const first = eq[0], last = eq[eq.length - 1];
    m.totalReturn = last.v / first.v - 1;
    const days = (last.t - first.t) / DAY;
    if (days > 0) m.cagr = Math.pow(1 + m.totalReturn, YEAR / days) - 1;
    else issues.push(iss("window-degenerate", "warning", "Equity series spans zero days — CAGR is null."));
    let peak = first.v, dd = 0;
    for (const p of eq) { if (p.v > peak) peak = p.v; const d = p.v / peak - 1; if (d < dd) dd = d; }
    m.maxDrawdown = dd;
    const rets = []; for (let i = 1; i < eq.length; i++) rets.push(eq[i].v / eq[i - 1].v - 1);
    if (rets.length >= 10) {
      const mean = rets.reduce((a, b) => a + b, 0) / rets.length;
      const sd = Math.sqrt(rets.reduce((a, b) => a + (b - mean) * (b - mean), 0) / (rets.length - 1));
      if (sd > 0) m.sharpe = (mean / sd) * Math.sqrt(252);
      else issues.push(iss("sharpe-degenerate", "warning", "Zero return variance — Sharpe is null."));
    } else issues.push(iss("sharpe-insufficient-observations", "warning", "Fewer than 10 return observations — Sharpe is null."));
  }
  if (!bench) issues.push(iss("benchmark-series-missing", "warning", "No benchmark series — benchmark and excess return are null."));
  else if (bench.length >= 2 && bench[0].v > 0) {
    m.benchmarkReturn = bench[bench.length - 1].v / bench[0].v - 1;
    if (m.totalReturn != null) m.excessReturn = m.totalReturn - m.benchmarkReturn;
  }
  if (!trades) issues.push(iss("trades-missing", "warning", "No trade record — win rate, holding period, turnover, and fees are null."));
  else {
    const closed = trades.filter((t) => t && t.exitAt != null && t.exitPx != null);
    if (!closed.length) issues.push(iss("no-closed-trades", "warning", "No closed trades — win rate and holding period are null."));
    else {
      let wins = 0, holdSum = 0;
      for (const t of closed) {
        const dir = /^(sell|short)$/i.test(String(t.side || "")) ? -1 : 1;
        const pnl = (Number(t.exitPx) - Number(t.entryPx)) * Number(t.qty) * dir - (t.fees != null ? Number(t.fees) : 0);
        if (pnl > 0) wins++;
        holdSum += (toMs(t.exitAt) - toMs(t.entryAt)) / DAY;
      }
      m.winRate = wins / closed.length;
      m.avgHoldingDays = holdSum / closed.length;
    }
    const withFees = trades.filter((t) => t && t.fees != null);
    if (!withFees.length) issues.push(iss("fees-unrecorded", "warning", "No trade carries a fees field — fees are null, not zero."));
    else m.fees = withFees.reduce((a, t) => a + Number(t.fees), 0);
    if (eq && eq.length >= 2) {
      const notional = trades.reduce((a, t) => a + Math.abs(Number(t.qty) * Number(t.entryPx || 0)) + (t.exitPx != null ? Math.abs(Number(t.qty) * Number(t.exitPx)) : 0), 0);
      const avgEq = eq.reduce((a, p) => a + p.v, 0) / eq.length;
      const years = (eq[eq.length - 1].t - eq[0].t) / DAY / YEAR;
      if (avgEq > 0 && years > 0) m.turnover = notional / avgEq / years;
    }
  }
  return { metrics: m, methods, issues };
}

// ---- proof integrity + freshness (independent axes) ------------------------------------------
/** Recompute both hashes from the artifact's own stored inputs. `mismatch` means the artifact
 *  was altered after finalization or hashed under a different canonical form. */
function verifyProof(artifact) {
  if (!artifact || !artifact.proof || !artifact.inputs || !artifact.inputs.snapshot) return { state: "unverified", reason: "No proof block or no stored inputs." };
  const snap = artifact.inputs.snapshot;
  const expectMaterial = materialHash({ mandate: snap.fields.mandate, universe: snap.fields.universe, portfolioRules: snap.fields.portfolioRules, riskGovernance: snap.fields.riskGovernance });
  const expectInput = sha256Hex(canonicalize({ materialHash: expectMaterial, request: artifact.request ?? null }));
  const ok = expectMaterial === artifact.proof.materialHash && expectInput === artifact.proof.inputHash;
  return { state: ok ? "verified" : "mismatch", expected: { materialHash: expectMaterial, inputHash: expectInput }, actual: { materialHash: artifact.proof.materialHash, inputHash: artifact.proof.inputHash } };
}
/** Freshness of an artifact AGAINST THE CURRENT DRAFT — derived centrally, never stored. */
function freshness(artifact, draft) {
  if (!artifact || !artifact.proof || !artifact.proof.materialHash) return { state: "unknown", changed: [], artifactHash: null, draftHash: draft ? materialHash(draft) : null };
  if (!draft) return { state: "unknown", changed: [], artifactHash: artifact.proof.materialHash, draftHash: null };
  const draftHash = materialHash(draft);
  if (draftHash === artifact.proof.materialHash) return { state: "current", changed: [], artifactHash: artifact.proof.materialHash, draftHash };
  const snapFields = (artifact.inputs && artifact.inputs.snapshot && artifact.inputs.snapshot.fields) || {};
  const changed = [];
  const secs = { benchmark: [(d) => (d.mandate || {}).benchmark ?? null, (f) => (f.mandate || {}).benchmark ?? null], universe: [(d) => d.universe ?? null, (f) => f.universe ?? null], portfolioRules: [(d) => d.portfolioRules ?? null, (f) => f.portfolioRules ?? null], limits: [(d) => pickLimits(d.riskGovernance), (f) => pickLimits(f.riskGovernance)] };
  for (const key of Object.keys(secs)) if (canonicalize(secs[key][0](draft)) !== canonicalize(secs[key][1](snapFields))) changed.push(key);
  return { state: "stale", changed, artifactHash: artifact.proof.materialHash, draftHash };
}
function pickLimits(r) { r = r || {}; return { maxDrawdownPct: r.maxDrawdownPct ?? null, grossExposureMax: r.grossExposureMax ?? null, perNameMax: r.perNameMax ?? null }; }

// ---- legacy Run adapters ----------------------------------------------------------------------
const LEGACY_STATUS = { running: "running", complete: "complete", completed: "complete", failed: "failed", queued: "queued", cancelled: "cancelled", canceled: "cancelled" };
/** Legacy `Run` (guidelines/ENTITY_SCHEMAS.md) → artifact-shaped record. Reported metrics stay in
 *  `reported` — they are never merged into derived `metrics`, which remain null with issues. */
function fromLegacyRun(run = {}) {
  const lifecycle = LEGACY_STATUS[String(run.status || "").toLowerCase()] || "complete";
  return {
    schema: "meridian.strategy.run-artifact/1", legacy: true,
    id: run.id || "RUN-LEGACY", strategyId: run.strategyId ?? null, strategyVersion: run.strategyVersion ?? null,
    env: normalizeEnv(run.env) || "fixture", lifecycle,
    requestedAt: run.startedAt ?? null, startedAt: run.startedAt ?? null, completedAt: run.completedAt ?? null,
    request: { window: null, dataset: null, engine: { name: run.kind || "legacy", version: null }, costModel: null },
    inputs: { snapshot: null }, proof: null, series: null, trades: null,
    metrics: null, methods: null, reported: run.metrics ? { ...run.metrics } : null,
    validation: { disposition: "pass-with-issues", issues: [iss("legacy-no-raw-evidence", "warning", "Ported from the legacy Run shape — raw series and trades were not recorded, so derived metrics are null. Reported figures are shown as provisional.")] },
    error: null,
  };
}
/** Artifact → legacy `Run` for run-history ledgers and StatusBanner. Only non-null derived
 *  metrics are flattened; a legacy record's `reported` figures pass through unchanged. */
function toLegacyRun(artifact = {}) {
  const status = { queued: "Queued", running: "Running", complete: "Complete", failed: "Failed", cancelled: "Cancelled" }[artifact.lifecycle] || "Complete";
  const metrics = {};
  const src = artifact.metrics || artifact.reported || {};
  for (const k of Object.keys(src)) if (src[k] != null) metrics[k] = src[k];
  return { id: artifact.id, kind: "strategy", startedAt: artifact.startedAt ?? artifact.requestedAt, completedAt: artifact.completedAt ?? undefined, status, metrics, env: artifact.env };
}

/** Bundle carrier: `const { StrategyRunModel } = window.<Namespace>`. */
export const StrategyRunModel = {
  AXES: STRATEGY_RUN_AXES, normalizeEnv,
  createRun, startRun, completeRun, failRun, cancelRun,
  deriveMetrics, verifyProof, freshness,
  fromLegacyRun, toLegacyRun, deepFreeze,
};
