// Meridian interval bar — one metric, one plausible range. The encoding for
// model uncertainty when a fan chart is more than the question needs: quantile
// rows on a shared domain, each with an explicit probability level, midpoint
// tick, and printed bounds. Label the meaning; opacity alone never states a
// probability.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-ivb{display:grid;grid-template-columns:auto 1fr auto;gap:6px 10px;align-items:center;width:100%;
  font-family:var(--font-body);}
.mds-ivb__lbl{font-family:var(--font-data);font-size:11px;color:var(--text-secondary);white-space:nowrap;}
.mds-ivb__track{position:relative;height:10px;background:var(--surface-recessed);
  border-radius:1px;overflow:hidden;}
.mds-ivb__band{position:absolute;top:0;bottom:0;background:var(--viz-band-inner);}
.mds-ivb__band--wide{background:var(--viz-band-outer);}
.mds-ivb__mid{position:absolute;top:-2px;bottom:-2px;width:2px;background:var(--series-actual);}
.mds-ivb__val{font-family:var(--font-data);font-size:10.5px;line-height:13px;color:var(--text-secondary);
  text-align:right;white-space:nowrap;font-variant-numeric:tabular-nums;}
.mds-ivb__note{grid-column:1 / -1;font-size:11px;line-height:15px;color:var(--text-muted);margin-top:2px;
  text-wrap:pretty;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "interval-bar");
  el.textContent = css;
  document.head.appendChild(el);
}

export function IntervalBar({ rows = [], domain, format = (v) => String(v), note, className = "", ...rest }) {
  inject();
  const all = [];
  rows.forEach((r) => { all.push(r.lo, r.hi); if (r.mid !== undefined) all.push(r.mid); });
  const finite = all.filter((v) => typeof v === "number" && isFinite(v));
  const dom = domain || [Math.min(...finite), Math.max(...finite)];
  const span = (dom[1] - dom[0]) || 1;
  const pct = (v) => `${(((v - dom[0]) / span) * 100).toFixed(2)}%`;
  return (
    <div className={`mds-ivb${className ? " " + className : ""}`} {...rest}>
      {rows.map((r, i) => (
        <React.Fragment key={r.id || r.label || i}>
          <span className="mds-ivb__lbl">{r.label}</span>
          <div className="mds-ivb__track" role="img"
            aria-label={`${r.label}: ${format(r.lo)} to ${format(r.hi)}${r.mid !== undefined ? `, midpoint ${format(r.mid)}` : ""}`}>
            <span className={`mds-ivb__band${r.wide ? " mds-ivb__band--wide" : ""}`}
              style={{ left: pct(r.lo), width: `${(((r.hi - r.lo) / span) * 100).toFixed(2)}%` }} />
            {r.mid !== undefined ? <span className="mds-ivb__mid" style={{ left: pct(r.mid) }} /> : null}
          </div>
          <span className="mds-ivb__val">{r.text || (r.lo === r.hi ? format(r.mid ?? r.lo) : `${format(r.lo)}–\u200B${format(r.hi)}`)}</span>
        </React.Fragment>
      ))}
      {note ? <p className="mds-ivb__note">{note}</p> : null}
    </div>
  );
}
