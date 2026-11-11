// Meridian workspace context — persistent entity, basis, period, as-of, currency, scenario, and freshness.
// 3.2.0: priority-ordered responsive collapse with an overflow disclosure.
import React from "react";
import { DataCondition } from "../operations/DataCondition";

const Context = React.createContext(null);
const DEFAULT_CONTEXT = { entity: { type: "Portfolio", label: "General Account" }, book: "STAT", basis: "Statutory", period: "2026-07", asOf: "2026-07-31 23:59 UTC", currency: "USD", scenario: "Actual", condition: "confirmed", locked: false };
export function WorkspaceContextProvider({ value, defaultValue, onChange, children }) {
  const controlled = value !== undefined;
  const [internal, setInternal] = React.useState({ ...DEFAULT_CONTEXT, ...(defaultValue || {}) });
  const context = controlled ? { ...DEFAULT_CONTEXT, ...value } : internal;
  const update = React.useCallback((patch) => {
    const next = { ...context, ...(typeof patch === "function" ? patch(context) : patch) };
    if (!controlled) setInternal(next);
    onChange?.(next);
  }, [context, controlled, onChange]);
  const api = React.useMemo(() => ({ context, update, reset: () => update({ ...DEFAULT_CONTEXT, ...(defaultValue || {}) }) }), [context, update, defaultValue]);
  return <Context.Provider value={api}>{children}</Context.Provider>;
}
export function useWorkspaceContext() {
  const value = React.useContext(Context);
  if (!value) throw new Error("useWorkspaceContext must be used inside WorkspaceContextProvider");
  return value;
}
/** Bundle carrier — the compiler only exposes capitalized exports, so consumers of
 *  `_ds_bundle.js` reach the hook through this object:
 *  `const { WorkspaceHooks } = window.<Namespace>; const { useWorkspaceContext } = WorkspaceHooks;` */
export const WorkspaceHooks = { useWorkspaceContext };
let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "workspace-context");
  el.textContent = `
.mds-wctx{min-height:44px;display:flex;align-items:stretch;border-bottom:1px solid var(--border-strong,#AAA49A);background:var(--surface-recessed,var(--bg-medium,#EBE8E1));font-family:var(--font-body);color:var(--text-secondary,#535C65);overflow:visible;position:relative;}
.mds-wctx--scroll{overflow-x:auto;}
.mds-wctx__item{display:grid;align-content:center;gap:1px;min-width:max-content;padding:5px 12px;border:0;border-right:1px solid var(--border-divider,#E2DED5);background:transparent;text-align:left;color:inherit;font-family:inherit;}
button.mds-wctx__item{cursor:pointer}button.mds-wctx__item:hover{background:var(--bg-hover,#EDEFF0)}button.mds-wctx__item:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-3px}
.mds-wctx__label{font:600 9px var(--font-data,monospace);letter-spacing:.06em;text-transform:uppercase;color:var(--text-muted,#606870)}.mds-wctx__value{font-size:12px;font-weight:600;color:var(--text-primary,#20252B)}.mds-wctx__meta{font:10px var(--font-data,monospace);color:var(--text-muted,#606870)}
.mds-wctx__spacer{flex:1;min-width:12px}.mds-wctx__actions{display:flex;align-items:center;gap:6px;padding:0 10px;border-left:1px solid var(--border-divider,#E2DED5)}.mds-wctx__lock{font:600 9px var(--font-data,monospace);text-transform:uppercase;color:var(--pending,#7A6B52)}
.mds-wctx__ghost{position:fixed;top:-9999px;left:0;display:flex;visibility:hidden;pointer-events:none;white-space:nowrap;}
.mds-wctx__more{display:flex;align-items:center;gap:5px;padding:0 10px;border:0;border-right:1px solid var(--border-divider,#E2DED5);background:transparent;color:var(--text-secondary,#535C65);font:600 10px var(--font-data,monospace);letter-spacing:.04em;text-transform:uppercase;cursor:pointer;white-space:nowrap;}
.mds-wctx__more:hover{background:var(--bg-hover,#EDEFF0);color:var(--text-primary,#20252B)}.mds-wctx__more:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-3px}
.mds-wctx__more[aria-expanded="true"]{background:var(--selected-surface,#E7EDF2);color:var(--text-primary,#20252B)}
.mds-wctx__overflow{position:absolute;top:100%;left:0;z-index:40;display:grid;min-width:220px;border:1px solid var(--border-strong,#AAA49A);border-radius:var(--radius-overlay,6px);background:var(--surface-overlay,var(--surface-overlay-base,#fff));box-shadow:var(--shadow-menu,0 2px 6px rgba(17,23,29,.18));overflow:hidden;}
.mds-wctx__overflow .mds-wctx__item{border-right:0;border-bottom:1px solid var(--border-divider,#E2DED5);padding:7px 12px}.mds-wctx__overflow .mds-wctx__item:last-child{border-bottom:0}
@media print{.mds-wctx{overflow:visible}.mds-wctx__more,.mds-wctx__overflow,.mds-wctx__ghost{display:none}}
`;
  document.head.appendChild(el);
}
function Item({ label, value, meta, onClick, children }) {
  const Tag = onClick ? "button" : "div";
  return <Tag type={onClick ? "button" : undefined} className="mds-wctx__item" onClick={onClick}><span className="mds-wctx__label">{label}</span>{children ?? <span className="mds-wctx__value">{value ?? "—"}</span>}{meta ? <span className="mds-wctx__meta">{meta}</span> : null}</Tag>;
}

export function WorkspaceContextBar({
  value, condition, onEntityClick, onBookClick, onPeriodClick, onAsOfClick, onCurrencyClick, onScenarioClick,
  actions, collapse = "auto", minVisible = 2, overflowLabel = "More", className = "", ...rest
}) {
  inject();
  let provided = null;
  try { provided = React.useContext(Context); } catch { /* bar used without a provider */ }
  const c = { ...DEFAULT_CONTEXT, ...(provided?.context || {}), ...(value || {}) };

  // Canonical DOM order, each with the priority it survives to. Higher drops first.
  const items = [
    { key: "entity", priority: 0, node: <Item key="entity" label={c.entity?.type || "Entity"} value={c.entity?.label || c.entity} onClick={onEntityClick} /> },
    { key: "book", priority: 5, node: <Item key="book" label="Book" value={c.book} meta={c.basis} onClick={onBookClick} /> },
    { key: "period", priority: 3, node: <Item key="period" label="Period" value={c.period} onClick={onPeriodClick} /> },
    { key: "asOf", priority: 4, node: <Item key="asOf" label="As of" value={c.asOf} onClick={onAsOfClick} /> },
    { key: "currency", priority: 6, node: <Item key="currency" label="Currency" value={c.currency} onClick={onCurrencyClick} /> },
    { key: "scenario", priority: 7, node: <Item key="scenario" label="Scenario" value={c.scenario} onClick={onScenarioClick} /> },
    { key: "condition", priority: 1, node: <Item key="condition" label="Data"><DataCondition condition={condition || c.condition} /></Item> },
    ...(c.locked ? [{ key: "locked", priority: 2, node: <Item key="locked" label="Period state"><span className="mds-wctx__lock">Locked</span></Item> }] : []),
  ];

  const navRef = React.useRef(null);
  const ghostRef = React.useRef(null);
  const actionsRef = React.useRef(null);
  const [hidden, setHidden] = React.useState([]);
  const [open, setOpen] = React.useState(false);
  const signature = items.map((item) => item.key).join("|");

  const measure = React.useCallback(() => {
    const nav = navRef.current, ghost = ghostRef.current;
    if (!nav || !ghost) return;
    if (collapse === "never") { setHidden((current) => (current.length ? [] : current)); return; }
    const widths = new Map();
    Array.from(ghost.children).forEach((child) => widths.set(child.dataset.key, child.offsetWidth));
    const available = nav.clientWidth - (actionsRef.current?.offsetWidth || 0) - 12;
    const keep = collapse === "always"
      ? items.slice().sort((a, b) => a.priority - b.priority).slice(0, minVisible).map((item) => item.key)
      : null;
    let next;
    if (keep) {
      next = items.filter((item) => !keep.includes(item.key)).map((item) => item.key);
    } else {
      const order = items.slice().sort((a, b) => a.priority - b.priority);
      const dropped = [];
      let total = order.reduce((sum, item) => sum + (widths.get(item.key) || 0), 0);
      for (let i = order.length - 1; i >= minVisible && total > available; i -= 1) {
        dropped.push(order[i].key);
        total -= widths.get(order[i].key) || 0;
      }
      // Reserve room for the disclosure itself once anything is hidden.
      if (dropped.length && total + 74 > available) {
        for (let i = order.length - 1 - dropped.length; i >= minVisible && total + 74 > available; i -= 1) {
          dropped.push(order[i].key);
          total -= widths.get(order[i].key) || 0;
        }
      }
      next = dropped;
    }
    setHidden((current) => (current.length === next.length && current.every((key, i) => key === next[i]) ? current : next));
  }, [collapse, minVisible, signature]);

  React.useEffect(() => {
    measure();
    if (typeof ResizeObserver === "undefined" || !navRef.current) return undefined;
    const observer = new ResizeObserver(() => measure());
    observer.observe(navRef.current);
    return () => observer.disconnect();
  }, [measure]);

  React.useEffect(() => {
    if (!open) return undefined;
    const onKey = (event) => { if (event.key === "Escape") setOpen(false); };
    const onDown = (event) => { if (!navRef.current?.contains(event.target)) setOpen(false); };
    document.addEventListener("keydown", onKey);
    document.addEventListener("mousedown", onDown);
    return () => { document.removeEventListener("keydown", onKey); document.removeEventListener("mousedown", onDown); };
  }, [open]);

  React.useEffect(() => { if (!hidden.length) setOpen(false); }, [hidden.length]);

  const visible = items.filter((item) => !hidden.includes(item.key));
  const overflow = items.filter((item) => hidden.includes(item.key));

  return <nav ref={navRef} className={`mds-wctx${collapse === "never" ? " mds-wctx--scroll" : ""}${className ? " " + className : ""}`} aria-label="Workspace context" {...rest}>
    <div ref={ghostRef} className="mds-wctx__ghost" aria-hidden="true">
      {items.map((item) => <div key={item.key} data-key={item.key}>{item.node}</div>)}
    </div>
    {visible.map((item) => item.node)}
    {overflow.length ? (
      <button type="button" className="mds-wctx__more" aria-expanded={open} aria-haspopup="true" onClick={() => setOpen((state) => !state)}>
        {overflowLabel} +{overflow.length}
      </button>
    ) : null}
    <span className="mds-wctx__spacer" />
    {actions ? <div ref={actionsRef} className="mds-wctx__actions">{actions}</div> : null}
    {open && overflow.length ? <div className="mds-wctx__overflow" role="group" aria-label="Additional workspace context">{overflow.map((item) => item.node)}</div> : null}
  </nav>;
}
