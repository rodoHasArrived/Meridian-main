// Meridian AnnotationLayer — a note anchored to a cell, a series point, a ledger line or an object
// header (UX-depth track E-1). Review in Meridian was attestation-only: an operator could sign a
// figure but not mark one up. Every note is versioned with the object it sits on and the operator
// chooses its lifecycle at write time: FOLLOW the object (a material change marks the note stale,
// mirroring attestation supersession) or PIN it to the version it was written against.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "annotation-layer");
  el.textContent = `
.mds-ann{position:relative;display:inline-flex;align-items:center;gap:4px;min-width:0;max-width:100%;}
.mds-ann--block{display:flex;}
.mds-ann__body{min-width:0;flex:1;}
.mds-ann__mark{flex:none;display:inline-flex;align-items:center;justify-content:center;gap:2px;min-width:24px;min-height:24px;padding:0 4px;
  border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-chip,2px);background:var(--bg-light,#F8F7F3);cursor:pointer;
  font:600 10px var(--font-data);color:var(--text-secondary,#535C65);letter-spacing:.02em;transition:border-color 120ms cubic-bezier(.2,0,0,1),background-color 120ms cubic-bezier(.2,0,0,1);}
.mds-ann__mark:hover{background:var(--bg-hover,#EDEFF0);border-color:var(--border-hover,#BEB8AD);}
.mds-ann__mark:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
.mds-ann__mark--empty{opacity:0;}
.mds-ann:hover .mds-ann__mark--empty,.mds-ann:focus-within .mds-ann__mark--empty,.mds-ann__mark--empty:focus-visible{opacity:1;}
.mds-ann__mark--has{border-color:var(--purple,#2F648A);color:var(--purple,#2F648A);}
.mds-ann__mark--stale{border-style:dashed;border-color:var(--cond-stale-fg,#986717);color:var(--cond-stale-fg,#986717);}
.mds-ann__glyph{font-size:9px;line-height:1;}
.mds-ann__pop{position:absolute;z-index:7;top:100%;right:0;margin-top:4px;width:320px;box-sizing:border-box;text-align:left;
  background:var(--surface-overlay-base,#FFF);border:1px solid var(--border-strong,#AAA49A);border-radius:var(--radius-overlay,6px);
  box-shadow:0 2px 8px rgba(0,0,0,.16);font:12px var(--font-body);color:var(--text-primary,#20252B);}
.mds-ann__pop--left{right:auto;left:0;}
.mds-ann__pop-h{display:flex;align-items:baseline;gap:8px;padding:8px 10px 6px;border-bottom:1px solid var(--border-divider,#E2DED5);
  font:600 10px var(--font-data);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870);}
.mds-ann__pop-h span:last-child{margin-left:auto;text-transform:none;letter-spacing:0;font-weight:400;}
.mds-ann__list{max-height:220px;overflow:auto;}
.mds-ann__item{padding:7px 10px;border-bottom:1px solid var(--border-divider,#E2DED5);}
.mds-ann__item--stale{background:var(--cond-stale-bg,transparent);}
.mds-ann__meta{display:flex;gap:8px;align-items:baseline;font:10.5px var(--font-data);color:var(--text-muted,#606870);margin-bottom:2px;}
.mds-ann__meta b{color:var(--text-secondary,#535C65);font-weight:600;}
.mds-ann__ver{margin-left:auto;white-space:nowrap;}
.mds-ann__ver--stale{color:var(--cond-stale-fg,#986717);font-weight:600;}
.mds-ann__ver--pinned{color:var(--purple,#2F648A);}
.mds-ann__text{line-height:1.45;color:var(--text-primary,#20252B);white-space:pre-wrap;}
.mds-ann__act{display:inline-flex;gap:8px;margin-top:4px;}
.mds-ann__lnk{background:none;border:0;padding:0;min-height:24px;font:11px var(--font-body);color:var(--link,#2A5F96);cursor:pointer;}
.mds-ann__lnk:hover{text-decoration:underline;}
.mds-ann__lnk:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
.mds-ann__form{padding:8px 10px 10px;display:flex;flex-direction:column;gap:6px;}
.mds-ann__ta{width:100%;box-sizing:border-box;min-height:56px;resize:vertical;padding:6px 8px;border:1px solid var(--border,#D2CDC3);border-radius:2px;
  background:var(--bg-light,#F8F7F3);color:var(--text-primary,#20252B);font:12px var(--font-body);line-height:1.45;}
.mds-ann__ta:focus{border-color:var(--interaction-focus,#2D6FB3);outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px;}
.mds-ann__life{display:grid;grid-template-columns:1fr 1fr;gap:6px;}
.mds-ann__life label{display:flex;gap:6px;align-items:flex-start;padding:6px 8px;border:1px solid var(--border,#D2CDC3);border-radius:2px;cursor:pointer;font:11px var(--font-body);line-height:1.4;color:var(--text-secondary,#535C65);}
.mds-ann__life label:has(input:checked){border-color:var(--interaction-focus,#2D6FB3);background:var(--selected-surface,#E7EDF2);color:var(--text-primary,#20252B);}
.mds-ann__life input{margin:2px 0 0;}
.mds-ann__life b{display:block;font-weight:600;}
.mds-ann__row{display:flex;gap:6px;justify-content:flex-end;align-items:center;}
.mds-ann__btn{min-height:24px;padding:0 10px;border-radius:var(--radius-button,4px);border:1px solid var(--border-strong,#AAA49A);background:var(--bg-light,#F8F7F3);color:var(--text-primary,#20252B);font:600 12px var(--font-body);cursor:pointer;}
.mds-ann__btn:hover{background:var(--bg-hover,#EDEFF0);}
.mds-ann__btn:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
.mds-ann__btn--primary{background:var(--brand-axis,#29323A);border-color:var(--brand-axis,#29323A);color:var(--ink-inverse,#F5F3EE);}
.mds-ann__btn[disabled]{opacity:.5;cursor:default;}
.mds-annpanel{display:flex;flex-direction:column;min-height:0;font:12px var(--font-body);color:var(--text-secondary,#535C65);}
.mds-annpanel__h{display:flex;align-items:baseline;gap:10px;padding:7px 10px;border-bottom:1px solid var(--border,#D2CDC3);
  font:600 10px var(--font-data);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870);}
.mds-annpanel__filters{margin-left:auto;display:inline-flex;gap:2px;}
.mds-annpanel__f{min-height:24px;padding:0 8px;border:1px solid transparent;border-radius:2px;background:none;font:600 10px var(--font-data);letter-spacing:.04em;text-transform:uppercase;color:var(--text-muted,#606870);cursor:pointer;}
.mds-annpanel__f[aria-pressed="true"]{border-color:var(--border-strong,#AAA49A);background:var(--bg-light,#F8F7F3);color:var(--text-primary,#20252B);}
.mds-annpanel__f:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
.mds-annpanel__list{overflow:auto;min-height:0;}
.mds-annpanel__item{display:grid;grid-template-columns:1fr auto;gap:2px 10px;padding:8px 10px;border-bottom:1px solid var(--border-divider,#E2DED5);text-align:left;width:100%;background:none;border-left:0;border-right:0;border-top:0;cursor:pointer;color:inherit;font:inherit;}
.mds-annpanel__item:hover{background:var(--bg-hover,#EDEFF0);}
.mds-annpanel__item:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px;}
.mds-annpanel__item--stale{background:var(--cond-stale-bg,transparent);}
.mds-annpanel__anchor{font:600 11px var(--font-data);color:var(--text-primary,#20252B);}
.mds-annpanel__kind{font:10px var(--font-data);letter-spacing:.04em;text-transform:uppercase;color:var(--text-muted,#606870);}
.mds-annpanel__txt{grid-column:1/-1;line-height:1.45;color:var(--text-secondary,#535C65);}
.mds-annpanel__empty{padding:14px 10px;font:11px var(--font-data);color:var(--text-muted,#606870);}
`;
  document.head.appendChild(el);
}

const KIND_LABEL = { cell: "Cell", point: "Series point", line: "Ledger line", header: "Object" };
const Ctx = React.createContext(null);
let seq = 0;

function createStore(initial, handlers, objectId) {
  // Persisted collections may contain several objects. Unscoped seed notes belong
  // to the object for which this store is first created.
  let notes = initial.filter((n) => n.objectId == null || n.objectId === objectId)
    .map((n) => ({ ...n, objectId }));
  const reservedIds = new Set(initial.map((n) => n.id));
  const subs = new Set();
  const emit = () => { subs.forEach((f) => f(notes)); handlers.current.onChange && handlers.current.onChange(notes); };
  return {
    subscribe(f) { subs.add(f); return () => subs.delete(f); },
    get notes() { return notes; },
    add(n) {
      // A persisted ann-1 must not collide with the first add after a reload.
      let id;
      do { id = `ann-${++seq}`; } while (reservedIds.has(id));
      reservedIds.add(id);
      const note = { at: Date.now(), author: handlers.current.author || "you", lifecycle: "follow", ...n, id, objectId };
      notes = [...notes, note]; emit(); return note;
    },
    remove(id) { notes = notes.filter((n) => n.id !== id); emit(); },
    repin(id, version) { notes = notes.map((n) => (n.id === id ? { ...n, version, lifecycle: n.lifecycle } : n)); emit(); },
  };
}

export function AnnotationProvider({ objectId, objectVersion = 1, author = "you", annotations = [], onChange, onFocus, children }) {
  const handlers = React.useRef({});
  handlers.current = { author, onChange };
  // Detail panes reuse their provider while moving between accounts or securities.
  // Keep one ledger per object, so a switch never relabels the previous object's
  // notes or judges their staleness against the newly selected object's version.
  const stores = React.useRef(null);
  if (!stores.current) stores.current = new Map();
  if (!stores.current.has(objectId)) stores.current.set(objectId, createStore(annotations, handlers, objectId));
  const store = stores.current.get(objectId);
  const [, force] = React.useState(0);
  React.useEffect(() => store.subscribe(() => force((n) => n + 1)), [store]);
  const value = React.useMemo(() => ({ store, objectId, objectVersion, onFocus }), [store, objectId, objectVersion, onFocus, store.notes]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

function useLayer() {
  const ctx = React.useContext(Ctx);
  const local = React.useRef(null);
  const handlers = React.useRef({ author: "you" });
  if (!ctx && !local.current) local.current = { store: createStore([], handlers, "local"), objectId: "local", objectVersion: 1 };
  const layer = ctx || local.current;
  const [, force] = React.useState(0);
  React.useEffect(() => layer.store.subscribe(() => force((n) => n + 1)), [layer.store]);
  return layer;
}
export function useAnnotations() {
  const layer = useLayer();
  const decorate = (n) => ({ ...n, stale: n.lifecycle === "follow" && n.version < layer.objectVersion });
  const add = React.useCallback((note) => layer.store.add({ ...note, version: note.version ?? layer.objectVersion }), [layer.store, layer.objectVersion]);
  return { ...layer, notes: layer.store.notes.map(decorate),
    add,
    remove: layer.store.remove, repin: layer.store.repin, current: layer.objectVersion };
}
/** Bundle carrier — the compiler exposes only capitalized exports. */
export const AnnotationHooks = { useAnnotations };

function fmtAt(t) { const d = new Date(t); return `${String(d.getUTCHours()).padStart(2, "0")}:${String(d.getUTCMinutes()).padStart(2, "0")}Z`; }

function NoteItem({ n, current, onRemove, onRepin }) {
  const cls = n.lifecycle === "pin" ? "mds-ann__ver--pinned" : n.stale ? "mds-ann__ver--stale" : "";
  const ver = n.lifecycle === "pin" ? `pinned v${n.version}${current !== n.version ? ` · now v${current}` : ""}` : n.stale ? `stale · v${n.version} → v${current}` : `follows · v${n.version}`;
  return (
    <div className={`mds-ann__item ${n.stale ? "mds-ann__item--stale" : ""}`}>
      <div className="mds-ann__meta"><b>{n.author}</b><span>{fmtAt(n.at)}</span><span className={`mds-ann__ver ${cls}`}>{ver}</span></div>
      <div className="mds-ann__text">{n.text}</div>
      <div className="mds-ann__act">
        {n.stale && onRepin && <button type="button" className="mds-ann__lnk" onClick={() => onRepin(n.id, current)}>Still holds at v{current}</button>}
        {onRemove && <button type="button" className="mds-ann__lnk" onClick={() => onRemove(n.id)}>Remove</button>}
      </div>
    </div>
  );
}

function NoteForm({ current, onAdd, onCancel }) {
  const [text, setText] = React.useState("");
  const [life, setLife] = React.useState("follow");
  const uid = React.useId();
  const ref = React.useRef(null);
  React.useEffect(() => { ref.current && ref.current.focus(); }, []);
  return (
    <form className="mds-ann__form" onSubmit={(e) => { e.preventDefault(); if (text.trim()) onAdd({ text: text.trim(), lifecycle: life, version: current }); }}>
      <textarea ref={ref} className="mds-ann__ta" value={text} onChange={(e) => setText(e.target.value)} placeholder="What a reader of this figure should know" aria-label="Annotation text" />
      <div className="mds-ann__life" role="radiogroup" aria-label="When the object changes">
        <label><input type="radio" name={`${uid}-life`} checked={life === "follow"} onChange={() => setLife("follow")} /><span><b>Follow the object</b>Marked stale when v{current} is superseded.</span></label>
        <label><input type="radio" name={`${uid}-life`} checked={life === "pin"} onChange={() => setLife("pin")} /><span><b>Pin to v{current}</b>Stays as written; later versions show the pin.</span></label>
      </div>
      <div className="mds-ann__row">
        <button type="button" className="mds-ann__btn" onClick={onCancel}>Cancel</button>
        <button type="submit" className="mds-ann__btn mds-ann__btn--primary" disabled={!text.trim()}>Add note</button>
      </div>
    </form>
  );
}

/** Wrap the thing a note anchors to. `anchor` identifies it: `{kind, ref, label}`. */
export function AnnotationAnchor({ anchor, block = false, popover = "right", readOnly = false, className = "", children, ...rest }) {
  inject();
  const layer = useAnnotations();
  const [open, setOpen] = React.useState(false);
  const [adding, setAdding] = React.useState(false);
  const root = React.useRef(null);
  // A list host renders placeholder rows before its data arrives, and those carry no anchor.
  // Reading anchor.label then threw and took the whole template down with it.
  const anchored = !!(anchor && anchor.ref != null);
  const mine = anchored ? layer.notes.filter((n) => n.anchor && n.anchor.ref === anchor.ref) : [];
  const stale = mine.some((n) => n.stale);
  React.useEffect(() => {
    if (!open) return;
    const off = (e) => { if (root.current && !root.current.contains(e.target)) { setOpen(false); setAdding(false); } };
    document.addEventListener("pointerdown", off);
    return () => document.removeEventListener("pointerdown", off);
  }, [open]);
  if (!anchored) return <span className={`mds-ann ${block ? "mds-ann--block" : ""} ${className}`}><span className="mds-ann__body">{children}</span></span>;
  const label = mine.length ? `${mine.length} annotation${mine.length === 1 ? "" : "s"}${stale ? ", some stale" : ""} on ${anchor.label || anchor.ref}` : `Annotate ${anchor.label || anchor.ref}`;
  return (
    <span ref={root} className={`mds-ann ${block ? "mds-ann--block" : ""} ${className}`} onKeyDown={(e) => { if (e.key === "Escape" && open) { e.stopPropagation(); setOpen(false); setAdding(false); } }} {...rest}>
      <span className="mds-ann__body">{children}</span>
      <button type="button" className={`mds-ann__mark ${mine.length ? "mds-ann__mark--has" : "mds-ann__mark--empty"} ${stale ? "mds-ann__mark--stale" : ""}`}
        aria-label={label} aria-expanded={open} onClick={(e) => { e.stopPropagation(); setOpen((o) => !o); setAdding(!mine.length && !readOnly); }}>
        <span className="mds-ann__glyph" aria-hidden="true">¶</span>{mine.length ? mine.length : ""}
      </button>
      {open && (
        <div className={`mds-ann__pop ${popover === "left" ? "mds-ann__pop--left" : ""}`} role="dialog" aria-label={`Annotations · ${anchor.label || anchor.ref}`} onClick={(e) => e.stopPropagation()}>
          <div className="mds-ann__pop-h"><span>{KIND_LABEL[anchor.kind] || anchor.kind}</span><span>{anchor.label || anchor.ref}</span><span>v{layer.current}</span></div>
          {mine.length > 0 && <div className="mds-ann__list">{mine.map((n) => <NoteItem key={n.id} n={n} current={layer.current} onRemove={readOnly ? null : layer.remove} onRepin={readOnly ? null : layer.repin} />)}</div>}
          {!readOnly && (adding
            ? <NoteForm current={layer.current} onCancel={() => { setAdding(false); if (!mine.length) setOpen(false); }} onAdd={(n) => { layer.add({ ...n, anchor, objectId: layer.objectId }); setAdding(false); }} />
            : <div className="mds-ann__row" style={{ padding: "8px 10px" }}><button type="button" className="mds-ann__btn" onClick={() => setAdding(true)}>Add note…</button></div>)}
        </div>
      )}
    </span>
  );
}

/** Module namespace — the layer as one object for `<x-import component-from-global-scope>` callers. */
export const AnnotationLayer = { Provider: AnnotationProvider, Anchor: AnnotationAnchor, get Panel() { return AnnotationPanel; }, useAnnotations };

/** The standing list — for an EvidenceRail or an inspector. Click a row to `onFocus(note)`. */
export function AnnotationPanel({ title = "Annotations", onFocus, emptyText = "No notes on this object yet. Hover a figure and press ¶.", className = "", ...rest }) {
  inject();
  const layer = useAnnotations();
  const [filter, setFilter] = React.useState("all");
  const focus = onFocus || layer.onFocus;
  const notes = layer.notes.filter((n) => filter === "all" || (filter === "stale" ? n.stale : n.lifecycle === "pin"));
  const nStale = layer.notes.filter((n) => n.stale).length;
  return (
    <section className={`mds-annpanel ${className}`} aria-label={title} {...rest}>
      <div className="mds-annpanel__h">
        <span>{title}</span><span style={{ fontWeight: 400 }}>{layer.notes.length}{nStale ? ` · ${nStale} stale` : ""}</span>
        <div className="mds-annpanel__filters" role="group" aria-label="Filter annotations">
          {[["all", "All"], ["stale", "Stale"], ["pin", "Pinned"]].map(([k, l]) => <button type="button" key={k} className="mds-annpanel__f" aria-pressed={filter === k} onClick={() => setFilter(k)}>{l}</button>)}
        </div>
      </div>
      <div className="mds-annpanel__list">
        {notes.length === 0 && <div className="mds-annpanel__empty">{layer.notes.length ? "Nothing matches this filter." : emptyText}</div>}
        {notes.map((n) => (
          <button type="button" key={n.id} className={`mds-annpanel__item ${n.stale ? "mds-annpanel__item--stale" : ""}`} onClick={() => focus && focus(n)}>
            <span className="mds-annpanel__anchor">{(n.anchor && n.anchor.label) || (n.anchor && n.anchor.ref) || "—"}</span>
            <span className="mds-annpanel__kind">{KIND_LABEL[n.anchor && n.anchor.kind] || ""} · {n.lifecycle === "pin" ? `pinned v${n.version}` : n.stale ? `stale v${n.version}→v${layer.current}` : `v${n.version}`}</span>
            <span className="mds-annpanel__txt">{n.text}</span>
            <span className="mds-annpanel__kind" style={{ gridColumn: "1 / -1" }}>{n.author} · {fmtAt(n.at)}</span>
          </button>
        ))}
      </div>
    </section>
  );
}
