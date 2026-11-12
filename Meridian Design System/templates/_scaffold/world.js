// Meridian world — the shared fiction every screen reads (docs/SUITE_COHESION_4_4.md › track C).
// Before this file the suite had six frozen wall clocks, three business dates, two names for the
// same fund, and six audit actors none of whom was the person who signed in. One instant, one
// calendar, one operator, one set of entity names — retyped literals become lookups.
//
// ds-base.js loads this for you; templates need no extra <script>.
//   window.MeridianWorld.clock()        → "14:32:08 UTC", ticking
//   window.MeridianWorld.OPERATOR.name  → "R. Alvarez"
//   window.MeridianWorld.since(t)       → "3s ago"
//
// Seed data keeps its own historical timestamps. Only *now*, *the period*, *who I am* and
// *what things are called* live here.
(function (global) {
  "use strict";

  // ── The instant ──────────────────────────────────────────────────────────────
  // 2026-08-12 14:32:08Z — the clock most screens already showed. Everything else dates
  // coherently behind it: July is the period being closed, Q2 is the last closed quarter.
  var EPOCH = Date.UTC(2026, 7, 12, 14, 32, 8);
  var mounted = Date.now();
  var subs = [];
  var timer = null;

  function now() { return EPOCH + (Date.now() - mounted); }
  function date() { return new Date(now()); }
  function two(n) { return n < 10 ? "0" + n : String(n); }

  /** "14:32:08 UTC" — the masthead clock. */
  function clock(opts) {
    var d = date(), s = two(d.getUTCHours()) + ":" + two(d.getUTCMinutes()) + ":" + two(d.getUTCSeconds());
    return (opts && opts.bare) ? s : s + " UTC";
  }
  /** "2026-08-12 14:32:08Z" — the stamp figures carry as `asOf`. */
  function stamp(t) {
    var d = t == null ? date() : new Date(t);
    return d.getUTCFullYear() + "-" + two(d.getUTCMonth() + 1) + "-" + two(d.getUTCDate())
      + " " + two(d.getUTCHours()) + ":" + two(d.getUTCMinutes()) + ":" + two(d.getUTCSeconds()) + "Z";
  }
  /** "2026-08-12" */
  function day(t) {
    var d = t == null ? date() : new Date(t);
    return d.getUTCFullYear() + "-" + two(d.getUTCMonth() + 1) + "-" + two(d.getUTCDate());
  }
  /** Elapsed, spoken: "4s ago" · "2m ago" · "3h ago" · "6d ago". */
  function since(t) {
    if (t == null) return "—";
    var ms = now() - (typeof t === "number" ? t : Date.parse(t));
    if (!isFinite(ms)) return "—";
    var s = Math.max(0, Math.round(ms / 1000));
    if (s < 60) return s + "s ago";
    if (s < 3600) return Math.floor(s / 60) + "m ago";
    if (s < 86400) return Math.floor(s / 3600) + "h ago";
    return Math.floor(s / 86400) + "d ago";
  }
  /** "00:00:04" — a lag duration, for freshness metrics. */
  function lag(seconds) {
    var s = Math.max(0, Math.round(seconds));
    return two(Math.floor(s / 3600)) + ":" + two(Math.floor(s / 60) % 60) + ":" + two(s % 60);
  }

  /** Subscribe to the tick. Returns unsubscribe. One interval for the whole page. */
  function subscribe(fn) {
    subs.push(fn);
    if (!timer) timer = setInterval(function () {
      for (var i = 0; i < subs.length; i++) { try { subs[i](now()); } catch (e) { /* a bad subscriber must not stop the clock */ } }
    }, 1000);
    return function () {
      var i = subs.indexOf(fn);
      if (i >= 0) subs.splice(i, 1);
      if (!subs.length && timer) { clearInterval(timer); timer = null; }
    };
  }

  // ── The calendar ─────────────────────────────────────────────────────────────
  // Today is 12 Aug 2026. July is being closed (as-of Jul 31); Q2 closed Jun 30. A screen saying
  // "as of 2026-07-31" is not stale — it is the open close period, and that now reads true.
  var CALENDAR = {
    businessDate: "2026-08-12",
    tradingSession: "RTH",
    closePeriod: { id: "2026-07", label: "July 2026", asOf: "2026-07-31", status: "open", fiscalYear: "FY2026" },
    lastClosed: { id: "2026-Q2", label: "2026-Q2", asOf: "2026-06-30", status: "closed", fiscalYear: "FY2026" },
    priorDay: "2026-08-11",
  };

  // ── Who is signed in ─────────────────────────────────────────────────────────
  // Deliberately one of the actors already in the seed audit trails (`r.alvarez`), so entries the
  // suite already attributes to that id become "things I did" rather than a stranger's work.
  var OPERATOR = {
    id: "r.alvarez",
    name: "R. Alvarez",
    fullName: "Rosa Alvarez",
    initials: "RA",
    role: "operator",
    roleLabel: "Operator",
    title: "Investment Operations",
    desk: "Investment Operations",
    email: "r.alvarez@meridian.example",
    permissions: ["read", "write", "post", "approve:tier1"],
  };

  /** Other people in the building — audit trails, approvals, assignment menus. */
  var PEOPLE = {
    "r.alvarez": { id: "r.alvarez", name: "R. Alvarez", title: "Investment Operations", role: "operator" },
    "m.alvarez": { id: "m.alvarez", name: "M. Alvarez", title: "Fund Accounting", role: "operator" },
    "k.ito": { id: "k.ito", name: "K. Ito", title: "Data Operations", role: "operator" },
    "e.torres": { id: "e.torres", name: "E. Torres", title: "Controller", role: "admin" },
    "a.rodriguez": { id: "a.rodriguez", name: "A. Rodriguez", title: "Reconciliation", role: "operator" },
    "d.kim": { id: "d.kim", name: "D. Kim", title: "Portfolio Management", role: "operator" },
  };
  function person(id) { return PEOPLE[String(id || "").toLowerCase()] || { id: id, name: id, title: "", role: "viewer" }; }

  // ── What things are called ───────────────────────────────────────────────────
  // One rendering per legal entity. "Meridian Fund I LP" and "Meridian Capital · Fund I" were the
  // same fund under two names, on two screens that hand off to each other.
  var ENTITIES = {
    "fund-1": { id: "fund-1", legal: "Meridian Fund I LP", short: "Fund I", code: "MF1", base: "USD", type: "fund" },
    "fund-2": { id: "fund-2", legal: "Meridian Global Macro LP", short: "Global Macro", code: "MGM", base: "USD", type: "fund" },
    "mgmt": { id: "mgmt", legal: "Meridian Capital Management LLC", short: "Meridian Capital", code: "MCM", base: "USD", type: "manager" },
  };
  var PRIMARY_ENTITY = "fund-1";
  function entity(id) { return ENTITIES[id || PRIMARY_ENTITY] || ENTITIES[PRIMARY_ENTITY]; }
  /** Fund label for chrome: "Meridian Fund I LP" (legal) or "Fund I" (short). */
  function entityLabel(id, form) { var e = entity(id); return form === "short" ? e.short : e.legal; }

  var CUSTODIANS = {
    "nt": { id: "nt", name: "Northern Trust", short: "NT", role: "custodian" },
    "ss": { id: "ss", name: "State Street", short: "SS", role: "custodian" },
    "pb-gs": { id: "pb-gs", name: "Goldman Sachs", short: "GS", role: "prime broker" },
  };

  // ── Connection state, derived from the clock ─────────────────────────────────
  // A green dot beside a frozen clock is the fastest way to read as a mock. These lag values move
  // with now(), so "updated 4s ago" is true when it says it.
  var FEEDS = [
    { id: "ibkr", label: "IBKR", period: 2, jitter: 1 },
    { id: "polygon", label: "Polygon", period: 4, jitter: 2 },
    { id: "databento", label: "Databento", period: 5, jitter: 2 },
    { id: "custody", label: "Custody file", period: 900, jitter: 0 },
  ];
  /** Deterministic pseudo-jitter — moves, never jumps around wildly. */
  function wobble(seed, spread) {
    if (!spread) return 0;
    var t = Math.floor(now() / 1000) + seed;
    return Math.abs((Math.sin(t * 12.9898) * 43758.5453) % 1) * spread | 0;
  }
  /** Seconds since a feed last delivered — ticks, bounded by the feed's period. */
  function feedAge(id) {
    var f = FEEDS.filter(function (x) { return x.id === id; })[0] || FEEDS[0];
    return (Math.floor(now() / 1000) % (f.period + 1)) + wobble(f.id.length, f.jitter);
  }
  /** { status, label, text } for a StatusBar row or the masthead pulse dot. */
  function feed(id) {
    var f = FEEDS.filter(function (x) { return x.id === id; })[0] || FEEDS[0];
    var age = feedAge(f.id);
    return { id: f.id, label: f.label, age: age, text: age + "s ago", status: age > f.period * 3 ? "warn" : "ok" };
  }
  /** Worst status across all feeds — what the masthead dot should show. */
  function health() {
    for (var i = 0; i < FEEDS.length; i++) if (feed(FEEDS[i].id).status !== "ok") return "warn";
    return "ok";
  }

  /**
   * Re-render a DC logic class once a second from the shared clock. One line in
   * componentDidMount, and every world-derived label on the screen stays true:
   *   componentDidMount() { this._untick = window.MeridianWorld && window.MeridianWorld.tick(this); }
   *   componentWillUnmount() { this._untick && this._untick(); }
   * Returns the unsubscribe fn; safe to call with no component.
   */
  function tick(component) {
    if (!component || typeof component.forceUpdate !== "function") return function () {};
    return subscribe(function () { try { component.forceUpdate(); } catch (e) {} });
  }

  global.MeridianWorld = {
    EPOCH: EPOCH,
    tick: tick,
    now: now, date: date, clock: clock, stamp: stamp, day: day, since: since, lag: lag, subscribe: subscribe,
    CALENDAR: CALENDAR,
    BUSINESS_DATE: CALENDAR.businessDate,
    PERIOD: CALENDAR.closePeriod,
    LAST_CLOSED: CALENDAR.lastClosed,
    OPERATOR: OPERATOR, PEOPLE: PEOPLE, person: person,
    ENTITIES: ENTITIES, PRIMARY_ENTITY: PRIMARY_ENTITY, entity: entity, entityLabel: entityLabel,
    CUSTODIANS: CUSTODIANS,
    FEEDS: FEEDS, feed: feed, feedAge: feedAge, health: health,
  };
})(typeof window !== "undefined" ? window : globalThis);
