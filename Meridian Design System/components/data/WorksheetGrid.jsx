// Meridian WorksheetGrid — spreadsheet-style cell grid for formula-driven worksheets (strategy
// builders, scenario sheets). Letter columns + numbered rows, an optional Excel-style formula bar,
// click / arrow-key cell selection (Home/End for row extremes, Ctrl+Home/End for the sheet
// corners, PageUp/PageDown for 10-row jumps), accent-cornered formula cells, and tinted error cells.
// Controlled (activeCell + onActiveCellChange) or uncontrolled (defaultActiveCell). DenseDataTable
// is for row records; reach for this when the model is addressable cells with formulas.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-ws{display:flex;flex-direction:column;border:1px solid var(--border,#D7DCE2);background:var(--card-surface,#fff);}
.mds-ws__bar{display:flex;align-items:stretch;border-bottom:1px solid var(--border,#D7DCE2);}
.mds-ws__ref{width:60px;flex:none;display:flex;align-items:center;justify-content:center;border-right:1px solid var(--border,#D7DCE2);background:var(--bg-medium,#F5F7FA);font:600 13px var(--font-data);color:var(--text-primary,#22272E);}
.mds-ws__fx{width:32px;flex:none;display:flex;align-items:center;justify-content:center;border-right:1px solid var(--border,#D7DCE2);font:italic 600 12px var(--font-display);color:var(--text-muted,#59636F);}
.mds-ws__formula{flex:1;display:flex;align-items:center;padding:0 10px;min-height:30px;font:13px var(--font-data);color:var(--text-primary,#22272E);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-ws__scroll{overflow:auto;}
.mds-ws__grid{display:inline-block;min-width:100%;outline:none;}
.mds-ws__grid:focus-visible{outline:none;}
.mds-ws__row{display:flex;}
.mds-ws__corner{position:sticky;top:0;z-index:3;}
.mds-ws__cell{box-sizing:border-box;display:flex;align-items:center;padding:0 8px;border-right:1px solid var(--border,#D7DCE2);border-bottom:1px solid var(--border,#D7DCE2);font:13px var(--font-data);color:var(--text-primary,#22272E);background:var(--card-surface,#fff);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;cursor:cell;position:relative;user-select:none;transition:background-color 100ms ease;}
.mds-ws__cell:hover{background:var(--bg-hover,#F1F4F7);}
.mds-ws__cell--active{outline:2px solid var(--accent,#2F6F8F);outline-offset:-2px;z-index:2;}
.mds-ws__cell--active,.mds-ws__cell--active:hover{background:var(--card-surface,#fff);}
.mds-ws__cell--error,.mds-ws__cell--error:hover{background:var(--red-a10);color:var(--red-dim);}
.mds-ws__hdr,.mds-ws__rownum{box-sizing:border-box;display:flex;align-items:center;justify-content:center;font:600 11px var(--font-data);color:var(--text-muted,#59636F);background:var(--bg-medium,#F5F7FA);}
.mds-ws__hdr{border-right:1px solid var(--border,#D7DCE2);border-bottom:1px solid var(--border-strong,#AAB4BF);letter-spacing:.06em;}
.mds-ws__rownum{border-right:1px solid var(--border-strong,#AAB4BF);border-bottom:1px solid var(--border,#D7DCE2);position:sticky;left:0;z-index:2;}
.mds-ws__fxmark{position:absolute;top:0;left:0;width:0;height:0;border-top:6px solid var(--accent,#2F6F8F);border-right:6px solid transparent;}
.mds-ws__input{width:100%;height:100%;box-sizing:border-box;border:none;outline:2px solid var(--accent,#2F6F8F);outline-offset:-2px;background:var(--card-surface,#fff);color:var(--text-primary,#22272E);font:13px var(--font-data);padding:0 6px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "worksheet-grid");
  el.textContent = css;
  document.head.appendChild(el);
}

const parseRef = (ref) => {
  if (!ref) return null;
  const m = String(ref).match(/^([A-Za-z]+)(\d+)$/);
  return m ? { col: m[1], row: parseInt(m[2], 10) } : null;
};

export function WorksheetGrid({
  columns = [],
  rows = 10,
  cells = {},
  activeCell,
  defaultActiveCell = null,
  onActiveCellChange,
  showFormulaBar = true,
  rowHeight = 32,
  headerHeight = 28,
  rowHeaderWidth = 38,
  emptyFormulaText = "Empty cell",
  editable = false,
  onCellCommit,
  style = {},
}) {
  inject();
  const cols = columns.map((c) => (typeof c === "string" ? { key: c } : c));
  const isControlled = activeCell !== undefined;
  const [internal, setInternal] = React.useState(defaultActiveCell);
  const active = isControlled ? activeCell : internal;
  const activeParsed = parseRef(active);

  const select = (ref) => {
    if (!isControlled) setInternal(ref);
    if (onActiveCellChange) onActiveCellChange(ref);
  };

  const [editing, setEditing] = React.useState(null);
  const [draft, setDraft] = React.useState("");
  const inputRef = React.useRef(null);
  React.useEffect(() => {
    if (editing && inputRef.current) { inputRef.current.focus(); if (inputRef.current.select) inputRef.current.select(); }
  }, [editing]);

  const startEdit = (ref, initial) => {
    if (!editable || !ref) return;
    const c = cells[ref];
    setDraft(initial != null ? initial : (c ? (c.formula || (c.value != null ? String(c.value) : "")) : ""));
    setEditing(ref);
  };
  const commit = (moveDir) => {
    if (editing == null) return;
    const ref = editing;
    if (onCellCommit) onCellCommit(ref, draft);
    setEditing(null);
    const p = parseRef(ref);
    if (moveDir && p) {
      let ci = cols.findIndex((c) => c.key === p.col), ri = p.row;
      if (moveDir === "down") ri = Math.min(rows, ri + 1);
      else if (moveDir === "right") ci = Math.min(cols.length - 1, ci + 1);
      if (cols[ci]) select(cols[ci].key + ri);
    }
  };
  const onInputKeyDown = (e) => {
    e.stopPropagation();
    if (e.key === "Enter") { e.preventDefault(); commit("down"); }
    else if (e.key === "Tab") { e.preventDefault(); commit("right"); }
    else if (e.key === "Escape") { e.preventDefault(); setEditing(null); }
  };

  const onKeyDown = (e) => {
    if (editing != null) return;
    if (!activeParsed) return;
    if (editable && (e.key === "Enter" || e.key === "F2")) { e.preventDefault(); startEdit(active); return; }
    if (editable && e.key.length === 1 && !e.metaKey && !e.ctrlKey && !e.altKey) { e.preventDefault(); startEdit(active, e.key); return; }
    let ci = cols.findIndex((c) => c.key === activeParsed.col);
    let ri = activeParsed.row;
    if (e.key === "ArrowRight") ci = Math.min(cols.length - 1, ci + 1);
    else if (e.key === "ArrowLeft") ci = Math.max(0, ci - 1);
    else if (e.key === "ArrowDown") ri = Math.min(rows, ri + 1);
    else if (e.key === "ArrowUp") ri = Math.max(1, ri - 1);
    else if (e.key === "Home") { ci = 0; if (e.ctrlKey) ri = 1; }
    else if (e.key === "End") { ci = cols.length - 1; if (e.ctrlKey) ri = rows; }
    else if (e.key === "PageDown") ri = Math.min(rows, ri + 10);
    else if (e.key === "PageUp") ri = Math.max(1, ri - 10);
    else return;
    e.preventDefault();
    if (cols[ci]) select(cols[ci].key + ri);
  };

  const activeObj = active ? cells[active] : null;
  const barText = activeObj ? (activeObj.formula || activeObj.value || "") : "";
  const barIsError = activeObj && activeObj.type === "error";

  const cellInner = (c, col) => {
    const t = c ? c.type : null;
    const mono = !(t === "label" || t === "text");
    const align = col.align || (mono ? "right" : "left");
    const w = col.width || 92;
    const s = {
      width: w, minWidth: w, height: rowHeight,
      justifyContent: align === "right" ? "flex-end" : "flex-start",
    };
    if (t === "label") { s.color = "var(--text-secondary,#4D5967)"; s.fontWeight = 600; s.fontFamily = "var(--font-body)"; }
    else if (t === "text") { s.fontFamily = "var(--font-body)"; }
    return s;
  };

  return (
    <div className="mds-ws" style={style}>
      {showFormulaBar && (
        <div className="mds-ws__bar">
          <div className="mds-ws__ref">{active || "—"}</div>
          <div className="mds-ws__fx">fx</div>
          <div className="mds-ws__formula" style={barIsError ? { color: "var(--red-dim)" } : undefined}>
            {barText || <span style={{ color: "var(--text-disabled,#889099)" }}>{emptyFormulaText}</span>}
          </div>
        </div>
      )}
      <div className="mds-ws__scroll">
        <div className="mds-ws__grid" tabIndex={0} role="grid" onKeyDown={onKeyDown}>
          <div className="mds-ws__row mds-ws__corner">
            <div className="mds-ws__hdr" style={{ width: rowHeaderWidth, minWidth: rowHeaderWidth, height: headerHeight, borderRightColor: "var(--border-strong,#AAB4BF)", position: "sticky", left: 0, zIndex: 4 }} />
            {cols.map((col) => {
              const on = activeParsed && activeParsed.col === col.key;
              const w = col.width || 92;
              return (
                <div key={col.key} className="mds-ws__hdr" style={{
                  width: w, minWidth: w, height: headerHeight,
                  color: on ? "var(--accent,#2F6F8F)" : undefined,
                  background: on ? "color-mix(in srgb, var(--accent) 12%, transparent)" : undefined,
                }}>{col.label != null ? col.label : col.key}</div>
              );
            })}
          </div>
          {Array.from({ length: rows }, (_, i) => i + 1).map((r) => {
            const rowOn = activeParsed && activeParsed.row === r;
            return (
              <div key={r} className="mds-ws__row">
                <div className="mds-ws__rownum" style={{ width: rowHeaderWidth, minWidth: rowHeaderWidth, height: rowHeight, color: rowOn ? "var(--accent,#2F6F8F)" : undefined }}>{r}</div>
                {cols.map((col) => {
                  const ref = col.key + r;
                  const c = cells[ref];
                  const cls = "mds-ws__cell"
                    + (active === ref ? " mds-ws__cell--active" : "")
                    + (c && c.type === "error" ? " mds-ws__cell--error" : "");
                  const isEditing = editing === ref;
                  const colAlign = col.align || (c && (c.type === "label" || c.type === "text") ? "left" : "right");
                  return (
                    <div key={ref} className={cls} style={cellInner(c, col)}
                      onClick={() => select(ref)}
                      onDoubleClick={() => startEdit(ref)}
                      title={c && c.formula ? c.formula : undefined}>
                      {isEditing ? (
                        <input ref={inputRef} className="mds-ws__input" value={draft}
                          style={{ textAlign: colAlign }}
                          onChange={(e) => setDraft(e.target.value)}
                          onKeyDown={onInputKeyDown}
                          onBlur={() => commit(null)} />
                      ) : (
                        <React.Fragment>
                          {c && c.type === "formula" && <span className="mds-ws__fxmark" />}
                          {c ? c.value : ""}
                        </React.Fragment>
                      )}
                    </div>
                  );
                })}
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
