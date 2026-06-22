// Meridian Select — single-value institutional dropdown. Matches Input height/border/focus.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-sel-wrap{position:relative;display:inline-block;width:100%;}
.mds-sel-btn{width:100%;height:34px;padding:7px 32px 7px 10px;
  border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-button,6px);
  background:var(--bg-light,#fff);color:var(--text-primary,#22272E);
  font-family:var(--font-data);font-size:13px;text-align:left;cursor:pointer;
  transition:border-color .12s ease,box-shadow .12s ease;appearance:none;
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-sel-btn:hover{border-color:var(--border-hover,#B8C2CC);}
.mds-sel-btn:focus,.mds-sel-btn[aria-expanded="true"]{outline:none;
  border-color:var(--border-focus,#2F6F8F);box-shadow:0 0 0 2px rgba(47,111,143,.20);}
.mds-sel-btn--placeholder{color:var(--text-disabled,#9AA4AF);}
.mds-sel-caret{position:absolute;right:10px;top:50%;transform:translateY(-50%);
  pointer-events:none;color:var(--text-muted,#6E7781);font-size:10px;}
.mds-sel-menu{position:absolute;top:calc(100% + 4px);left:0;right:0;z-index:200;
  background:var(--bg-light,#fff);border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-button,6px);
  box-shadow:0 4px 16px rgba(23,26,31,.12),0 1px 4px rgba(23,26,31,.08);
  overflow:hidden;max-height:240px;overflow-y:auto;}
.mds-sel-opt{padding:8px 12px;font-family:var(--font-data);font-size:12px;
  color:var(--text-primary,#22272E);cursor:pointer;display:flex;align-items:center;gap:8px;
  transition:background 80ms ease;}
.mds-sel-opt:hover{background:var(--bg-active,#E6EEF5);}
.mds-sel-opt--active{background:var(--bg-active,#E6EEF5);color:var(--accent,#2F6F8F);font-weight:600;}
.mds-sel-opt--active::after{content:"✓";margin-left:auto;font-size:11px;}
.mds-sel-divider{height:1px;background:var(--border,#D7DCE2);margin:4px 0;}
.mds-sel-label{display:block;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#6E7781);margin-bottom:5px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds","select");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Select({ label, options = [], value, onChange, placeholder = "Select…", disabled = false }) {
  inject();
  const [open, setOpen] = React.useState(false);
  const ref = React.useRef(null);

  React.useEffect(() => {
    const handler = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  const selected = options.find(o => (o.value ?? o) === value);
  const displayLabel = selected ? (selected.label ?? selected) : null;

  const handleKey = (e) => {
    if (e.key === "Escape") setOpen(false);
    if (e.key === "Enter" || e.key === " ") { e.preventDefault(); setOpen(o => !o); }
  };

  return (
    <div ref={ref}>
      {label && <span className="mds-sel-label">{label}</span>}
      <div className="mds-sel-wrap">
        <button
          type="button"
          className={`mds-sel-btn${!displayLabel ? " mds-sel-btn--placeholder" : ""}`}
          aria-expanded={open}
          aria-haspopup="listbox"
          disabled={disabled}
          onClick={() => setOpen(o => !o)}
          onKeyDown={handleKey}
        >
          {displayLabel ?? placeholder}
        </button>
        <span className="mds-sel-caret">▾</span>
        {open && (
          <div className="mds-sel-menu" role="listbox">
            {options.map((opt, i) => {
              if (opt === "---") return <div key={i} className="mds-sel-divider" />;
              const v = opt.value ?? opt;
              const l = opt.label ?? opt;
              return (
                <div key={i} role="option" aria-selected={v === value}
                  className={`mds-sel-opt${v === value ? " mds-sel-opt--active" : ""}`}
                  onClick={() => { onChange?.(v); setOpen(false); }}
                >
                  {l}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
