// Meridian CoverageMatrix — symbol × session data-availability heat grid for a market-data
// platform: which instrument has which day, at what completeness. Statuses render as flat
// alpha washes (full / partial / gap / pending / none) on a hairline grid; hover writes a
// mono readout line under the grid — evidence, not decoration. With onCellClick the grid is
// ONE tab stop: arrows move the focused cell, Home/End jump within the row (Ctrl+Home/End to
// the grid corners), Enter/Space activates — the readout follows keyboard focus too.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-covm{font-family:var(--font-body);display:inline-flex;flex-direction:column;gap:8px;max-width:100%;}
.mds-covm__scroll{overflow:auto;max-width:100%;}
.mds-covm__grid{display:grid;gap:2px;align-items:center;width:max-content;}
.mds-covm__rowlab{font-family:var(--font-data);font-size:11px;color:var(--text-secondary,#4D5967);
  padding-right:10px;white-space:nowrap;text-align:right;}
.mds-covm__collab{font-family:var(--font-data);font-size:9px;color:var(--text-muted,#59636F);
  writing-mode:vertical-rl;transform:rotate(180deg);justify-self:center;padding-top:2px;line-height:1;}
.mds-covm__cell{box-sizing:border-box;border:1px solid var(--border,#CBD3DC);background:var(--bg-light,#FFFFFF);
  padding:0;display:block;border-radius:0;}
button.mds-covm__cell{cursor:pointer;}
.mds-covm__cell:hover{border-color:var(--text-primary,#22272E);position:relative;z-index:1;}
.mds-covm__cell:focus-visible{outline:var(--focus-ring);outline-offset:1px;position:relative;z-index:1;}
.mds-covm__cell--full{background:var(--green-a20,rgba(22,136,95,.2));border-color:var(--green,#16885F);}
.mds-covm__cell--partial{background:var(--orange-a20,rgba(138,82,14,.2));border-color:var(--orange,#8A520E);}
.mds-covm__cell--gap{background:var(--red-a20,rgba(186,63,85,.2));border-color:var(--red,#BA3F55);}
.mds-covm__cell--pending{background:var(--bg-hover,#EAEEF3);border-color:var(--border,#CBD3DC);}
.mds-covm__cell--none{background:transparent;border-color:var(--border,#CBD3DC);opacity:.45;}
.mds-covm__foot{display:flex;align-items:baseline;gap:16px;flex-wrap:wrap;}
.mds-covm__readout{font-family:var(--font-data);font-size:11px;color:var(--text-primary,#22272E);min-height:15px;}
.mds-covm__readout--idle{color:var(--text-muted,#59636F);}
.mds-covm__legend{display:flex;align-items:center;gap:10px;margin-left:auto;}
.mds-covm__key{display:inline-flex;align-items:center;gap:5px;font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.04em;color:var(--text-muted,#59636F);}
.mds-covm__swatch{width:10px;height:10px;box-sizing:border-box;display:inline-block;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "coverage-matrix");
  el.textContent = css;
  document.head.appendChild(el);
}

const STATUS_LABEL = {
  full: "Full", partial: "Partial", gap: "Gap", pending: "Pending", none: "No session",
};

function norm(entry) {
  if (entry == null) return { status: "none" };
  if (typeof entry === "string") return { status: entry };
  return entry;
}

export function CoverageMatrix({
  rows = [],
  cols = [],
  data,
  cell,
  cellSize = 16,
  colLabels = true,
  legend = true,
  readout = true,
  onCellClick,
  className = "",
  ...rest
}) {
  inject();
  const [hover, setHover] = React.useState(null);
  const R = rows.map((r) => (typeof r === "string" ? { id: r, label: r } : r));
  const C = cols.map((c) => (typeof c === "string" ? { id: c, label: c } : c));
  const lookup = (r, c) =>
    norm(cell ? cell(r, c) : data && data[r.id] ? data[r.id][c.id] : undefined);

  const interactive = !!onCellClick;
  const CellTag = interactive ? "button" : "span";
  const template = `max-content repeat(${C.length}, ${cellSize}px)`;

  // Roving tabindex: one tab stop, arrows move the focused cell.
  const [focusPos, setFocusPos] = React.useState([0, 0]);
  const cellRefs = React.useRef({});
  const onKeyDown = (e) => {
    if (!interactive || !R.length || !C.length) return;
    let [ri, ci] = focusPos;
    if (e.key === "ArrowRight") ci = Math.min(C.length - 1, ci + 1);
    else if (e.key === "ArrowLeft") ci = Math.max(0, ci - 1);
    else if (e.key === "ArrowDown") ri = Math.min(R.length - 1, ri + 1);
    else if (e.key === "ArrowUp") ri = Math.max(0, ri - 1);
    else if (e.key === "Home") { if (e.ctrlKey) ri = 0; ci = 0; }
    else if (e.key === "End") { if (e.ctrlKey) ri = R.length - 1; ci = C.length - 1; }
    else return;
    e.preventDefault();
    setFocusPos([ri, ci]);
    const el = cellRefs.current[ri + "-" + ci];
    if (el) el.focus();
  };

  const describe = (r, c, e) =>
    `${r.label} · ${c.label} · ${STATUS_LABEL[e.status] || e.status}${e.detail ? ` (${e.detail})` : ""}`;

  return (
    <div className={`mds-covm${className ? " " + className : ""}`} {...rest}>
      <div className="mds-covm__scroll">
        <div className="mds-covm__grid" style={{ gridTemplateColumns: template }} role="grid" aria-label="Coverage matrix" onKeyDown={onKeyDown}>
          {colLabels && (
            <React.Fragment>
              <span />
              {C.map((c) => (
                <span key={c.id} className="mds-covm__collab">{c.label}</span>
              ))}
            </React.Fragment>
          )}
          {R.map((r, ri) => (
            <React.Fragment key={r.id}>
              <span className="mds-covm__rowlab" role="rowheader">{r.label}</span>
              {C.map((c, ci) => {
                const e = lookup(r, c);
                const label = describe(r, c, e);
                return (
                  <CellTag
                    key={c.id}
                    type={interactive ? "button" : undefined}
                    className={`mds-covm__cell mds-covm__cell--${e.status || "none"}`}
                    style={{ width: cellSize, height: cellSize }}
                    title={label}
                    aria-label={label}
                    ref={interactive ? (el) => { cellRefs.current[ri + "-" + ci] = el; } : undefined}
                    tabIndex={interactive ? (focusPos[0] === ri && focusPos[1] === ci ? 0 : -1) : undefined}
                    onMouseEnter={() => setHover(label)}
                    onMouseLeave={() => setHover(null)}
                    onFocus={() => { setHover(label); if (interactive) setFocusPos([ri, ci]); }}
                    onBlur={() => setHover(null)}
                    onClick={interactive ? () => onCellClick(r, c, e) : undefined}
                  />
                );
              })}
            </React.Fragment>
          ))}
        </div>
      </div>
      {(readout || legend) && (
        <div className="mds-covm__foot">
          {readout && (
            <span className={`mds-covm__readout${hover ? "" : " mds-covm__readout--idle"}`} aria-live="polite">
              {hover || "Hover a cell for the session readout"}
            </span>
          )}
          {legend && (
            <span className="mds-covm__legend">
              {["full", "partial", "gap", "pending", "none"].map((s) => (
                <span key={s} className="mds-covm__key">
                  <span className={`mds-covm__swatch mds-covm__cell mds-covm__cell--${s}`} aria-hidden="true" />
                  {STATUS_LABEL[s]}
                </span>
              ))}
            </span>
          )}
        </div>
      )}
    </div>
  );
}

CoverageMatrix.displayName = "CoverageMatrix";
