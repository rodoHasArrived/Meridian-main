// Meridian tax-lot table — cost-basis lots with holding-period classification and unrealized
// (or realized) P&L. Each lot: acquisition date, quantity, cost basis, market/proceeds value,
// gain/loss with %, and a short/long-term badge derived from holding days vs. a 365-day
// threshold. Footer rolls up basis, value, and total gain. Light institutional grid.
import React from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.tlt-wrap{overflow-x:auto;border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-chip,4px);background:var(--bg-light,#fff);}
.tlt{width:100%;min-width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.tlt thead th{padding:9px 12px;text-align:left;white-space:nowrap;position:sticky;top:0;z-index:1;
  background:var(--bg-medium,#F5F7FA);
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#6E7781);
  border-bottom:1px solid var(--border,#D7DCE2);}
.tlt th.tlt--r{text-align:right;}
.tlt td{padding:7px 12px;white-space:nowrap;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#D7DCE2);vertical-align:middle;font-variant-numeric:tabular-nums;}
.tlt tbody tr:first-child td{border-top:none;}
.tlt td.tlt--r{text-align:right;}
.tlt tbody tr:hover{background:var(--bg-hover,#F1F4F7);}
.tlt__date{color:var(--text-secondary,#4D5967);}
.tlt__sym{font-weight:600;color:var(--text-primary,#22272E);}
.tlt__days{color:var(--text-muted,#6E7781);font-size:11px;}
.tlt__hp{display:inline-flex;align-items:center;gap:5px;border:1px solid;border-radius:var(--radius-chip,4px);
  padding:2px 7px;font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;white-space:nowrap;line-height:1.3;}
.tlt__hp--short{background:var(--orange-a10,rgba(183,121,31,.10));border-color:var(--orange,#B7791F);color:var(--orange-dim,#946216);}
.tlt__hp--long{background:var(--blue-a10,rgba(47,111,143,.10));border-color:var(--accent,#2F6F8F);color:var(--accent,#2F6F8F);}
.tlt__hp__dot{height:5px;width:5px;border-radius:50%;background:currentColor;flex:0 0 auto;}
.tlt__pct{font-family:var(--font-data);font-size:10.5px;margin-left:7px;}
.tlt tfoot td{padding:9px 12px;background:var(--bg-medium,#F5F7FA);font-weight:600;
  border-top:2px solid var(--border-strong,#AAB4BF);color:var(--text-primary,#22272E);}
.tlt tfoot td.tlt--r{text-align:right;}
.tlt__foot-lbl{font-family:var(--font-body);font-size:11px;font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-secondary,#4D5967);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "taxlot");
  el.textContent = css;
  document.head.appendChild(el);
}

const DAY = 86400000;
function daysHeld(acquired, asOf) {
  const a = Date.parse(acquired);
  const b = asOf ? Date.parse(asOf) : Date.now();
  if (!isFinite(a) || !isFinite(b)) return null;
  return Math.max(0, Math.floor((b - a) / DAY));
}
function fmtDays(d) {
  if (d == null) return "";
  if (d < 365) return `${d}d`;
  const y = Math.floor(d / 365);
  const r = d % 365;
  return r >= 30 ? `${y}y ${Math.floor(r / 30)}m` : `${y}y`;
}
function fmtQty(q) {
  const n = toNumber(q);
  if (!isFinite(n)) return q;
  return n.toLocaleString("en-US", { maximumFractionDigits: 4 });
}

export function TaxLotTable({
  lots,
  currency = "USD",
  asOf,                      // ISO date the holding period & market value are measured at
  longTermDays = 365,        // ≥ this many days held ⇒ long-term
  mode = "unrealized",       // "unrealized" (market value) | "realized" (proceeds)
  showSymbol = false,        // include a Symbol column (cross-instrument lot list)
  caption,
}) {
  inject();
  const valueHead = mode === "realized" ? "Proceeds" : "Market value";
  const gainHead = mode === "realized" ? "Realized P&L" : "Unrealized P&L";

  const computed = lots.map((l) => {
    const basis = toNumber(l.costBasis);
    const value = toNumber(mode === "realized" ? l.proceeds : l.marketValue);
    const gain = isFinite(basis) && isFinite(value) ? value - basis : NaN;
    const pct = isFinite(gain) && basis !== 0 ? (gain / Math.abs(basis)) * 100 : NaN;
    const held = l.daysHeld != null ? l.daysHeld : daysHeld(l.acquired, asOf);
    const long = l.term ? l.term === "long" : held != null && held >= longTermDays;
    return { ...l, basis, value, gain, pct, held, long };
  });

  const tBasis = computed.reduce((a, r) => a + (isFinite(r.basis) ? r.basis : 0), 0);
  const tValue = computed.reduce((a, r) => a + (isFinite(r.value) ? r.value : 0), 0);
  const tGain = tValue - tBasis;
  const colCount = 6 + (showSymbol ? 1 : 0);

  return (
    <div className="tlt-wrap" role="region" aria-label={caption || "Tax lots"}>
      <table className="tlt">
        <thead>
          <tr>
            <th>Acquired</th>
            {showSymbol && <th>Symbol</th>}
            <th>Holding period</th>
            <th className="tlt--r">Quantity</th>
            <th className="tlt--r">Cost basis</th>
            <th className="tlt--r">{valueHead}</th>
            <th className="tlt--r">{gainHead}</th>
          </tr>
        </thead>
        <tbody>
          {computed.map((r, i) => (
            <tr key={r.id ?? i}>
              <td className="tlt__date">
                {r.acquired}
                {r.held != null && <span className="tlt__days">  ·  {fmtDays(r.held)}</span>}
              </td>
              {showSymbol && <td className="tlt__sym">{r.symbol}</td>}
              <td>
                <span className={`tlt__hp ${r.long ? "tlt__hp--long" : "tlt__hp--short"}`}>
                  <span className="tlt__hp__dot" aria-hidden="true" />
                  {r.long ? "Long-term" : "Short-term"}
                </span>
              </td>
              <td className="tlt--r">{fmtQty(r.quantity)}</td>
              <td className="tlt--r"><AmountCell value={r.basis} currency={currency} /></td>
              <td className="tlt--r"><AmountCell value={r.value} currency={currency} /></td>
              <td className="tlt--r">
                <AmountCell value={r.gain} currency={currency} mode="pnl" signed zeroDash />
                {isFinite(r.pct) && (
                  <span className="tlt__pct" style={{ color: r.gain < 0 ? "var(--red-dim,#983244)" : r.gain > 0 ? "var(--green-dim,#126C4D)" : "var(--text-muted,#6E7781)" }}>
                    {r.gain >= 0 ? "+" : "\u2212"}{Math.abs(r.pct).toFixed(2)}%
                  </span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td className="tlt__foot-lbl" colSpan={showSymbol ? 4 : 3}>
              {computed.length} {computed.length === 1 ? "lot" : "lots"}
            </td>
            <td className="tlt--r"><AmountCell value={tBasis} currency={currency} strong /></td>
            <td className="tlt--r"><AmountCell value={tValue} currency={currency} strong /></td>
            <td className="tlt--r"><AmountCell value={tGain} currency={currency} mode="pnl" signed strong /></td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
