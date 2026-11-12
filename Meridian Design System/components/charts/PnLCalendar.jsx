// Meridian PnLCalendar — month-grid daily P&L view. Cells carry a green/red alpha wash
// scaled to the month's max magnitude (a10, a20 past half), mono signed values, Monday-first,
// UTC dates. Optional per-day intraday micro-bars (bars) draw around a dashed midline;
// heat={false} drops the wash for the ink-on-paper sheet style (values alone carry the sign
// color). A footer double-rules the month total, statement-style.
import React from "react";
import { formatNumber } from "../core/format.js";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-pnlcal{border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);font-family:var(--font-body);}
.mds-pnlcal__grid{display:grid;grid-template-columns:repeat(7,1fr);}
.mds-pnlcal__wd{padding:6px 8px;font-size:9px;font-weight:600;font-variant:all-small-caps;letter-spacing:.04em;
  color:var(--text-muted,#606870);background:var(--bg-medium,#EBE8E1);text-align:right;
  border-bottom:1px solid var(--border-strong,#AAA49A);border-right:1px solid var(--border-divider,#E2DED5);}
.mds-pnlcal__wd:last-child{border-right:none;}
.mds-pnlcal__cell{position:relative;padding:5px 8px;border-right:1px solid var(--border-divider,#E2DED5);
  border-bottom:1px solid var(--border-divider,#E2DED5);display:flex;flex-direction:column;justify-content:space-between;
  gap:2px;min-width:0;}
.mds-pnlcal__cell:nth-child(7n){border-right:none;}
.mds-pnlcal__day{font-family:var(--font-data);font-size:9px;color:var(--text-muted,#606870);}
.mds-pnlcal__val{font-family:var(--font-data);font-size:11px;font-variant-numeric:tabular-nums;text-align:right;
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-pnlcal__cell--up .mds-pnlcal__val{color:var(--green-dim,#225840);}
.mds-pnlcal__cell--down .mds-pnlcal__val{color:var(--red-dim,#883832);}
.mds-pnlcal__cell--flat .mds-pnlcal__val{color:var(--text-muted,#606870);}
.mds-pnlcal__foot{display:flex;justify-content:space-between;align-items:baseline;padding:8px 12px;
  border-top:2px solid var(--border-strong,#AAA49A);}
.mds-pnlcal__foot-label{font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
  color:var(--text-muted,#606870);}
.mds-pnlcal__foot-val{font-family:var(--font-data);font-size:14px;font-weight:600;font-variant-numeric:tabular-nums;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "pnlcal");
  el.textContent = css;
  document.head.appendChild(el);
}

const WD = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const pad = (n) => String(n).padStart(2, "0");

function MicroBars({ vals }) {
  const n = vals.length, mx = Math.max(1e-9, ...vals.map((v) => Math.abs(v)));
  const W = 100, H = 16, mid = H / 2, bw = (W / n) * 0.55;
  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" aria-hidden="true"
      style={{ width: "100%", height: 14, display: "block", marginTop: 2 }}>
      <line x1="0" x2={W} y1={mid} y2={mid} stroke="var(--chart-grid,#E2DED5)" strokeWidth="1" strokeDasharray="2 2" />
      {vals.map((v, i) => {
        const h = Math.max(1, (Math.abs(v) / mx) * (mid - 1.5));
        return <rect key={i} x={(i + 0.225) * (W / n)} width={bw} y={v >= 0 ? mid - h : mid} height={h}
          fill={v > 0 ? "var(--green,#2E7655)" : v < 0 ? "var(--red,#B54B42)" : "var(--text-disabled,#A4ABB2)"} opacity="0.9" />;
      })}
    </svg>
  );
}

export function PnLCalendar({ month, values = {}, bars = null, heat = true, valueFormat, cellHeight = 48, showTotal = true, style = {} }) {
  inject();
  const [y, m] = String(month).split("-").map(Number);
  const days = new Date(Date.UTC(y, m, 0)).getUTCDate();
  const lead = (new Date(Date.UTC(y, m - 1, 1)).getUTCDay() + 6) % 7; // Monday-first
  const monthKeys = Array.from({ length: Number.isFinite(days) ? days : 0 }, (_, index) => `${y}-${pad(m)}-${pad(index + 1)}`);
  const nums = monthKeys.map(key => values[key]).filter((value) => Number.isFinite(value));
  const maxAbs = Math.max(1e-9, ...nums.map((v) => Math.abs(v)));
  const total = nums.reduce((a, v) => a + v, 0);
  const fmt =
    valueFormat ||
    ((v) => (v > 0 ? "+" : v < 0 ? "-" : "±") + formatNumber(Math.abs(v), { maxDecimals: 0 }));
  const cells = [];
  for (let i = 0; i < lead; i++) cells.push(<div key={`b${i}`} className="mds-pnlcal__cell" style={{ minHeight: cellHeight }} />);
  for (let d = 1; d <= days; d++) {
    const key = `${y}-${pad(m)}-${pad(d)}`;
    const v = values[key];
    const has = Number.isFinite(v);
    const dir = !has ? null : v > 0 ? "up" : v < 0 ? "down" : "flat";
    const strong = has && Math.abs(v) > maxAbs / 2;
    const wash = !heat || !has || dir === "flat" ? undefined
      : dir === "up" ? `var(--green-a${strong ? "20" : "10"})`
      : `var(--red-a${strong ? "20" : "10"})`;
    cells.push(
      <div key={key} className={`mds-pnlcal__cell${dir ? " mds-pnlcal__cell--" + dir : ""}`}
        style={{ minHeight: cellHeight, background: wash }} title={has ? `${key} · ${fmt(v)}` : key}>
        <span className="mds-pnlcal__day">{pad(d)}</span>
        <span className="mds-pnlcal__val">{has ? fmt(v) : ""}</span>
        {bars && bars[key] && bars[key].length > 0 && <MicroBars vals={bars[key]} />}
      </div>
    );
  }
  return (
    <div className="mds-pnlcal" style={style}>
      <div className="mds-pnlcal__grid">
        {WD.map((w) => <div key={w} className="mds-pnlcal__wd">{w}</div>)}
        {cells}
      </div>
      {showTotal && (
        <div className="mds-pnlcal__foot">
          <span className="mds-pnlcal__foot-label">{month} P&L</span>
          <span className="mds-pnlcal__foot-val"
            style={{ color: total > 0 ? "var(--green-dim)" : total < 0 ? "var(--red-dim)" : "var(--text-muted)" }}>
            {fmt(total)}
          </span>
        </div>
      )}
    </div>
  );
}
