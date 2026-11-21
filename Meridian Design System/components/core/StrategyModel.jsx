// Meridian StrategyModel — identity, lifecycle, and formation contract of the strategy object
// (the seventeenth entry in the ObjectModel ledger). Pure data and pure functions: no DOM, no
// fetch, no store. Screens read STRATEGY_FORMATION_STEPS for the six guarded formation stages,
// STRATEGY_LIFECYCLE for the state graph, and StrategyModel for validation, canonical hashing,
// checkpointing, and transitions. Every gate is recomputed from current field values — visiting
// a stage proves nothing; edits invalidate dependent stages, proof, and review by construction.
// Wire shapes: guidelines/ENTITY_SCHEMAS.md (Strategy · StrategyFormationSnapshot).

/** The strategy state graph. `severity` maps to the canonical Status scale. */
export const STRATEGY_LIFECYCLE = [
  { id: "draft", label: "Draft", severity: "info", next: ["validating", "retired"] },
  { id: "validating", label: "Validating", severity: "review", next: ["blocked", "ready-for-review"] },
  { id: "blocked", label: "Blocked", severity: "blocked", next: ["draft", "validating"] },
  { id: "ready-for-review", label: "Ready for review", severity: "review", next: ["in-review", "draft"] },
  { id: "in-review", label: "In review", severity: "review", next: ["approved", "changes-requested"] },
  { id: "approved", label: "Approved", severity: "ready", next: ["active", "superseded", "retired"] },
  { id: "changes-requested", label: "Changes requested", severity: "action", next: ["draft", "validating"] },
  { id: "active", label: "Active", severity: "ready", next: ["suspended", "superseded", "retired"] },
  { id: "suspended", label: "Suspended", severity: "action", next: ["active", "retired"] },
  { id: "superseded", label: "Superseded", severity: "info", next: ["retired"] },
  { id: "retired", label: "Retired", severity: "info", next: [] },
];
const LC = STRATEGY_LIFECYCLE.reduce((m, s) => { m[s.id] = s; return m; }, {});

/** The six guarded formation stages, in order. `fields` are the draft paths each stage owns. */
export const STRATEGY_FORMATION_STEPS = [
  { id: "mandate", label: "Mandate", blurb: "What this strategy is for — objective, benchmark, capital band, horizon.", fields: ["name", "mandate.objective", "mandate.benchmark", "mandate.capitalBand", "mandate.horizon"] },
  { id: "thesis", label: "Thesis", blurb: "The edge and why it persists — statement, source, expected decay.", fields: ["thesis.statement", "thesis.edge", "thesis.decay"] },
  { id: "universe", label: "Universe", blurb: "What it may trade — dataset, asset class, filters, liquidity floor.", fields: ["universe.datasetId", "universe.assetClass", "universe.filters", "universe.minAdv"] },
  { id: "portfolioRules", label: "Portfolio rules", blurb: "How it trades — entry, exit, sizing, position and rebalance limits, worksheet.", fields: ["portfolioRules.entry", "portfolioRules.exit", "portfolioRules.sizing", "portfolioRules.maxPositions", "portfolioRules.rebalance", "portfolioRules.worksheet"] },
  { id: "riskGovernance", label: "Risk & governance", blurb: "What stops it — drawdown policy, exposure limits, review cadence, approvers.", fields: ["riskGovernance.maxDrawdownPct", "riskGovernance.grossExposureMax", "riskGovernance.perNameMax", "riskGovernance.reviewCadence", "riskGovernance.approvers"] },
  { id: "review", label: "Review", blurb: "Confirm the record — every gate green, acknowledgment bound to this exact draft.", fields: ["review.acknowledged"] },
];

// ---- canonical form (mjcf/1) + SHA-256 ------------------------------------------------------
/** Deterministic JSON: object keys sorted, `undefined` members dropped, arrays in order. */
function canonicalize(v) {
  if (v === null || v === undefined) return "null";
  const t = typeof v;
  if (t === "number") return Number.isFinite(v) ? JSON.stringify(v) : "null";
  if (t === "boolean" || t === "string") return JSON.stringify(v);
  if (Array.isArray(v)) return "[" + v.map((x) => canonicalize(x)).join(",") + "]";
  if (t === "object") { const k = Object.keys(v).filter((key) => v[key] !== undefined).sort(); return "{" + k.map((key) => JSON.stringify(key) + ":" + canonicalize(v[key])).join(",") + "}"; }
  return "null";
}
const K = [0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070, 0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2];
/** Synchronous SHA-256 of a UTF-8 string (or byte array); lowercase hex. Offline, dependency-free. */
function sha256Hex(input) {
  const bytes = typeof input === "string" ? new TextEncoder().encode(input) : new Uint8Array(input);
  const l = bytes.length, bitLen = l * 8, padded = ((l + 8) >> 6 << 6) + 64;
  const m = new Uint8Array(padded); m.set(bytes); m[l] = 0x80;
  const dv = new DataView(m.buffer);
  dv.setUint32(padded - 4, bitLen >>> 0); dv.setUint32(padded - 8, Math.floor(bitLen / 4294967296));
  let h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a, h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;
  const w = new Int32Array(64);
  for (let i = 0; i < padded; i += 64) {
    for (let t = 0; t < 16; t++) w[t] = dv.getInt32(i + t * 4);
    for (let t = 16; t < 64; t++) { const x = w[t - 15], y = w[t - 2]; const s0 = ((x >>> 7) | (x << 25)) ^ ((x >>> 18) | (x << 14)) ^ (x >>> 3); const s1 = ((y >>> 17) | (y << 15)) ^ ((y >>> 19) | (y << 13)) ^ (y >>> 10); w[t] = (w[t - 16] + s0 + w[t - 7] + s1) | 0; }
    let a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
    for (let t = 0; t < 64; t++) {
      const S1 = ((e >>> 6) | (e << 26)) ^ ((e >>> 11) | (e << 21)) ^ ((e >>> 25) | (e << 7));
      const ch = (e & f) ^ (~e & g), t1 = (h + S1 + ch + K[t] + w[t]) | 0;
      const S0 = ((a >>> 2) | (a << 30)) ^ ((a >>> 13) | (a << 19)) ^ ((a >>> 22) | (a << 10));
      const maj = (a & b) ^ (a & c) ^ (b & c), t2 = (S0 + maj) | 0;
      h = g; g = f; f = e; e = (d + t1) | 0; d = c; c = b; b = a; a = (t1 + t2) | 0;
    }
    h0 = (h0 + a) | 0; h1 = (h1 + b) | 0; h2 = (h2 + c) | 0; h3 = (h3 + d) | 0; h4 = (h4 + e) | 0; h5 = (h5 + f) | 0; h6 = (h6 + g) | 0; h7 = (h7 + h) | 0;
  }
  return [h0, h1, h2, h3, h4, h5, h6, h7].map((x) => (x >>> 0).toString(16).padStart(8, "0")).join("");
}

// ---- validation ------------------------------------------------------------------------------
function get(obj, path) { return String(path).split(".").reduce((o, k) => (o == null ? o : o[k]), obj); }
function issue(stage, code, severity, message, path) { return { stage, code, severity, message, path: path || null }; }
const blank = (v) => v == null || (typeof v === "string" && v.trim() === "");
const finiteNumber = (v) => !blank(v) && (typeof v === "number" || typeof v === "string") && Number.isFinite(Number(v));

/** Recompute one stage's gate from current field values. Returns `{code,severity,message}` issues. */
function validateStage(draft = {}, stageId) {
  const out = [], m = draft.mandate || {}, th = draft.thesis || {}, u = draft.universe || {}, p = draft.portfolioRules || {}, r = draft.riskGovernance || {}, rv = draft.review || {};
  switch (stageId) {
    case "mandate": {
      if (blank(draft.name) || draft.name.trim().length < 3) out.push(issue(stageId, "name-missing", "blocking", "Name the strategy — three characters or more.", "name"));
      if (blank(m.objective)) out.push(issue(stageId, "objective-missing", "blocking", "State the mandate objective.", "mandate.objective"));
      if (blank(m.benchmark)) out.push(issue(stageId, "benchmark-missing", "warning", "No benchmark declared — benchmark and excess return will be null on every run.", "mandate.benchmark"));
      const band = m.capitalBand || {};
      if (band.min == null || band.max == null) out.push(issue(stageId, "capital-band-missing", "blocking", "Declare the capital band (minimum and maximum).", "mandate.capitalBand"));
      else if (!finiteNumber(band.min) || !finiteNumber(band.max)) out.push(issue(stageId, "capital-band-invalid", "blocking", "Capital band minimum and maximum must be finite numbers.", "mandate.capitalBand"));
      else if (Number(band.min) > Number(band.max)) out.push(issue(stageId, "capital-band-inverted", "blocking", "Capital band minimum exceeds maximum.", "mandate.capitalBand"));
      if (blank(m.horizon)) out.push(issue(stageId, "horizon-missing", "blocking", "Declare the holding horizon.", "mandate.horizon"));
      break;
    }
    case "thesis": {
      if (blank(th.statement) || th.statement.trim().length < 40) out.push(issue(stageId, "thesis-thin", "blocking", "State the thesis in at least 40 characters — it is the record a reviewer approves.", "thesis.statement"));
      if (blank(th.edge)) out.push(issue(stageId, "edge-missing", "blocking", "Name the source of edge.", "thesis.edge"));
      if (blank(th.decay)) out.push(issue(stageId, "decay-undeclared", "warning", "No decay assumption — reviewers will ask when this edge stops working.", "thesis.decay"));
      break;
    }
    case "universe": {
      if (blank(u.datasetId)) out.push(issue(stageId, "dataset-missing", "blocking", "Pick the dataset the universe is drawn from.", "universe.datasetId"));
      if (blank(u.assetClass)) out.push(issue(stageId, "asset-class-missing", "blocking", "Declare the asset class.", "universe.assetClass"));
      if (!Array.isArray(u.filters) || u.filters.length === 0) out.push(issue(stageId, "universe-unfiltered", "warning", "No universe filters — the strategy may trade anything in the dataset.", "universe.filters"));
      if (u.minAdv != null && (!finiteNumber(u.minAdv) || !(Number(u.minAdv) >= 0))) out.push(issue(stageId, "min-adv-invalid", "blocking", "Liquidity floor must be finite and zero or positive.", "universe.minAdv"));
      break;
    }
    case "portfolioRules": {
      if (blank(p.entry)) out.push(issue(stageId, "entry-missing", "blocking", "Define the entry rule.", "portfolioRules.entry"));
      if (blank(p.exit)) out.push(issue(stageId, "exit-missing", "blocking", "Define the exit rule.", "portfolioRules.exit"));
      if (blank(p.sizing)) out.push(issue(stageId, "sizing-missing", "blocking", "Define position sizing.", "portfolioRules.sizing"));
      if (!finiteNumber(p.maxPositions) || !Number.isInteger(Number(p.maxPositions)) || !(Number(p.maxPositions) >= 1)) out.push(issue(stageId, "max-positions-invalid", "blocking", "Maximum concurrent positions must be a finite integer of at least 1.", "portfolioRules.maxPositions"));
      if (blank(p.rebalance)) out.push(issue(stageId, "rebalance-undeclared", "warning", "No rebalance cadence declared.", "portfolioRules.rebalance"));
      (Array.isArray(p.worksheet) ? p.worksheet : []).forEach((cell) => { if (cell && cell.status === "error") out.push(issue(stageId, "worksheet-error", "blocking", "Worksheet cell " + (cell.ref || "?") + ": " + (cell.message || "does not evaluate."), "portfolioRules.worksheet." + (cell.ref || ""))); });
      break;
    }
    case "riskGovernance": {
      const dd = Number(r.maxDrawdownPct);
      if (r.maxDrawdownPct == null || !(dd > 0) || dd > 60) out.push(issue(stageId, "drawdown-policy-invalid", "blocking", "Maximum drawdown policy must be between 0 and 60 percent.", "riskGovernance.maxDrawdownPct"));
      if (r.grossExposureMax == null) out.push(issue(stageId, "gross-exposure-missing", "blocking", "Declare the gross exposure limit.", "riskGovernance.grossExposureMax"));
      else if (!finiteNumber(r.grossExposureMax)) out.push(issue(stageId, "gross-exposure-invalid", "blocking", "Gross exposure limit must be finite.", "riskGovernance.grossExposureMax"));
      if (r.perNameMax == null) out.push(issue(stageId, "per-name-missing", "blocking", "Declare the per-name exposure limit.", "riskGovernance.perNameMax"));
      else if (!finiteNumber(r.perNameMax)) out.push(issue(stageId, "per-name-invalid", "blocking", "Per-name exposure limit must be finite.", "riskGovernance.perNameMax"));
      if (r.grossExposureMax != null && r.perNameMax != null && Number(r.perNameMax) > Number(r.grossExposureMax)) out.push(issue(stageId, "limits-inverted", "blocking", "Per-name limit exceeds the gross exposure limit.", "riskGovernance.perNameMax"));
      if (blank(r.reviewCadence)) out.push(issue(stageId, "cadence-missing", "blocking", "Declare the governance review cadence.", "riskGovernance.reviewCadence"));
      if (!Array.isArray(r.approvers) || r.approvers.length === 0) out.push(issue(stageId, "approvers-missing", "blocking", "Name at least one approver.", "riskGovernance.approvers"));
      break;
    }
    case "review": {
      const upstream = STRATEGY_FORMATION_STEPS.slice(0, 5).flatMap((s) => validateStage(draft, s.id));
      if (upstream.some((i) => i.severity === "blocking")) out.push(issue(stageId, "formation-incomplete", "blocking", "Earlier stages carry blocking issues — resolve them before review.", null));
      if (!rv.acknowledged) out.push(issue(stageId, "not-acknowledged", "blocking", "Acknowledge the formation record to complete review.", "review.acknowledged"));
      else if (rv.acknowledgedHash !== formationHash(draft)) out.push(issue(stageId, "acknowledgment-stale", "blocking", "The draft changed after acknowledgment — re-review against the current values.", "review.acknowledged"));
      break;
    }
    default: break;
  }
  return out;
}
/** `complete` (no issues) · `attention` (warnings only) · `blocked` (any blocking issue). */
function stageDisposition(draft, stageId) {
  const issues = validateStage(draft, stageId);
  if (issues.some((i) => i.severity === "blocking")) return "blocked";
  return issues.length ? "attention" : "complete";
}
function formationIssues(draft) { return STRATEGY_FORMATION_STEPS.flatMap((s) => validateStage(draft, s.id)); }
function formationReady(draft) { return !formationIssues(draft).some((i) => i.severity === "blocking"); }

// ---- hashing, snapshot, checkpoint -----------------------------------------------------------
/** Hash of every formation field (narrative included) — binds review acknowledgment to one draft. */
function formationHash(draft = {}) {
  return sha256Hex(canonicalize({ name: draft.name ?? null, mandate: draft.mandate ?? null, thesis: draft.thesis ?? null, universe: draft.universe ?? null, portfolioRules: draft.portfolioRules ?? null, riskGovernance: draft.riskGovernance ?? null }));
}
/** Hash of the MATERIAL inputs only — what a run's proof binds to. Narrative (thesis wording,
 *  description) is excluded; universe, rules, worksheet, limits, and benchmark are included. */
function materialHash(draft = {}) {
  const r = draft.riskGovernance || {};
  return sha256Hex(canonicalize({ benchmark: (draft.mandate || {}).benchmark ?? null, universe: draft.universe ?? null, portfolioRules: draft.portfolioRules ?? null, limits: { maxDrawdownPct: r.maxDrawdownPct ?? null, grossExposureMax: r.grossExposureMax ?? null, perNameMax: r.perNameMax ?? null } }));
}
const clone = (v) => (v === undefined ? v : JSON.parse(JSON.stringify(v)));
/** Freeze the current draft as a StrategyFormationSnapshot (see `guidelines/ENTITY_SCHEMAS.md`). */
function snapshot(draft = {}, opts = {}) {
  const s = {
    schema: "meridian.strategy.formation/1",
    strategyId: draft.id ?? null, strategyVersion: draft.version ?? null,
    capturedAt: opts.at ?? Date.now(),
    fields: clone({ name: draft.name ?? null, mandate: draft.mandate ?? null, thesis: draft.thesis ?? null, universe: draft.universe ?? null, portfolioRules: draft.portfolioRules ?? null, riskGovernance: draft.riskGovernance ?? null }),
    review: clone({ acknowledged: !!(draft.review || {}).acknowledged, acknowledgedHash: (draft.review || {}).acknowledgedHash ?? null, notes: (draft.review || {}).notes ?? null }),
    formationHash: formationHash(draft), materialHash: materialHash(draft),
  };
  return deepFreeze(s);
}
/** Version-bump the strategy and return the snapshot that records the checkpoint. */
function checkpoint(draft, opts = {}) {
  const snap = snapshot(draft, opts);
  return { strategy: { ...draft, version: (draft.version ?? 1) + 1, updatedAt: opts.at ?? Date.now() }, snapshot: snap };
}
/** Bind acknowledgment to the exact current draft. Any later edit makes it stale. */
function acknowledgeReview(draft, opts = {}) {
  return { ...draft, review: { ...(draft.review || {}), acknowledged: true, acknowledgedHash: formationHash(draft), notes: opts.notes ?? (draft.review || {}).notes ?? null, acknowledgedAt: opts.at ?? Date.now(), acknowledgedBy: opts.actor ?? null }, updatedAt: opts.at ?? Date.now() };
}
function deepFreeze(v, seen = new WeakSet()) {
  if (!v || typeof v !== "object" || seen.has(v)) return v;
  seen.add(v);
  Object.freeze(v);
  // An already frozen parent can still contain mutable financial inputs.
  Object.keys(v).forEach((key) => deepFreeze(v[key], seen));
  return v;
}

// ---- lifecycle -------------------------------------------------------------------------------
function lifecycleMeta(id) { return LC[id] || { id, label: id || "Unknown", severity: "info", next: [] }; }
function canTransition(from, to) { return (LC[from] ? LC[from].next : []).includes(to); }
function nextStates(from) { return LC[from] ? LC[from].next.slice() : []; }
function isTerminal(id) { return !!LC[id] && LC[id].next.length === 0; }
/** Pure transition. Returns `{ok:true, strategy}` with history appended, or `{ok:false, issue}`. */
function applyTransition(strategy, to, ctx = {}) {
  const from = strategy.lifecycle;
  if (!canTransition(from, to)) return { ok: false, issue: { code: "transition-invalid", severity: "blocking", message: "No transition " + from + " \u2192 " + to + " in STRATEGY_LIFECYCLE." } };
  const entry = { from, to, at: ctx.at ?? Date.now(), actor: ctx.actor ?? null, note: ctx.note ?? null };
  return { ok: true, strategy: { ...strategy, lifecycle: to, history: [...(strategy.history || []), entry], updatedAt: entry.at } };
}
/** A complete empty draft — every section present so gates evaluate without null checks. */
function createDraft(seed = {}) {
  return {
    id: seed.id || "STR-" + Math.random().toString(36).slice(2, 6).toUpperCase(),
    version: seed.version ?? 1, lifecycle: "draft",
    name: seed.name ?? "", description: seed.description ?? "",
    mandate: { objective: "", benchmark: "", capitalBand: { min: null, max: null }, horizon: "", owner: "", ...(seed.mandate || {}) },
    thesis: { statement: "", edge: "", decay: "", capacity: "", ...(seed.thesis || {}) },
    universe: { datasetId: "", assetClass: "", filters: [], minAdv: null, exclusions: [], ...(seed.universe || {}) },
    portfolioRules: { entry: "", exit: "", sizing: "", maxPositions: null, rebalance: "", worksheet: [], ...(seed.portfolioRules || {}) },
    riskGovernance: { maxDrawdownPct: null, grossExposureMax: null, perNameMax: null, stopPolicy: "", reviewCadence: "", approvers: [], ...(seed.riskGovernance || {}) },
    review: { acknowledged: false, acknowledgedHash: null, notes: null, ...(seed.review || {}) },
    history: seed.history || [], createdAt: seed.createdAt ?? Date.now(), updatedAt: seed.updatedAt ?? Date.now(),
  };
}

/** Bundle carrier — every helper reachable from `window.<Namespace>.StrategyModel`. */
export const StrategyModel = {
  LIFECYCLE: STRATEGY_LIFECYCLE, STEPS: STRATEGY_FORMATION_STEPS,
  lifecycleMeta, canTransition, nextStates, isTerminal, applyTransition,
  createDraft, validateStage, stageDisposition, formationIssues, formationReady,
  canonicalize, sha256Hex, formationHash, materialHash,
  snapshot, checkpoint, acknowledgeReview, deepFreeze, get,
};
