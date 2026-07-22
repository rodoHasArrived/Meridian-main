// Meridian AR/AP aging schedule — counterparty rows spread across age buckets with row totals,
// bucket-total footer with share-of-whole, and escalating amber → red washes on late buckets.
import React from "react";
import { AmountCell } from "./AmountCell";
import { toNumber, sumAmounts } from "./money";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.agt-wrap{overflow-x:auto;border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-chip,2px);background:var(--bg-light,#fff);}
.agt{width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.agt thead th{padding:9px 12px;text-align:right;white-space:nowrap;position:sticky;top:0;
  background:var(--bg-medium,#F5F7FA);z-index:1;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border-strong,#AAB4BF);border-right:1px solid var(--border-divider,#DDE3EA);}
.agt thead th:last-child{border-right:none;}
.agt thead th.agt--l{text-align:left;}
.agt td{padding:9px 12px;white-space:nowrap;text-align:right;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#D7DCE2);border-right:1px solid var(--border-divider,#DDE3EA);
  vertical-align:baseline;}
.agt td:last-child{border-right:none;}
.agt td.agt--l{text-align:left;}
.agt tbody tr:hover td{background:var(--bg-hover,#F1F4F7);}
.agt tbody tr.agt__row--click{cursor:pointer;}
.agt tbody tr.agt__row--on td{background:var(--bg-active,#E6EEF5);}
.agt tbody tr.agt__row--on td:first-child{box-shadow:inset 3px 0 0 var(--accent,#2F6F8F);}
.agt td.agt--warn{background:var(--orange-a10,rgba(138,82,14,.07));}
.agt td.agt--late{background:var(--red-a10,rgba(186,63,85,.08));}
.agt__name{font-family:var(--font-body);color:var(--text-primary,#22272E);}
.agt__ref{color:var(--text-muted,#59636F);}
.agt tfoot td{padding:9px 12px;background:var(--bg-medium,#F5F7FA);font-weight:600;
  border-top:2px solid var(--border-strong,#AAB4BF);color:var(--text-primary,#22272E);text-align:right;}
.agt tfoot td.agt--l{text-align:left;}
.agt__foot-label{font-family:var(--font-body);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-secondary,#4D5967);}
.agt__share{display:block;font-family:var(--font-body);font-size:10px;font-weight:400;
  color:var(--text-muted,#59636F);margin-top:2px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "aging");
  el.textContent = css;
  document.head.appendChild(el);
}

export function AgingTable({
  rows,
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

  const colTotals = buckets.map((_, b) => sumAmounts(rows.map((r) => r.amounts?.[b])));
  const grand = sumAmounts(colTotals);

  const cellCls = (b, v) => {
    const n = toNumber(v);
    if (!isFinite(n) || n === 0) return undefined;
    if (b === last) return "agt--late";
    if (b >= warnFrom) return "agt--warn";
    return undefined;
  };

  return (
    <div className="agt-wrap" role="region" aria-label={caption || "Aging schedule"}>
      <table className="agt">
        <thead>
          <tr>
            <th className="agt--l">Counterparty</th>
            {showRef && <th className="agt--l">Ref</th>}
            {buckets.map((b) => <th key={b}>{b}</th>)}
            <th>Total</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r, i) => {
            const cls = [
              onRowClick ? "agt__row--click" : "",
              selectedIndex === i ? "agt__row--on" : "",
            ].filter(Boolean).join(" ") || undefined;
            return (
              <tr key={i} className={cls}
                onClick={onRowClick ? () => onRowClick(r, i) : undefined}>
                <td className="agt--l agt__name">{r.name}</td>
                {showRef && <td className="agt--l agt__ref">{r.ref}</td>}
                {buckets.map((_, b) => (
                  <td key={b} className={cellCls(b, r.amounts?.[b])}>
                    <AmountCell value={toNumber(r.amounts?.[b]) || 0} currency={currency} zeroDash />
                  </td>
                ))}
                <td><AmountCell value={sumAmounts(r.amounts || [])} currency={currency} strong /></td>
              </tr>
            );
          })}
        </tbody>
        <tfoot>
          <tr>
            <td className="agt--l agt__foot-label" colSpan={showRef ? 2 : 1}>Totals</td>
            {colTotals.map((t, b) => (
              <td key={b}>
                <AmountCell value={t} currency={currency} zeroDash strong />
                {grand > 0 && (
                  <span className="agt__share">{((t / grand) * 100).toFixed(1)}%</span>
                )}
              </td>
            ))}
            <td><AmountCell value={grand} currency={currency} strong /></td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
