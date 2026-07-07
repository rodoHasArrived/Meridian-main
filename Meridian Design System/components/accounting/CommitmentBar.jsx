// Meridian commitment bar — capital commitment funding state mirroring
// `CapitalCommitmentDto` / `PrivateAssetSummaryDto`: commitment, called, distributed,
// current NAV. A hairline funding bar (called share filled accent) over a mono figure
// row, with DPI / TVPI derived when distributions and NAV are present. Cent math is
// done on Numbers the contract already provides; formatting is compact currency.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-cmt{display:flex;flex-direction:column;gap:8px;font-family:var(--font-body,"Segoe UI Variable Text",sans-serif);}
.mds-cmt__head{display:flex;align-items:baseline;justify-content:space-between;gap:10px;}
.mds-cmt__label{font-size:12px;font-weight:600;color:var(--text-primary,#1A2027);
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-cmt__vintage{font-family:var(--font-data,"Cascadia Mono",monospace);font-size:9.5px;font-weight:700;
  letter-spacing:.04em;text-transform:uppercase;color:var(--text-muted,#6E7781);white-space:nowrap;}
.mds-cmt__bar{position:relative;height:8px;border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-chip,2px);background:var(--bg-medium,#EDF0F3);overflow:hidden;}
.mds-cmt__called{position:absolute;inset:0 auto 0 0;background:var(--accent,#2F6F8F);}
.mds-cmt__figs{display:flex;flex-wrap:wrap;gap:0 18px;row-gap:6px;}
.mds-cmt__fig{display:flex;flex-direction:column;gap:1px;min-width:72px;}
.mds-cmt__k{font-size:9px;font-weight:600;font-variant:all-small-caps;letter-spacing:.05em;
  color:var(--text-muted,#6E7781);}
.mds-cmt__v{font-family:var(--font-data,"Cascadia Mono",monospace);font-size:11.5px;font-weight:600;
  color:var(--text-primary,#1A2027);font-variant-numeric:tabular-nums;}
.mds-cmt__v small{font-size:9.5px;font-weight:500;color:var(--text-muted,#6E7781);margin-left:3px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "commitment-bar");
  el.textContent = css;
  document.head.appendChild(el);
}

function fmt(v, currency) {
  if (v == null || Number.isNaN(Number(v))) return "—";
  try {
    return new Intl.NumberFormat("en-US", {
      style: "currency", currency, notation: "compact", maximumFractionDigits: 1,
    }).format(Number(v));
  } catch {
    return String(v);
  }
}

export function CommitmentBar({
  commitment,
  called = 0,
  distributed,
  nav,
  currency = "USD",
  label,
  vintage,
  className = "",
  ...rest
}) {
  inject();
  const cmt = Number(commitment) || 0;
  const cal = Number(called) || 0;
  const unfunded = Math.max(0, cmt - cal);
  const pct = cmt > 0 ? Math.min(100, Math.max(0, (cal / cmt) * 100)) : 0;
  const dpi = distributed != null && cal > 0 ? Number(distributed) / cal : null;
  const tvpi = nav != null && cal > 0 ? ((Number(distributed) || 0) + Number(nav)) / cal : null;

  return (
    <div className={`mds-cmt${className ? " " + className : ""}`} {...rest}>
      {(label || vintage) && (
        <div className="mds-cmt__head">
          {label ? <span className="mds-cmt__label">{label}</span> : <span />}
          {vintage ? <span className="mds-cmt__vintage">Vintage {vintage}</span> : null}
        </div>
      )}
      <div className="mds-cmt__bar" role="img"
        aria-label={`${pct.toFixed(0)}% called — ${fmt(cal, currency)} of ${fmt(cmt, currency)}`}>
        <span className="mds-cmt__called" style={{ width: `${pct}%` }} />
      </div>
      <div className="mds-cmt__figs">
        <span className="mds-cmt__fig">
          <span className="mds-cmt__k">Committed</span>
          <span className="mds-cmt__v">{fmt(cmt, currency)}</span>
        </span>
        <span className="mds-cmt__fig">
          <span className="mds-cmt__k">Called</span>
          <span className="mds-cmt__v">{fmt(cal, currency)}<small>{pct.toFixed(0)}%</small></span>
        </span>
        <span className="mds-cmt__fig">
          <span className="mds-cmt__k">Unfunded</span>
          <span className="mds-cmt__v">{fmt(unfunded, currency)}</span>
        </span>
        {distributed != null && (
          <span className="mds-cmt__fig">
            <span className="mds-cmt__k">Distributed</span>
            <span className="mds-cmt__v">{fmt(distributed, currency)}{dpi != null ? <small>{dpi.toFixed(2)}x DPI</small> : null}</span>
          </span>
        )}
        {nav != null && (
          <span className="mds-cmt__fig">
            <span className="mds-cmt__k">NAV</span>
            <span className="mds-cmt__v">{fmt(nav, currency)}{tvpi != null ? <small>{tvpi.toFixed(2)}x TVPI</small> : null}</span>
          </span>
        )}
      </div>
    </div>
  );
}
