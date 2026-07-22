// Meridian Combobox — searchable single-select. Type to filter, arrow keys to navigate.
// Functional only: no radius, no shadows, no transitions.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-cb-wrap{position:relative;width:100%;}
.mds-cb-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);margin-bottom:5px;}
.mds-cb-field{display:flex;align-items:center;width:100%;height:32px;
  border:1px solid var(--border,#D7DCE2);background:var(--bg-light,#FAFBFC);}
.mds-cb-field:focus-within{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-cb-input{flex:1;min-width:0;height:100%;padding:7px 10px;border:none;background:transparent;
  color:var(--text-primary,#22272E);font-family:var(--font-data);font-size:13px;outline:none;}
.mds-cb-input::placeholder{color:var(--text-disabled,#889099);}
.mds-cb-caret{padding:0 10px;color:var(--text-muted,#59636F);pointer-events:none;font-size:11px;}
.mds-cb-menu{position:absolute;top:calc(100% + 4px);left:0;right:0;z-index:100;
  background:var(--bg-light,#FAFBFC);border:1px solid var(--border,#D7DCE2);
  max-height:240px;overflow-y:auto;box-shadow:var(--shadow-menu,0 4px 12px rgba(0,0,0,.10));}
.mds-cb-opt{padding:8px 12px;font-family:var(--font-data);font-size:12px;
  color:var(--text-primary,#22272E);cursor:pointer;display:flex;align-items:center;gap:8px;}
.mds-cb-opt--hl{background:var(--bg-hover,#F1F4F7);}
.mds-cb-opt--active{background:var(--bg-active,#E1EAF2);color:var(--accent,#2F6F8F);font-weight:600;}
.mds-cb-opt--active::after{content:"✓";margin-left:auto;font-size:11px;}
.mds-cb-empty{padding:10px 12px;font-family:var(--font-body);font-size:12px;color:var(--text-muted,#59636F);}
.mds-cb-mark{background:var(--accent-ghost,#E8F0F7);color:var(--accent,#2F6F8F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "combobox");
  el.textContent = css;
  document.head.appendChild(el);
}

function highlight(label, query) {
  if (!query) return label;
  const idx = label.toLowerCase().indexOf(query.toLowerCase());
  if (idx === -1) return label;
  return (
    <>
      {label.slice(0, idx)}
      <span className="mds-cb-mark">{label.slice(idx, idx + query.length)}</span>
      {label.slice(idx + query.length)}
    </>
  );
}

export function Combobox({
  label,
  options = [],
  value,
  onChange,
  placeholder = "Search…",
  disabled = false,
  emptyText = "No matches",
}) {
  inject();
  const [open, setOpen] = React.useState(false);
  const [query, setQuery] = React.useState("");
  const [hl, setHl] = React.useState(0);
  const ref = React.useRef(null);

  const norm = options.map((o) => ({ value: o.value ?? o, label: String(o.label ?? o) }));
  const selected = norm.find((o) => o.value === value);
  const filtered = query
    ? norm.filter((o) => o.label.toLowerCase().includes(query.toLowerCase()))
    : norm;

  React.useEffect(() => {
    const handler = (e) => { if (ref.current && !ref.current.contains(e.target)) { setOpen(false); setQuery(""); } };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  React.useEffect(() => { setHl(0); }, [query]);

  const commit = (opt) => { onChange?.(opt.value); setOpen(false); setQuery(""); };

  const handleKey = (e) => {
    if (e.key === "ArrowDown") { e.preventDefault(); setOpen(true); setHl((h) => Math.min(h + 1, filtered.length - 1)); }
    else if (e.key === "ArrowUp") { e.preventDefault(); setHl((h) => Math.max(h - 1, 0)); }
    else if (e.key === "Enter") { e.preventDefault(); if (filtered[hl]) commit(filtered[hl]); }
    else if (e.key === "Escape") { setOpen(false); setQuery(""); }
  };

  return (
    <div ref={ref}>
      {label && <span className="mds-cb-label">{label}</span>}
      <div className="mds-cb-wrap">
        <div className="mds-cb-field">
          <input
            type="text"
            className="mds-cb-input"
            role="combobox"
            aria-expanded={open}
            aria-controls="mds-cb-listbox"
            aria-autocomplete="list"
            disabled={disabled}
            placeholder={selected && !open ? selected.label : placeholder}
            value={open ? query : (selected ? selected.label : "")}
            onFocus={() => setOpen(true)}
            onChange={(e) => { setQuery(e.target.value); setOpen(true); }}
            onKeyDown={handleKey}
          />
          <span className="mds-cb-caret">▾</span>
        </div>
        {open && (
          <div className="mds-cb-menu" role="listbox" id="mds-cb-listbox">
            {filtered.length === 0 && <div className="mds-cb-empty">{emptyText}</div>}
            {filtered.map((opt, i) => (
              <div
                key={opt.value}
                role="option"
                aria-selected={opt.value === value}
                className={`mds-cb-opt${i === hl ? " mds-cb-opt--hl" : ""}${opt.value === value ? " mds-cb-opt--active" : ""}`}
                onMouseEnter={() => setHl(i)}
                onClick={() => commit(opt)}
              >
                {highlight(opt.label, query)}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

Combobox.displayName = "Combobox";
