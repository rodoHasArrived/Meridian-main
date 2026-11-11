// Meridian TimeAndSalesTape — the market-wide print tape (FillsFeed covers only OWN
// executions). Newest first, mono rows: time, optional symbol, tick-direction-colored
// price (derived per symbol from the prior print, or explicit `dir`), size with bucket
// emphasis tiers, block prints washed + tagged, optional venue. Quiet: color and weight
// carry the signal, no animation.
import React from "react";
import { Timestamp } from "../core/Timestamp";
import { formatQuantity } from "../core/format.js";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-tape{border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);
  font-family:var(--font-data);font-size:11px;overflow-y:auto;}
.mds-tape__row{display:grid;gap:8px;padding:4px 10px;border-top:1px solid var(--border-divider,#E2DED5);
  align-items:baseline;font-variant-numeric:tabular-nums;}
.mds-tape__row:first-child{border-top:none;}
.mds-tape__row--block{background:var(--brand-signal-ghost,#F6E6DF);}
.mds-tape__time{color:var(--text-muted,#606870);font-size:10px;}
.mds-tape__sym{font-weight:600;color:var(--text-primary,#20252B);}
.mds-tape__px{text-align:right;}
.mds-tape__px--up{color:var(--green-dim,#225840);}
.mds-tape__px--down{color:var(--red-dim,#883832);}
.mds-tape__px--flat{color:var(--text-secondary,#535C65);}
.mds-tape__tick{font-size:8px;margin-left:4px;vertical-align:1px;}
.mds-tape__size{text-align:right;color:var(--text-muted,#606870);}
.mds-tape__size--t1{color:var(--text-secondary,#535C65);}
.mds-tape__size--t2{color:var(--text-primary,#20252B);font-weight:600;}
.mds-tape__blk{font-family:var(--font-body);font-size:9px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.05em;color:var(--brand-signal-dim,#CB6E4E);margin-left:6px;}
.mds-tape__venue{color:var(--text-muted,#606870);font-size:10px;text-align:right;}
.mds-tape__empty{padding:18px 10px;text-align:center;font-family:var(--font-body);
  font-size:12px;color:var(--text-muted,#606870);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "tape");
  el.textContent = css;
  document.head.appendChild(el);
}

export function TimeAndSalesTape({
  prints = [],            // newest first: { time, symbol?, price, size, venue?, dir? }
  showSymbol = false,
  showVenue = false,
  priceDecimals = 2,
  sizeBuckets = [1000, 5000],  // ascending thresholds → emphasis tiers
  blockSize = null,            // size ≥ this → block print (wash + tag)
  maxHeight = 300,
  emptyLabel = "No prints",
  locale,
}) {
  inject();
  // Tick direction per print vs the prior print of the SAME symbol (array is newest-first,
  // so walk from the oldest end). Explicit `dir` on a print wins.
  const dirs = React.useMemo(() => {
    const last = new Map();
    const out = new Array(prints.length);
    for (let i = prints.length - 1; i >= 0; i--) {
      const p = prints[i];
      const k = p.symbol || "";
      const prev = last.get(k);
      out[i] = p.dir || (prev == null || +p.price === +prev ? "flat" : +p.price > +prev ? "up" : "down");
      last.set(k, p.price);
    }
    return out;
  }, [prints]);

  const cols = ["76px", showSymbol && "minmax(52px,auto)", "1fr", "minmax(64px,auto)", showVenue && "auto"]
    .filter(Boolean).join(" ");
  const tier = (size) => {
    let t = 0;
    for (const b of sizeBuckets) if (+size >= b) t++;
    return Math.min(t, 2);
  };

  return (
    <div className="mds-tape" style={{ maxHeight }} role="log" aria-label="Time and sales">
      {prints.length === 0 && <div className="mds-tape__empty">{emptyLabel}</div>}
      {prints.map((p, i) => {
        const dir = dirs[i];
        const block = blockSize != null && +p.size >= blockSize;
        const n = +p.price;
        const px = isFinite(n)
          ? (locale ? n.toLocaleString(locale, { minimumFractionDigits: priceDecimals, maximumFractionDigits: priceDecimals }) : n.toFixed(priceDecimals))
          : String(p.price ?? "—");
        return (
          <div key={p.id || i} className={`mds-tape__row${block ? " mds-tape__row--block" : ""}`}
            style={{ gridTemplateColumns: cols }}>
            <Timestamp className="mds-tape__time" value={p.time} format="time" />
            {showSymbol && <span className="mds-tape__sym">{p.symbol}</span>}
            <span className={`mds-tape__px mds-tape__px--${dir}`}>
              {px}
              {dir !== "flat" && <span className="mds-tape__tick" aria-hidden="true">{dir === "up" ? "▲" : "▼"}</span>}
            </span>
            <span className={`mds-tape__size${tier(p.size) ? ` mds-tape__size--t${tier(p.size)}` : ""}`}>
              {formatQuantity(p.size, 0, locale)}
              {block && <span className="mds-tape__blk">blk</span>}
            </span>
            {showVenue && <span className="mds-tape__venue">{p.venue || ""}</span>}
          </div>
        );
      })}
    </div>
  );
}
