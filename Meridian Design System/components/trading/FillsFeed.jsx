// Meridian FillsFeed — streaming execution tape. Newest first, mono rows: time, symbol,
// washed side, qty @ price. Quiet by design — no animation, no highlights; the feed itself
// is the motion.
import React from "react";
import { Timestamp } from "../core/Timestamp";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-fills{border:1px solid var(--border,#CBD3DC);background:var(--bg-light,#fff);
  font-family:var(--font-data);font-size:11px;overflow-y:auto;}
.mds-fills__row{display:grid;grid-template-columns:76px 1fr 44px 1fr auto;gap:8px;
  padding:5px 10px;border-top:1px solid var(--border-divider,#D2D9E2);align-items:baseline;
  font-variant-numeric:tabular-nums;}
.mds-fills__row:first-child{border-top:none;}
.mds-fills__time{color:var(--text-muted,#59636F);font-size:10px;}
.mds-fills__sym{font-weight:600;color:var(--text-primary,#22272E);}
.mds-fills__side{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;}
.mds-fills__side--buy{color:var(--green-dim,#10663F);}
.mds-fills__side--sell{color:var(--red-dim,#8C2F40);}
.mds-fills__qty{text-align:right;color:var(--text-secondary,#4D5967);}
.mds-fills__px{text-align:right;color:var(--text-primary,#22272E);}
.mds-fills__empty{padding:18px 10px;text-align:center;font-family:var(--font-body);
  font-size:12px;color:var(--text-muted,#59636F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "fillsfeed");
  el.textContent = css;
  document.head.appendChild(el);
}

export function FillsFeed({ fills = [], maxHeight = 260, emptyLabel = "No fills this session" }) {
  inject();
  return (
    <div className="mds-fills" style={{ maxHeight }} role="log" aria-label="Execution fills">
      {fills.length === 0 && <div className="mds-fills__empty">{emptyLabel}</div>}
      {fills.map((f, i) => {
        const side = String(f.side || "").toLowerCase();
        return (
          <div key={f.id || i} className="mds-fills__row">
            <Timestamp className="mds-fills__time" value={f.time} format="time" />
            <span className="mds-fills__sym">{f.symbol}</span>
            <span className={`mds-fills__side mds-fills__side--${side === "sell" ? "sell" : "buy"}`}>{f.side}</span>
            <span className="mds-fills__qty">{f.qty}</span>
            <span className="mds-fills__px">@ {f.price}</span>
          </div>
        );
      })}
    </div>
  );
}
