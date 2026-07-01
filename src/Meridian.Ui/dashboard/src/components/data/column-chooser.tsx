// Meridian ColumnChooser (Concrete) — a dropdown to toggle column visibility in a
// DenseDataTable / FilteredDataTable. Flat hairline-bordered trigger and menu; the menu
// carries the one tight hard-edged popover shadow. At least one column always stays
// visible.
import { useEffect, useMemo, useRef, useState } from "react";
import { injectStyle } from "../operations/inject-style";

const CSS = `
.mds-colchz{position:relative;display:inline-block;}
.mds-colchz__btn{display:inline-flex;align-items:center;gap:6px;padding:6px 11px;
  border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-button,2px);
  background:var(--bg-panel,var(--bg-light,#fff));color:var(--text-secondary,#4D5967);
  font-family:var(--font-body);font-size:12px;cursor:pointer;
  transition:background .1s ease,border-color .1s ease;}
.mds-colchz__btn:hover{background:var(--bg-active,#E6EEF5);border-color:var(--accent,#2F6F8F);}
.mds-colchz__btn:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:2px;}
.mds-colchz__btn--active{border-color:var(--accent,#2F6F8F);color:var(--accent,#2F6F8F);}
.mds-colchz__menu{position:absolute;top:calc(100% + 4px);right:0;z-index:200;min-width:180px;
  background:var(--bg-panel,var(--bg-light,#fff));border:1px solid var(--border,#CBD3DC);
  border-radius:var(--radius-button,2px);
  box-shadow:var(--shadow-menu,0 2px 6px rgba(0,0,0,.18));overflow:hidden;}
.mds-colchz__head{padding:8px 12px 6px;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.04em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border,#CBD3DC);}
.mds-colchz__item{display:flex;align-items:center;gap:9px;padding:8px 12px;cursor:pointer;width:100%;
  font-family:var(--font-body);font-size:12px;color:var(--text-primary,#22272E);text-align:left;
  background:transparent;border:none;transition:background .08s ease;}
.mds-colchz__item:hover{background:var(--bg-active,#E6EEF5);}
.mds-colchz__ck{width:14px;height:14px;border:1.5px solid var(--border,#CBD3DC);border-radius:2px;
  flex-shrink:0;display:flex;align-items:center;justify-content:center;font-size:9px;}
.mds-colchz__ck--on{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);color:white;}
.mds-colchz__foot{padding:8px 12px;border-top:1px solid var(--border,#CBD3DC);}
.mds-colchz__reset{appearance:none;border:none;background:transparent;
  font-family:var(--font-body);font-size:11px;color:var(--accent,#2F6F8F);cursor:pointer;padding:0;}
.mds-colchz__reset:hover{opacity:.75;}
`;

export interface ColumnChooserColumn {
  key: string;
  label: string;
}

export interface ColumnChooserProps {
  columns?: ColumnChooserColumn[];
  /** Visible column keys. Defaults to all columns visible. */
  visible?: string[];
  onChange?: (visibleKeys: string[]) => void;
}

/**
 * ColumnChooser — toggle which table columns are visible. The last visible column can't
 * be hidden, so a table is never left with zero columns.
 *
 * @example
 * <ColumnChooser columns={cols} visible={visible} onChange={setVisible} />
 */
export function ColumnChooser({ columns = [], visible, onChange }: ColumnChooserProps) {
  injectStyle("colchooser", CSS);
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const visSet = useMemo(
    () => new Set(visible ?? columns.map((c) => c.key)),
    [visible, columns],
  );

  useEffect(() => {
    if (!open) return;
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [open]);

  const toggle = (key: string) => {
    const next = new Set(visSet);
    if (next.has(key)) {
      if (next.size > 1) next.delete(key);
    } else {
      next.add(key);
    }
    onChange?.([...next]);
  };

  const reset = () => onChange?.(columns.map((c) => c.key));
  const hiddenCount = columns.length - visSet.size;

  return (
    <div ref={ref} className="mds-colchz">
      <button
        type="button"
        className={`mds-colchz__btn${hiddenCount > 0 ? " mds-colchz__btn--active" : ""}`}
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="true"
        aria-expanded={open}
      >
        Columns {hiddenCount > 0 && `· ${hiddenCount} hidden`}
      </button>
      {open && (
        <div className="mds-colchz__menu" role="menu">
          <div className="mds-colchz__head">Show / hide columns</div>
          {columns.map((col) => (
            <button
              type="button"
              key={col.key}
              className="mds-colchz__item"
              onClick={() => toggle(col.key)}
              role="menuitemcheckbox"
              aria-checked={visSet.has(col.key)}
            >
              <span className={`mds-colchz__ck${visSet.has(col.key) ? " mds-colchz__ck--on" : ""}`}>
                {visSet.has(col.key) && "✓"}
              </span>
              {col.label}
            </button>
          ))}
          {hiddenCount > 0 && (
            <div className="mds-colchz__foot">
              <button type="button" className="mds-colchz__reset" onClick={reset}>
                Reset to default
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
