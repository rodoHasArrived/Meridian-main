// Meridian ColumnManager — the keyboard-accessible counterpart to the table's mouse-only
// header drag/resize/pin. A popover panel listing every column in order with: move up/down,
// pin toggle, show/hide, and width steppers — all real buttons, fully operable by keyboard and
// screen reader. Binds directly to a useTableColumns() return value.
//
//   const cols = useTableColumns(BASE, { persistKey: "blotter.cols" });
//   <ColumnManager cols={cols} />
//   <DenseDataTable columns={cols.visibleColumns} onColumnResize={cols.resize} … />
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-colmgr{position:relative;display:inline-block;font-family:var(--font-body);}
.mds-colmgr__btn{display:inline-flex;align-items:center;gap:6px;padding:6px 11px;
  border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-button,2px);
  background:var(--bg-light,#fff);color:var(--text-secondary,#4D5967);font-size:12px;cursor:pointer;}
.mds-colmgr__btn:hover{background:var(--bg-active,#E6EEF5);border-color:var(--accent,#2F6F8F);}
.mds-colmgr__btn:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:1px;}
.mds-colmgr__btn--active{border-color:var(--accent,#2F6F8F);color:var(--accent,#2F6F8F);}
.mds-colmgr__menu{position:absolute;top:calc(100% + 4px);right:0;z-index:var(--z-dropdown,100);
  width:280px;background:var(--bg-light,#fff);border:1px solid var(--border-strong,#99A5B2);
  box-shadow:var(--shadow-menu);}
.mds-colmgr__head{display:flex;align-items:center;justify-content:space-between;padding:8px 12px;
  border-bottom:1px solid var(--border,#CBD3DC);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.04em;color:var(--text-muted,#59636F);}
.mds-colmgr__reset{appearance:none;border:none;background:transparent;cursor:pointer;
  font-family:var(--font-body);font-size:11px;color:var(--accent,#2F6F8F);padding:0;font-variant:normal;letter-spacing:0;}
.mds-colmgr__reset:hover{text-decoration:underline;}
.mds-colmgr__row{display:grid;grid-template-columns:auto 1fr auto;align-items:center;gap:8px;
  padding:6px 12px;border-top:1px solid var(--border-divider,#D2D9E2);}
.mds-colmgr__row:first-of-type{border-top:none;}
.mds-colmgr__row--hidden .mds-colmgr__label{color:var(--text-muted,#59636F);text-decoration:line-through;}
.mds-colmgr__vis{appearance:none;width:15px;height:15px;border:1.5px solid var(--border,#CBD3DC);
  background:var(--bg-light,#fff);cursor:pointer;flex:none;display:inline-flex;align-items:center;
  justify-content:center;font-size:9px;color:var(--text-on-accent,#fff);border-radius:2px;padding:0;}
.mds-colmgr__vis--on{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);}
.mds-colmgr__vis:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:1px;}
.mds-colmgr__label{font-size:12px;color:var(--text-primary,#22272E);overflow:hidden;
  text-overflow:ellipsis;white-space:nowrap;display:flex;align-items:center;gap:6px;}
.mds-colmgr__pintag{font-size:8px;font-weight:700;font-variant:all-small-caps;letter-spacing:.05em;
  color:var(--accent-dim,#255B75);border:1px solid var(--accent,#2F6F8F);border-radius:2px;padding:0 3px;}
.mds-colmgr__ctrls{display:flex;align-items:center;gap:2px;}
.mds-colmgr__ic{appearance:none;border:1px solid transparent;background:transparent;cursor:pointer;
  width:22px;height:22px;display:inline-flex;align-items:center;justify-content:center;
  color:var(--text-muted,#59636F);font-size:12px;line-height:1;border-radius:2px;padding:0;}
.mds-colmgr__ic:hover:not(:disabled){background:var(--bg-active,#E6EEF5);color:var(--text-primary,#22272E);}
.mds-colmgr__ic:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:-1px;}
.mds-colmgr__ic:disabled{opacity:.3;cursor:not-allowed;}
.mds-colmgr__ic--on{color:var(--accent,#2F6F8F);}
.mds-colmgr__w{font-family:var(--font-data);font-size:10px;color:var(--text-muted,#59636F);
  min-width:30px;text-align:right;font-variant-numeric:tabular-nums;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "colmanager");
  el.textContent = css;
  document.head.appendChild(el);
}

export function ColumnManager({ cols, label = "Columns", widthStep = 20 }) {
  inject();
  const [open, setOpen] = React.useState(false);
  const ref = React.useRef(null);
  React.useEffect(() => {
    if (!open) return;
    const onDown = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    const onKey = (e) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => { document.removeEventListener("mousedown", onDown); document.removeEventListener("keydown", onKey); };
  }, [open]);

  const byKey = Object.fromEntries(cols.allColumns.map((c) => [c.key, c]));
  const rows = cols.order.filter((k) => byKey[k]);
  const hiddenCount = rows.filter((k) => cols.hidden[k]).length;
  const pinnedSet = new Set(cols.pinnedKeys);

  return (
    <div className="mds-colmgr" ref={ref}>
      <button type="button" aria-haspopup="true" aria-expanded={open}
        className={`mds-colmgr__btn${hiddenCount > 0 ? " mds-colmgr__btn--active" : ""}`}
        onClick={() => setOpen((o) => !o)}>
        {label}{hiddenCount > 0 && ` · ${hiddenCount} hidden`}
      </button>
      {open && (
        <div className="mds-colmgr__menu" role="dialog" aria-label="Manage columns">
          <div className="mds-colmgr__head">
            <span>Order · pin · width · show</span>
            <button type="button" className="mds-colmgr__reset" onClick={cols.reset}>Reset</button>
          </div>
          {rows.map((key, i) => {
            const col = byKey[key];
            const hidden = !!cols.hidden[key];
            const pinned = pinnedSet.has(key);
            return (
              <div key={key} className={`mds-colmgr__row${hidden ? " mds-colmgr__row--hidden" : ""}`}>
                <button type="button" role="checkbox" aria-checked={!hidden}
                  aria-label={`${hidden ? "Show" : "Hide"} ${col.label}`}
                  className={`mds-colmgr__vis${hidden ? "" : " mds-colmgr__vis--on"}`}
                  onClick={() => cols.toggleVisible(key)}>{hidden ? "" : "✓"}</button>
                <span className="mds-colmgr__label">
                  {col.label}
                  {pinned && <span className="mds-colmgr__pintag">pin</span>}
                </span>
                <span className="mds-colmgr__ctrls">
                  <button type="button" className="mds-colmgr__ic" aria-label={`Move ${col.label} up`}
                    disabled={i === 0} onClick={() => cols.move(key, -1)}>▲</button>
                  <button type="button" className="mds-colmgr__ic" aria-label={`Move ${col.label} down`}
                    disabled={i === rows.length - 1} onClick={() => cols.move(key, 1)}>▼</button>
                  <button type="button" className={`mds-colmgr__ic${pinned ? " mds-colmgr__ic--on" : ""}`}
                    aria-label={`${pinned ? "Unpin" : "Pin"} ${col.label}`} aria-pressed={pinned}
                    onClick={() => cols.togglePin(key)}>⊟</button>
                  <button type="button" className="mds-colmgr__ic" aria-label={`Narrow ${col.label}`}
                    onClick={() => cols.resize(key, (cols.widths[key] || 140) - widthStep)}>−</button>
                  <span className="mds-colmgr__w">{cols.widths[key] || 140}</span>
                  <button type="button" className="mds-colmgr__ic" aria-label={`Widen ${col.label}`}
                    onClick={() => cols.resize(key, (cols.widths[key] || 140) + widthStep)}>+</button>
                </span>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
