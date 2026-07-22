// Meridian PnLCalendar — month-grid daily P&L heat view. Cells carry a green/red alpha wash
// scaled to the month's max magnitude (a10, a20 past half), mono signed values, Monday-first,
// UTC dates. A footer double-rules the month total, statement-style.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-pnlcal{border:1px solid var(--border,#CBD3DC);background:var(--bg-light,#fff);font-family:var(--font-body);}
.mds-pnlcal__grid{display:grid;grid-template-columns:repeat(7,1fr);}
.mds-pnlcal__wd{padding:6px 8px;font-size:9px;font-weight:600;font-variant:all-small-caps;letter-spacing:.04em;
  color:var(--text-muted,#59636F);background:var(--bg-medium,#EBEFF4);text-align:right;
  border-bottom:1px solid var(--border-strong,#99A5B2);border-right:1px solid var(--border-divider,#D2D9E2);}
.mds-pnlcal__wd:last-child{border-right:none;}
.mds-pnlcal__cell{position:relative;padding:5px 8px;border-right:1px solid var(--border-divider,#D2D9E2);
  border-bottom:1px solid var(--border-divider,#D2D9E2);display:flex;flex-direction:column;justify-content:space-between;
  gap:2px;min-width:0;}
.mds-pnlcal__cell:nth-child(7n){border-right:none;}
.mds-pnlcal__day{font-family:var(--font-data);font-size:9px;color:var(--text-muted,#59636F);}
.mds-pnlcal__val{font-family:var(--font-data);font-size:11px;font-variant-numeric:tabular-nums;text-align:right;
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-pnlcal__cell--up .mds-pnlcal__val{color:var(--green-dim,#10663F);}
.mds-pnlcal__cell--down .mds-pnlcal__val{color:var(--red-dim,#8C2F40);}
.mds-pnlcal__cell--flat .mds-pnlcal__val{color:var(--text-muted,#59636F);}
.mds-pnlcal__foot{display:flex;justify-content:space-between;align-items:baseline;padding:8px 12px;
  border-top:2px solid var(--border-strong,#99A5B2);}
.mds-pnlcal__foot-label{font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
  color:var(--text-muted,#59636F);}
.mds-pnlcal__foot-val{font-family:var(--font-data);font-size:14px;font-weight:600;font-variant-numeric:tabular-nums;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "pnlcal");
  el.textContent = css;
  document.head.appendChild(el);
}

const WD = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const pad = (n) => String(n).padStart(2, "0");

export function PnLCalendar({ month, values = {}, valueFormat, cellHeight = 48, showTotal = true, style = {} }) {
  inject();
  const [y, m] = String(month).split("-").map(Number);
  const days = new Date(Date.UTC(y, m, 0)).getUTCDate();
  const lead = (new Date(Date.UTC(y, m - 1, 1)).getUTCDay() + 6) % 7; // Monday-first
  const nums = Object.values(values).filter((v) => Number.isFinite(v));
  const maxAbs = Math.max(1e-9, ...nums.map((v) => Math.abs(v)));
  const total = nums.reduce((a, v) => a + v, 0);
  const fmt =
    valueFormat ||
    ((v) => (v > 0 ? "+" : v < 0 ? "-" : "±") + Math.abs(v).toLocaleString("en-US", { maximumFractionDigits: 0 }));
  const cells = [];
  for (let i = 0; i < lead; i++) cells.push(<div key={`b${i}`} className="mds-pnlcal__cell" style={{ minHeight: cellHeight }} />);
  for (let d = 1; d <= days; d++) {
    const key = `${y}-${pad(m)}-${pad(d)}`;
    const v = values[key];
    const has = Number.isFinite(v);
    const dir = !has ? null : v > 0 ? "up" : v < 0 ? "down" : "flat";
    const strong = has && Math.abs(v) > maxAbs / 2;
    const wash = !has || dir === "flat" ? undefined
      : dir === "up" ? `var(--green-a${strong ? "20" : "10"})`
      : `var(--red-a${strong ? "20" : "10"})`;
    cells.push(
      <div key={key} className={`mds-pnlcal__cell${dir ? " mds-pnlcal__cell--" + dir : ""}`}
        style={{ minHeight: cellHeight, background: wash }} title={has ? `${key} · ${fmt(v)}` : key}>
        <span className="mds-pnlcal__day">{pad(d)}</span>
        <span className="mds-pnlcal__val">{has ? fmt(v) : ""}</span>
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
