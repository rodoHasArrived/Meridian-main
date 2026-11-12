// Meridian Strategy Executor — suite-local controller (window.MeridianStrategyExecutor).
// One versioned Strategy identity and one immutable StrategyRunArtifact shared by the seven
// executor screens plus the Basket Builder and Covered-Call Lab adapters.
//
// Persistence: localStorage under "meridian.strategyExecutor.store.v1", fixture-first.
// Identity precedence: URL parameters → valid local store → canonical fixture.
// Corrupt, unavailable, or unsupported storage falls back to a session-only in-memory store and
// reports it via storageNotice() — screens surface the visible session-only/read-only banner.
// Stale/current is DERIVED from the current draft hash (StrategyRunModel.freshness), never stored.
// URLs carry identifiers only (strategy, run, runA, runB, formationStage) — never artifact JSON.
(function (global) {
  "use strict";
  var KEY = "meridian.strategyExecutor.store.v1";
  var SCHEMA_VERSION = 1;
  var FIXTURE_STRATEGY_ID = "STR-0007";
  function ns() { return global.MeridianDesignSystem_205d17 || {}; }
  var state = { store: null, storage: "ok", reason: null };

  // ---- deterministic fixture engine ----------------------------------------------------------
  function seedOf(str) { var h = 2166136261; str = String(str); for (var i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619); } return h >>> 0; }
  function rng(seed) { var a = seed >>> 0; return function () { a |= 0; a = (a + 0x6D2B79F5) | 0; var t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
  function businessDays(startIso, endIso) {
    var out = [], t = new Date(startIso + "T00:00:00Z").getTime(), end = new Date(endIso + "T00:00:00Z").getTime();
    while (t <= end) { var d = new Date(t).getUTCDay(); if (d !== 0 && d !== 6) out.push(t); t += 86400000; }
    return out;
  }
  function synthSeries(seed, days) {
    var r = rng(seed), eq = [], bm = [], e = 100, b = 100;
    for (var i = 0; i < days.length; i++) {
      e *= 1 + 0.00042 + (r() - 0.5) * 0.0062 + (r() < 0.02 ? -(r() * 0.012) : 0);
      b *= 1 + 0.00021 + (r() - 0.5) * 0.0038;
      eq.push({ t: days[i], v: Math.round(e * 10000) / 10000 });
      bm.push({ t: days[i], v: Math.round(b * 10000) / 10000 });
    }
    return { equity: eq, benchmark: bm };
  }
  var SYMBOLS = ["T 4.375 34", "VZ 5.05 33", "ORCL 4.9 31", "BA 5.15 30", "F 6.1 32", "CVS 5.05 48", "DUK 4.5 32", "KHC 4.375 46", "GM 5.4 29", "OXY 6.45 36", "WBD 5.141 41", "CCI 4.75 47"];
  function synthTrades(seed, days) {
    var r = rng(seed ^ 0x9e3779b9), out = [], n = Math.max(14, Math.floor(days.length / 22));
    for (var i = 0; i < n; i++) {
      var ei = Math.floor(r() * Math.max(1, days.length - 40));
      var hold = 8 + Math.floor(r() * 46);
      var xi = Math.min(days.length - 1, ei + hold);
      var px = 84 + r() * 28, drift = (r() - 0.42) * 6;
      out.push({ id: "TRD-" + (1000 + i), symbol: SYMBOLS[Math.floor(r() * SYMBOLS.length)], side: r() < 0.85 ? "Buy" : "Sell", qty: (5 + Math.floor(r() * 45)) * 10000, entryAt: days[ei], entryPx: Math.round(px * 100) / 100, exitAt: i < n - 2 ? days[xi] : null, exitPx: i < n - 2 ? Math.round((px + drift) * 100) / 100 : null, fees: Math.round((120 + r() * 640) * 100) / 100 });
    }
    return out;
  }

  // ---- worksheet formula validation (shared by Builder and Field & Formula) ------------------
  var FUNCTIONS = ["COUPON", "PRICE", "YTM", "DURATION", "RATING", "SPREAD_TSY", "ZSCORE", "ROLL_3M", "CONVEXITY", "CLEAN_PRICE", "ADV", "AMT_OUT"];
  function validateFormula(formula) {
    var f = String(formula || "").trim();
    if (!f) return { status: "error", message: "Empty cell — write Let, Filter, or Rank." };
    if (!/^(Let|Filter|Rank)\b/.test(f)) return { status: "error", message: "Statements start with Let, Filter, or Rank." };
    var open = (f.match(/\(/g) || []).length, close = (f.match(/\)/g) || []).length;
    if (open !== close) return { status: "error", message: "Unbalanced parentheses." };
    var calls = f.match(/\b[A-Z][A-Z_0-9]{1,}\s*\(/g) || [];
    for (var i = 0; i < calls.length; i++) {
      var fn = calls[i].replace(/\s*\($/, "");
      if (FUNCTIONS.indexOf(fn) < 0) {
        var near = FUNCTIONS.filter(function (k) { return k[0] === fn[0]; })[0];
        return { status: "error", message: "Unknown function " + fn + (near ? " — did you mean " + near + "?" : ".") };
      }
    }
    return { status: "ok", message: null };
  }

  // ---- canonical fixture ----------------------------------------------------------------------
  function goodWorksheet() {
    return [
      { ref: "B2", formula: "Let carry_roll = COUPON(cusip) / PRICE(cusip) + ROLL_3M(cusip)", status: "ok" },
      { ref: "B3", formula: "Let spread_z = ZSCORE(SPREAD_TSY(cusip), 252)", status: "ok" },
      { ref: "B4", formula: "Filter DURATION(cusip) >= 3 And DURATION(cusip) <= 8", status: "ok" },
      { ref: "B5", formula: "Rank carry_roll Desc Top 40", status: "ok" },
    ];
  }
  function fixtureFields() {
    return {
      name: "Bond Carry & Roll",
      mandate: { objective: "Harvest carry and roll-down in IG and crossover credit while capping duration risk against the aggregate benchmark.", benchmark: "US Agg (fixture)", capitalBand: { min: 25000000, max: 80000000 }, horizon: "6\u201318 months", owner: "J. Okafor" },
      thesis: { statement: "Credit curves between 3y and 7y are persistently steep relative to realized default and downgrade experience; harvesting carry and roll-down on IG and crossover names funds a duration hedge and still clears the benchmark after costs.", edge: "Structural \u2014 insurance and pension demand pins the long end while dealers underprice 3\u20137y roll-down.", decay: "Erodes when the 3y\u20137y credit curve flattens below 40bp; monitored quarterly.", capacity: "$120M before market impact exceeds 8bp per rebalance." },
      universe: { datasetId: "ig-corp", assetClass: "fixed-income", filters: [{ field: "RATING", op: ">=", value: "BBB-" }, { field: "DURATION", op: "between", value: [3, 8] }, { field: "AMT_OUT", op: ">=", value: 250000000 }], minAdv: 2000000, exclusions: ["financials-subordinated"] },
      portfolioRules: { entry: "Enter top-quintile carry_roll names when spread_z \u2265 0.5.", exit: "Exit on spread_z \u2264 \u22120.5 or a downgrade below BBB-.", sizing: "Equal risk weight, duration-neutral versus benchmark.", maxPositions: 40, rebalance: "Monthly, third business day", worksheet: goodWorksheet() },
      riskGovernance: { maxDrawdownPct: 12, grossExposureMax: 1.5, perNameMax: 0.04, stopPolicy: "Suspend on 3-day drawdown > 4% or tracking error > 350bp.", reviewCadence: "Quarterly", approvers: ["M. Reyes (Risk)", "D. Tan (PM oversight)"] },
    };
  }
  function buildRun(SM, RM, draft, opts) {
    var run = RM.createRun(draft, opts);
    run = RM.startRun(run, opts.at + 45000);
    var days = businessDays(opts.window.start, opts.window.end);
    var seed = seedOf(run.proof.materialHash + "|" + opts.window.start + "|" + opts.window.end + "|" + opts.seedTag);
    return RM.completeRun(run, { series: synthSeries(seed, days), trades: synthTrades(seed, days), at: opts.at + 141000 });
  }
  function buildFixtureStore() {
    var SM = ns().StrategyModel, RM = ns().StrategyRunModel;
    var f = fixtureFields();
    // The good draft the historical runs were taken from (formation v2/v3 material).
    var good = SM.createDraft({ id: FIXTURE_STRATEGY_ID, version: 3, name: f.name, mandate: f.mandate, thesis: f.thesis, universe: f.universe, portfolioRules: f.portfolioRules, riskGovernance: f.riskGovernance, createdAt: Date.parse("2026-02-09T14:02:00Z"), updatedAt: Date.parse("2026-08-10T09:41:00Z") });
    var v2 = { ...good, version: 2 };
    var runA = buildRun(SM, RM, v2, { id: "RUN-CARRY-A", env: "fixture", window: { start: "2024-01-02", end: "2025-06-30" }, dataset: { id: "ig-corp", label: "IG Corp Bonds \u00b7 fixture" }, engine: { name: "meridian-backtest", version: "2.3.1" }, at: Date.parse("2026-05-04T10:12:00Z"), seedTag: "A" });
    var runB = buildRun(SM, RM, v2, { id: "RUN-CARRY-B", env: "fixture", window: { start: "2024-07-01", end: "2026-06-30" }, dataset: { id: "ig-corp", label: "IG Corp Bonds \u00b7 fixture" }, engine: { name: "meridian-backtest", version: "2.3.1" }, at: Date.parse("2026-06-15T15:30:00Z"), seedTag: "B" });
    var runC = buildRun(SM, RM, good, { id: "RUN-CARRY-C", env: "paper", window: { start: "2026-01-02", end: "2026-06-30" }, dataset: { id: "ig-corp", label: "IG Corp Bonds \u00b7 paper shadow" }, engine: { name: "meridian-backtest", version: "2.4.0" }, at: Date.parse("2026-07-01T08:05:00Z"), seedTag: "C" });
    var legacy = RM.fromLegacyRun({ id: "RUN-8841", kind: "backtest", startedAt: Date.parse("2025-11-03T09:00:00Z"), completedAt: Date.parse("2025-11-03T09:04:12Z"), status: "Complete", metrics: { sharpe: 1.12, totalReturn: 0.089, maxDrawdown: -0.061 }, env: "FIXTURE", strategyId: FIXTURE_STRATEGY_ID, strategyVersion: 1 });
    // The live draft: same strategy, but B5 carries the unresolved worksheet blocker.
    var draft = { ...good, portfolioRules: { ...good.portfolioRules, worksheet: good.portfolioRules.worksheet.map(function (c) { return c.ref === "B5" ? { ref: "B5", formula: "RNK carry_roll Desc Top 40", status: "error", message: "Statements start with Let, Filter, or Rank." } : c; }) } };
    draft.history = [
      { from: "draft", to: "validating", at: Date.parse("2026-03-02T10:00:00Z"), actor: "j.okafor", note: "v1 formation complete" },
      { from: "validating", to: "ready-for-review", at: Date.parse("2026-03-02T10:02:00Z"), actor: "system", note: null },
      { from: "ready-for-review", to: "in-review", at: Date.parse("2026-03-04T09:00:00Z"), actor: "m.reyes", note: null },
      { from: "in-review", to: "approved", at: Date.parse("2026-03-06T16:20:00Z"), actor: "m.reyes", note: "v2 approved on RUN-CARRY-A proof" },
      { from: "approved", to: "superseded", at: Date.parse("2026-08-10T09:41:00Z"), actor: "j.okafor", note: "v3 draft opened \u2014 worksheet edit" },
    ];
    draft.lifecycle = "draft";
    var atts = [
      { id: "ATT-0031", strategyId: FIXTURE_STRATEGY_ID, strategyVersion: 2, runId: "RUN-CARRY-A", proofHash: runA.proof.inputHash, decision: "approve", signedBy: "M. Reyes", role: "Risk", signedAt: Date.parse("2026-03-06T16:20:00Z"), note: "Approved for paper trading at $25M." },
    ];
    return { schemaVersion: SCHEMA_VERSION, current: { strategyId: FIXTURE_STRATEGY_ID, runId: "RUN-CARRY-B" }, strategies: keyed([draft], "id"), runs: keyed([runA, runB, runC, legacy], "id"), snapshots: {}, attestations: atts, savedAt: null };
  }
  function keyed(list, k) { var m = {}; list.forEach(function (x) { m[x[k]] = x; }); return m; }

  // ---- storage --------------------------------------------------------------------------------
  function migrate(raw) {
    if (!raw || typeof raw !== "object") return null;
    if (raw.schemaVersion === SCHEMA_VERSION) return raw;
    if (raw.schemaVersion == null && raw.strategies) { raw.schemaVersion = SCHEMA_VERSION; raw.snapshots = raw.snapshots || {}; raw.attestations = raw.attestations || []; raw.current = raw.current || {}; return raw; } // v0 → v1
    return null; // unsupported (newer or unrecognized)
  }
  function load() {
    if (state.store) return state;
    var fixture;
    try { fixture = buildFixtureStore(); } catch (e) { return state; } // design system not loaded yet
    var raw = null;
    try { raw = global.localStorage ? global.localStorage.getItem(KEY) : undefined; }
    catch (e) { state.storage = "session-only"; state.reason = "Local storage is unavailable \u2014 " + (e && e.name === "SecurityError" ? "blocked by the browser" : "access denied") + "."; state.store = fixture; return state; }
    if (raw == null) { state.store = fixture; persist(); return state; }
    var parsed = null;
    try { parsed = JSON.parse(raw); } catch (e) { state.storage = "session-only"; state.reason = "Stored suite state is corrupt \u2014 running read-only on the canonical fixture. Reset to clear."; state.store = fixture; return state; }
    var migrated = migrate(parsed);
    if (!migrated) { state.storage = "session-only"; state.reason = "Stored suite state uses schema v" + (parsed && parsed.schemaVersion) + ", which this build does not support \u2014 running read-only on the canonical fixture."; state.store = fixture; return state; }
    if (!migrated.strategies[FIXTURE_STRATEGY_ID]) migrated.strategies[FIXTURE_STRATEGY_ID] = fixture.strategies[FIXTURE_STRATEGY_ID];
    Object.keys(fixture.runs).forEach(function (id) { if (!migrated.runs[id]) migrated.runs[id] = fixture.runs[id]; });
    state.store = migrated;
    return state;
  }
  function persist() {
    if (state.storage !== "ok") return { ok: false, reason: state.reason };
    try { state.store.savedAt = Date.now(); global.localStorage.setItem(KEY, JSON.stringify(state.store)); return { ok: true }; }
    catch (e) { state.storage = "session-only"; state.reason = "Saving failed (" + (e && e.name || "storage error") + ") \u2014 changes are session-only from here."; return { ok: false, reason: state.reason }; }
  }

  // ---- URL identity ---------------------------------------------------------------------------
  var ID_KEYS = ["strategy", "run", "runA", "runB"];
  function readParams() {
    var q = new URLSearchParams(global.location.search), out = {};
    ID_KEYS.concat(["formationStage", "view"]).forEach(function (k) { var v = q.get(k); if (v) out[k] = v; });
    return out;
  }
  function withIdentity(href, extra) {
    extra = extra || {};
    var cur = readParams(), url = new URL(href, global.location.href);
    ID_KEYS.forEach(function (k) { var v = k in extra ? extra[k] : cur[k]; if (v) url.searchParams.set(k, v); else url.searchParams.delete(k); });
    Object.keys(extra).forEach(function (k) { if (ID_KEYS.indexOf(k) < 0) { if (extra[k] == null) url.searchParams.delete(k); else url.searchParams.set(k, String(extra[k])); } });
    return url.pathname + url.search + url.hash;
  }
  function setUrlState(patch) {
    var url = new URL(global.location.href);
    Object.keys(patch).forEach(function (k) { if (patch[k] == null) url.searchParams.delete(k); else url.searchParams.set(k, String(patch[k])); });
    global.history.replaceState(null, "", url.pathname + url.search + url.hash);
  }
  function resolveIdentity() {
    var s = load().store; if (!s) return null;
    var p = readParams();
    var strategyId, source;
    if (p.strategy && s.strategies[p.strategy]) { strategyId = p.strategy; source = "url"; }
    else if (s.current.strategyId && s.strategies[s.current.strategyId]) { strategyId = s.current.strategyId; source = "store"; }
    else { strategyId = FIXTURE_STRATEGY_ID; source = "fixture"; }
    function runFor(key, fallback) {
      if (p[key] && s.runs[p[key]]) return p[key];
      if (fallback && s.runs[fallback]) return fallback;
      return null;
    }
    var completed = listRuns(strategyId).filter(function (r) { return r.lifecycle === "complete"; });
    return {
      strategyId: strategyId, source: source,
      runId: runFor("run", s.current.runId) || (completed[0] && completed[0].id) || null,
      runA: runFor("runA", completed[0] && completed[0].id), runB: runFor("runB", completed[1] && completed[1].id),
      stage: p.formationStage || null,
    };
  }

  // ---- accessors + mutations ------------------------------------------------------------------
  function ready() { return !!load().store; }
  function getStore() { return load().store; }
  function getStrategy(id) { var s = getStore(); return s ? s.strategies[id] || null : null; }
  function updateStrategy(id, updater) {
    var s = getStore(); if (!s || !s.strategies[id]) return null;
    var next = typeof updater === "function" ? updater(s.strategies[id]) : updater;
    next = { ...next, updatedAt: Date.now() };
    s.strategies[id] = next; s.current.strategyId = id; persist();
    return next;
  }
  function updateWorksheetCell(id, ref, formula) {
    return updateStrategy(id, function (d) {
      var v = validateFormula(formula);
      var cell = { ref: ref, formula: formula, status: v.status };
      if (v.message) cell.message = v.message;
      var ws = (d.portfolioRules.worksheet || []).map(function (c) { return c.ref === ref ? cell : c; });
      if (!ws.some(function (c) { return c.ref === ref; })) ws = ws.concat([cell]);
      return { ...d, portfolioRules: { ...d.portfolioRules, worksheet: ws } };
    });
  }
  function checkpoint(id, opts) {
    var SM = ns().StrategyModel, s = getStore(); if (!s || !s.strategies[id]) return null;
    var out = SM.checkpoint(s.strategies[id], opts || {});
    s.strategies[id] = out.strategy;
    s.snapshots[id] = (s.snapshots[id] || []).concat([out.snapshot]);
    persist();
    return out;
  }
  function transition(id, to, ctx) {
    var SM = ns().StrategyModel, s = getStore(); if (!s || !s.strategies[id]) return { ok: false };
    var res = SM.applyTransition(s.strategies[id], to, ctx || {});
    if (res.ok) { s.strategies[id] = res.strategy; persist(); }
    return res;
  }
  function listRuns(strategyId) {
    var s = getStore(); if (!s) return [];
    return Object.keys(s.runs).map(function (k) { return s.runs[k]; })
      .filter(function (r) { return !strategyId || r.strategyId === strategyId; })
      .sort(function (a, b) { return (b.requestedAt || 0) - (a.requestedAt || 0); });
  }
  function getRun(id) { var s = getStore(); return s ? s.runs[id] || null : null; }
  function runBacktest(strategyId, opts) {
    opts = opts || {};
    var SM = ns().StrategyModel, RM = ns().StrategyRunModel, s = getStore();
    var draft = s.strategies[strategyId]; if (!draft) return null;
    var blockers = SM.formationIssues(draft).filter(function (i) { return i.severity === "blocking" && i.stage !== "review"; });
    if (blockers.length) return { ok: false, issues: blockers };
    var at = Date.now();
    var artifact = buildRun(SM, RM, draft, { env: opts.env || "fixture", window: opts.window || { start: "2024-07-01", end: "2026-06-30" }, dataset: opts.dataset || { id: draft.universe.datasetId, label: (opts.datasetLabel || draft.universe.datasetId) + " \u00b7 " + (opts.env || "fixture") }, engine: { name: "meridian-backtest", version: "2.4.0" }, at: at, seedTag: opts.seedTag || "user" });
    s.runs[artifact.id] = artifact; s.current.runId = artifact.id; s.current.strategyId = strategyId; persist();
    return { ok: true, artifact: artifact };
  }
  function freshnessFor(runId) {
    var RM = ns().StrategyRunModel, run = getRun(runId);
    return RM.freshness(run, run ? getStrategy(run.strategyId) : null);
  }
  function attest(rec) {
    var s = getStore(), run = getRun(rec.runId), strat = getStrategy(rec.strategyId);
    var att = { id: "ATT-" + String(1000 + s.attestations.length).slice(1), strategyId: rec.strategyId, strategyVersion: strat ? strat.version : null, runId: rec.runId, proofHash: run && run.proof ? run.proof.inputHash : null, decision: rec.decision, signedBy: rec.signedBy, role: rec.role || null, signedAt: Date.now(), note: rec.note || null };
    s.attestations = s.attestations.concat([att]); persist();
    return att;
  }
  function attestationState(att) {
    var strat = getStrategy(att.strategyId), run = getRun(att.runId);
    if (!strat || !run) return { state: "superseded", reason: "Bound object no longer exists." };
    if (att.strategyVersion !== strat.version) return { state: "superseded", reason: "Strategy moved to v" + strat.version + " after signing (signed v" + att.strategyVersion + ")." };
    if (run.proof && att.proofHash !== run.proof.inputHash) return { state: "superseded", reason: "Run proof no longer matches the signed hash." };
    var fr = freshnessFor(att.runId);
    if (fr.state === "stale") return { state: "superseded", reason: "Draft material inputs changed after signing (" + fr.changed.join(", ") + ")." };
    return { state: "standing", reason: null };
  }
  function resetToFixture() {
    try { global.localStorage && global.localStorage.removeItem(KEY); } catch (e) { /* read-only */ }
    state = { store: null, storage: "ok", reason: null };
    return ready();
  }
  function storageNotice() { load(); return state.storage === "ok" ? null : { tone: "warning", title: "Session-only \u00b7 read-only persistence", message: state.reason }; }

  global.MeridianStrategyExecutor = {
    KEY: KEY, SCHEMA_VERSION: SCHEMA_VERSION, FIXTURE_STRATEGY_ID: FIXTURE_STRATEGY_ID,
    ready: ready, getStore: getStore, storageNotice: storageNotice, resetToFixture: resetToFixture,
    readParams: readParams, withIdentity: withIdentity, setUrlState: setUrlState, resolveIdentity: resolveIdentity,
    getStrategy: getStrategy, updateStrategy: updateStrategy, updateWorksheetCell: updateWorksheetCell,
    validateFormula: validateFormula, FUNCTIONS: FUNCTIONS.slice(),
    checkpoint: checkpoint, transition: transition,
    listRuns: listRuns, getRun: getRun, runBacktest: runBacktest, freshnessFor: freshnessFor,
    attest: attest, attestationState: attestationState,
    _internals: { migrate: migrate, seedOf: seedOf, rng: rng, businessDays: businessDays, synthSeries: synthSeries, synthTrades: synthTrades, buildFixtureStore: buildFixtureStore },
  };
})(typeof window !== "undefined" ? window : globalThis);
