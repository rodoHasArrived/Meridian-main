// Meridian operator preferences — theme · density · environment that survive a navigation
// (docs/SUITE_COHESION_4_4.md › track D).
//
// Before this file the three were per-screen props re-applied from each screen's own default in
// applyChrome(). Walk from a terminal-density dark Ops Wall to the Trading Desk and you landed in
// default-density light PAPER: the suite forgot the operator's choices on every click.
//
// The rule is **tweak seeds, operator wins**. A screen still declares its default (Ops Wall is a
// dark wallboard, the asset sheet is light paper) and that default is what a first-time visit
// shows. The moment the operator chooses, the choice is theirs and follows them everywhere until
// they reset it.
//
// Screens read it through applyChrome:
//   html.setAttribute("data-theme", MeridianPrefs.theme(this.props.theme ?? "light"));
// The masthead's user menu writes it:
//   MeridianPrefs.set("theme", "dark")
(function (global) {
  "use strict";

  var KEY = "meridian.prefs.v1";
  var VALID = {
    theme: ["light", "dark"],
    density: ["default", "compact", "terminal", "spacious"],
    environment: ["LIVE", "PAPER", "FIXTURE"],
  };
  var subs = [];

  function store() {
    try { return global.localStorage; } catch (e) { return null; }
  }
  function read() {
    var s = store();
    if (!s) return {};
    try { return JSON.parse(s.getItem(KEY) || "{}") || {}; } catch (e) { return {}; }
  }
  function write(obj) {
    var s = store();
    if (!s) return;
    try { s.setItem(KEY, JSON.stringify(obj)); } catch (e) { /* private mode — prefs are per-tab then */ }
  }

  var cache = read();

  function norm(key, value) {
    if (value == null) return null;
    var v = String(value);
    if (key === "environment") v = v.toUpperCase();
    return VALID[key] && VALID[key].indexOf(v) >= 0 ? v : null;
  }

  /** The operator's stored choice for `key`, or null if they have not chosen. */
  function get(key) { return norm(key, cache[key]); }

  /**
   * Resolve a value: the operator's choice if they made one, else the screen's own seed.
   * This is what applyChrome calls — so a screen keeps its intended default on a first visit.
   */
  function resolve(key, seed) { return get(key) || norm(key, seed) || VALID[key][0]; }

  function theme(seed) { return resolve("theme", seed); }
  function density(seed) { return resolve("density", seed); }
  function environment(seed) { return resolve("environment", seed); }

  /** Record an operator choice and apply it to the document immediately. */
  function set(key, value) {
    var v = norm(key, value);
    if (!v || !VALID[key]) return false;
    cache[key] = v;
    write(cache);
    paint();
    for (var i = 0; i < subs.length; i++) { try { subs[i](key, v); } catch (e) {} }
    return true;
  }

  /** Drop one stored choice (or all of them) and fall back to whatever each screen declares. */
  function clear(key) {
    if (key) delete cache[key]; else cache = {};
    write(cache);
    for (var i = 0; i < subs.length; i++) { try { subs[i](key || null, null); } catch (e) {} }
    return true;
  }

  /** True when the operator has overridden this screen's default — the menu shows a reset. */
  function isSet(key) { return key ? get(key) != null : Object.keys(cache).length > 0; }

  /**
   * Write stored choices onto the document. Called on load (before the DC mounts, so there is no
   * flash of the wrong theme) and again on every set(). Only keys the operator actually chose are
   * written — an unset key is left for the screen's own applyChrome to fill in.
   */
  function paint() {
    var html = global.document && global.document.documentElement;
    if (!html) return;
    var t = get("theme"), d = get("density"), e = get("environment");
    if (t) html.setAttribute("data-theme", t);
    if (e) html.setAttribute("data-environment", e);
    if (d && global.document.body) global.document.body.setAttribute("data-theme-density", d);
  }

  function subscribe(fn) {
    subs.push(fn);
    return function () { var i = subs.indexOf(fn); if (i >= 0) subs.splice(i, 1); };
  }

  global.MeridianPrefs = {
    KEY: KEY, VALID: VALID,
    get: get, set: set, clear: clear, isSet: isSet, resolve: resolve, paint: paint, subscribe: subscribe,
    theme: theme, density: density, environment: environment,
    all: function () { return { theme: get("theme"), density: get("density"), environment: get("environment") }; },
  };

  paint();
  if (global.document && global.document.readyState === "loading") {
    global.document.addEventListener("DOMContentLoaded", paint);
  }
})(typeof window !== "undefined" ? window : globalThis);
