// Meridian workspace layout — named, persisted arrangement for a workstation screen.
// 4.1.0: track A of session continuity. Arrangement only: size, collapse, visibility.
// Never filters, selection, period, or as-of (guidelines/SESSION_CONTINUITY.md).
import React from "react";

const Ctx = React.createContext(null);
const ARRANGEMENT_KEYS = ["size", "collapsed", "hidden"];
const storeKey = (screenId) => `mds.workspace-layout.${screenId}`;

function readStore(screenId, storage) {
  try {
    const store = storage || (typeof window !== "undefined" ? window.localStorage : null);
    const raw = store?.getItem(storeKey(screenId));
    return raw ? JSON.parse(raw) : null;
  } catch { return null; }
}
function writeStore(screenId, value, storage) {
  try {
    const store = storage || (typeof window !== "undefined" ? window.localStorage : null);
    store?.setItem(storeKey(screenId), JSON.stringify(value));
  } catch { /* private mode, quota — arrangement is never load-bearing */ }
}
/** Only arrangement survives a patch. A layout that carries data context makes a stale figure
 *  look intentional, so the provider filters rather than trusting callers. */
function arrangementOnly(patch) {
  const out = {};
  for (const key of ARRANGEMENT_KEYS) if (patch && patch[key] !== undefined) out[key] = patch[key];
  return out;
}
/** Accepts both published shapes: the `regions` prop's array of `{id, …}` and a `NamedLayout.regions`
 *  keyed record. Both are normative — the d.ts, the prompt and SESSION_CONTINUITY all publish the
 *  record form for presets — so the provider narrows rather than assuming one. */
function normalizeRegions(regions) {
  const out = {};
  if (!regions) return out;
  const list = Array.isArray(regions)
    ? regions
    : Object.entries(regions).map(([id, a]) => ({ id, ...(a || {}) }));
  for (const r of list) {
    if (!r || r.id == null) continue;
    out[r.id] = arrangementOnly({ size: r.size ?? 1, collapsed: !!r.collapsed, hidden: !!r.hidden });
  }
  return out;
}
function sameArrangement(a, b) {
  const keys = new Set([...Object.keys(a || {}), ...Object.keys(b || {})]);
  for (const id of keys) {
    const x = a?.[id] || {}, y = b?.[id] || {};
    for (const key of ARRANGEMENT_KEYS) {
      const xv = key === "size" ? Math.round((x[key] ?? 1) * 1000) : !!x[key];
      const yv = key === "size" ? Math.round((y[key] ?? 1) * 1000) : !!y[key];
      if (xv !== yv) return false;
    }
  }
  return true;
}

export function WorkspaceLayoutProvider({ screenId, regions, layouts, persist = true, storage, onChange, children }) {
  const declared = React.useMemo(() => normalizeRegions(regions), [JSON.stringify(regions || [])]);
  const presets = React.useMemo(() => {
    const list = [{ name: "Default", regions: declared }, ...(layouts || []).map((l) => ({ name: l.name, regions: { ...declared, ...normalizeRegions(l.regions) } }))];
    const seen = new Set();
    return list.filter((l) => (seen.has(l.name) ? false : (seen.add(l.name), true)));
  }, [declared, JSON.stringify(layouts || [])]);

  const initialState = () => {
    const saved = persist ? readStore(screenId, storage) : null;
    const base = presets.find((p) => p.name === saved?.active) || presets[0];
    return {
      active: saved?.custom?.[saved?.active] ? saved.active : base.name,
      current: { ...base.regions, ...(saved?.regions || {}) },
      saved: { ...base.regions, ...(saved?.saved || saved?.regions || {}) },
      custom: saved?.custom || {},
      restored: !!saved,
    };
  };
  const [state, setState] = React.useState(initialState);
  const [meta, setMeta] = React.useState({});
  const previousScreen = React.useRef(screenId);
  React.useLayoutEffect(() => {
    if (previousScreen.current === screenId) return;
    previousScreen.current = screenId;
    setState(initialState());
    setMeta({});
  }, [screenId]);

  const dirty = !sameArrangement(state.current, state.saved);

  const persistNow = React.useCallback((next) => {
    if (persist) writeStore(screenId, { active: next.active, regions: next.current, saved: next.saved, custom: next.custom }, storage);
    onChange?.({ screenId, active: next.active, regions: next.current, dirty: !sameArrangement(next.current, next.saved) });
  }, [persist, screenId, storage, onChange]);

  const commit = React.useCallback((updater) => {
    setState((prev) => { const next = typeof updater === "function" ? updater(prev) : updater; persistNow(next); return next; });
  }, [persistNow]);

  const api = React.useMemo(() => ({
    screenId,
    active: state.active,
    dirty,
    restored: state.restored,
    names: [...new Set([...presets.map((p) => p.name), ...Object.keys(state.custom)])],
    regions: state.current,
    meta,
    region: (id) => ({ size: 1, collapsed: false, hidden: false, ...(state.current[id] || {}) }),
    /** Regions announce themselves so the bar can list what exists without prop drilling. */
    declare: (id, info) => setMeta((prev) => (prev[id] && prev[id].label === info.label && prev[id].minSize === info.minSize ? prev : { ...prev, [id]: info })),
    setRegion: (id, patch) => commit((prev) => ({ ...prev, current: { ...prev.current, [id]: { ...prev.current[id], ...arrangementOnly(typeof patch === "function" ? patch(prev.current[id] || {}) : patch) } } })),
    toggleCollapse: (id) => commit((prev) => ({ ...prev, current: { ...prev.current, [id]: { ...prev.current[id], collapsed: !prev.current[id]?.collapsed } } })),
    select: (name) => commit((prev) => {
      const found = prev.custom[name] ? { name, regions: prev.custom[name] } : presets.find((p) => p.name === name);
      if (!found) return prev;
      return { ...prev, active: found.name, current: { ...found.regions }, saved: { ...found.regions } };
    }),
    /** Deliberate save — Meridian never silently overwrites a named arrangement. */
    save: (name) => commit((prev) => {
      const target = name || prev.active;
      const isPreset = presets.some((p) => p.name === target);
      return { ...prev, active: target, saved: { ...prev.current }, custom: isPreset && target === prev.active ? { ...prev.custom, [target]: { ...prev.current } } : { ...prev.custom, [target]: { ...prev.current } } };
    }),
    reset: () => commit((prev) => ({ ...prev, current: { ...prev.saved } })),
    resetToDefault: () => commit((prev) => ({ ...prev, active: presets[0].name, current: { ...presets[0].regions }, saved: { ...presets[0].regions } })),
    remove: (name) => commit((prev) => { if (!prev.custom[name]) return prev; const custom = { ...prev.custom }; delete custom[name]; const fallback = presets[0]; return { ...prev, custom, ...(prev.active === name ? { active: fallback.name, current: { ...fallback.regions }, saved: { ...fallback.regions } } : {}) }; }),
  }), [screenId, state, dirty, presets, meta, commit]);

  return <Ctx.Provider value={api}>{children}</Ctx.Provider>;
}

export function useWorkspaceLayout() {
  const value = React.useContext(Ctx);
  if (!value) throw new Error("useWorkspaceLayout must be used inside WorkspaceLayoutProvider");
  return value;
}
/** Bundle carrier — the compiler exposes only capitalized exports, so consumers of
 *  `_ds_bundle.js` reach the hook through this object. */
export const LayoutHooks = { useWorkspaceLayout };

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "workspace-layout");
  el.textContent = `
.mds-wlay{min-height:30px;display:flex;align-items:center;gap:8px;padding:0 10px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--bg-medium,#EBE8E1);font-family:var(--font-body);color:var(--text-secondary,#535C65)}
.mds-wlay__label{font:600 9px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-wlay__select{height:24px;border:1px solid var(--border,#D2CDC3);border-radius:2px;background:var(--bg-light,#F8F7F3);color:var(--text-primary,#20252B);font:600 11px var(--font-body);padding:0 4px}
.mds-wlay__select:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-wlay__dirty{display:inline-flex;align-items:center;gap:5px;font:600 9.5px var(--font-data,monospace);letter-spacing:.04em;text-transform:uppercase;color:var(--pending,#7A6B52)}
.mds-wlay__dirty::before{content:"";width:5px;height:5px;border-radius:50%;background:var(--pending,#7A6B52)}
.mds-wlay__btn{height:24px;padding:0 8px;border:1px solid var(--border,#D2CDC3);border-radius:2px;background:var(--bg-light,#F8F7F3);color:var(--text-secondary,#535C65);font:600 10.5px var(--font-body);cursor:pointer}
.mds-wlay__btn:hover:not(:disabled){background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B)}
.mds-wlay__btn:disabled{opacity:.45;cursor:default}
.mds-wlay__btn:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px}
.mds-wlay__spacer{flex:1}
.mds-wlay__note{font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-lgrid{display:flex;min-height:0;min-width:0;width:100%;height:100%;align-items:stretch}
.mds-lgrid--column{flex-direction:column}
.mds-lreg{position:relative;display:flex;flex-direction:column;min-width:0;min-height:0;overflow:hidden;background:var(--bg-light,#F8F7F3);border:1px solid var(--border,#D2CDC3)}
.mds-lreg__head{display:flex;align-items:center;gap:6px;min-height:26px;padding:0 6px 0 9px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--bg-medium,#EBE8E1)}
.mds-lreg__title{font:600 10px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.mds-lreg__acts{margin-left:auto;display:flex;align-items:center;gap:2px}
.mds-lreg__icon{width:24px;height:24px;display:grid;place-items:center;border:0;border-radius:2px;background:transparent;color:var(--text-muted,#606870);font-family:var(--font-data,monospace);font-size:11px;line-height:1;cursor:pointer}
.mds-lreg__icon:hover{background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B)}
.mds-lreg__icon:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-lreg__body{flex:1;min-height:0;min-width:0;overflow:auto}
.mds-lreg__rail{width:100%;height:100%;display:flex;align-items:center;justify-content:flex-start;gap:8px;padding:8px 0;border:0;background:transparent;color:var(--text-muted,#606870);cursor:pointer;writing-mode:vertical-rl;font:600 10px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase}
.mds-lreg--collapsed.mds-lreg--h .mds-lreg__rail{writing-mode:horizontal-tb;justify-content:center}
.mds-lreg__rail:hover{background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B)}
.mds-lreg__rail:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-ldiv{flex:0 0 7px;position:relative;z-index:1;box-sizing:content-box;border:0;padding:0 9px;margin:0 -9px;background:transparent;cursor:col-resize}
.mds-lgrid--column>.mds-ldiv{flex:0 0 7px;padding:9px 0;margin:-9px 0;cursor:row-resize}
.mds-ldiv::after{content:"";position:absolute;inset:2px 12px;background:var(--border-divider,#E2DED5);border-radius:1px}
.mds-lgrid--column>.mds-ldiv::after{inset:12px 2px}
.mds-ldiv:hover::after,.mds-ldiv[data-dragging="true"]::after{background:var(--focus,#2D6FB3)}
.mds-ldiv:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-1px}
@media print{.mds-wlay,.mds-ldiv{display:none}.mds-lgrid{display:block}.mds-lreg{break-inside:avoid;margin-bottom:8px}}
`;
  document.head.appendChild(el);
}

export function WorkspaceLayoutBar({ actions, showRestored = true, saveLabel = "Save", resetLabel = "Reset", className = "", ...rest }) {
  inject();
  const api = React.useContext(Ctx);
  if (!api) return null;
  const hiddenIds = Object.entries(api.regions).filter(([, r]) => r && r.hidden).map(([id]) => id);
  const hidden = hiddenIds.length;
  return <div className={`mds-wlay${className ? " " + className : ""}`} role="group" aria-label="Workspace layout" {...rest}>
    <span className="mds-wlay__label">Layout</span>
    <select className="mds-wlay__select" aria-label="Active workspace layout" value={api.active} onChange={(e) => { if (e.target.value === "__new") { const name = window.prompt("Name this layout"); if (name) { api.save(name); } } else api.select(e.target.value); }}>
      {api.names.map((name) => <option key={name} value={name}>{name}</option>)}
      <option value="__new">Save as…</option>
    </select>
    {api.dirty ? <span className="mds-wlay__dirty" role="status">Modified</span> : null}
    <button type="button" className="mds-wlay__btn" disabled={!api.dirty} aria-label={`${saveLabel} layout ${api.active}`} onClick={() => api.save()}>{saveLabel}</button>
    <button type="button" className="mds-wlay__btn" disabled={!api.dirty} aria-label={`${resetLabel} layout ${api.active} to its saved arrangement`} onClick={() => api.reset()}>{resetLabel}</button>
    <span className="mds-wlay__spacer" />
    {hidden ? <button type="button" className="mds-wlay__btn"
      aria-label={`Show ${hidden} hidden region${hidden === 1 ? "" : "s"}: ${hiddenIds.map((id) => api.meta[id]?.label || id).join(", ")}`}
      onClick={() => hiddenIds.forEach((id) => api.setRegion(id, { hidden: false }))}>
      Show {hidden} hidden</button> : null}
    {showRestored && api.restored && !api.dirty ? <span className="mds-wlay__note">Restored · {api.active}</span> : null}
    {actions}
  </div>;
}

const GridCtx = React.createContext({ direction: "row" });

export function WorkspaceLayoutGrid({ direction = "row", gap = 8, slots, children, className = "", style, ...rest }) {
  inject();
  const api = React.useContext(Ctx);
  const ref = React.useRef(null);
  // `slots` lets markup-first callers (DC templates, plain children) declare regions positionally:
  // each child is wrapped in its own LayoutRegion, so chrome, collapse, sizing and the keyboard
  // dividers all behave as if the caller had written the regions by hand.
  const raw = React.Children.toArray(children).filter(Boolean);
  const kids = slots && slots.length
    ? raw.map((child, i) => {
        const slot = slots[i] || {};
        const id = slot.id || `region-${i}`;
        return <LayoutRegion key={id} id={id} label={slot.label} actions={slot.actions}
          minSize={slot.minSize} collapsible={slot.collapsible !== false} chrome={slot.chrome !== false}
          onPopOut={slot.onPopOut}>{child}</LayoutRegion>;
      })
    : raw;
  const drag = React.useRef(null);

  const onDown = (index) => (event) => {
    if (!api || !ref.current) return;
    const before = kids[index]?.props?.id, after = kids[index + 1]?.props?.id;
    if (!before || !after) return;
    const rect = ref.current.getBoundingClientRect();
    const extent = direction === "column" ? rect.height : rect.width;
    drag.current = { before, after, extent, start: direction === "column" ? event.clientY : event.clientX, sizes: [api.region(before).size, api.region(after).size] };
    event.currentTarget.dataset.dragging = "true";
    event.currentTarget.setPointerCapture?.(event.pointerId);
  };
  const onMove = (event) => {
    const d = drag.current;
    if (!d || !api) return;
    const pos = direction === "column" ? event.clientY : event.clientX;
    const total = d.sizes[0] + d.sizes[1];
    const share = ((pos - d.start) / Math.max(d.extent, 1)) * (kids.length ? kids.filter((k) => !api.region(k.props.id).collapsed).reduce((sum, k) => sum + api.region(k.props.id).size, 0) : total);
    const minBefore = api.meta[d.before]?.minSize ?? 0.08, minAfter = api.meta[d.after]?.minSize ?? 0.08;
    const nextBefore = Math.min(Math.max(d.sizes[0] + share, minBefore), total - minAfter);
    api.setRegion(d.before, { size: nextBefore });
    api.setRegion(d.after, { size: total - nextBefore });
  };
  const onUp = (event) => { drag.current = null; if (event.currentTarget) event.currentTarget.dataset.dragging = "false"; };
  const nudge = (index, delta) => {
    if (!api) return;
    const before = kids[index]?.props?.id, after = kids[index + 1]?.props?.id;
    if (!before || !after) return;
    const a = api.region(before).size, b = api.region(after).size, total = a + b;
    const step = total * 0.04 * delta;
    const minBefore = api.meta[before]?.minSize ?? 0.08, minAfter = api.meta[after]?.minSize ?? 0.08;
    const nextBefore = Math.min(Math.max(a + step, minBefore), total - minAfter);
    api.setRegion(before, { size: nextBefore });
    api.setRegion(after, { size: total - nextBefore });
  };

  const out = [];
  kids.forEach((child, i) => {
    out.push(child);
    const next = kids[i + 1];
    if (!next) return;
    const beforeCollapsed = api ? api.region(child.props.id).collapsed || api.region(child.props.id).hidden : false;
    const afterCollapsed = api ? api.region(next.props.id).collapsed || api.region(next.props.id).hidden : false;
    if (beforeCollapsed || afterCollapsed) return;
    const pct = api ? Math.round((api.region(child.props.id).size / (api.region(child.props.id).size + api.region(next.props.id).size)) * 100) : 50;
    out.push(<div key={`div-${i}`} className="mds-ldiv" role="separator" tabIndex={0}
      aria-orientation={direction === "column" ? "horizontal" : "vertical"}
      aria-label={`Resize ${api?.meta[child.props.id]?.label || child.props.id}`}
      aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100}
      aria-valuetext={`${pct}% ${api?.meta[child.props.id]?.label || child.props.id}, ${100 - pct}% ${api?.meta[next.props.id]?.label || next.props.id}`}
      onPointerDown={onDown(i)} onPointerMove={onMove} onPointerUp={onUp} onPointerCancel={onUp}
      onKeyDown={(e) => {
        const dec = direction === "column" ? "ArrowUp" : "ArrowLeft", inc = direction === "column" ? "ArrowDown" : "ArrowRight";
        if (e.key === dec) { e.preventDefault(); nudge(i, -1); }
        if (e.key === inc) { e.preventDefault(); nudge(i, 1); }
      }} />);
  });

  return <GridCtx.Provider value={{ direction }}>
    <div ref={ref} className={`mds-lgrid${direction === "column" ? " mds-lgrid--column" : ""}${className ? " " + className : ""}`} style={{ gap, ...style }} {...rest}>{out}</div>
  </GridCtx.Provider>;
}

/** Convenience composite: provider → bar → grid. For a screen that wants the whole obligation in
 *  one tag. Compose the three parts directly when the bar belongs elsewhere in the chrome. */
export function WorkspaceLayout({ screenId, regions, layouts, persist, storage, onChange, direction = "row", gap, bar = true, barActions, children, ...rest }) {
  return <WorkspaceLayoutProvider screenId={screenId} regions={regions} layouts={layouts} persist={persist} storage={storage} onChange={onChange}>
    {bar ? <WorkspaceLayoutBar actions={barActions} /> : null}
    <WorkspaceLayoutGrid direction={direction} gap={gap} {...rest}>{children}</WorkspaceLayoutGrid>
  </WorkspaceLayoutProvider>;
}

export function LayoutRegion({ id, label, actions, collapsible = true, chrome = true, onPopOut, minSize = 0.1, size, children, className = "", style, ...rest }) {
  inject();
  const api = React.useContext(Ctx);
  const { direction } = React.useContext(GridCtx);
  React.useEffect(() => { api?.declare(id, { label: label || id, minSize }); }, [api, id, label, minSize]);
  const state = api ? api.region(id) : { size: size ?? 1, collapsed: false, hidden: false };
  const collapsed = !!state.collapsed;
  // Focus must never fall to <body> when the region a user is working in collapses (law rule 5,
  // and the one focus defect the 4.1 static audit could not close). Collapsing removes the body
  // from the DOM; if focus was inside it, the labelled rail that replaces it receives focus.
  const sectionRef = React.useRef(null), focusWasInside = React.useRef(false);
  const rememberFocus = () => { focusWasInside.current = !!(sectionRef.current && sectionRef.current.contains(document.activeElement)); };
  React.useEffect(() => {
    if (!focusWasInside.current) return;
    focusWasInside.current = false;
    const target = sectionRef.current && (collapsed ? sectionRef.current.querySelector(".mds-lreg__rail") : sectionRef.current.querySelector("[aria-label^='Collapse']") || sectionRef.current.querySelector(".mds-lreg__body"));
    if (target && document.activeElement === document.body) target.focus();
  }, [collapsed]);
  if (state.hidden) return null;
  const horizontal = direction === "row";
  const flex = collapsed ? "0 0 auto" : `${state.size ?? 1} 1 0%`;
  const collapsedStyle = collapsed ? (horizontal ? { width: 30 } : { height: 28 }) : null;
  const bodyId = `mds-lreg-${api?.screenId || "screen"}-${id}`;
  // A collapsed region is a 30px rail, not a flex participant. The component's own flex and extent
  // therefore win over a consumer `style` by ORDER when collapsed — this used to be a
  // `.mds-lreg--collapsed{flex:0 0 auto!important}` stylesheet rule, which is the same intent
  // stated where specificity could not be reasoned about (guidelines/CONFORMANCE.md › C-14).
  return <section className={`mds-lreg${collapsed ? " mds-lreg--collapsed" : ""}${collapsed && !horizontal ? " mds-lreg--h" : ""}${className ? " " + className : ""}`}
    ref={sectionRef} style={collapsed ? { ...style, flex, ...collapsedStyle } : { flex, ...style }} aria-label={label || id} data-region={id} {...rest}>
    {collapsed
      ? <button type="button" className="mds-lreg__rail" onClick={() => { rememberFocus(); api?.toggleCollapse(id); }}
          aria-expanded="false" aria-controls={bodyId} aria-label={`Expand ${label || id}`}>{label || id}</button>
      : <>
          {chrome ? <header className="mds-lreg__head">
            <span className="mds-lreg__title">{label || id}</span>
            <span className="mds-lreg__acts">
              {actions}
              {onPopOut ? <button type="button" className="mds-lreg__icon" title="Open in its own window" aria-label={`Open ${label || id} in its own window`} onClick={() => onPopOut(id)}>⧉</button> : null}
              {collapsible ? <button type="button" className="mds-lreg__icon" title="Collapse" aria-label={`Collapse ${label || id}`} aria-expanded="true" aria-controls={bodyId} onClick={() => { rememberFocus(); api?.toggleCollapse(id); }}>{horizontal ? "‹" : "⌃"}</button> : null}
            </span>
          </header> : null}
          <div className="mds-lreg__body" id={bodyId} tabIndex={-1}>{children}</div>
        </>}
  </section>;
}
