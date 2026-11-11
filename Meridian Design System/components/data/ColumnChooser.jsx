// Meridian ColumnChooser — dropdown to toggle column visibility in a DataGrid.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-colchz{position:relative;display:inline-block;}
.mds-colchz__btn{display:inline-flex;align-items:center;gap:6px;padding:6px 11px;
  border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-button,2px);
  background:var(--bg-light,#F8F7F3);color:var(--text-secondary,#535C65);
  font-family:var(--font-body);font-size:12px;cursor:pointer;
  transition:background .1s ease,border-color .1s ease;}
.mds-colchz__btn:hover{background:var(--bg-active,#E7EDF2);border-color:var(--brand-signal,#C56343);}
.mds-colchz__btn--active{border-color:var(--interaction-selected,#496C8F);color:var(--interaction-selected,#496C8F);}
.mds-colchz__menu{position:absolute;top:calc(100% + 4px);right:0;z-index:200;min-width:180px;
  background:var(--bg-light,#F8F7F3);border:1px solid var(--border,#D2CDC3);
  border-radius:var(--radius-button,2px);
  box-shadow:var(--shadow-menu);overflow:hidden;}
.mds-colchz__head{padding:8px 12px 6px;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.04em;color:var(--text-muted,#6B737B);
  border-bottom:1px solid var(--border,#D2CDC3);}
.mds-colchz__item{display:flex;align-items:center;gap:9px;padding:8px 12px;cursor:pointer;
  appearance:none;border:0;background:transparent;width:100%;text-align:left;
  font-family:var(--font-body);font-size:12px;color:var(--text-primary,#20252B);
  transition:background .08s ease;}
.mds-colchz__item:hover{background:var(--bg-active,#E7EDF2);}
.mds-colchz__ck{width:14px;height:14px;border:1.5px solid var(--border,#D2CDC3);border-radius:2px;
  flex-shrink:0;display:flex;align-items:center;justify-content:center;font-size:9px;}
.mds-colchz__ck--on{background:var(--interaction-selected,#496C8F);border-color:var(--interaction-selected,#496C8F);color:var(--text-on-accent,#fff);}
.mds-colchz__foot{padding:8px 12px;border-top:1px solid var(--border,#D2CDC3);}
.mds-colchz__reset{appearance:none;border:none;background:transparent;
  font-family:var(--font-body);font-size:11px;color:var(--brand-signal,#C56343);cursor:pointer;padding:0;}
.mds-colchz__reset:hover{opacity:.75;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds","colchooser");
  el.textContent = css;
  document.head.appendChild(el);
}

export function ColumnChooser({ columns = [], visible, onChange }) {
  // columns: [{ key, label }]
  // visible: Set<string> or array of visible keys
  // onChange: (visibleKeys: string[]) => void
  inject();
  const [open, setOpen] = React.useState(false);
  const ref = React.useRef(null);
  const buttonRef = React.useRef(null);
  const visSet = React.useMemo(() => new Set(visible ?? columns.map(c => c.key)), [visible, columns]);

  React.useEffect(() => {
    const h = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    const key = (e) => { if (e.key === "Escape" && ref.current?.contains(e.target)) { e.stopPropagation(); setOpen(false); buttonRef.current?.focus(); } };
    document.addEventListener("mousedown", h);
    document.addEventListener("keydown", key);
    return () => { document.removeEventListener("mousedown", h); document.removeEventListener("keydown", key); };
  }, []);

  const toggle = (key) => {
    const next = new Set(visSet);
    if (next.has(key)) { if (next.size > 1) next.delete(key); }
    else next.add(key);
    onChange?.([...next]);
  };

  const reset = () => onChange?.(columns.map(c => c.key));
  const hiddenCount = columns.length - visSet.size;

  return (
    <div ref={ref} className="mds-colchz">
      <button type="button" ref={buttonRef} aria-expanded={open}
        className={`mds-colchz__btn${hiddenCount > 0 ? " mds-colchz__btn--active" : ""}`}
        onClick={() => setOpen(o => !o)}>
        Columns {hiddenCount > 0 && `· ${hiddenCount} hidden`}
      </button>
      {open && (
        <div className="mds-colchz__menu">
          <div className="mds-colchz__head">Show / hide columns</div>
          {columns.map(col => (
            <button type="button" role="checkbox" aria-checked={visSet.has(col.key)} key={col.key} className="mds-colchz__item" onClick={() => toggle(col.key)}>
              <span aria-hidden="true" className={`mds-colchz__ck${visSet.has(col.key) ? " mds-colchz__ck--on" : ""}`}>
                {visSet.has(col.key) && "✓"}
              </span>
              {col.label}
            </button>
          ))}
          {hiddenCount > 0 && (
            <div className="mds-colchz__foot">
              <button type="button" className="mds-colchz__reset" onClick={reset}>Reset to default</button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
