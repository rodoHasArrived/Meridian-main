// Meridian ChartTooltip — the floating readout for the chart interaction layer. Given a
// crosshair index and the data point, renders a small bordered card of label/value rows that
// tracks the cursor. Pairs with useChartCrosshair / useSyncedCursor. Pure presentation:
// position it inside the crosshair-bound wrapper (position:relative). Pixel-based since the
// real-pixel frame rewrite: pass the SAME padLeft/rail the chart uses.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-ctip{position:absolute;top:10px;z-index:var(--z-tooltip,500);pointer-events:none;
  min-width:120px;background:var(--bg-light,#F8F7F3);border:1px solid var(--border-strong,#AAA49A);
  box-shadow:var(--shadow-menu);font-family:var(--font-data);font-size:11px;padding:7px 9px;}
.mds-ctip__hd{font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#606870);
  margin-bottom:4px;padding-bottom:4px;border-bottom:1px solid var(--border-divider,#E2DED5);}
.mds-ctip__row{display:flex;justify-content:space-between;gap:14px;line-height:1.7;}
.mds-ctip__k{color:var(--text-secondary,#535C65);}
.mds-ctip__v{color:var(--text-primary,#20252B);font-variant-numeric:tabular-nums;font-weight:600;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "charttooltip");
  el.textContent = css;
  document.head.appendChild(el);
}

export function ChartTooltip({ index, count, title, rows = [], padLeft = 10, rail = 62 }) {
  inject();
  if (index == null || !rows.length) return null;
  // Follow the cursor horizontally; flip to the left of center to avoid running off the edge.
  const frac = count > 1 ? index / (count - 1) : 0;
  const flip = frac > 0.55;
  const pos = flip
    ? { right: `calc(${rail}px + ${(1 - frac).toFixed(4)} * (100% - ${padLeft + rail}px))`, marginRight: 12 }
    : { left: `calc(${padLeft}px + ${frac.toFixed(4)} * (100% - ${padLeft + rail}px))`, marginLeft: 12 };
  return (
    <div className="mds-ctip" style={pos} role="status" aria-live="off">
      {title != null && <div className="mds-ctip__hd">{title}</div>}
      {rows.map((r, i) => (
        <div className="mds-ctip__row" key={i}>
          <span className="mds-ctip__k">{r.label}</span>
          <span className="mds-ctip__v" style={r.color ? { color: r.color } : undefined}>{r.value}</span>
        </div>
      ))}
    </div>
  );
}
