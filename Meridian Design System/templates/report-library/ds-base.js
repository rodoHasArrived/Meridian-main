// Meridian template loader — CANONICAL SOURCE.
// scripts/sync_template_scaffold.py copies this file (and the current support.js runtime) into
// every templates/<slug>/ folder. Edit here, re-run the script; never edit a per-template copy.
//
// In a consuming project, point `base` at the bound design-system folder relative to this file
// (e.g. '_ds/<folder>' at the project root, '../_ds/<folder>' one level down) — one line to edit.
//
// Beyond loading the theme and the bundle, this file owns the chrome the SYSTEM owns rather than
// any one screen (docs/SUITE_COHESION_4_4.md): the environment badge, the live wall clock, and the
// masthead search field. Screens write nothing for any of them.
(() => {
  const base = '../..';
  const scaffold = '../_scaffold';

  // styles.css @imports the whole token closure — fonts, colors, dark mode, contrast modes,
  // print. One link; a template can never end up with a partial theme.
  for (const p of ['styles.css']) {
    const l = document.createElement('link');
    l.rel = 'stylesheet';
    l.href = base + '/' + p;
    document.head.appendChild(l);
  }

  // The suite spine. prefs.js is first: it paints the operator's stored theme/density/environment
  // onto <html> before anything renders, so navigating between screens never flashes the wrong
  // chrome. app-map.js is the one address book (every rail, every route, every object type);
  // world.js is the one clock, calendar, operator and entity table. Loaded here so every screen
  // has them with no per-screen <script> — order preserved with async=false.
  // books.js is the one scoped accounting read model (increment B) and instructions.js is the
  // pending-instruction store that outlives a screen — both spine, both loaded once, here.
  for (const p of ['prefs.js', 'world.js', 'app-map.js', 'books.js', 'instructions.js']) {
    const s = document.createElement('script');
    s.src = scaffold + '/' + p;
    s.async = false;
    s.onerror = () => console.warn('ds-base.js: ' + p + ' not found at ' + scaffold + ' — copy templates/_scaffold/ alongside this template');
    document.head.appendChild(s);
  }

  const s = document.createElement('script');
  s.src = base + '/_ds_bundle.js';
  s.async = false;
  s.onerror = () => console.error('ds-base.js: failed to load ' + s.src + ' — if this is a consuming project, point the base line in ds-base.js at the bound _ds/<folder> tree relative to this page (e.g. _ds/<folder> at the project root, ../_ds/<folder> one level down); in a fresh design system this can just mean the bundle is not compiled yet');
  s.onload = () => {
    // Graceful degradation: templates wrap their work plane in DataStateSwitch. Against an older
    // bundle that predates it, fall back to a pass-through so the screen still renders.
    const ns = window.MeridianDesignSystem_205d17;
    if (ns && !ns.DataStateSwitch) {
      console.warn('ds-base.js: bundle has no DataStateSwitch — rendering states as pass-through');
      ns.DataStateSwitch = (p) => (window.React
        ? window.React.createElement(window.React.Fragment, null, p.children)
        : p.children);
    }
    mountSearch();
  };
  document.head.appendChild(s);

  // ── Environment badge — chrome the system owns, not the screen ───────────────
  // The contract requires a visible Live / Paper / Fixture badge on every surface. Screens built
  // on WorkstationTopbar already render one (it carries [data-environment-badge]); anything else
  // gets this fallback, driven by <html data-environment="…"> so the template tweak moves it.
  const MODES = ['LIVE', 'PAPER', 'FIXTURE'];
  const FILL = { LIVE: 'var(--mode-live)', PAPER: 'var(--mode-paper)', FIXTURE: 'var(--mode-fixture)' };
  const OWN = 'data-environment-badge-fallback';
  function syncEnvBadge() {
    if (!document.body) return;
    let env = String(document.documentElement.getAttribute('data-environment') || 'PAPER').toUpperCase();
    // An environment outside the mode set is a contract violation, not a fourth mode — say so
    // once and fall back, rather than rendering a silently-neutral badge.
    if (!MODES.includes(env)) {
      if (!syncEnvBadge._warned) { syncEnvBadge._warned = env; console.warn('ds-base.js: data-environment="' + env + '" is not one of LIVE / PAPER / FIXTURE — falling back to PAPER'); }
      env = 'PAPER';
    }
    const own = document.querySelector('[' + OWN + ']');
    const theirs = document.querySelector('[data-environment-badge]:not([' + OWN + '])');
    if (theirs) { if (own) own.remove(); return; }
    const el = own || document.body.appendChild(document.createElement('div'));
    el.setAttribute('data-environment-badge', '');
    el.setAttribute(OWN, '');
    el.textContent = env;
    el.style.cssText = 'position:fixed;top:9px;right:12px;z-index:2147482000;padding:3px 9px;'
      + 'border-radius:var(--radius-chip,3px);font:700 10px/1.5 var(--font-data,monospace);'
      + 'letter-spacing:.09em;color:var(--text-on-fill,#fff);pointer-events:none;'
      + 'box-shadow:var(--shadow-float);background:' + (FILL[env] || FILL.PAPER);
  }

  // ── The wall clock ───────────────────────────────────────────────────────────
  // Owned by WorkstationTopbar, which subscribes to MeridianWorld itself — the loader must NOT
  // also write into .ws-status, or React and the DOM fight over one node and the clock flickers.
  // Screens with their own masthead call MeridianWorld.tick(this) in componentDidMount.

  // ── The masthead search field, made real ─────────────────────────────────────
  // WorkstationTopbar's search button had no handler on any screen in the suite. It opens
  // GlobalSearch: the current screen's objects (published by the operator layer) plus every
  // destination in the app map. Screens write nothing.
  let searchApi = null;
  function mountSearch() {
    const ns = window.MeridianDesignSystem_205d17;
    const React = window.React, ReactDOM = window.ReactDOM;
    if (!ns || !ns.GlobalSearch || !React || !ReactDOM || searchApi) return;
    const host = document.createElement('div');
    host.setAttribute('data-meridian-search', '');
    (document.body || document.documentElement).appendChild(host);

    let setOpen = null;
    function Host() {
      const [open, _setOpen] = React.useState(false);
      setOpen = _setOpen;
      const layer = window.MeridianOperatorLayer && window.MeridianOperatorLayer.current;
      const M = window.MeridianAppMap;
      const destinations = M ? M.SCREENS.filter((sc) => !sc.hidden).map((sc) => ({
        id: 'go-' + sc.id, label: sc.label, meta: sc.shortcut || '',
        run: () => M.go(sc.id),
      })) : [];
      return React.createElement(ns.GlobalSearch, {
        open, onClose: () => _setOpen(false),
        resolve: layer && layer.resolve,
        destinations,
        scopeLabel: layer && layer.resolve ? (layer.screenId || 'This screen') : 'Destinations only',
        emptyHint: layer && layer.resolve ? undefined
          : 'This screen publishes no objects yet — add an `objects` list to its operatorLayer() and they become searchable here.',
      });
    }
    ReactDOM.createRoot
      ? ReactDOM.createRoot(host).render(React.createElement(Host))
      : ReactDOM.render(React.createElement(Host), host);
    searchApi = { open: () => setOpen && setOpen(true), close: () => setOpen && setOpen(false) };
    window.MeridianSearch = searchApi;

    // The masthead button, and "/" — the binding the topbar now advertises.
    document.addEventListener('click', (e) => {
      const btn = e.target && e.target.closest && e.target.closest('.ws-search');
      if (btn) { e.preventDefault(); searchApi.open(); }
    });
    document.addEventListener('keydown', (e) => {
      if (e.key !== '/' || e.metaKey || e.ctrlKey || e.altKey) return;
      const t = e.target, tag = t && t.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (t && t.isContentEditable)) return;
      e.preventDefault();
      searchApi.open();
    });
  }

  const tick = () => { syncEnvBadge(); };
  const kick = () => { tick(); [120, 500, 1500].forEach((t) => setTimeout(tick, t)); mountSearch(); };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', kick);
  else kick();
  setInterval(tick, 1000);
  new MutationObserver(syncEnvBadge).observe(document.documentElement, {
    attributes: true, attributeFilter: ['data-environment', 'data-theme'],
  });
})();
