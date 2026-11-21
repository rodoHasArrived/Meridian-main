// Meridian Strategy Executor — shared screen kit (window.MeridianExecutorKit).
// Boot polling, the one suite nav rail, identity-carrying navigation, chart sampling, field
// metadata, and formatters shared by the seven executor screens. Pure glue — the contracts
// live in StrategyModel / StrategyRunModel; the store in executor-store.js.
(function (global) {
  "use strict";
  var K = {};
  K.ROUTES = { formation: "../strategy-formation/StrategyFormation.dc.html", builder: "../strategy-formation/StrategyFormation.dc.html?view=builder", fields: "../field-formula/FieldFormula.dc.html", backtest: "../strategy-runs/StrategyRuns.dc.html?view=backtest", runs: "../strategy-runs/StrategyRuns.dc.html?view=runs", compare: "../strategy-runs/StrategyRuns.dc.html?view=compare", governance: "../strategy-governance/StrategyGovernance.dc.html" };
  // The rail is the suite-wide app map (templates/_scaffold/app-map.js), so an executor screen
  // lists the same destinations as every other screen. Intra-suite ids still route through
  // ROUTES below, which carries strategy/run identity in the URL.
  Object.defineProperty(K, "NAV_SECTIONS", { get: function () {
    return global.MeridianAppMap ? global.MeridianAppMap.sections() : [];
  } });
  K.ds = function () { return global.MeridianDesignSystem_205d17 || {}; };
  K.X = function () { return global.MeridianStrategyExecutor; };
  K.applyChrome = function (props, defEnv) {
    var html = document.documentElement;
    var P = global.MeridianPrefs;
    // Tweak seeds, operator wins (docs/SUITE_COHESION_4_4.md › D): a stored display choice
    // follows the operator across every screen in the suite, including these seven.
    var theme = (props && props.theme) || "light";
    var env = String((props && props.environment) || defEnv || "FIXTURE").toUpperCase();
    var density = (props && props.density) || "default";
    html.setAttribute("data-theme", P ? P.theme(theme) : theme);
    html.setAttribute("data-environment", P ? P.environment(env) : env);
    if (document.body) document.body.setAttribute("data-theme-density", P ? P.density(density) : density);
  };
  /** Poll until the bundle + store are live, then setState({booted, …patch(X, identity)}). */
  K.boot = function (comp, patch) {
    comp._kitPoll = setInterval(function () {
      var X = K.X();
      if (X && K.ds().StrategyModel && X.ready()) {
        clearInterval(comp._kitPoll);
        var idn = X.resolveIdentity();
        comp.identity = idn;
        X.setUrlState({ strategy: idn.strategyId });
        comp.setState(Object.assign({ booted: true }, patch ? patch(X, idn) : { draft: X.getStrategy(idn.strategyId) }));
      }
    }, 40);
    comp._kitDeadline = setTimeout(function () { if (!comp.state.booted) { clearInterval(comp._kitPoll); comp.setState({ bootError: "Suite store failed to load — check ../_strategy-executor/executor-store.js" }); } }, 8000);
  };
  K.unboot = function (comp) { clearInterval(comp._kitPoll); clearTimeout(comp._kitDeadline); };
  K.nav = function (id, extra) {
    // Inside the suite, carry strategy/run identity. Outside it, the app map owns the route.
    var r = K.ROUTES[id];
    if (!r) { if (global.MeridianAppMap) return global.MeridianAppMap.go(id); r = id; }
    global.location.href = K.X() ? K.X().withIdentity(r, extra || {}) : r;
  };
  K.topbarEl = function (React, moduleLabel, env) { var T = K.ds().WorkstationTopbar; return T && React.createElement(T, { moduleLabel: moduleLabel, environment: env,  brandSrc: "../../assets/brand/meridian-mark-light.svg" }); };
  K.navEl = function (React, activeId) { var N = K.ds().NavRail; return N && React.createElement(N, { activeId: activeId, onSelect: function (id) { K.nav(id); }, sections: K.NAV_SECTIONS }); };
  K.statusEl = function (React, items) { var S = K.ds().StatusBar; return S && React.createElement(S, { items: items }); };
  K.statusTail = function (env) {
    var X = K.X(), storage = X && X.ready() ? X.storageNotice() : null;
    return [{ status: storage ? "warn" : "ok", label: "Store", value: storage ? "Session-only" : "Persisted" }, { status: "ok", label: "Env", value: env, push: true }];
  };
  K.pct = function (v, dp) { return v == null ? "—" : ((v >= 0 ? "+" : "\u2212") + Math.abs(v * 100).toFixed(dp == null ? 1 : dp) + "%"); };
  K.pctu = function (v, dp) { return v == null ? "—" : ((v * 100).toFixed(dp == null ? 1 : dp) + "%"); };
  K.n2 = function (v) { return v == null ? "—" : Number(v).toFixed(2); };
  K.iso = function (t) { return t ? new Date(t).toISOString().replace(".000Z", "Z").replace("T", " ") : "—"; };
  // Calendar day of an instant — the operator's day, through the namespace's LocalDate authority.
  K.isoD = function (t) { if (!t) return "—"; var LD = K.ds().LocalDate; if (LD) return LD.dayOfInstant(t); var d = new Date(t); return d.getFullYear() + "-" + String(d.getMonth() + 1).padStart(2, "0") + "-" + String(d.getDate()).padStart(2, "0"); };
  // Numbers go through the namespace's Format authority (C-01) — never a local Intl or toLocaleString.
  K.fmt = function () { return K.ds().Format; };
  K.int = function (v) { var F = K.fmt(); return F ? F.number(v, { decimals: 0 }) : String(v); };
  K.usd0 = function (v) { var F = K.fmt(); return F ? F.money(Math.round(v), { currency: "USD", decimals: 0 }) : "$" + Math.round(v); };
  K.usd0s = function (v) { return (v >= 0 ? "+" : "\u2212") + K.usd0(Math.abs(v)); };
  K.hash8 = function (h) { return h ? String(h).slice(0, 8) : "—"; };
  K.durS = function (a, b) { if (a == null || b == null) return "—"; var s = Math.max(0, Math.round((b - a) / 1000)); return Math.floor(s / 60) + "m " + (s % 60).toString().padStart(2, "0") + "s"; };
  var MON = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
  /** Downsample [{t,v}] to n points → { values, labels } (month labels every ~n/8). */
  K.sample = function (pts, n) {
    if (!pts || pts.length < 2) return { values: [], labels: [] };
    n = Math.min(n || 64, pts.length);
    var values = [], labels = [], step = Math.max(1, Math.ceil(n / 8));
    for (var i = 0; i < n; i++) {
      var p = pts[Math.round(i * (pts.length - 1) / (n - 1))];
      values.push(p.v);
      var d = new Date(p.t);
      labels.push(i % step === 0 ? MON[d.getUTCMonth()] + " " + String(d.getUTCFullYear()).slice(2) : "");
    }
    return { values: values, labels: labels };
  };
  /** Rolling-peak drawdown in % (≤ 0), parallel to values. */
  K.ddPct = function (values) { var peak = -Infinity; return values.map(function (v) { if (v > peak) peak = v; return Math.round((v / peak - 1) * 1000) / 10; }); };
  /** Close-to-close returns in %, length n−1. */
  K.retPct = function (values) { var out = []; for (var i = 1; i < values.length; i++) out.push(Math.round((values[i] / values[i - 1] - 1) * 10000) / 100); return out; };
  K.freshBadge = function (state) { return state === "current" ? { variant: "success", label: "Proof current" } : state === "stale" ? { variant: "warning", label: "Proof stale" } : { variant: "neutral", label: "No proof" }; };
  /** AMX field catalog metadata — one source for Field & Formula and the Governance catalog. */
  K.FIELD_META = [
    { name: "PRICE", sig: "PRICE(cusip)", cat: "Price", tone: "info", desc: "Clean price, 4dp, session close", freq: "Passed", freqLabel: "daily", owner: "amx-core", since: "1.0", fallback: "Prior close, flagged stale" },
    { name: "CLEAN_PRICE", sig: "CLEAN_PRICE(cusip)", cat: "Price", tone: "info", desc: "Price excluding accrued interest", freq: "Passed", freqLabel: "daily", owner: "amx-core", since: "1.0", fallback: "Derived from PRICE − accrual" },
    { name: "COUPON", sig: "COUPON(cusip)", cat: "Yield", tone: "success", desc: "Annual coupon rate from terms", freq: "Passed", freqLabel: "static", owner: "security-master", since: "1.0", fallback: "None — terms are authoritative" },
    { name: "YTM", sig: "YTM(cusip)", cat: "Yield", tone: "success", desc: "Yield to maturity, ACT/365", freq: "Passed", freqLabel: "daily", owner: "amx-analytics", since: "1.2", fallback: "Newton solve on last price" },
    { name: "DURATION", sig: "DURATION(cusip)", cat: "Risk", tone: "warning", desc: "Modified duration, years", freq: "Passed", freqLabel: "daily", owner: "amx-analytics", since: "1.2", fallback: "Terms-based approximation" },
    { name: "CONVEXITY", sig: "CONVEXITY(cusip)", cat: "Risk", tone: "warning", desc: "Convexity, second-order rate risk", freq: "ReviewRequired", freqLabel: "daily", owner: "amx-analytics", since: "2.0", fallback: "None — null when unpriced" },
    { name: "RATING", sig: "RATING(cusip)", cat: "Data", tone: "neutral", desc: "Composite agency rating", freq: "Passed", freqLabel: "on change", owner: "security-master", since: "1.0", fallback: "Last known, flagged stale" },
    { name: "SPREAD_TSY", sig: "SPREAD_TSY(cusip)", cat: "Signal", tone: "danger", desc: "Spread to interpolated treasury, bp", freq: "Passed", freqLabel: "daily", owner: "amx-analytics", since: "1.4", fallback: "Curve rebuild from last marks" },
    { name: "ZSCORE", sig: "ZSCORE(series, window)", cat: "Signal", tone: "danger", desc: "Rolling z-score over window sessions", freq: "Passed", freqLabel: "derived", owner: "amx-analytics", since: "1.4", fallback: "Null under window minimum" },
    { name: "ROLL_3M", sig: "ROLL_3M(cusip)", cat: "Signal", tone: "danger", desc: "3-month roll-down estimate", freq: "ReviewRequired", freqLabel: "weekly", owner: "amx-research", since: "2.1", fallback: "Curve-implied estimate" },
    { name: "ADV", sig: "ADV(cusip)", cat: "Data", tone: "neutral", desc: "Average daily volume, 20-session", freq: "Passed", freqLabel: "daily", owner: "amx-core", since: "1.1", fallback: "TRACE-reported volume" },
    { name: "AMT_OUT", sig: "AMT_OUT(cusip)", cat: "Data", tone: "neutral", desc: "Amount outstanding", freq: "Passed", freqLabel: "on change", owner: "security-master", since: "1.0", fallback: "Last filing value" },
  ];
  global.MeridianExecutorKit = K;
})(typeof window !== "undefined" ? window : globalThis);
