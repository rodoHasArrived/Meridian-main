// Meridian FX revaluation — foreign-currency balances at booked vs. current rates with
// per-row unrealized gain/loss (Money.fxRevalue, half-even) and net G/L footer.
import React from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "../core/format.js";
import { fxRevalue } from "./money";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.fxr-wrap{overflow-x:auto;border:1px solid var(--border,#D2CDC3);
  border-radius:var(--radius-chip,2px);background:var(--bg-light,#F8F7F3);}
.fxr{width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.fxr thead th{padding:9px 12px;text-align:right;white-space:nowrap;position:sticky;top:0;
  background:var(--bg-medium,#EBE8E1);z-index:1;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#606870);
  border-bottom:1px solid var(--border-strong,#AAA49A);border-right:1px solid var(--border-divider,#E2DED5);}
.fxr thead th:last-child{border-right:none;}
.fxr thead th.fxr--l{text-align:left;}
.fxr td{padding:9px 12px;white-space:nowrap;text-align:right;color:var(--text-primary,#20252B);
  border-top:1px solid var(--border,#D2CDC3);border-right:1px solid var(--border-divider,#E2DED5);
  vertical-align:baseline;}
.fxr td:last-child{border-right:none;}
.fxr td.fxr--l{text-align:left;}
.fxr tbody tr:hover td{background:var(--bg-hover,#EDEFF0);}
.fxr tbody tr.fxr__row--click{cursor:pointer;}
.fxr tbody tr.fxr__row--on td{background:var(--bg-active,#E7EDF2);}
.fxr tbody tr.fxr__row--on td:first-child{box-shadow:inset 3px 0 0 var(--interaction-selected,#496C8F);}
.fxr tbody tr.fxr__row--click:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.fxr__empty{padding:16px 12px;font-family:var(--font-body);font-size:12px;color:var(--text-muted,#606870);text-align:center;}
.fxr__ccy{font-weight:600;color:var(--text-primary,#20252B);}
.fxr__acct{font-family:var(--font-body);color:var(--text-secondary,#535C65);}
.fxr__rate{color:var(--text-secondary,#535C65);}
.fxr tfoot td{padding:9px 12px;background:var(--bg-medium,#EBE8E1);font-weight:600;
  border-top:2px solid var(--border-strong,#AAA49A);color:var(--text-primary,#20252B);text-align:right;}
.fxr tfoot td.fxr--l{text-align:left;}
.fxr__foot-label{font-family:var(--font-body);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-secondary,#535C65);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "fxreval");
  el.textContent = css;
  document.head.appendChild(el);
}

export function FxRevaluationTable({
  rows = [],
  base = "USD",
  rateDecimals = 4,
  caption,
  onRowClick,
  selectedIndex,
}) {
  inject();
  const showAcct = rows.some((r) => r.account != null && r.account !== "");

  const computed = rows.map((r, i) => ({ ...r, ...fxRevalue(r.local, r.bookedRate, r.currentRate), _i: i }));
  const totBooked = computed.reduce((a, r) => a + (isFinite(r.booked) ? r.booked : 0), 0);
  const totCurrent = computed.reduce((a, r) => a + (isFinite(r.current) ? r.current : 0), 0);
  const totGL = computed.reduce((a, r) => a + (isFinite(r.gainLoss) ? r.gainLoss : 0), 0);

  const rate = (v) => {
    const n = toNumber(v);
    return isFinite(n) ? n.toFixed(rateDecimals) : "\u2014";
  };

  return (
    <div className="fxr-wrap" role="region" aria-label={caption || "FX revaluation"}>
      <table className="fxr">
        <thead>
          <tr>
            <th className="fxr--l" scope="col">Ccy</th>
            {showAcct && <th className="fxr--l" scope="col">Account</th>}
            <th scope="col">Local balance</th>
            <th scope="col">Booked rate</th>
            <th scope="col">Current rate</th>
            <th scope="col">Base @ booked</th>
            <th scope="col">Base @ current</th>
            <th scope="col">Unrealized G/L</th>
          </tr>
        </thead>
        <tbody>
          {computed.length === 0 && (
            <tr><td className="fxr__empty" colSpan={showAcct ? 8 : 7}>No foreign-currency balances</td></tr>
          )}
          {computed.map((r) => {
            const cls = [
              onRowClick ? "fxr__row--click" : "",
              selectedIndex === r._i ? "fxr__row--on" : "",
            ].filter(Boolean).join(" ") || undefined;
            return (
              <tr key={r._i} className={cls} tabIndex={onRowClick ? 0 : undefined}
                onKeyDown={onRowClick ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onRowClick(rows[r._i], r._i); } } : undefined}
                onClick={onRowClick ? () => onRowClick(rows[r._i], r._i) : undefined}>
                <td className="fxr--l fxr__ccy">{r.currency}</td>
                {showAcct && <td className="fxr--l fxr__acct">{r.account}</td>}
                <td><AmountCell value={r.local} currency={r.currency} decimals="auto" /></td>
                <td className="fxr__rate">{rate(r.bookedRate)}</td>
                <td className="fxr__rate">{rate(r.currentRate)}</td>
                <td><AmountCell value={r.booked} currency={base} /></td>
                <td><AmountCell value={r.current} currency={base} /></td>
                <td><AmountCell value={r.gainLoss} currency={base} mode="pnl" signed zeroDash /></td>
              </tr>
            );
          })}
        </tbody>
        <tfoot>
          <tr>
            <td className="fxr--l fxr__foot-label" colSpan={showAcct ? 5 : 4}>Net</td>
            <td><AmountCell value={totBooked} currency={base} strong /></td>
            <td><AmountCell value={totCurrent} currency={base} strong /></td>
            <td><AmountCell value={totGL} currency={base} mode="pnl" signed strong /></td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
