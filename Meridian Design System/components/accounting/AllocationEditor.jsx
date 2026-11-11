// Meridian allocation editor — split a fixed total across editable weights with cent-exact
// arithmetic (allocateAmount, largest-remainder). Amounts and % shares update live; the footer
// proves the parts sum exactly to the total.
import React from "react";
const { useState } = React;
import { AmountCell } from "./AmountCell";
import { toNumber, formatMoney } from "../core/format.js";
import { allocateAmount } from "./money";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.alc-wrap{border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-chip,2px);
  background:var(--bg-light,#F8F7F3);overflow:hidden;}
.alc{width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.alc thead th{padding:9px 12px;text-align:right;white-space:nowrap;
  background:var(--bg-medium,#EBE8E1);
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#606870);
  border-bottom:1px solid var(--border-strong,#AAA49A);border-right:1px solid var(--border-divider,#E2DED5);}
.alc thead th:last-child{border-right:none;}
.alc thead th.alc--l{text-align:left;}
.alc td{padding:8px 12px;white-space:nowrap;text-align:right;color:var(--text-primary,#20252B);
  border-top:1px solid var(--border,#D2CDC3);border-right:1px solid var(--border-divider,#E2DED5);
  vertical-align:middle;}
.alc td:last-child{border-right:none;}
.alc td.alc--l{text-align:left;}
.alc__label{font-family:var(--font-body);color:var(--text-primary,#20252B);}
.alc__w{width:72px;padding:4px 8px;text-align:right;font-family:var(--font-data);font-size:12px;
  color:var(--text-primary,#20252B);background:var(--bg-light,#F8F7F3);
  border:1px solid var(--border-strong,#AAA49A);border-radius:var(--radius-chip,2px);}
.alc__w:focus{outline:2px solid var(--brand-signal,#C56343);outline-offset:-1px;}
.alc__share{color:var(--text-muted,#606870);}
.alc__empty{padding:16px 12px;font-family:var(--font-body);font-size:12px;color:var(--text-muted,#606870);text-align:center;}
.alc tfoot td{padding:9px 12px;background:var(--bg-medium,#EBE8E1);font-weight:600;
  border-top:2px solid var(--border-strong,#AAA49A);color:var(--text-primary,#20252B);text-align:right;}
.alc tfoot td.alc--l{text-align:left;}
.alc__foot-label{font-family:var(--font-body);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-secondary,#535C65);}
.alc__foot-note{font-family:var(--font-body);font-size:10px;font-weight:400;
  color:var(--text-muted,#606870);margin-left:8px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "allocation");
  el.textContent = css;
  document.head.appendChild(el);
}

export function AllocationEditor({
  total,
  lines = [],
  currency = "USD",
  decimals = 2,
  onChange,
  caption,
}) {
  inject();
  const [weights, setWeights] = useState(() => lines.map((l) => toNumber(l.weight) || 0));
  React.useEffect(() => { setWeights(lines.map((l) => toNumber(l.weight) || 0)); }, [lines.length]);

  const amounts = allocateAmount(total, weights, decimals);
  const wSum = weights.reduce((a, b) => a + (b > 0 ? b : 0), 0);

  const setWeight = (i, v) => {
    const next = weights.slice();
    next[i] = Math.max(0, toNumber(v) || 0);
    setWeights(next);
    onChange?.(allocateAmount(total, next, decimals), next);
  };

  return (
    <div className="alc-wrap" role="region" aria-label={caption || "Allocation"}>
      <table className="alc">
        <thead>
          <tr>
            <th className="alc--l" scope="col">Line</th>
            <th scope="col">Weight</th>
            <th scope="col">Share</th>
            <th scope="col">Amount</th>
          </tr>
        </thead>
        <tbody>
          {lines.length === 0 && (
            <tr><td className="alc__empty" colSpan={4}>No allocation lines</td></tr>
          )}
          {lines.map((l, i) => (
            <tr key={i}>
              <td className="alc--l alc__label">{l.label}</td>
              <td>
                <input
                  className="alc__w"
                  type="number"
                  min="0"
                  step="1"
                  value={weights[i]}
                  aria-label={`${l.label} weight`}
                  onChange={(e) => setWeight(i, e.target.value)}
                />
              </td>
              <td className="alc__share">
                {wSum > 0 && weights[i] > 0 ? `${((weights[i] / wSum) * 100).toFixed(1)}%` : "\u2014"}
              </td>
              <td><AmountCell value={amounts[i]} currency={currency} zeroDash /></td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td className="alc--l alc__foot-label" colSpan={3}>
              Allocated
              <span className="alc__foot-note">
                largest-remainder · sums to {formatMoney(total, { currency, decimals })} exactly
              </span>
            </td>
            <td><AmountCell value={amounts.reduce((a, b) => a + b, 0)} currency={currency} strong /></td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
