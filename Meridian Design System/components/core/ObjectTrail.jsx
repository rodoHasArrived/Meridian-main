// Meridian object trail — per-operator traversal across the seventeen object types.
// 4.1.0: track A of session continuity. Entries are references with the version viewed,
// never snapshots; returning to a moved version is a navigation with a notice.
import React from "react";
import { objectMeta } from "./ObjectModel";

const Ctx = React.createContext(null);
const storeKey = (screenId) => `mds.object-trail.${screenId}`;
export function trailRef(entry) { return `${entry?.type || "object"}:${entry?.id ?? entry?.label ?? ""}`; }
/** An entry is stale when the version it was viewed at is no longer the current one. */
export function trailMoved(entry) {
  if (!entry || entry.version == null || entry.currentVersion == null) return false;
  return String(entry.version) !== String(entry.currentVersion);
}
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
  } catch { /* traversal is never load-bearing */ }
}

export function ObjectTrailProvider({ screenId = "global", max = 12, persist = true, storage, defaultEntries, defaultPinned, onNavigate, children }) {
  const initialState = () => {
    const saved = persist ? readStore(screenId, storage) : null;
    const recents = saved?.recents || defaultEntries || [];
    return { stack: recents.length ? [recents[0]] : [], index: recents.length ? 0 : -1, recents, pinned: saved?.pinned || defaultPinned || [], notice: null };
  };
  const [state, setState] = React.useState(initialState);
  const previousScreen = React.useRef(screenId);
  React.useLayoutEffect(() => {
    if (previousScreen.current === screenId) return;
    previousScreen.current = screenId;
    setState(initialState());
  }, [screenId]);

  const persistNow = React.useCallback((next) => {
    if (persist) writeStore(screenId, { recents: next.recents.slice(0, max), pinned: next.pinned }, storage);
  }, [persist, screenId, storage, max]);

  const commit = React.useCallback((updater) => {
    setState((prev) => { const next = typeof updater === "function" ? updater(prev) : updater; persistNow(next); return next; });
  }, [persistNow]);

  const navigate = React.useCallback((entry, detail) => {
    const moved = trailMoved(entry);
    onNavigate?.(entry, { ...detail, versionMoved: moved });
    return moved ? { type: "version-moved", entry } : null;
  }, [onNavigate]);

  const api = React.useMemo(() => ({
    screenId,
    current: state.index >= 0 ? state.stack[state.index] : null,
    stack: state.stack,
    recents: state.recents,
    pinned: state.pinned,
    canBack: state.index > 0,
    canForward: state.index >= 0 && state.index < state.stack.length - 1,
    notice: state.notice,
    moved: trailMoved,
    refOf: trailRef,
    isPinned: (entry) => state.pinned.some((p) => trailRef(p) === trailRef(entry)),
    /** Record an arrival. Called by the screen, not by the trail. */
    visit: (entry) => commit((prev) => {
      if (!entry) return prev;
      const key = trailRef(entry);
      if (prev.index >= 0 && trailRef(prev.stack[prev.index]) === key) return prev;
      const stack = [...prev.stack.slice(0, prev.index + 1), entry].slice(-40);
      return { ...prev, stack, index: stack.length - 1, recents: [entry, ...prev.recents.filter((r) => trailRef(r) !== key)].slice(0, max), notice: null };
    }),
    back: () => commit((prev) => {
      if (prev.index <= 0) return prev;
      const entry = prev.stack[prev.index - 1];
      return { ...prev, index: prev.index - 1, notice: navigate(entry, { direction: "back" }) };
    }),
    forward: () => commit((prev) => {
      if (prev.index >= prev.stack.length - 1) return prev;
      const entry = prev.stack[prev.index + 1];
      return { ...prev, index: prev.index + 1, notice: navigate(entry, { direction: "forward" }) };
    }),
    /** Jump to a recent or pinned entry. Pushes onto the stack — history is not rewritten. */
    go: (entry) => commit((prev) => {
      const key = trailRef(entry);
      const stack = [...prev.stack.slice(0, prev.index + 1), entry];
      return { ...prev, stack, index: stack.length - 1, recents: [entry, ...prev.recents.filter((r) => trailRef(r) !== key)].slice(0, max), notice: navigate(entry, { direction: "jump" }) };
    }),
    pin: (entry) => commit((prev) => (prev.pinned.some((p) => trailRef(p) === trailRef(entry)) ? prev : { ...prev, pinned: [...prev.pinned, entry] })),
    unpin: (entry) => commit((prev) => ({ ...prev, pinned: prev.pinned.filter((p) => trailRef(p) !== trailRef(entry)) })),
    togglePin: (entry) => commit((prev) => (prev.pinned.some((p) => trailRef(p) === trailRef(entry))
      ? { ...prev, pinned: prev.pinned.filter((p) => trailRef(p) !== trailRef(entry)) }
      : { ...prev, pinned: [...prev.pinned, entry] })),
    clearRecents: () => commit((prev) => ({ ...prev, recents: prev.pinned.length ? prev.recents.filter((r) => prev.pinned.some((p) => trailRef(p) === trailRef(r))) : [] })),
    dismissNotice: () => setState((prev) => ({ ...prev, notice: null })),
  }), [screenId, state, commit, navigate, max]);

  return <Ctx.Provider value={api}>{children}</Ctx.Provider>;
}

export function useObjectTrail() {
  const value = React.useContext(Ctx);
  if (!value) throw new Error("useObjectTrail must be used inside ObjectTrailProvider");
  return value;
}
/** Bundle carrier — the compiler exposes only capitalized exports. */
export const TrailHooks = { useObjectTrail, trailRef, trailMoved };

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "object-trail");
  el.textContent = `
.mds-trail{position:relative;display:flex;align-items:stretch;min-height:30px;border-bottom:1px solid var(--border-divider,#E2DED5);background:var(--bg-light,#F8F7F3);font-family:var(--font-body)}
.mds-trail__nav{width:26px;border:0;border-right:1px solid var(--border-divider,#E2DED5);background:transparent;color:var(--text-secondary,#535C65);font-family:var(--font-data,monospace);font-size:12px;cursor:pointer}
.mds-trail__nav:hover:not(:disabled){background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B)}
.mds-trail__nav:disabled{color:var(--border-strong,#AAA49A);cursor:default}
.mds-trail__nav:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-trail__current{display:flex;align-items:center;gap:7px;padding:0 10px;min-width:0}
.mds-trail__type{font:600 9px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870);white-space:nowrap}
.mds-trail__label{font-size:12px;font-weight:600;color:var(--text-primary,#20252B);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.mds-trail__ver{font:10px var(--font-data,monospace);color:var(--text-muted,#606870);white-space:nowrap}
.mds-trail__spacer{flex:1;min-width:8px}
.mds-trail__pins{display:flex;align-items:center;gap:0;border-left:1px solid var(--border-divider,#E2DED5)}
.mds-trail__pin{display:flex;align-items:center;gap:5px;max-width:190px;padding:0 9px;height:100%;border:0;border-right:1px solid var(--border-divider,#E2DED5);background:transparent;font-family:inherit;font-size:11.5px;color:var(--text-secondary,#535C65);cursor:pointer;white-space:nowrap;overflow:hidden}
.mds-trail__pin:hover{background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B)}
.mds-trail__pin:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-trail__pin[data-active="true"]{background:var(--selected-surface,#E7EDF2);color:var(--text-primary,#20252B);box-shadow:inset 2px 0 0 var(--selected,#496C8F)}
.mds-trail__pin em{font-style:normal;font:600 9px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-trail__star{min-width:24px;min-height:24px;border:0;background:transparent;color:var(--text-muted,#606870);font-size:11px;line-height:1;cursor:pointer}
.mds-trail__star[data-on="true"]{color:var(--accent,#C56343)}
.mds-trail__star:hover{background:var(--bg-hover,#EDEFF0)}
.mds-trail__star:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-trail__more{display:flex;align-items:center;gap:5px;padding:0 10px;border:0;border-left:1px solid var(--border-divider,#E2DED5);background:transparent;color:var(--text-secondary,#535C65);font:600 10px var(--font-data,monospace);letter-spacing:.04em;text-transform:uppercase;cursor:pointer;white-space:nowrap}
.mds-trail__more:hover{background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B)}
.mds-trail__more[aria-expanded="true"]{background:var(--selected-surface,#E7EDF2);color:var(--text-primary,#20252B)}
.mds-trail__more:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-trail__panel{position:absolute;top:100%;right:0;z-index:45;width:340px;max-height:340px;overflow:auto;border:1px solid var(--border-strong,#AAA49A);border-radius:var(--radius-overlay,6px);background:var(--surface-overlay,#fff);box-shadow:var(--shadow-menu,0 2px 6px rgba(17,23,29,.18))}
.mds-trail__group{font:600 9px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870);padding:7px 10px 4px;border-bottom:1px solid var(--border-divider,#E2DED5);display:flex;justify-content:space-between;align-items:baseline}
.mds-trail__clear{border:0;background:transparent;color:var(--link,#2A5F96);font:inherit;text-transform:none;letter-spacing:0;cursor:pointer}
.mds-trail__row{width:100%;display:grid;grid-template-columns:1fr auto;gap:2px 8px;align-items:baseline;padding:6px 10px;border:0;border-bottom:1px solid var(--border-divider,#E2DED5);background:transparent;text-align:left;font-family:inherit;cursor:pointer}
.mds-trail__row:hover{background:var(--bg-hover,#EDEFF0)}
.mds-trail__row:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-trail__row b{font-size:12px;font-weight:600;color:var(--text-primary,#20252B);overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.mds-trail__row .mds-trail__meta{grid-column:1/-1;font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-trail__moved{grid-column:1/-1;font:600 9.5px var(--font-data,monospace);letter-spacing:.03em;text-transform:uppercase;color:var(--severity-action-fg,#724D11)}
.mds-trail__notice{display:flex;align-items:center;gap:8px;padding:5px 10px;border-bottom:1px solid var(--severity-action-bd,#986717);background:var(--severity-action-bg,#986717);color:var(--text-primary,#20252B);font-size:11.5px}
.mds-trail__notice strong{font:600 9.5px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--severity-action-fg,#724D11)}
.mds-trail__notice button{margin-left:auto;border:0;background:transparent;color:var(--link,#2A5F96);font:inherit;cursor:pointer}
.mds-tlist{font-family:var(--font-body)}
.mds-tlist__head{display:flex;justify-content:space-between;align-items:baseline;font:600 9px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870);padding:0 0 5px;border-bottom:1px solid var(--border-strong,#AAA49A)}
.mds-tlist__row{width:100%;display:grid;grid-template-columns:1fr auto;gap:2px 8px;align-items:baseline;padding:7px 2px;border:0;border-bottom:1px solid var(--border-divider,#E2DED5);background:transparent;text-align:left;font-family:inherit;cursor:pointer}
.mds-tlist__row:hover{background:var(--bg-hover,#EDEFF0)}
.mds-tlist__row:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px}
.mds-tlist__row b{font-size:12.5px;font-weight:600;color:var(--text-primary,#20252B)}
.mds-tlist__row em{font-style:normal;font:600 9px var(--font-data,monospace);letter-spacing:.05em;text-transform:uppercase;color:var(--text-muted,#606870)}
.mds-tlist__row .t{font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-tlist__empty{padding:10px 2px;font-size:11.5px;color:var(--text-muted,#606870)}
@media print{.mds-trail{display:none}}
`;
  document.head.appendChild(el);
}

function typeLabel(entry) { return entry?.typeLabel || objectMeta(entry?.type).label; }

function Row({ entry, api, className, onPick }) {
  const moved = trailMoved(entry);
  return <button type="button" className={className} onClick={() => { api.go(entry); onPick?.(entry); }}>
    <b>{entry.label ?? entry.id}</b>
    <em>{typeLabel(entry)}</em>
    {entry.meta || entry.at ? <span className="mds-trail__meta">{[entry.meta, entry.at].filter(Boolean).join(" · ")}</span> : null}
    {moved ? <span className="mds-trail__moved">Version moved · viewed v{entry.version} → now v{entry.currentVersion}</span> : null}
  </button>;
}

export function ObjectTrail({ label = "Trail", showPinned = true, maxPinned = 4, recentsLabel = "Recent", className = "", ...rest }) {
  inject();
  const api = React.useContext(Ctx);
  const [open, setOpen] = React.useState(false);
  const ref = React.useRef(null);
  const recentRef = React.useRef(null);
  React.useEffect(() => {
    if (!open) return undefined;
    const onKey = (e) => { if (e.key === "Escape" && ref.current?.contains(e.target)) { e.stopPropagation(); setOpen(false); recentRef.current?.focus(); } };
    const onDown = (e) => { if (!ref.current?.contains(e.target)) setOpen(false); };
    document.addEventListener("keydown", onKey); document.addEventListener("mousedown", onDown);
    return () => { document.removeEventListener("keydown", onKey); document.removeEventListener("mousedown", onDown); };
  }, [open]);
  if (!api) return null;
  const current = api.current;
  const pins = api.pinned.slice(0, maxPinned);
  const panelId = `mds-trail-panel-${api.screenId || "global"}`;
  return <div>
    {api.notice ? <div className="mds-trail__notice" role="status">
      <strong>Version moved</strong>
      <span>{typeLabel(api.notice.entry)} {api.notice.entry.label ?? api.notice.entry.id} was viewed at v{api.notice.entry.version}; v{api.notice.entry.currentVersion} is now in force.</span>
      <button type="button" onClick={api.dismissNotice}>Dismiss</button>
    </div> : null}
    <nav ref={ref} className={`mds-trail${className ? " " + className : ""}`} aria-label={`${label} — object history`} {...rest}>
      <button type="button" className="mds-trail__nav" onClick={api.back} disabled={!api.canBack} aria-label="Back to the previous object" title="Back">‹</button>
      <button type="button" className="mds-trail__nav" onClick={api.forward} disabled={!api.canForward} aria-label="Forward" title="Forward">›</button>
      {current ? <div className="mds-trail__current">
        <span className="mds-trail__type">{typeLabel(current)}</span>
        <span className="mds-trail__label">{current.label ?? current.id}</span>
        {current.version != null ? <span className="mds-trail__ver">v{current.version}</span> : null}
        <button type="button" className="mds-trail__star" data-on={api.isPinned(current)} aria-pressed={api.isPinned(current)}
          aria-label={api.isPinned(current) ? "Unpin this object" : "Pin this object"} title={api.isPinned(current) ? "Unpin" : "Pin"}
          onClick={() => api.togglePin(current)}>{api.isPinned(current) ? "★" : "☆"}</button>
      </div> : <div className="mds-trail__current"><span className="mds-trail__type">No object opened yet</span></div>}
      <span className="mds-trail__spacer" />
      {showPinned && pins.length ? <div className="mds-trail__pins" role="group" aria-label="Pinned objects">
        {pins.map((entry) => <button key={trailRef(entry)} type="button" className="mds-trail__pin" data-active={current ? trailRef(current) === trailRef(entry) : false} onClick={() => api.go(entry)}>
          <em>{typeLabel(entry)}</em><span style={{ overflow: "hidden", textOverflow: "ellipsis" }}>{entry.label ?? entry.id}</span>
        </button>)}
      </div> : null}
      <button ref={recentRef} type="button" className="mds-trail__more" aria-expanded={open} aria-controls={open ? panelId : undefined}
        aria-label={`${recentsLabel} ${api.recents.length || ""} objects${api.recents.length ? `, ${api.recents.length} in this session` : ""}`}
        onClick={() => setOpen((s) => !s)}>
        {recentsLabel} {api.recents.length ? `+${api.recents.length}` : ""}
      </button>
      {open ? <div className="mds-trail__panel" id={panelId} role="group" aria-label="Recent and pinned objects">
        {api.pinned.length ? <>
          <div className="mds-trail__group"><span>Pinned · intent</span></div>
          {api.pinned.map((entry) => <Row key={`p-${trailRef(entry)}`} entry={entry} api={api} className="mds-trail__row" onPick={() => setOpen(false)} />)}
        </> : null}
        <div className="mds-trail__group"><span>Recent · behaviour</span>{api.recents.length ? <button type="button" className="mds-trail__clear" onClick={api.clearRecents}>Clear</button> : null}</div>
        {api.recents.length
          ? api.recents.map((entry) => <Row key={`r-${trailRef(entry)}`} entry={entry} api={api} className="mds-trail__row" onPick={() => setOpen(false)} />)
          : <div className="mds-tlist__empty" style={{ padding: "10px" }}>No objects opened in this session.</div>}
      </div> : null}
    </nav>
  </div>;
}

export function ObjectTrailList({ heading = "Where you were", limit = 6, showPinned = true, emptyStatement = "No objects opened yet. Anything you open here is retrievable for the rest of the session.", className = "", ...rest }) {
  inject();
  const api = React.useContext(Ctx);
  if (!api) return null;
  const recents = api.recents.slice(0, limit);
  return <div className={`mds-tlist${className ? " " + className : ""}`} {...rest}>
    {showPinned && api.pinned.length ? <>
      <div className="mds-tlist__head"><span>Pinned</span><span>{api.pinned.length}</span></div>
      {api.pinned.map((entry) => <button key={`p-${trailRef(entry)}`} type="button" className="mds-tlist__row" onClick={() => api.go(entry)}>
        <b>{entry.label ?? entry.id}</b><em>{typeLabel(entry)}</em>
        <span className="t">{[entry.meta, entry.at].filter(Boolean).join(" · ")}</span>
        {trailMoved(entry) ? <span className="mds-trail__moved">Version moved · v{entry.version} → v{entry.currentVersion}</span> : null}
      </button>)}
    </> : null}
    <div className="mds-tlist__head" style={{ marginTop: api.pinned.length && showPinned ? 14 : 0 }}><span>{heading}</span>{recents.length ? <span>{recents.length}</span> : null}</div>
    {recents.length ? recents.map((entry) => <button key={`r-${trailRef(entry)}`} type="button" className="mds-tlist__row" onClick={() => api.go(entry)}>
      <b>{entry.label ?? entry.id}</b><em>{typeLabel(entry)}</em>
      <span className="t">{[entry.meta, entry.at].filter(Boolean).join(" · ")}</span>
      {trailMoved(entry) ? <span className="mds-trail__moved">Version moved · v{entry.version} → v{entry.currentVersion}</span> : null}
    </button>) : <p className="mds-tlist__empty">{emptyStatement}</p>}
  </div>;
}
