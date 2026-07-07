// Meridian general-ledger / journal table — double-entry rows with Debit / Credit columns and
// a running Balance, plus a totals footer that proves Σdebit = Σcredit. Rows may carry a
// posting status: "pending" postings are washed amber, "void" postings are struck through and
// excluded from totals and the running balance. Sortable headers (self-sorting unless an
// `onSort` delegate is given), click-to-inspect rows with selection. Light institutional grid.
import React from "react";
const { useState } = React;
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.ldg-wrap{overflow-x:auto;border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-chip,2px);background:var(--bg-light,#fff);}
.ldg{width:100%;min-width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.ldg thead th{padding:9px 12px;text-align:left;white-space:nowrap;position:sticky;top:0;
  background:var(--bg-medium,#F5F7FA);z-index:1;cursor:pointer;user-select:none;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border-strong,#AAB4BF);border-right:1px solid var(--border-divider,#DDE3EA);}
.ldg thead th:hover{color:var(--text-primary,#22272E);}
.ldg thead th:focus-visible{outline:var(--focus-ring);outline-offset:-2px;color:var(--text-primary,#22272E);}
.ldg thead th:last-child{border-right:none;}
.ldg th.ldg--r{text-align:right;}
.ldg td{padding:11px 12px;white-space:nowrap;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#D7DCE2);border-right:1px solid var(--border-divider,#DDE3EA);
  vertical-align:baseline;height:40px;}
.ldg td:last-child{border-right:none;}
.ldg tbody tr:first-child td{border-top:none;}
.ldg td.ldg--r{text-align:right;}
.ldg tbody tr:hover{background:var(--bg-hover,#F1F4F7);}
.ldg tbody tr.ldg__row--click{cursor:pointer;}
.ldg tbody tr.ldg__row--on td{background:var(--bg-active,#E6EEF5);}
.ldg tbody tr.ldg__row--on td:first-child{box-shadow:inset 3px 0 0 var(--accent,#2F6F8F);}
.ldg tbody tr.ldg__row--pending td{background:var(--orange-a10,rgba(138,82,14,.07));}
.ldg tbody tr.ldg__row--void td{color:var(--text-disabled,#889099);text-decoration:line-through;}
.ldg tbody tr.ldg__row--void td span{color:var(--text-disabled,#889099) !important;}
.ldg__date{color:var(--text-secondary,#4D5967);}
.ldg__ref{color:var(--accent,#2F6F8F);}
.ldg__chip{display:inline-block;margin-left:7px;font-family:var(--font-body);font-size:9px;
  font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;border:1px solid;
  border-radius:var(--radius-chip,2px);padding:0 5px;line-height:1.5;vertical-align:1px;}
.ldg__chip--pending{background:var(--orange-a10,rgba(183,121,31,.10));border-color:var(--orange,#8A520E);color:var(--orange-dim,#683E0B);}
.ldg__chip--void{background:var(--red-a10,rgba(186,63,85,.10));border-color:var(--red,#BA3F55);color:var(--red-dim,#8C2F40);}
.ldg__memo{font-family:var(--font-body);color:var(--text-secondary,#4D5967);
  white-space:normal;min-width:160px;}
.ldg__acct{color:var(--text-primary,#22272E);}
.ldg__open td{background:var(--bg-medium,#F5F7FA);color:var(--text-muted,#59636F);}
.ldg__open .ldg__memo{font-style:italic;}
.ldg tfoot td{padding:9px 12px;background:var(--bg-medium,#F5F7FA);font-weight:600;
  border-top:2px solid var(--border-strong,#AAB4BF);color:var(--text-primary,#22272E);}
.ldg tfoot td.ldg--r{text-align:right;}
.ldg__foot-label{font-family:var(--font-body);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-secondary,#4D5967);}
.ldg__foot-note{font-weight:400;color:var(--text-muted,#59636F);margin-left:8px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "ledger");
  el.textContent = css;
  document.head.appendChild(el);
}

function compareRows(a, b, key) {
  if (key === "debit" || key === "credit") return (toNumber(a[key]) || 0) - (toNumber(b[key]) || 0);
  if (key === "balance") return (a._bal ?? 0) - (b._bal ?? 0);
  const av = (a[key] ?? "").toString();
  const bv = (b[key] ?? "").toString();
  return av.localeCompare(bv, undefined, { numeric: true });
}

export function LedgerTable({
  rows,
  currency = "USD",
  opening,
  openingBalance,          // alias of `opening`
  showAccount = false,
  normalSide = "debit",
  caption,
  onSort,                  // external sort delegate; omit to sort in place
  onRowClick,              // (row, index) — original row index
  selectedIndex,           // highlight one row (original index)
}) {
  inject();
  const [sortKey, setSortKey] = useState(null);
  const [sortDir, setSortDir] = useState(1);
  const open = opening != null ? opening : openingBalance;

  const handleSort = (key) => {
    const dir = sortKey === key ? -sortDir : 1;
    setSortKey(key);
    setSortDir(dir);
    onSort?.(key, dir);
  };

  // Running balance is chronological (source order); void rows contribute nothing.
  let bal = isFinite(toNumber(open)) ? toNumber(open) : 0;
  let hasRunning = isFinite(toNumber(open));
  const computed = rows.map((r, i) => {
    const isVoid = r.status === "void";
    const d = isVoid ? 0 : toNumber(r.debit) || 0;
    const c = isVoid ? 0 : toNumber(r.credit) || 0;
    const delta = normalSide === "credit" ? c - d : d - c;
    if (!isVoid && r.balance != null && isFinite(toNumber(r.balance))) {
      bal = toNumber(r.balance);
      hasRunning = true;
    } else {
      bal += delta;
    }
    return { ...r, _bal: bal, _void: isVoid, _i: i };
  });

  let display = computed;
  if (sortKey && !onSort) {
    display = [...computed].sort((a, b) => sortDir * compareRows(a, b, sortKey));
  }

  const live = computed.filter((r) => !r._void);
  const totalD = live.reduce((a, r) => a + (toNumber(r.debit) || 0), 0);
  const totalC = live.reduce((a, r) => a + (toNumber(r.credit) || 0), 0);
  const voided = computed.length - live.length;

  const Head = ({ k, children, right }) => (
    <th className={right ? "ldg--r" : undefined} scope="col" tabIndex={0} onClick={() => handleSort(k)}
      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); handleSort(k); } }}
      aria-sort={sortKey === k ? (sortDir === 1 ? "ascending" : "descending") : "none"}>
      {children} {sortKey === k && (sortDir === 1 ? "\u2191" : "\u2193")}
    </th>
  );

  return (
    <div className="ldg-wrap" role="region" aria-label={caption || "General ledger"}>
      <table className="ldg">
        <thead>
          <tr>
            <Head k="date">Date</Head>
            <Head k="ref">Ref</Head>
            {showAccount && <Head k="account">Account</Head>}
            <Head k="memo">Description</Head>
            <Head k="debit" right>Debit</Head>
            <Head k="credit" right>Credit</Head>
            <Head k="balance" right>Balance</Head>
          </tr>
        </thead>
        <tbody>
          {isFinite(toNumber(open)) && (
            <tr className="ldg__open">
              <td className="ldg__date" />
              <td />
              {showAccount && <td />}
              <td className="ldg__memo">Opening balance</td>
              <td className="ldg--r" />
              <td className="ldg--r" />
              <td className="ldg--r">
                <AmountCell value={toNumber(open)} currency={currency} mode="muted" />
              </td>
            </tr>
          )}
          {display.map((r) => {
            const status = r.status && r.status !== "posted" ? r.status : null;
            const cls = [
              status ? `ldg__row--${status}` : "",
              onRowClick ? "ldg__row--click" : "",
              selectedIndex === r._i ? "ldg__row--on" : "",
            ].filter(Boolean).join(" ") || undefined;
            return (
              <tr key={r._i} className={cls}
                onClick={onRowClick ? () => onRowClick(rows[r._i], r._i) : undefined}>
                <td className="ldg__date">{r.date}</td>
                <td>
                  <span className="ldg__ref">{r.ref}</span>
                  {status && <span className={`ldg__chip ldg__chip--${status}`}>{status}</span>}
                </td>
                {showAccount && <td className="ldg__acct">{r.account}</td>}
                <td className="ldg__memo">{r.memo}</td>
                <td className="ldg--r">
                  <AmountCell value={r.debit} currency={currency} zeroDash />
                </td>
                <td className="ldg--r">
                  <AmountCell value={r.credit} currency={currency} zeroDash />
                </td>
                <td className="ldg--r">
                  {r._void || !hasRunning
                    ? <span style={{ color: "var(--text-disabled,#889099)" }}>&mdash;</span>
                    : <AmountCell value={r._bal} currency={currency} parens />}
                </td>
              </tr>
            );
          })}
        </tbody>
        <tfoot>
          <tr>
            <td className="ldg__foot-label" colSpan={showAccount ? 4 : 3}>
              Totals
              {voided > 0 && (
                <span className="ldg__foot-note">
                  {voided} void {voided === 1 ? "entry" : "entries"} excluded
                </span>
              )}
            </td>
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
