// Meridian general-ledger / journal table — double-entry rows with Debit / Credit columns and
// a running Balance, plus a totals footer that proves Σdebit = Σcredit. Light institutional
// grid: white paper, small-caps muted headers, hairline rows, mono tabular figures. Mirrors the
// dense workstation table surface, specialized for accounting.
import React from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.ldg-wrap{overflow-x:auto;border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-chip,4px);background:var(--bg-light,#fff);}
.ldg{width:100%;min-width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.ldg thead th{padding:9px 12px;text-align:left;white-space:nowrap;position:sticky;top:0;
  background:var(--bg-medium,#F5F7FA);z-index:1;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#6E7781);
  border-bottom:1px solid var(--border,#D7DCE2);}
.ldg th.ldg--r{text-align:right;}
.ldg td{padding:10px 12px;white-space:nowrap;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#D7DCE2);vertical-align:baseline;}
.ldg tbody tr:first-child td{border-top:none;}
.ldg td.ldg--r{text-align:right;}
.ldg tbody tr:hover{background:var(--bg-hover,#F1F4F7);}
.ldg__date{color:var(--text-secondary,#4D5967);}
.ldg__ref{color:var(--accent,#2F6F8F);}
.ldg__memo{font-family:var(--font-body);color:var(--text-secondary,#4D5967);
  white-space:normal;min-width:160px;}
.ldg__acct{color:var(--text-primary,#22272E);}
.ldg__open td{background:var(--bg-medium,#F5F7FA);color:var(--text-muted,#6E7781);}
.ldg__open .ldg__memo{font-style:italic;}
.ldg tfoot td{padding:9px 12px;background:var(--bg-medium,#F5F7FA);font-weight:600;
  border-top:2px solid var(--border-strong,#AAB4BF);color:var(--text-primary,#22272E);}
.ldg tfoot td.ldg--r{text-align:right;}
.ldg__foot-label{font-family:var(--font-body);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-secondary,#4D5967);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "ledger");
  el.textContent = css;
  document.head.appendChild(el);
}

export function LedgerTable({
  rows,
  currency = "USD",
  opening,                 // optional opening-balance number → renders a leading muted row
  showAccount = false,     // include an Account column (journal across accounts)
  normalSide = "debit",    // running balance increases on this side
  caption,
}) {
  inject();

  // Compute running balances when a row omits `balance`.
  let bal = isFinite(toNumber(opening)) ? toNumber(opening) : 0;
  let hasRunning = isFinite(toNumber(opening));
  const computed = rows.map((r) => {
    const d = toNumber(r.debit) || 0;
    const c = toNumber(r.credit) || 0;
    const delta = normalSide === "credit" ? c - d : d - c;
    if (r.balance != null && isFinite(toNumber(r.balance))) {
      bal = toNumber(r.balance);
      hasRunning = true;
    } else {
      bal += delta;
    }
    return { ...r, _bal: bal };
  });

  const totalD = rows.reduce((a, r) => a + (toNumber(r.debit) || 0), 0);
  const totalC = rows.reduce((a, r) => a + (toNumber(r.credit) || 0), 0);

  return (
    <div className="ldg-wrap" role="region" aria-label={caption || "General ledger"}>
      <table className="ldg">
        <thead>
          <tr>
            <th>Date</th>
            <th>Ref</th>
            {showAccount && <th>Account</th>}
            <th>Description</th>
            <th className="ldg--r">Debit</th>
            <th className="ldg--r">Credit</th>
            <th className="ldg--r">Balance</th>
          </tr>
        </thead>
        <tbody>
          {isFinite(toNumber(opening)) && (
            <tr className="ldg__open">
              <td className="ldg__date" />
              <td />
              {showAccount && <td />}
              <td className="ldg__memo" colSpan={1}>Opening balance</td>
              <td className="ldg--r" />
              <td className="ldg--r" />
              <td className="ldg--r">
                <AmountCell value={toNumber(opening)} currency={currency} mode="muted" />
              </td>
            </tr>
          )}
          {computed.map((r, i) => (
            <tr key={i}>
              <td className="ldg__date">{r.date}</td>
              <td className="ldg__ref">{r.ref}</td>
              {showAccount && <td className="ldg__acct">{r.account}</td>}
              <td className="ldg__memo">{r.memo}</td>
              <td className="ldg--r">
                <AmountCell value={r.debit} currency={currency} zeroDash />
              </td>
              <td className="ldg--r">
                <AmountCell value={r.credit} currency={currency} zeroDash />
              </td>
              <td className="ldg--r">
                {hasRunning
                  ? <AmountCell value={r._bal} currency={currency} parens />
                  : <span style={{ color: "var(--text-disabled,#9AA4AF)" }}>&mdash;</span>}
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td className="ldg__foot-label" colSpan={showAccount ? 4 : 3}>Totals</td>
            <td className="ldg--r"><AmountCell value={totalD} currency={currency} strong /></td>
            <td className="ldg--r"><AmountCell value={totalC} currency={currency} strong /></td>
            <td className="ldg--r">
              <AmountCell
                value={totalD - totalC}
                currency={currency}
                mode={Math.abs(totalD - totalC) < 0.005 ? "muted" : "pnl"}
                parens
                strong
              />
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
