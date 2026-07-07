// Meridian OptionChainTable — trading-grade option chain in the classic straddle layout:
// call columns | strike ladder | put columns (mirrored). ITM cells carry the accent
// alpha-10 wash (calls below spot, puts above); a spot marker row shows where the
// underlying trades; greeks/IV/OI columns are configurable. One tab stop — ↑/↓ move the
// focused strike row, Enter/Space selects (onSelect). Mono numerals throughout.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-ochain{font-family:var(--font-data);font-size:12px;border:1px solid var(--border,#CBD3DC);
  background:var(--bg-light,#FFFFFF);display:inline-block;min-width:100%;outline:none;}
.mds-ochain__grid{display:grid;}
.mds-ochain__group{padding:5px 8px;text-align:center;font-family:var(--font-body);font-size:10px;
  font-weight:600;font-variant:all-small-caps;letter-spacing:.06em;color:var(--text-secondary,#4D5967);
  background:var(--bg-medium,#EBEFF4);border-bottom:1px solid var(--border,#CBD3DC);}
.mds-ochain__group--strike{border-left:1px solid var(--border,#CBD3DC);border-right:1px solid var(--border,#CBD3DC);}
.mds-ochain__hdr{padding:4px 8px;text-align:right;font-family:var(--font-body);font-size:10px;
  font-weight:600;font-variant:all-small-caps;letter-spacing:.05em;color:var(--text-muted,#59636F);
  background:var(--bg-medium,#EBEFF4);border-bottom:1px solid var(--border-strong,#99A5B2);}
.mds-ochain__hdr--strike{text-align:center;border-left:1px solid var(--border,#CBD3DC);
  border-right:1px solid var(--border,#CBD3DC);}
.mds-ochain__row{display:contents;}
.mds-ochain__cell{padding:0 8px;height:var(--ochain-row,26px);display:flex;align-items:center;
  justify-content:flex-end;font-variant-numeric:tabular-nums;color:var(--text-secondary,#4D5967);
  border-bottom:1px solid var(--bg-hover,#EAEEF3);white-space:nowrap;}
.mds-ochain__cell--itm{background:var(--blue-a10,rgba(47,111,143,.1));}
.mds-ochain__cell--strike{justify-content:center;font-weight:600;color:var(--text-primary,#22272E);
  border-left:1px solid var(--border,#CBD3DC);border-right:1px solid var(--border,#CBD3DC);}
.mds-ochain__cell--up{color:var(--green-dim);}
.mds-ochain__cell--down{color:var(--red-dim);}
.mds-ochain--interactive .mds-ochain__cell{cursor:pointer;}
.mds-ochain__row:hover .mds-ochain__cell{background:var(--bg-hover,#EAEEF3);}
.mds-ochain__row:hover .mds-ochain__cell--itm{background:color-mix(in srgb,var(--accent) 16%,var(--bg-light));}
.mds-ochain__row--selected .mds-ochain__cell,
.mds-ochain__row--selected:hover .mds-ochain__cell{background:var(--bg-active,#D7E5F1);}
.mds-ochain__row--selected .mds-ochain__cell--strike{box-shadow:inset 3px 0 0 var(--accent,#2F6F8F);}
.mds-ochain__row--focus .mds-ochain__cell--strike{outline:var(--focus-ring);outline-offset:-2px;}
.mds-ochain__spot{grid-column:1/-1;display:flex;align-items:center;gap:8px;height:20px;padding:0 8px;
  background:var(--card-surface-raised,#F3F6F9);border-top:1px solid var(--accent,#2F6F8F);
  border-bottom:1px solid var(--border,#CBD3DC);}
.mds-ochain__spotlab{font-size:11px;color:var(--accent-dim,#255B75);font-variant-numeric:tabular-nums;font-weight:600;}
.mds-ochain__spotrule{flex:1;border-top:1px dashed color-mix(in srgb,var(--accent) 45%,transparent);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "option-chain-table");
  el.textContent = css;
  document.head.appendChild(el);
}

const COLS = {
  bid:    { label: "Bid",   fmt: (v, dp) => v.toFixed(dp) },
  ask:    { label: "Ask",   fmt: (v, dp) => v.toFixed(dp) },
  last:   { label: "Last",  fmt: (v, dp) => v.toFixed(dp) },
  change: { label: "Chg",   fmt: (v, dp) => `${v > 0 ? "+" : v < 0 ? "−" : ""}${Math.abs(v).toFixed(dp)}`, tone: true },
  delta:  { label: "Delta", fmt: (v) => v.toFixed(2) },
  gamma:  { label: "Gamma", fmt: (v) => v.toFixed(3) },
  theta:  { label: "Theta", fmt: (v) => v.toFixed(2) },
  vega:   { label: "Vega",  fmt: (v) => v.toFixed(2) },
  iv:     { label: "IV",    fmt: (v) => v.toFixed(1) + "%" },
  oi:     { label: "OI",    fmt: (v) => v.toLocaleString("en-US") },
  volume: { label: "Vol",   fmt: (v) => v.toLocaleString("en-US") },
};

export function OptionChainTable({
  rows = [],
  underlyingPrice = null,
  underlyingLabel = "underlying",
  columns = ["oi", "volume", "iv", "delta", "bid", "ask"],
  priceDecimals = 2,
  strikeDecimals = 2,
  spotRow = true,
  onSelect,
  selectedStrike,
  defaultSelectedStrike = null,
  className = "",
  ...rest
}) {
  inject();
  const sorted = rows.slice().sort((a, b) => a.strike - b.strike);
  const callCols = columns;
  const putCols = columns.slice().reverse();
  const nC = callCols.length, nP = putCols.length;

  const isControlled = selectedStrike !== undefined;
  const [internalSel, setInternalSel] = React.useState(defaultSelectedStrike);
  const selected = isControlled ? selectedStrike : internalSel;

  const atmIdx = React.useMemo(() => {
    if (underlyingPrice == null || !sorted.length) return 0;
    let best = 0;
    sorted.forEach((r, i) => {
      if (Math.abs(r.strike - underlyingPrice) < Math.abs(sorted[best].strike - underlyingPrice)) best = i;
    });
    return best;
  }, [rows, underlyingPrice]);

  const [focusIdx, setFocusIdx] = React.useState(atmIdx);
  const interactive = !!onSelect;

  const select = (r) => {
    if (!isControlled) setInternalSel(r.strike);
    if (onSelect) onSelect(r);
  };

  const onKeyDown = (e) => {
    if (!interactive || !sorted.length) return;
    let i = focusIdx;
    if (e.key === "ArrowDown") i = Math.min(sorted.length - 1, i + 1);
    else if (e.key === "ArrowUp") i = Math.max(0, i - 1);
    else if (e.key === "Home") i = 0;
    else if (e.key === "End") i = sorted.length - 1;
    else if (e.key === "Enter" || e.key === " ") { e.preventDefault(); select(sorted[focusIdx]); return; }
    else return;
    e.preventDefault();
    setFocusIdx(i);
  };

  const fmtCell = (side, key, r) => {
    const def = COLS[key] || { label: key, fmt: (v) => String(v) };
    const v = r[side] ? r[side][key] : undefined;
    if (v == null) return { text: "—", tone: null };
    const dp = priceDecimals;
    return { text: def.fmt(v, dp), tone: def.tone ? (v > 0 ? "up" : v < 0 ? "down" : null) : null };
  };

  const template = `repeat(${nC}, minmax(58px,1fr)) 80px repeat(${nP}, minmax(58px,1fr))`;

  // spot marker sits after the last strike below the underlying
  let spotAfter = -1;
  if (spotRow && underlyingPrice != null) {
    sorted.forEach((r, i) => { if (r.strike < underlyingPrice) spotAfter = i; });
  }

  const cells = (r, side, cols, itm) =>
    cols.map((key) => {
      const { text, tone } = fmtCell(side, key, r);
      return (
        <span key={side + key} role="gridcell"
          className={`mds-ochain__cell${itm ? " mds-ochain__cell--itm" : ""}${tone ? ` mds-ochain__cell--${tone}` : ""}`}>
          {text}
        </span>
      );
    });

  return (
    <div
      className={`mds-ochain${interactive ? " mds-ochain--interactive" : ""}${className ? " " + className : ""}`}
      role="grid" aria-label="Option chain"
      tabIndex={interactive ? 0 : undefined}
      onKeyDown={onKeyDown}
      {...rest}
    >
      <div className="mds-ochain__grid" style={{ gridTemplateColumns: template }}>
        <span className="mds-ochain__group" style={{ gridColumn: `span ${nC}` }}>Calls</span>
        <span className="mds-ochain__group mds-ochain__group--strike">Strike</span>
        <span className="mds-ochain__group" style={{ gridColumn: `span ${nP}` }}>Puts</span>

        {callCols.map((k) => (
          <span key={"ch" + k} className="mds-ochain__hdr" role="columnheader">{(COLS[k] || { label: k }).label}</span>
        ))}
        <span className="mds-ochain__hdr mds-ochain__hdr--strike" role="columnheader">Strike</span>
        {putCols.map((k) => (
          <span key={"ph" + k} className="mds-ochain__hdr" role="columnheader">{(COLS[k] || { label: k }).label}</span>
        ))}

        {sorted.map((r, i) => {
          const callItm = underlyingPrice != null && r.strike < underlyingPrice;
          const putItm = underlyingPrice != null && r.strike > underlyingPrice;
          const isSel = selected != null && r.strike === selected;
          const isFocus = interactive && i === focusIdx;
          return (
            <React.Fragment key={r.strike}>
              <div
                className={`mds-ochain__row${isSel ? " mds-ochain__row--selected" : ""}${isFocus ? " mds-ochain__row--focus" : ""}`}
                role="row" aria-selected={interactive ? isSel : undefined}
                onClick={interactive ? () => { setFocusIdx(i); select(r); } : undefined}
              >
                {cells(r, "call", callCols, callItm)}
                <span className="mds-ochain__cell mds-ochain__cell--strike" role="gridcell">
                  {r.strike.toFixed(strikeDecimals)}
                </span>
                {cells(r, "put", putCols, putItm)}
              </div>
              {i === spotAfter && (
                <div className="mds-ochain__spot" role="row" aria-label={`${underlyingLabel} ${underlyingPrice}`}>
                  <span className="mds-ochain__spotrule" aria-hidden="true"></span>
                  <span className="mds-ochain__spotlab">
                    {underlyingPrice.toFixed(priceDecimals)} · {underlyingLabel}
                  </span>
                  <span className="mds-ochain__spotrule" aria-hidden="true"></span>
                </div>
              )}
            </React.Fragment>
          );
        })}
      </div>
    </div>
  );
}

OptionChainTable.displayName = "OptionChainTable";
