// Meridian ColumnChooser — dropdown to toggle column visibility in DenseDataTable/FilteredDataTable.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-colchz{position:relative;display:inline-block;}
.mds-colchz__btn{display:inline-flex;align-items:center;gap:6px;padding:6px 11px;
  border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-button,6px);
  background:var(--bg-light,#fff);color:var(--text-secondary,#4D5967);
  font-family:var(--font-body);font-size:12px;cursor:pointer;
  transition:background .1s ease,border-color .1s ease;}
.mds-colchz__btn:hover{background:var(--bg-active,#E6EEF5);border-color:var(--accent,#2F6F8F);}
.mds-colchz__btn--active{border-color:var(--accent,#2F6F8F);color:var(--accent,#2F6F8F);}
.mds-colchz__menu{position:absolute;top:calc(100% + 4px);right:0;z-index:200;min-width:180px;
  background:var(--bg-light,#fff);border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-button,6px);
  box-shadow:0 4px 16px rgba(23,26,31,.12);overflow:hidden;}
.mds-colchz__head{padding:8px 12px 6px;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.04em;color:var(--text-muted,#6E7781);
  border-bottom:1px solid var(--border,#D7DCE2);}
.mds-colchz__item{display:flex;align-items:center;gap:9px;padding:8px 12px;cursor:pointer;
  font-family:var(--font-body);font-size:12px;color:var(--text-primary,#22272E);
  transition:background .08s ease;}
.mds-colchz__item:hover{background:var(--bg-active,#E6EEF5);}
.mds-colchz__ck{width:14px;height:14px;border:1.5px solid var(--border,#D7DCE2);border-radius:2px;
  flex-shrink:0;display:flex;align-items:center;justify-content:center;font-size:9px;}
.mds-colchz__ck--on{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);color:white;}
.mds-colchz__foot{padding:8px 12px;border-top:1px solid var(--border,#D7DCE2);}
.mds-colchz__reset{appearance:none;border:none;background:transparent;
  font-family:var(--font-body);font-size:11px;color:var(--accent,#2F6F8F);cursor:pointer;padding:0;}
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
  const visSet = React.useMemo(() => new Set(visible ?? columns.map(c => c.key)), [visible, columns]);

  React.useEffect(() => {
    const h = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    document.addEventListener("mousedown", h);
    return () => document.removeEventListener("mousedown", h);
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
      <button type="button"
        className={`mds-colchz__btn${hiddenCount > 0 ? " mds-colchz__btn--active" : ""}`}
        onClick={() => setOpen(o => !o)}>
        Columns {hiddenCount > 0 && `· ${hiddenCount} hidden`}
      </button>
      {open && (
        <div className="mds-colchz__menu">
          <div className="mds-colchz__head">Show / hide columns</div>
          {columns.map(col => (
            <div key={col.key} className="mds-colchz__item" onClick={() => toggle(col.key)}>
              <div className={`mds-colchz__ck${visSet.has(col.key) ? " mds-colchz__ck--on" : ""}`}>
                {visSet.has(col.key) && "✓"}
              </div>
              {col.label}
            </div>
          ))}
          {hiddenCount > 0 && (
            <div className="mds-colchz__foot">
              <button className="mds-colchz__reset" onClick={reset}>Reset to default</button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
