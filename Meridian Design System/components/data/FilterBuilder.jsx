// Meridian FilterBuilder — structured field/op/value query rows for registry surfaces.
// Where FilteredDataTable does free text + facets, FilterBuilder composes explicit
// predicates: field → operator → value, AND-combined. Emits rows on change and a compiled
// predicate on Apply — `FilterBuilder.predicate(fields, rows)` builds the same test anywhere.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-fb{display:flex;flex-direction:column;gap:6px;font-family:var(--font-body);}
.mds-fb__row{display:flex;align-items:center;gap:6px;}
.mds-fb__and{flex:0 0 34px;text-align:right;font-family:var(--font-data);font-size:9.5px;font-weight:700;
  letter-spacing:.06em;color:var(--text-muted,#59636F);}
.mds-fb__ctl{height:26px;box-sizing:border-box;border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-input,2px);
  background:var(--bg-light,#FFFFFF);color:var(--text-primary,#22272E);font-family:var(--font-body);font-size:12px;
  padding:0 7px;min-width:0;}
.mds-fb__ctl:hover{border-color:var(--border-hover,#ADB8C4);}
.mds-fb__ctl:focus,.mds-fb__ctl:focus-visible{outline:var(--focus-ring);outline-offset:-1px;border-color:var(--border-focus,#2F6F8F);}
select.mds-fb__ctl{appearance:auto;}
.mds-fb__ctl--field{flex:0 0 148px;font-weight:600;}
.mds-fb__ctl--op{flex:0 0 118px;color:var(--text-secondary,#4D5967);}
.mds-fb__ctl--value{flex:1 1 120px;font-family:var(--font-data);}
.mds-fb__x{flex:0 0 24px;height:26px;display:inline-flex;align-items:center;justify-content:center;cursor:pointer;
  border:1px solid transparent;border-radius:var(--radius-button,2px);background:transparent;
  color:var(--text-muted,#59636F);font-size:13px;line-height:1;}
.mds-fb__x:hover{color:var(--red-dim,#8C2F40);border-color:var(--border,#CBD3DC);background:var(--bg-hover,#EAEEF3);}
.mds-fb__x:focus-visible{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-fb__foot{display:flex;align-items:center;gap:8px;padding-top:2px;}
.mds-fb__btn{height:24px;box-sizing:border-box;display:inline-flex;align-items:center;gap:6px;cursor:pointer;
  border-radius:var(--radius-button,2px);font-family:var(--font-body);font-size:11px;font-weight:600;padding:0 10px;}
.mds-fb__btn--add{border:1px dashed var(--border-strong,#99A5B2);background:transparent;color:var(--text-secondary,#4D5967);}
.mds-fb__btn--add:hover{border-color:var(--accent,#2F6F8F);color:var(--accent,#2F6F8F);}
.mds-fb__btn--apply{border:1px solid var(--accent,#2F6F8F);background:var(--accent,#2F6F8F);color:var(--text-on-accent,#FFFFFF);}
.mds-fb__btn--apply:hover{background:var(--accent-dim,#255B75);border-color:var(--accent-dim,#255B75);}
.mds-fb__btn--clear{border:none;background:transparent;color:var(--text-muted,#59636F);text-decoration:underline;text-underline-offset:2px;}
.mds-fb__btn--clear:hover{color:var(--text-primary,#22272E);}
.mds-fb__btn:focus-visible{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-fb__summary{margin-left:auto;font-family:var(--font-data);font-size:10.5px;color:var(--text-muted,#59636F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "filter-builder");
  el.textContent = css;
  document.head.appendChild(el);
}

const OPS = {
  text: [
    { id: "contains", label: "contains" },
    { id: "is", label: "is" },
    { id: "is-not", label: "is not" },
    { id: "starts", label: "starts with" },
    { id: "empty", label: "is empty" },
  ],
  number: [
    { id: "eq", label: "=" }, { id: "ne", label: "≠" },
    { id: "gt", label: ">" }, { id: "gte", label: "≥" },
    { id: "lt", label: "<" }, { id: "lte", label: "≤" },
    { id: "between", label: "between" },
  ],
  date: [
    { id: "on", label: "on" }, { id: "before", label: "before" },
    { id: "after", label: "after" }, { id: "between", label: "between" },
  ],
  enum: [
    { id: "is", label: "is" }, { id: "is-not", label: "is not" },
  ],
};

function fieldByKey(fields, key) { return fields.find((f) => f.key === key); }
function opsFor(field) { return OPS[(field && field.type) || "text"] || OPS.text; }
function needsValue(op) { return op !== "empty"; }
function needsSecond(op) { return op === "between"; }
function dayOf(v) { const d = new Date(v); return isNaN(d) ? null : d.toISOString().slice(0, 10); }

function rowActive(fields, r) {
  if (!r || !r.field || !r.op) return false;
  if (!needsValue(r.op)) return true;
  if (r.value == null || r.value === "") return false;
  if (needsSecond(r.op) && (r.value2 == null || r.value2 === "")) return false;
  return true;
}

function testRow(field, r, raw) {
  const type = (field && field.type) || "text";
  if (type === "number") {
    const n = Number(raw), a = Number(r.value), b = Number(r.value2);
    if (isNaN(n)) return false;
    switch (r.op) {
      case "eq": return n === a; case "ne": return n !== a;
      case "gt": return n > a; case "gte": return n >= a;
      case "lt": return n < a; case "lte": return n <= a;
      case "between": return n >= Math.min(a, b) && n <= Math.max(a, b);
      default: return true;
    }
  }
  if (type === "date") {
    const d = new Date(raw), a = new Date(r.value), b = new Date(r.value2);
    if (isNaN(d)) return false;
    switch (r.op) {
      case "on": return dayOf(raw) === dayOf(r.value);
      case "before": return d < a;
      case "after": return d > a;
      case "between": return d >= (a < b ? a : b) && d <= (a < b ? b : a);
      default: return true;
    }
  }
  const s = raw == null ? "" : String(raw);
  const v = r.value == null ? "" : String(r.value);
  switch (r.op) {
    case "contains": return s.toLowerCase().includes(v.toLowerCase());
    case "is": return type === "enum" ? s === v : s.toLowerCase() === v.toLowerCase();
    case "is-not": return type === "enum" ? s !== v : s.toLowerCase() !== v.toLowerCase();
    case "starts": return s.toLowerCase().startsWith(v.toLowerCase());
    case "empty": return s.trim() === "";
    default: return true;
  }
}

function buildPredicate(fields, rows) {
  const active = (rows || []).filter((r) => rowActive(fields, r));
  return (item) => active.every((r) => testRow(fieldByKey(fields, r.field), r, item ? item[r.field] : undefined));
}

const newRow = (fields) => {
  const f = fields[0] || {};
  return { field: f.key || "", op: (opsFor(f)[0] || {}).id || "contains", value: "", value2: "" };
};

export function FilterBuilder({
  fields = [],
  value: controlled,
  defaultValue,
  onChange,
  onApply,
  showApply = true,
  summary,
  addLabel = "+ Add filter",
  className = "",
  ...rest
}) {
  inject();
  const isControlled = controlled != null;
  const [internal, setInternal] = React.useState(() => defaultValue || [newRow(fields)]);
  const rows = isControlled ? controlled : internal;

  const commit = (next) => {
    if (!isControlled) setInternal(next);
    onChange && onChange(next);
  };
  const patch = (i, part) => {
    const next = rows.map((r, j) => {
      if (j !== i) return r;
      const merged = { ...r, ...part };
      if (part.field != null && part.field !== r.field) {
        const f = fieldByKey(fields, part.field);
        merged.op = (opsFor(f)[0] || {}).id;
        merged.value = ""; merged.value2 = "";
      }
      return merged;
    });
    commit(next);
  };

  const activeCount = rows.filter((r) => rowActive(fields, r)).length;

  const valueInput = (r, i) => {
    const f = fieldByKey(fields, r.field);
    if (!needsValue(r.op)) return <span style={{ flex: "1 1 120px" }} />;
    if (f && f.type === "enum") {
      const opts = (f.options || []).map((o) => (typeof o === "string" ? { value: o, label: o } : o));
      return (
        <select className="mds-fb__ctl mds-fb__ctl--value" aria-label="Filter value"
          value={r.value} onChange={(e) => patch(i, { value: e.target.value })}>
          <option value="">Select…</option>
          {opts.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      );
    }
    const type = f && f.type === "number" ? "number" : f && f.type === "date" ? "date" : "text";
    return (
      <React.Fragment>
        <input className="mds-fb__ctl mds-fb__ctl--value" aria-label="Filter value" type={type}
          placeholder={type === "text" ? "Value…" : undefined}
          value={r.value} onChange={(e) => patch(i, { value: e.target.value })} />
        {needsSecond(r.op) && (
          <React.Fragment>
            <span style={{ font: "10px var(--font-data)", color: "var(--text-muted,#59636F)" }}>and</span>
            <input className="mds-fb__ctl mds-fb__ctl--value" aria-label="Filter upper bound" type={type}
              value={r.value2} onChange={(e) => patch(i, { value2: e.target.value })} />
          </React.Fragment>
        )}
      </React.Fragment>
    );
  };

  return (
    <div className={`mds-fb${className ? " " + className : ""}`} role="group" aria-label="Filters" {...rest}>
      {rows.map((r, i) => (
        <div className="mds-fb__row" key={i}>
          <span className="mds-fb__and" aria-hidden="true">{i === 0 ? "WHERE" : "AND"}</span>
          <select className="mds-fb__ctl mds-fb__ctl--field" aria-label="Filter field"
            value={r.field} onChange={(e) => patch(i, { field: e.target.value })}>
            {fields.map((f) => <option key={f.key} value={f.key}>{f.label || f.key}</option>)}
          </select>
          <select className="mds-fb__ctl mds-fb__ctl--op" aria-label="Filter operator"
            value={r.op} onChange={(e) => patch(i, { op: e.target.value })}>
            {opsFor(fieldByKey(fields, r.field)).map((o) => <option key={o.id} value={o.id}>{o.label}</option>)}
          </select>
          {valueInput(r, i)}
          <button type="button" className="mds-fb__x" aria-label="Remove filter"
            onClick={() => commit(rows.length === 1 ? [newRow(fields)] : rows.filter((_, j) => j !== i))}>✕</button>
        </div>
      ))}
      <div className="mds-fb__foot">
        <button type="button" className="mds-fb__btn mds-fb__btn--add" onClick={() => commit([...rows, newRow(fields)])}>{addLabel}</button>
        {showApply && (
          <button type="button" className="mds-fb__btn mds-fb__btn--apply"
            onClick={() => onApply && onApply(rows, buildPredicate(fields, rows))}>
            Apply{activeCount > 0 ? ` · ${activeCount}` : ""}
          </button>
        )}
        <button type="button" className="mds-fb__btn mds-fb__btn--clear" onClick={() => {
          const cleared = [newRow(fields)];
          commit(cleared);
          onApply && onApply(cleared, buildPredicate(fields, cleared));
        }}>Clear</button>
        {summary != null && <span className="mds-fb__summary">{summary}</span>}
      </div>
    </div>
  );
}

FilterBuilder.predicate = buildPredicate;
FilterBuilder.displayName = "FilterBuilder";
