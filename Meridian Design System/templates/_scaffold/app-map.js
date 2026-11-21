// Meridian app map — the suite's one address book (docs/SUITE_COHESION_4_4.md › track A).
// Before this file: twelve nav vocabularies, nine rails whose onSelect was a no-op, twelve screens
// with no rail at all, and exactly one real route table. The information architecture was
// unknowable from any single screen.
//
// ds-base.js loads this for you. A screen becomes two lines:
//   sections: MeridianAppMap.sections("trading-desk"),   // rail + palette "Go to" + G chords
//   go: MeridianAppMap.go,                                // real navigation
//
// Object routing — every screen can send any object to the screen that owns its type:
//   MeridianAppMap.open("security", "US0378331005")  →  ../security-master-registry/SecurityDetail.dc.html?security=…
(function (global) {
  "use strict";
  var M = {};

  var ICON = "../../assets/icons/";

  // ── Domains ─────────────────────────────────────────────────────────────────
  // Five business domains plus a footer. This IS the product's IA; nothing else declares one.
  M.DOMAINS = [
    { id: "trading", label: "Trading" },
    { id: "accounting", label: "Accounting" },
    { id: "operations", label: "Operations" },
    { id: "research", label: "Research" },
    { id: "reporting", label: "Reporting" },
    { id: "admin", label: "Admin", footer: true },
  ];

  // ── Screens ─────────────────────────────────────────────────────────────────
  // id · the canonical screen id, used as NavRail activeId and palette row id
  // label · what this screen is called EVERYWHERE — rail, masthead, palette, breadcrumb
  // href · relative to templates/<slug>/, so every screen resolves it identically
  // rail · false = deliberately no rail (pre-auth, wallboard, specimen, print artifact)
  M.SCREENS = [
    // Trading
    { id: "dashboard", label: "Portfolio Overview", domain: "trading", href: "../dashboard-workstation/DashboardWorkstation.dc.html", icon: "dashboard.svg", shortcut: "G D" },
    { id: "positions", label: "Positions", domain: "trading", href: "../position-detail/PositionDetail.dc.html", icon: "account-portfolio.svg", shortcut: "G H" },
    { id: "trading-desk", label: "Trading Desk", domain: "trading", href: "../trading-desk/TradingDesk.dc.html", icon: "trading.svg", shortcut: "G T" },
    { id: "basket-builder", label: "Basket Builder", domain: "trading", href: "../basket-builder/BasketBuilder.dc.html", icon: "portfolio-import.svg", shortcut: "G K" },
    { id: "charting", label: "Charting", domain: "trading", href: "../charting-workstation/ChartingWorkstation.dc.html", icon: "charting.svg", shortcut: "G C" },

    // Accounting
    { id: "accounting", label: "General Ledger", domain: "accounting", href: "../accounting-workstation/AccountingWorkstation.dc.html", icon: "account-portfolio.svg", shortcut: "G L" },
    { id: "account-sheet", label: "Account Sheet", domain: "accounting", href: "../accounting-workstation/AccountingWorkstation.dc.html?view=account", icon: "aggregate-portfolio.svg", shortcut: "G A", hidden: true, parent: "accounting" },
    { id: "reconciliation", label: "Reconciliation", domain: "accounting", href: "../reconciliation/Reconciliation.dc.html", icon: "data-quality.svg", shortcut: "G R" },
    { id: "close", label: "Close Management", domain: "accounting", href: "../close-management/CloseManagement.dc.html", icon: "data-calendar.svg", shortcut: "G X" },
    { id: "event-ledger", label: "Event Ledger", domain: "accounting", href: "../investment-event-ledger/InvestmentEventLedger.dc.html", icon: "event-replay.svg", shortcut: "G E" },

    // Operations
    { id: "ops-wall", label: "Ops Wall", domain: "operations", href: "../ops-wall/OpsWall.dc.html", icon: "system-health.svg", rail: false },
    { id: "alerting", label: "Alert Triage", domain: "operations", href: "../alerting-workstation/AlertingWorkstation.dc.html", icon: "diagnostics.svg", shortcut: "G Q" },
    { id: "ingestion", label: "Ingestion", domain: "operations", href: "../ingestion-operations/IngestionOperations.dc.html", icon: "data-operations.svg", shortcut: "G I" },
    { id: "adapters", label: "Adapter Ops", domain: "operations", href: "../adapter-operations/AdapterOperations.dc.html", icon: "service-manager.svg" },
    { id: "security-master", label: "Security Master", domain: "operations", href: "../security-master-registry/SecurityMasterRegistry.dc.html", icon: "security-master.svg", shortcut: "G S" },

    // Research
    { id: "workbench", label: "Analytical Workbench", domain: "research", href: "../analytical-workbench/AnalyticalWorkbench.dc.html", icon: "research.svg", shortcut: "G N" },
    { id: "formation", label: "Strategy Formation", domain: "research", href: "../strategy-formation/StrategyFormation.dc.html", icon: "governance.svg" },
    { id: "builder", label: "Strategy Builder", domain: "research", href: "../strategy-formation/StrategyFormation.dc.html?view=builder", icon: "strategy-builder.svg", shortcut: "G G", parent: "formation" },
    { id: "fields", label: "Field & Formula", domain: "research", href: "../field-formula/FieldFormula.dc.html", icon: "formula.svg" },
    { id: "backtest", label: "Backtest", domain: "research", href: "../strategy-runs/StrategyRuns.dc.html?view=backtest", icon: "backtest.svg", shortcut: "G B", parent: "runs" },
    { id: "runs", label: "Strategy Runs", domain: "research", href: "../strategy-runs/StrategyRuns.dc.html", icon: "strategy-runs.svg", shortcut: "G U" },
    { id: "compare", label: "Run Compare", domain: "research", href: "../strategy-runs/StrategyRuns.dc.html?view=compare", icon: "run-mat.svg", hidden: true, parent: "runs" },
    { id: "lab", label: "Covered-Call Lab", domain: "research", href: "../strategy-runs/StrategyRuns.dc.html?view=lab", icon: "run-detail.svg", hidden: true, parent: "runs" },
    { id: "governance", label: "Governance", domain: "research", href: "../strategy-governance/StrategyGovernance.dc.html", icon: "governance.svg" },
    { id: "alm", label: "Asset-Liability", domain: "research", href: "../alm-review/AlmReview.dc.html", icon: "aggregate-portfolio.svg" },
    { id: "ownership", label: "Ownership Graph", domain: "research", href: "../ownership-graph/OwnershipGraph.dc.html", icon: "symbols.svg" },

    // Reporting
    { id: "report-library", label: "Report Library", domain: "reporting", href: "../report-library/ReportLibrary.dc.html", icon: "data-browser.svg", shortcut: "G P" },
    { id: "report-scheduler", label: "Scheduled Delivery", domain: "reporting", href: "../report-scheduler/ReportScheduler.dc.html", icon: "schedule-manager.svg" },
    { id: "report-pack", label: "Report Pack", domain: "reporting", href: "../report-pack/ReportPack.dc.html", icon: "data-export.svg", rail: false },
    { id: "asset-sheet", label: "Asset Sheet", domain: "reporting", href: "../asset-sheet/AssetSheet.dc.html", icon: "data-export.svg", rail: false },

    // Admin
    { id: "settings", label: "Settings & Admin", domain: "admin", href: "../settings-admin/SettingsAdmin.dc.html", icon: "settings.svg", shortcut: "G ," },

    // Off-rail surfaces — addressable, never listed.
    { id: "session-start", label: "Sign in", domain: "admin", href: "../session-start/SessionStart.dc.html", icon: "admin-maintenance.svg", rail: false, hidden: true },
    { id: "blank", label: "Untitled Workstation", domain: "admin", href: "../blank-workstation/BlankWorkstation.dc.html", icon: "service-manager.svg", hidden: true },
    { id: "interaction-depth", label: "Interaction Depth", domain: "admin", href: "../interaction-depth/InteractionDepth.dc.html", icon: "keyboard-shortcuts.svg", rail: false, hidden: true },
    { id: "security-detail", label: "Security Detail", domain: "operations", href: "../security-master-registry/SecurityDetail.dc.html", icon: "security-master.svg", hidden: true, parent: "security-master" },
    { id: "position-detail", label: "Position Detail", domain: "trading", href: "../position-detail/PositionDetail.dc.html", icon: "account-portfolio.svg", hidden: true, parent: "positions" },
    { id: "tie-out", label: "Tie-out Proof", domain: "accounting", href: "../close-management/CloseManagement.dc.html?view=tie-out", icon: "retention-assurance.svg", hidden: true, parent: "close" },
    { id: "exception", label: "Exception Resolution", domain: "accounting", href: "../reconciliation/Reconciliation.dc.html?view=exception", icon: "data-quality.svg", hidden: true, parent: "reconciliation" },
  ];

  var BY_ID = {};
  M.SCREENS.forEach(function (s) { BY_ID[s.id] = s; });

  /** The screen record for an id — or undefined. */
  M.screen = function (id) { return BY_ID[id]; };
  /** The label the whole suite uses for a screen. Masthead `moduleLabel` reads this. */
  M.label = function (id) { var s = BY_ID[id]; return s ? s.label : "Workstation"; };
  /** Absolute-ish href for a screen id, with optional extra query params. */
  M.href = function (id, params) {
    var s = BY_ID[id];
    if (!s) return null;
    var href = s.href;
    if (params) {
      var qs = Object.keys(params).filter(function (k) { return params[k] != null; })
        .map(function (k) { return encodeURIComponent(k) + "=" + encodeURIComponent(params[k]); }).join("&");
      if (qs) href += (href.indexOf("?") >= 0 ? "&" : "?") + qs;
    }
    return href;
  };

  // ── The rail ────────────────────────────────────────────────────────────────
  /**
   * NavRail `sections` for a screen. Every rail in the suite is this call, so every rail lists
   * the same destinations in the same order under the same section names.
   *   opts.only     — array of domain ids to include (default: all)
   *   opts.showHidden — include `hidden` screens (default false)
   */
  M.sections = function (activeId, opts) {
    opts = opts || {};
    var only = opts.only;
    var out = [];
    M.DOMAINS.forEach(function (d) {
      if (only && only.indexOf(d.id) < 0) return;
      var items = M.SCREENS.filter(function (s) {
        return s.domain === d.id && (opts.showHidden || !s.hidden);
      }).map(function (s) {
        return { id: s.id, label: s.label, icon: ICON + s.icon, shortcut: s.shortcut };
      });
      if (items.length) out.push({ label: d.label, items: items });
    });
    return out;
  };

  /** The active screen id inferred from the URL — so a rail highlights correctly with no prop. */
  M.activeFromLocation = function () {
    var path = String(global.location && global.location.pathname || "");
    var m = path.match(/templates\/([^/]+)\/([^/]+)\.dc\.html/);
    if (!m) return null;
    var slug = m[1], file = m[2];
    var view = new URLSearchParams(global.location.search || "").get("view");
    // Prefer a screen whose href matches slug + view, else the first screen in that slug.
    var candidates = M.SCREENS.filter(function (s) { return s.href.indexOf("../" + slug + "/") === 0; });
    if (view) {
      var withView = candidates.filter(function (s) { return s.href.indexOf("view=" + view) >= 0; })[0];
      if (withView) return withView.id;
    }
    var exact = candidates.filter(function (s) { return s.href.indexOf("/" + file + ".dc.html") >= 0 && s.href.indexOf("?") < 0; })[0];
    return (exact || candidates[0] || {}).id || null;
  };

  // ── Navigation ──────────────────────────────────────────────────────────────
  /**
   * Go to a screen by id. The one navigation function in the suite — pass it straight to
   * NavRail's `onSelect` and the operator layer's `go`.
   * Unknown ids toast rather than silently doing nothing (the old no-op rails' real sin).
   */
  M.go = function (id, params) {
    if (!id) return;
    var here = M.activeFromLocation();
    if (id === here && !params) return;
    var href = M.href(id, params);
    if (href) { global.location.href = href; return; }
    var t = global.MeridianToast;
    if (t && t.info) t.info("No screen bound", String(id) + " — add it to templates/_scaffold/app-map.js.");
    else console.warn("MeridianAppMap.go: unknown screen id " + id);
  };

  // ── Object routing ──────────────────────────────────────────────────────────
  // Every object type in the suite, and the screen that owns it. `session-start` used to send
  // every handover object to Reconciliation regardless of type; now each lands where it lives.
  //
  // `position` owns a screen of its own (4.5). It used to route to the portfolio dashboard with no
  // param — a position was something you found inside a portfolio, not an object you could open,
  // so nothing could link to one. Security ⇄ position is now a real two-way relation.
  M.OBJECT_TYPES = {
    position: { screen: "position-detail", param: "position", label: "Position" },
    portfolio: { screen: "dashboard", param: "portfolio", label: "Portfolio" },
    order: { screen: "trading-desk", param: "order", label: "Order" },
    basket: { screen: "basket-builder", param: "basket", label: "Basket" },
    symbol: { screen: "charting", param: "symbol", label: "Symbol" },
    security: { screen: "security-detail", param: "security", label: "Security" },
    account: { screen: "account-sheet", param: "account", label: "Account" },
    entry: { screen: "accounting", param: "entry", label: "Journal entry" },
    break: { screen: "reconciliation", param: "break", label: "Break" },
    exception: { screen: "exception", param: "break", label: "Exception" },
    task: { screen: "close", param: "task", label: "Close task" },
    tieout: { screen: "tie-out", param: "task", label: "Tie-out" },
    event: { screen: "event-ledger", param: "event", label: "Investment event" },
    alert: { screen: "alerting", param: "alert", label: "Alert" },
    job: { screen: "ingestion", param: "job", label: "Pipeline job" },
    adapter: { screen: "adapters", param: "adapter", label: "Adapter" },
    strategy: { screen: "formation", param: "strategy", label: "Strategy" },
    run: { screen: "runs", param: "run", label: "Run" },
    field: { screen: "fields", param: "field", label: "Field" },
    entity: { screen: "ownership", param: "entity", label: "Entity" },
    report: { screen: "report-library", param: "report", label: "Report" },
    setting: { screen: "settings", param: "section", label: "Setting" },
  };

  /** href for an object — null when the type has no owning screen. */
  M.objectHref = function (type, id, params) {
    var t = M.OBJECT_TYPES[type];
    if (!t) return null;
    var p = Object.assign({}, params || {});
    if (t.param && id != null) p[t.param] = id;
    return M.href(t.screen, p);
  };
  /** Navigate to an object. The suite-wide answer to "open this thing". */
  M.open = function (type, id, params) {
    var href = M.objectHref(type, id, params);
    if (href) { global.location.href = href; return true; }
    var t = global.MeridianToast;
    if (t && t.info) t.info("Nowhere to open", String(type) + " has no owning screen in app-map.js.");
    return false;
  };
  /** Human label for an object type — palette group headings, trail rows. */
  M.typeLabel = function (type) { var t = M.OBJECT_TYPES[type]; return t ? t.label : String(type || "Object"); };

  // ── Return loops ────────────────────────────────────────────────────────────
  // Operators work in loops, not chains (docs/SUITE_COHESION_4_4.md · E-2). The suite documented
  // reconciliation → accounting and close → tie-out but never the returns: a journal entry could
  // not open the break that caused it, an account sheet could not open its own reconciliation.
  //
  // Each entry is "from this kind of object, you can get back to these". `verb` is the words the
  // affordance uses — written from the operator's side of the loop, never "View related".
  M.LOOPS = {
    entry: [{ to: "break", via: "sourceBreak", verb: "Open the break that caused this" },
            { to: "account", via: "accountId", verb: "Open the account sheet" },
            { to: "task", via: "closeTask", verb: "Open the close task" }],
    "break": [{ to: "entry", via: "postedEntry", verb: "Open the entry that resolved this" },
            { to: "account", via: "accountId", verb: "Open the account sheet" },
            { to: "exception", via: "exceptionId", verb: "Open the exception record" }],
    exception: [{ to: "break", via: "breakId", verb: "Back to the break queue" },
            { to: "entry", via: "postedEntry", verb: "Open the compensating entry" }],
    account: [{ to: "break", via: "reconciliationId", verb: "Open this account's reconciliation" },
            { to: "entry", via: "lastEntry", verb: "Open the last entry posted here" }],
    task: [{ to: "tieout", via: "tieOutId", verb: "Open the tie-out proof" },
            { to: "break", via: "blockingBreak", verb: "Open the break blocking this task" }],
    tieout: [{ to: "task", via: "taskId", verb: "Back to the close plane" }],
    position: [{ to: "security", via: "securityId", verb: "Open the security master record" },
            { to: "portfolio", via: "portfolioId", verb: "Open the portfolio" },
            { to: "account", via: "accountId", verb: "Open the account sheet" },
            { to: "symbol", via: "symbol", verb: "Chart this" }],
    security: [{ to: "position", via: "positionId", verb: "Open this holding" }],
    event: [{ to: "entry", via: "postedEntry", verb: "Open the resulting entry" },
            { to: "security", via: "securityId", verb: "Open the security" }],
    run: [{ to: "strategy", via: "strategyId", verb: "Back to the strategy" }],
    alert: [{ to: "job", via: "jobId", verb: "Open the pipeline run" },
            { to: "adapter", via: "adapterId", verb: "Open the adapter" }],
    job: [{ to: "adapter", via: "adapterId", verb: "Open the adapter" }],
  };

  /**
   * The loops available FROM an object, given what that object actually carries.
   * `obj` is the screen's own record; a loop appears only when its `via` field is populated, so
   * an unresolved break offers no "entry that resolved this".
   * Returns [{ to, id, verb, label, href, open() }].
   */
  M.loops = function (type, obj) {
    var defs = M.LOOPS[type] || [];
    var out = [];
    defs.forEach(function (d) {
      var id = obj && obj[d.via];
      if (id == null || id === "") return;
      var href = M.objectHref(d.to, id);
      if (!href) return;
      out.push({
        to: d.to, id: id, verb: d.verb, label: M.typeLabel(d.to) + " " + id, href: href,
        open: function () { global.location.href = href; },
      });
    });
    return out;
  };

  /**
   * The loops as operator-layer actions, so every return is also a palette row and a keystroke
   * on the screen that owns the object — `actions: [...mine, ...MeridianAppMap.loopActions(…)]`.
   */
  M.loopActions = function (type, obj) {
    return M.loops(type, obj).map(function (l, i) {
      return { id: "loop-" + l.to + "-" + i, label: l.verb, group: "Go back to", keywords: [l.to, String(l.id)], run: l.open };
    });
  };

  // ── The way back ────────────────────────────────────────────────────────────
  // docs/SUITE_COHESION_4_4.md · E-1. `ObjectTrail` states where the *person* has been; this
  // states where the *object* sits — Domain › parent screen › screen › object. Derived from the
  // app map and the URL, so WorkstationTopbar renders it on all 27 screens with no screen work.

  var NAMES = {};        // "type:id" → the human label a screen taught us
  var watchers = [];
  function notify() { for (var i = 0; i < watchers.length; i++) { try { watchers[i](); } catch (e) {} } }

  /** Subscribe to trail changes (a screen naming an object). Returns an unsubscribe. */
  M.onChange = function (fn) {
    watchers.push(fn);
    return function () { var i = watchers.indexOf(fn); if (i >= 0) watchers.splice(i, 1); };
  };

  /**
   * Teach the suite what an object is CALLED, so the breadcrumb reads "AAPL · Apple Inc." and not
   * "Security US0378331005". One line in a screen's componentDidMount, and every crumb, chip and
   * search row that names this object agrees:
   *   MeridianAppMap.nameObject("security", id, rec.symbol + " · " + rec.name);
   */
  M.nameObject = function (type, id, label) {
    if (!type || id == null || !label) return;
    var k = type + ":" + id;
    if (NAMES[k] === label) return;
    NAMES[k] = label;
    notify();
  };
  /** The taught label for an object, else "Security US0378331005". */
  M.objectLabel = function (type, id) {
    return NAMES[type + ":" + id] || (M.typeLabel(type) + " " + id);
  };

  /**
   * The object named by the current URL, if any: the first param that some object type owns on
   * this screen. `?security=US037…` on security-detail → { type: "security", id: "US037…" }.
   */
  M.objectFromLocation = function (activeId) {
    var q = new URLSearchParams(global.location && global.location.search || "");
    var here = activeId || M.activeFromLocation();
    var best = null;
    Object.keys(M.OBJECT_TYPES).forEach(function (type) {
      var t = M.OBJECT_TYPES[type];
      if (!t.param || !q.has(t.param)) return;
      var id = q.get(t.param);
      if (!id) return;
      // Prefer the type this screen actually owns; otherwise take the first param that resolves.
      if (t.screen === here) best = { type: type, id: id };
      else if (!best) best = { type: type, id: id };
    });
    return best;
  };

  /**
   * The breadcrumb for the current location (or an explicit screen id).
   * Returns [{ id?, label, href?, onClick?, current? }] — the shape `Breadcrumb` and
   * `WorkstationTopbar` both take. The last crumb is always the current place and never a link.
   *   opts.object  — { type, id } to override what the URL says
   *   opts.leaf    — a final crumb label (a drawer, a step) appended as current
   */
  M.trail = function (activeId, opts) {
    opts = opts || {};
    var id = activeId || M.activeFromLocation();
    var s = BY_ID[id];
    if (!s) return [];
    var crumbs = [];

    var d = M.DOMAINS.filter(function (x) { return x.id === s.domain; })[0];
    if (d) crumbs.push({ label: d.label, domain: true });

    // Parent screens, outermost first — at most a couple of levels in this suite.
    var chain = [], walk = s, guard = 0;
    while (walk && walk.parent && guard++ < 6) {
      var p = BY_ID[walk.parent];
      if (!p) break;
      chain.unshift(p);
      walk = p;
    }
    chain.forEach(function (p) {
      crumbs.push({ id: p.id, label: p.label, href: p.href, onClick: function () { M.go(p.id); } });
    });

    crumbs.push({ id: s.id, label: s.label, href: s.href, onClick: function () { M.go(s.id); } });

    var obj = opts.object !== undefined ? opts.object : M.objectFromLocation(id);
    if (obj && obj.id != null) crumbs.push({ label: M.objectLabel(obj.type, obj.id), object: obj });
    if (opts.leaf) crumbs.push({ label: String(opts.leaf) });

    var last = crumbs[crumbs.length - 1];
    last.current = true;
    delete last.href;
    delete last.onClick;
    return crumbs;
  };

  /**
   * Where "back" goes, and what to call it. Browser history when the operator actually came from
   * inside the suite; otherwise the parent screen; otherwise nothing (the caller hides the
   * affordance). Returns null when there is no honest way back — never a dead chevron.
   */
  M.backTarget = function (activeId) {
    var id = activeId || M.activeFromLocation();
    var s = BY_ID[id];
    var ref = String(global.document && global.document.referrer || "");
    var sameOrigin = ref && global.location && ref.indexOf(global.location.origin) === 0;
    var inSuite = sameOrigin && ref.indexOf("/templates/") >= 0 && ref !== String(global.location.href);
    if (inSuite) {
      var m = ref.match(/templates\/([^/]+)\//);
      var from = m && M.SCREENS.filter(function (x) { return x.href.indexOf("../" + m[1] + "/") === 0; })[0];
      return { label: "Back to " + (from ? from.label : "the previous screen"), kind: "history",
        run: function () { global.history.back(); } };
    }
    var parent = s && s.parent && BY_ID[s.parent];
    if (parent) return { label: "Back to " + parent.label, kind: "parent", run: function () { M.go(parent.id); } };
    return null;
  };

  global.MeridianAppMap = M;
})(typeof window !== "undefined" ? window : globalThis);
