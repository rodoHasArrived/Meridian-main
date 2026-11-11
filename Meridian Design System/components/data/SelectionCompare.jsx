// Meridian SelectionCompare — comparison by selection (UX-depth track E-2). Two or more committed
// selections — grid rows, figure series, ledger lines — are promoted into a PlateGrid from the
// screen itself, rather than only declaratively in template source. The tray keeps the candidate
// set visible and the sheet applies the comparison-plate contract: one shared domain, one tick set.
import React from "react";
import { useOverlayFocus } from "../core/useOverlayFocus";
import { PlateGrid } from "../charts/ComparisonPlate";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "selection-compare");
  el.textContent = `
.mds-cmp__tog{display:inline-flex;align-items:center;gap:4px;min-width:24px;min-height:24px;padding:0 7px;border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-chip,2px);
  background:var(--bg-light,#F8F7F3);color:var(--text-secondary,#535C65);font:600 10px var(--font-data);letter-spacing:.03em;text-transform:uppercase;cursor:pointer;white-space:nowrap;
  transition:border-color 120ms cubic-bezier(.2,0,0,1),background-color 120ms cubic-bezier(.2,0,0,1);}
.mds-cmp__tog:hover{background:var(--bg-hover,#EDEFF0);border-color:var(--border-hover,#BEB8AD);}
.mds-cmp__tog:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
.mds-cmp__tog[aria-pressed="true"]{background:var(--selected-surface,#E7EDF2);border-color:var(--interaction-selected,#496C8F);color:var(--text-primary,#20252B);}
.mds-cmp__tog[disabled]{opacity:.45;cursor:default;}
.mds-cmp__tog i{font-style:normal;font-size:11px;line-height:1;}
.mds-cmptray{display:flex;align-items:center;gap:8px;min-height:var(--theme-row-review,var(--theme-control-height,34px));padding:0 10px;
  border:1px solid var(--border,#D2CDC3);background:var(--card-surface-raised,#FCFBF8);font:12px var(--font-body);color:var(--text-secondary,#535C65);}
.mds-cmptray--corner{position:fixed;right:16px;bottom:44px;z-index:30;box-shadow:0 2px 8px rgba(0,0,0,.16);border-radius:var(--radius-card,4px);}
.mds-cmptray__n{font:600 12px var(--font-data);color:var(--text-primary,#20252B);font-variant-numeric:tabular-nums;white-space:nowrap;}
.mds-cmptray__chips{display:flex;gap:4px;flex-wrap:wrap;min-width:0;}
.mds-cmptray__chip{display:inline-flex;align-items:center;gap:4px;min-height:24px;padding:0 2px 0 7px;border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-chip,2px);background:var(--bg-light,#F8F7F3);font:11px var(--font-data);color:var(--text-primary,#20252B);white-space:nowrap;}
.mds-cmptray__chip b{font-weight:600;color:var(--text-muted,#606870);}
.mds-cmptray__x{min-width:24px;min-height:22px;border:0;background:none;color:var(--text-muted,#606870);cursor:pointer;font:12px var(--font-body);border-radius:2px;}
.mds-cmptray__x:hover{background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B);}
.mds-cmptray__x:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-1px;}
.mds-cmptray__spacer{flex:1;}
.mds-cmp__btn{min-height:24px;padding:0 10px;border-radius:var(--radius-button,4px);border:1px solid var(--border-strong,#AAA49A);background:var(--bg-light,#F8F7F3);color:var(--text-primary,#20252B);font:600 12px var(--font-body);cursor:pointer;white-space:nowrap;}
.mds-cmp__btn:hover{background:var(--bg-hover,#EDEFF0);}
.mds-cmp__btn:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
.mds-cmp__btn--primary{background:var(--brand-axis,#29323A);border-color:var(--brand-axis,#29323A);color:var(--ink-inverse,#F5F3EE);}
.mds-cmp__btn[disabled]{opacity:.5;cursor:default;}
.mds-cmpsheet{display:flex;flex-direction:column;min-height:0;border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);font:12px var(--font-body);color:var(--text-secondary,#535C65);}
.mds-cmpsheet--overlay{position:fixed;inset:auto 16px 44px auto;z-index:31;width:min(920px,calc(100vw - 32px));max-height:min(70vh,640px);box-shadow:0 6px 24px rgba(0,0,0,.22);border-radius:var(--radius-overlay,6px);}
.mds-cmpsheet__h{display:flex;align-items:baseline;gap:10px;padding:8px 12px;border-bottom:1px solid var(--border,#D2CDC3);}
.mds-cmpsheet__t{font:600 13px var(--font-body);color:var(--text-primary,#20252B);}
.mds-cmpsheet__meta{font:10.5px var(--font-data);color:var(--text-muted,#606870);}
.mds-cmpsheet__body{padding:12px;overflow:auto;min-height:0;}
.mds-cmpsheet__foot{padding:6px 12px;border-top:1px solid var(--border-divider,#E2DED5);font:10px var(--font-data);color:var(--text-muted,#606870);}
`;
  document.head.appendChild(el);
}

const Ctx = React.createContext(null);

function createSet(handlers) {
  let items = [];
  const subs = new Set();
  const emit = () => { subs.forEach((f) => f(items)); handlers.current.onChange && handlers.current.onChange(items); };
  return {
    subscribe(f) { subs.add(f); return () => subs.delete(f); },
    get items() { return items; },
    has(id) { return items.some((i) => i.id === id); },
    toggle(item) { items = items.some((i) => i.id === item.id) ? items.filter((i) => i.id !== item.id) : items.length >= (handlers.current.max ?? 6) ? items : [...items, item]; emit(); },
    remove(id) { items = items.filter((i) => i.id !== id); emit(); },
    clear() { items = []; emit(); },
  };
}

export function SelectionCompareProvider({ max = 6, categories, domain, format, onChange, onCompare, children }) {
  const handlers = React.useRef({});
  handlers.current = { max, onChange };
  const set = React.useRef(null);
  if (!set.current) set.current = createSet(handlers);
  const [open, setOpen] = React.useState(false);
  const [, force] = React.useState(0);
  React.useEffect(() => set.current.subscribe(() => force((n) => n + 1)), []);
  const value = React.useMemo(() => ({ set: set.current, max, categories, domain, format, open, setOpen, onCompare }), [max, categories, domain, format, open, onCompare, set.current.items]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

function useCtx() {
  const ctx = React.useContext(Ctx);
  const local = React.useRef(null);
  const handlers = React.useRef({ max: 6 });
  const [open, setOpen] = React.useState(false);
  if (!ctx && !local.current) local.current = { set: createSet(handlers), max: 6 };
  const c = ctx || { ...local.current, open, setOpen };
  const [, force] = React.useState(0);
  React.useEffect(() => c.set.subscribe(() => force((n) => n + 1)), [c.set]);
  return c;
}
export function useSelectionCompare() {
  const c = useCtx();
  return { items: c.set.items, max: c.max, has: c.set.has, toggle: c.set.toggle, remove: c.set.remove, clear: () => { c.set.clear(); c.setOpen(false); }, open: c.open, setOpen: c.setOpen, onCompare: c.onCompare };
}
/** Bundle carrier — the compiler exposes only capitalized exports. */
export const SelectionCompareHooks = { useSelectionCompare };

/** Per-row / per-series control. `item` is the PlateSpec this thing becomes when compared. */
export function CompareToggle({ item, label = "Compare", pressedLabel = "In compare", compact = false, className = "", ...rest }) {
  inject();
  const c = useSelectionCompare();
  const on = c.has(item.id);
  const full = !on && c.items.length >= c.max;
  return (
    <button type="button" className={`mds-cmp__tog ${className}`} aria-pressed={on} disabled={full} title={full ? `Compare set is full (${c.max})` : undefined}
      aria-label={`${on ? pressedLabel : label}: ${item.title}; ${on ? "remove from" : "add to"} comparison`} onClick={(e) => { e.stopPropagation(); c.toggle(item); }} {...rest}>
      <i aria-hidden="true">{on ? "▣" : "▢"}</i>{!compact && (on ? pressedLabel : label)}
    </button>
  );
}

/** The candidate set, visible while it is being built. Hides when empty unless `emptyLabel`. */
export function CompareTray({ docked = "inline", label = "to compare", emptyLabel = null, className = "", ...rest }) {
  inject();
  const c = useSelectionCompare();
  const n = c.items.length;
  if (!n && !emptyLabel) return null;
  return (
    <div className={`mds-cmptray ${docked === "corner" ? "mds-cmptray--corner" : ""} ${className}`} role="region" aria-label="Comparison set" {...rest}>
      <span className="mds-cmptray__n">{n} of {c.max}</span><span>{n ? label : emptyLabel}</span>
      <div className="mds-cmptray__chips">
        {c.items.map((i) => (
          <span key={i.id} className="mds-cmptray__chip">{i.title}{i.tag && <b>{i.tag}</b>}
            <button type="button" className="mds-cmptray__x" aria-label={`Remove ${i.title} from comparison`} onClick={() => c.remove(i.id)}>×</button>
          </span>
        ))}
      </div>
      <span className="mds-cmptray__spacer" />
      {n > 0 && <button type="button" className="mds-cmp__btn" onClick={c.clear}>Clear</button>}
      <button type="button" className="mds-cmp__btn mds-cmp__btn--primary" disabled={n < 2} onClick={() => { c.onCompare?.(c.items); c.setOpen(true); }}>Compare{n >= 2 ? ` ${n}` : ""}</button>
    </div>
  );
}

/** Module namespace — the set's three parts as one object. */
export const SelectionCompare = { Provider: SelectionCompareProvider, Toggle: CompareToggle, Tray: CompareTray, get Sheet() { return CompareSheet; }, useSelectionCompare };

/** The PlateGrid built from the set. `mode="overlay"` floats above the plane; `"inline"` sits where placed. */
export function CompareSheet({ mode = "overlay", title = "Comparison by selection", categories, domain, format, footerLabel, footerValue, className = "", ...rest }) {
  inject();
  const c = useCtx();
  const items = c.set.items;
  const [sel, setSel] = React.useState(null);
  const sheetRef = React.useRef(null);
  useOverlayFocus(sheetRef, c.open && items.length >= 2 && mode === "overlay");
  const close = () => c.setOpen(false);
  React.useEffect(() => { if (c.open && items.length < 2) c.setOpen(false); }, [c.open, items.length]);
  React.useEffect(() => {
    if (!c.open || mode !== "overlay") return;
    const onKey = (e) => { if (e.key === "Escape") close(); };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [c.open, mode]);
  if (!c.open || items.length < 2) return null;
  const cats = categories || c.categories || (items[0] && items[0].categories) || items[0].values.map((_, i) => String(i + 1));
  const plates = items.map((i) => ({ ...i, footerLabel: i.footerLabel ?? (footerLabel ? footerLabel : undefined), footerValue: i.footerValue ?? (footerValue ? footerValue(i) : undefined) }));
  return (
    <section ref={sheetRef} className={`mds-cmpsheet ${mode === "overlay" ? "mds-cmpsheet--overlay" : ""} ${className}`} role={mode === "overlay" ? "dialog" : undefined} aria-modal={mode === "overlay" ? true : undefined} aria-label={title} {...rest}>
      <div className="mds-cmpsheet__h">
        <span className="mds-cmpsheet__t">{title}</span>
        <span className="mds-cmpsheet__meta">{items.length} plates · one shared domain · {cats.length} categories</span>
        <span className="mds-cmptray__spacer" />
        <button type="button" className="mds-cmp__btn" onClick={c.set.clear}>Clear set</button>
        <button type="button" className="mds-cmp__btn" onClick={close} aria-label="Close comparison">Close</button>
      </div>
      <div className="mds-cmpsheet__body">
        <PlateGrid plates={plates} categories={cats} domain={domain || c.domain} format={format || c.format} selectedId={sel} onSelect={setSel} />
      </div>
      <div className="mds-cmpsheet__foot">Plates share plot size, domain and ticks; a visible difference is a real difference. Selecting a plate never resizes it.</div>
    </section>
  );
}
