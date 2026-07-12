// Meridian DepthLadder — classic price ladder (DOM view): bidSize | price | askSize rows
// around a spread row. Depth renders as washed green/red horizontal bars behind the size
// cells — never solid fills. Mono numerals throughout; last-price marker on its row;
// optional onPriceClick turns price cells into click targets for ticket prefill — the price
// column then becomes ONE tab stop: ↑/↓ move between levels, Home/End jump to the extremes,
// Enter/Space fires the click (roving tabindex, starting at the best bid).
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-ladder{font-family:var(--font-data);font-size:12px;display:inline-flex;flex-direction:column;
  border:1px solid var(--border,#CBD3DC);background:var(--bg-light,#FFFFFF);min-width:264px;}
.mds-ladder__head{display:grid;grid-template-columns:1fr 88px 1fr;border-bottom:1px solid var(--border,#CBD3DC);
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.05em;color:var(--text-muted,#59636F);}
.mds-ladder__head>span{padding:4px 8px;}
.mds-ladder__head>span:first-child{text-align:right;}
.mds-ladder__head>span:nth-child(2){text-align:center;border-left:1px solid var(--border,#CBD3DC);
  border-right:1px solid var(--border,#CBD3DC);}
.mds-ladder__row{display:grid;grid-template-columns:1fr 88px 1fr;height:var(--ladder-row,22px);
  align-items:stretch;}
.mds-ladder__row+.mds-ladder__row{border-top:1px solid var(--bg-hover,#EAEEF3);}
.mds-ladder__size{position:relative;display:flex;align-items:center;overflow:hidden;}
.mds-ladder__size--bid{justify-content:flex-end;}
.mds-ladder__size--ask{justify-content:flex-start;}
.mds-ladder__bar{position:absolute;top:2px;bottom:2px;}
.mds-ladder__bar--bid{right:0;background:var(--green-a20,rgba(22,136,95,.2));}
.mds-ladder__bar--ask{left:0;background:var(--red-a20,rgba(186,63,85,.2));}
.mds-ladder__num{position:relative;padding:0 8px;color:var(--text-primary,#22272E);font-variant-numeric:tabular-nums;}
.mds-ladder__price{display:flex;align-items:center;justify-content:center;gap:5px;
  border-left:1px solid var(--border,#CBD3DC);border-right:1px solid var(--border,#CBD3DC);
  font-variant-numeric:tabular-nums;background:none;font:inherit;color:var(--text-secondary,#4D5967);
  padding:0;margin:0;border-top:0;border-bottom:0;}
.mds-ladder__price--bid{color:var(--green,#16885F);font-weight:600;}
.mds-ladder__price--ask{color:var(--red,#BA3F55);font-weight:600;}
button.mds-ladder__price{cursor:pointer;}
button.mds-ladder__price:hover{background:var(--bg-hover,#EAEEF3);}
button.mds-ladder__price:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.mds-ladder__last{width:0;height:0;border-style:solid;border-width:4px 0 4px 5px;
  border-color:transparent transparent transparent var(--text-primary,#22272E);}
.mds-ladder__spread{display:grid;grid-template-columns:1fr 88px 1fr;height:20px;align-items:center;
  border-top:1px solid var(--border,#CBD3DC);border-bottom:1px solid var(--border,#CBD3DC);
  background:var(--card-surface-raised,#F4F6F9);}
.mds-ladder__spreadlab{grid-column:2;display:flex;align-items:center;justify-content:center;gap:6px;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.05em;color:var(--text-muted,#59636F);border-left:1px solid var(--border,#CBD3DC);
  border-right:1px solid var(--border,#CBD3DC);height:100%;white-space:nowrap;}
.mds-ladder__spreadval{font-family:var(--font-data);font-variant-numeric:tabular-nums;
  color:var(--text-secondary,#4D5967);font-weight:400;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "depth-ladder");
  el.textContent = css;
  document.head.appendChild(el);
}

function fmt(n, dp) {
  return typeof n === "number" ? n.toFixed(dp) : "";
}
function fmtSize(n) {
  return typeof n === "number" ? n.toLocaleString("en-US") : "";
}

export function DepthLadder({
  bids = [],
  asks = [],
  lastPrice = null,
  priceDecimals = 2,
  levels = 8,
  onPriceClick,
  className = "",
  ...rest
}) {
  inject();
  const A = asks.slice(0, levels);
  const B = bids.slice(0, levels);
  const maxSize = Math.max(1, ...A.map((l) => l.size || 0), ...B.map((l) => l.size || 0));

  const bestBid = B.length ? B[0].price : null;
  const bestAsk = A.length ? A[0].price : null;
  const spread = bestBid != null && bestAsk != null ? bestAsk - bestBid : null;
  const mid = spread != null ? (bestAsk + bestBid) / 2 : null;
  const spreadBps = spread != null && mid ? (spread / mid) * 10000 : null;

  const PriceTag = onPriceClick ? "button" : "span";

  // Roving tabindex over the price buttons, in visual order (asks top-down, then bids).
  const keyboard = !!onPriceClick;
  const visualCount = A.length + B.length;
  const [focusIdx, setFocusIdx] = React.useState(A.length); // best bid
  const btnRefs = React.useRef([]);
  const onKeyDown = (e) => {
    if (!keyboard || !visualCount) return;
    let i = Math.min(focusIdx, visualCount - 1);
    if (e.key === "ArrowDown") i = Math.min(visualCount - 1, i + 1);
    else if (e.key === "ArrowUp") i = Math.max(0, i - 1);
    else if (e.key === "Home") i = 0;
    else if (e.key === "End") i = visualCount - 1;
    else return;
    e.preventDefault();
    setFocusIdx(i);
    const el = btnRefs.current[i];
    if (el) el.focus();
  };

  const nearLast = (price) => {
    if (lastPrice == null) return false;
    const all = [...A, ...B];
    if (!all.length) return false;
    let best = all[0].price;
    for (const l of all) if (Math.abs(l.price - lastPrice) < Math.abs(best - lastPrice)) best = l.price;
    return price === best;
  };

  const row = (level, side, idx) => (
    <div className="mds-ladder__row" key={`${side}-${level.price}`} role="row">
      <span className="mds-ladder__size mds-ladder__size--bid" role="cell">
        {side === "bid" && (
          <React.Fragment>
            <span
              className="mds-ladder__bar mds-ladder__bar--bid"
              style={{ width: `${Math.min(100, (level.size / maxSize) * 100)}%` }}
              aria-hidden="true"
            />
            <span className="mds-ladder__num">{fmtSize(level.size)}</span>
          </React.Fragment>
        )}
      </span>
      <PriceTag
        type={onPriceClick ? "button" : undefined}
        className={`mds-ladder__price mds-ladder__price--${side}`}
        onClick={onPriceClick ? () => onPriceClick(level.price, side) : undefined}
        ref={keyboard ? (el) => { btnRefs.current[idx] = el; } : undefined}
        tabIndex={keyboard ? (Math.min(focusIdx, visualCount - 1) === idx ? 0 : -1) : undefined}
        onFocus={keyboard ? () => setFocusIdx(idx) : undefined}
        role="cell"
        aria-label={`${side} ${fmt(level.price, priceDecimals)}`}
      >
        {nearLast(level.price) && <span className="mds-ladder__last" aria-label="last price" />}
        {fmt(level.price, priceDecimals)}
      </PriceTag>
      <span className="mds-ladder__size mds-ladder__size--ask" role="cell">
        {side === "ask" && (
          <React.Fragment>
            <span
              className="mds-ladder__bar mds-ladder__bar--ask"
              style={{ width: `${Math.min(100, (level.size / maxSize) * 100)}%` }}
              aria-hidden="true"
            />
            <span className="mds-ladder__num">{fmtSize(level.size)}</span>
          </React.Fragment>
        )}
      </span>
    </div>
  );

  return (
    <div className={`mds-ladder${className ? " " + className : ""}`} role="table" aria-label="Depth ladder" onKeyDown={onKeyDown} {...rest}>
      <div className="mds-ladder__head" role="row">
        <span role="columnheader">Bid size</span>
        <span role="columnheader">Price</span>
        <span role="columnheader">Ask size</span>
      </div>
      {A.slice().reverse().map((l, i) => row(l, "ask", i))}
      <div className="mds-ladder__spread" role="row">
        <span className="mds-ladder__spreadlab" role="cell">
          Spread{" "}
          <span className="mds-ladder__spreadval">
            {spread != null ? `${fmt(spread, priceDecimals)} · ${spreadBps.toFixed(1)}bp` : "—"}
          </span>
        </span>
      </div>
      {B.map((l, i) => row(l, "bid", A.length + i))}
    </div>
  );
}

DepthLadder.displayName = "DepthLadder";
