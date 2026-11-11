// Meridian analytical scope — the object a figure is true of, stated on the figure.
// "Duration 8.22" is four different numbers depending on whether it describes the instrument, the
// holding, the portfolio, or the accounting proxy. Meridian refuses the ambiguity: a surface
// declares the object it is about, and any figure that belongs to a DIFFERENT object carries its
// own scope word. Law: guidelines/ANALYTICAL_SCOPE.md.
import React from "react";
import { objectMeta } from "./ObjectModel";

/** The scope ladder — object types from the object model that can own a figure, grouped by the
 *  kind of claim they make. `kind` is what makes the relation between two scopes nameable:
 *  instrument truth is true wherever the thing is held; holding truth is true of one owner's
 *  slice; aggregate truth is true of a population. */
export const ANALYTICAL_SCOPES = [
  { id: "security", kind: "instrument", answers: "How does the instrument behave, wherever it is held?" },
  { id: "lot", kind: "holding", answers: "What did this acquisition cost, and what is left of it?" },
  { id: "position", kind: "holding", answers: "What does this holding own, owe, and contribute?" },
  { id: "account", kind: "aggregate", answers: "What does the book carry?" },
  { id: "portfolio", kind: "aggregate", answers: "What is true of the whole population?" },
  { id: "legalEntity", kind: "aggregate", answers: "What does the entity report?" },
];
const BY_ID = ANALYTICAL_SCOPES.reduce((m, s) => { m[s.id] = s; return m; }, {});

/** Scope metadata for an id — label and identifier wording come from the object model, so the
 *  scope word on a figure and the type chip in an ObjectHeader are the same word. */
export function scopeMeta(id) {
  const s = BY_ID[id];
  const meta = objectMeta(id);
  return { id: id || null, label: meta.label, kind: s ? s.kind : "aggregate", answers: s ? s.answers : null, known: !!s };
}

const RELATIONS = {
  same: { id: "same", note: null },
  sibling: { id: "sibling", note: (d) => "Scoped to the " + d.label.toLowerCase() + ", not the surface's own object." },
  instrument: { id: "instrument", note: (d) => "A property of the instrument — true of every holding of it, not of this one." },
  holding: { id: "holding", note: (d) => "True of one holding only — not of the instrument or the population." },
  aggregate: { id: "aggregate", note: (d) => "Measured across the " + d.label.toLowerCase() + " — this object is one contributor." },
};

/** How a figure's scope relates to the surface it is read on. The whole point of the primitive:
 *  a reader should never have to infer this. */
export function scopeRelation(declared, ambient) {
  if (!declared || !ambient || declared === ambient) return { ...RELATIONS.same, note: null };
  const d = scopeMeta(declared), a = scopeMeta(ambient);
  const key = d.kind === a.kind ? "sibling" : d.kind;
  const r = RELATIONS[key] || RELATIONS.sibling;
  return { id: r.id, note: r.note ? r.note(d, a) : null };
}

const Ctx = React.createContext(null);
/** The scope in force, and the surface's own object. */
function useScope() { return React.useContext(Ctx); }

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "object-scope");
  el.textContent = `
.mds-scope{font:700 9.5px var(--font-body);font-variant:all-small-caps;letter-spacing:.07em;color:var(--text-muted,#606870);white-space:nowrap}
.mds-scope[data-rel="same"]{color:var(--text-disabled,#A4ABB2)}
.mds-scope[data-rel="instrument"],.mds-scope[data-rel="aggregate"],.mds-scope[data-rel="sibling"],.mds-scope[data-rel="holding"]{color:var(--text-secondary,#535C65)}
.mds-scope--chip{border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-chip,2px);padding:0 5px;line-height:15px;display:inline-block}
.mds-scope--chip[data-foreign="on"]{border-style:dashed;border-color:var(--text-muted,#606870)}
.mds-scope--inline[data-foreign="on"]{text-decoration:underline dotted;text-underline-offset:2px}
.mds-scoped{display:grid;gap:1px;align-content:start;min-width:0}
.mds-scoped--inline{display:inline-flex;align-items:baseline;gap:7px}
.mds-scoped[data-foreign="on"]{padding-left:7px;border-left:2px solid var(--border-strong,#AAA49A)}
.mds-scoped__lead{display:flex;align-items:baseline;gap:6px;min-width:0}
.mds-scoped__k{font:11px var(--font-body);color:var(--text-muted,#606870);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.mds-scoped__n{font:600 13px var(--font-data,monospace);color:var(--text-primary,#20252B);font-variant-numeric:slashed-zero tabular-nums}
.mds-scoped--lg .mds-scoped__n{font-size:17px}
.mds-scoped--sm .mds-scoped__n{font-size:12px}
.mds-scoped__note{font:10.5px var(--font-body);color:var(--text-muted,#606870);text-wrap:pretty}
.mds-scopemis{display:grid;gap:6px;border:1px solid var(--border-strong,#AAA49A);border-left:3px solid var(--orange,#986717);background:var(--card-surface,#F8F7F3);padding:9px 12px;font-family:var(--font-body)}
.mds-scopemis__t{font:600 11.5px var(--font-body);color:var(--text-primary,#20252B)}
.mds-scopemis__r{display:flex;align-items:baseline;gap:8px;font-size:11.5px;color:var(--text-secondary,#535C65)}
.mds-scopemis__r b{font:600 9.5px var(--font-body);font-variant:all-small-caps;letter-spacing:.07em;color:var(--text-muted,#606870)}
`;
  document.head.appendChild(el);
}

/** Declares the object a region is about. Every `ScopeMark` inside it that names a different
 *  object reads as foreign, and `ScopeMismatch` can list them. Nest freely: a position drawer
 *  inside a security workspace re-declares the scope for its own subtree. */
export function ObjectScopeProvider({ scope, object, label, children }) {
  inject();
  const parent = useScope();
  const reg = React.useRef(new Map());
  const subs = React.useRef(new Set());
  const value = React.useMemo(() => ({
    scope: scope || null, object: object ?? null, label: label ?? null, parent: parent ? parent.scope : null,
    report(key, entry) {
      if (entry) reg.current.set(key, entry); else reg.current.delete(key);
      subs.current.forEach((fn) => fn(Array.from(reg.current.values())));
    },
    subscribe(fn) { subs.current.add(fn); fn(Array.from(reg.current.values())); return () => subs.current.delete(fn); },
  }), [scope, object, label, parent]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

/** The scope word itself — the persistent, subtle indicator. Pass `scope` only when the figure
 *  belongs to something other than the surface's own object; with no ambient scope it always
 *  prints, because an unscoped figure on an unscoped surface is exactly the ambiguity to kill. */
export function ScopeMark({ scope, variant = "label", relation, title, className = "", ...rest }) {
  inject();
  const ctx = useScope();
  const declared = scope || (ctx && ctx.scope) || null;
  if (!declared) return null;
  const meta = scopeMeta(declared);
  const rel = relation || scopeRelation(declared, ctx && ctx.scope);
  const foreign = rel.id !== "same";
  return <span className={`mds-scope mds-scope--${variant}${className ? " " + className : ""}`} data-rel={rel.id} data-foreign={foreign ? "on" : "off"} title={title || rel.note || meta.answers || undefined} {...rest}>{meta.label}</span>;
}

/** A figure that states its own scope: scope word · label · value. The §20 pattern — use it
 *  wherever a number's object is not obvious from two feet away, and always for a figure whose
 *  object differs from the surface's. */
export function ScopedValue({
  scope, label, value, note, size = "md", stack = true, explain = false, id, className = "", children, ...rest
}) {
  inject();
  const ctx = useScope();
  const declared = scope || (ctx && ctx.scope) || null;
  const rel = scopeRelation(declared, ctx && ctx.scope);
  const foreign = !!declared && rel.id !== "same";
  const key = id || label || String(value);
  React.useEffect(() => {
    if (!ctx || !ctx.report) return;
    if (!foreign) { ctx.report(key, null); return; }
    ctx.report(key, { key, scope: declared, label, value, note: rel.note });
    return () => ctx.report(key, null);
  }, [ctx, key, foreign, declared, label, value, rel.note]);
  return <div className={`mds-scoped${stack ? "" : " mds-scoped--inline"} mds-scoped--${size}${className ? " " + className : ""}`} data-foreign={foreign ? "on" : "off"} data-scope={declared || undefined} {...rest}>
    <span className="mds-scoped__lead">
      <ScopeMark scope={declared} relation={rel} variant="label" />
      {label ? <span className="mds-scoped__k">{label}</span> : null}
    </span>
    {children ?? <span className="mds-scoped__n">{value ?? "—"}</span>}
    {note ? <span className="mds-scoped__note">{note}</span> : null}
    {explain && rel.note ? <span className="mds-scoped__note">{rel.note}</span> : null}
  </div>;
}

/** Audits the region: lists the figures inside it that belong to some other object. Renders
 *  nothing when every figure is in scope — the absence of the notice is the clean state. */
export function ScopeMismatch({ title, className = "", ...rest }) {
  inject();
  const ctx = useScope();
  const [items, setItems] = React.useState([]);
  React.useEffect(() => (ctx && ctx.subscribe ? ctx.subscribe(setItems) : undefined), [ctx]);
  if (!ctx || !items.length) return null;
  const here = scopeMeta(ctx.scope);
  return <div className={`mds-scopemis${className ? " " + className : ""}`} role="note" {...rest}>
    <span className="mds-scopemis__t">{title || `${items.length} figure${items.length === 1 ? "" : "s"} here are not ${here.label.toLowerCase()}-level`}</span>
    {items.map((it) => <span key={it.key} className="mds-scopemis__r">
      <b>{scopeMeta(it.scope).label}</b>{it.label ? <span>{it.label}</span> : null}{it.note ? <span style={{ color: "var(--text-muted,#606870)" }}>{it.note}</span> : null}
    </span>)}
  </div>;
}

/** Hook + helper carrier — the compiler exposes only capitalized exports. */
export const ScopeHooks = { useScope, scopeMeta, scopeRelation, ANALYTICAL_SCOPES };
/** Module carrier — `ObjectScope.Provider`, `ObjectScope.Mark`, `ObjectScope.Value`. */
export const ObjectScope = { Provider: ObjectScopeProvider, Mark: ScopeMark, Value: ScopedValue, Mismatch: ScopeMismatch, meta: scopeMeta, relation: scopeRelation, SCOPES: ANALYTICAL_SCOPES };
