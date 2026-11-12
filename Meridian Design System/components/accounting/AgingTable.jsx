// Meridian AR/AP aging schedule — counterparty rows spread across age buckets with row totals,
// bucket-total footer with share-of-whole, and escalating amber → red washes on late buckets.
import React from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "../core/format.js";
import { knownAmount, sumKnown, displayAmount } from "./knownAmounts.js";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.agt-wrap:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.agt-wrap{overflow-x:auto;border:1px solid var(--border,#D2CDC3);
  border-radius:var(--radius-chip,2px);background:var(--bg-light,#F8F7F3);}
.agt{width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.agt thead th{padding:9px 12px;text-align:right;white-space:nowrap;position:sticky;top:0;
  background:var(--bg-medium,#EBE8E1);z-index:1;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#606870);
  border-bottom:1px solid var(--border-strong,#AAA49A);border-right:1px solid var(--border-divider,#E2DED5);}
.agt thead th:last-child{border-right:none;}
.agt thead th.agt--l{text-align:left;}
.agt td{padding:9px 12px;white-space:nowrap;text-align:right;color:var(--text-primary,#20252B);
  border-top:1px solid var(--border,#D2CDC3);border-right:1px solid var(--border-divider,#E2DED5);
  vertical-align:baseline;}
.agt td:last-child{border-right:none;}
.agt td.agt--l{text-align:left;}
.agt tbody tr:hover td{background:var(--bg-hover,#EDEFF0);}
.agt tbody tr.agt__row--click{cursor:pointer;}
.agt tbody tr.agt__row--on td{background:var(--selected-surface,#E7EDF2);}
.agt tbody tr.agt__row--on td:first-child{box-shadow:inset 3px 0 0 var(--interaction-selected,#496C8F);}
.agt tbody tr.agt__row--click:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.agt__empty{padding:16px 12px;font-family:var(--font-body);font-size:12px;color:var(--text-muted,#606870);text-align:center;}
.agt td.agt--warn{background:var(--orange-a10,rgba(152,103,23,.07));}
.agt td.agt--late{background:var(--red-a10,rgba(181,75,66,.08));}
.agt__name{font-family:var(--font-body);color:var(--text-primary,#20252B);}
.agt__ref{color:var(--text-muted,#606870);}
.agt tfoot td{padding:9px 12px;background:var(--bg-medium,#EBE8E1);font-weight:600;
  border-top:2px solid var(--border-strong,#AAA49A);color:var(--text-primary,#20252B);text-align:right;}
.agt tfoot td.agt--l{text-align:left;}
.agt__foot-label{font-family:var(--font-body);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-secondary,#535C65);}
.agt__share{display:block;font-family:var(--font-body);font-size:10px;font-weight:400;
  color:var(--text-muted,#606870);margin-top:2px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "aging");
  el.textContent = css;
  document.head.appendChild(el);
}

export function AgingTable({
  rows = [],
  buckets = ["Current", "1\u201330", "31\u201360", "61\u201390", "90+"],
  currency = "USD",
  warnFrom = 2,
  caption,
  onRowClick,
  selectedIndex,
}) {
  inject();
  const showRef = rows.some((r) => r.ref != null && r.ref !== "");
  const last = buckets.length - 1;

  const colTotals = buckets.map((_, b) => sumKnown(rows.map((r) => r.amounts?.[b])));
  const grand = sumKnown(colTotals);

  const cellCls = (b, v) => {
    const n = toNumber(v);
    if (!isFinite(n) || n === 0) return undefined;
    if (b === last) return "agt--late";
    if (b >= warnFrom) return "agt--warn";
    return undefined;
  };

  return (
    <div className="agt-wrap" tabIndex={0} role="region" aria-label={caption || "Aging schedule"}>
      <table className="agt">
        <thead>
          <tr>
            <th className="agt--l" scope="col">Counterparty</th>
            {showRef && <th className="agt--l" scope="col">Ref</th>}
            {buckets.map((b) => <th key={b} scope="col">{b}</th>)}
            <th scope="col">Total</th>
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 && (
            <tr><td className="agt__empty" colSpan={(showRef ? 2 : 1) + buckets.length + 1}>No open items</td></tr>
          )}
          {rows.map((r, i) => {
            const cls = [
              onRowClick ? "agt__row--click" : "",
              selectedIndex === i ? "agt__row--on" : "",
            ].filter(Boolean).join(" ") || undefined;
            return (
              <tr key={i} className={cls} tabIndex={onRowClick ? 0 : undefined}
                onKeyDown={onRowClick ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onRowClick(r, i); } } : undefined}
                onClick={onRowClick ? () => onRowClick(r, i) : undefined}>
                <td className="agt--l agt__name">{r.name}</td>
                {showRef && <td className="agt--l agt__ref">{r.ref}</td>}
                {buckets.map((_, b) => (
                  <td key={b} className={cellCls(b, r.amounts?.[b])}>
                    <AmountCell value={displayAmount(r.amounts?.[b])} currency={currency} zeroDash />
                  </td>
                ))}
                <td><AmountCell value={displayAmount(sumKnown(buckets.map((_,b)=>r.amounts?.[b])))} currency={currency} strong /></td>
              </tr>
            );
          })}
        </tbody>
        <tfoot>
          <tr>
            <td className="agt--l agt__foot-label" colSpan={showRef ? 2 : 1}>Totals</td>
            {colTotals.map((t, b) => (
              <td key={b}>
                <AmountCell value={displayAmount(t)} currency={currency} zeroDash strong />
                {grand > 0 && (
                  <span className="agt__share">{((t / grand) * 100).toFixed(1)}%</span>
                )}
              </td>
            ))}
            <td><AmountCell value={displayAmount(grand)} currency={currency} strong /></td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
