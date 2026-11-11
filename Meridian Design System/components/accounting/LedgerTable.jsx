// Meridian general-ledger / journal table — double-entry rows with Debit / Credit columns and
// a running Balance, plus a totals footer that proves Σdebit = Σcredit. Rows may carry a
// posting status: "pending" postings are washed amber, "void" postings are struck through and
// excluded from totals and the running balance. Sortable headers (self-sorting unless an
// `onSort` delegate is given), click-to-inspect rows with selection. Light institutional grid.
import React from "react";
const { useState } = React;
import { AmountCell, splitEnvelopeValue } from "./AmountCell";
import { toNumber } from "../core/format.js";

// A debit/credit may arrive as a bare number or as an envelope-shaped object
// ({ value, condition, authority, … }) — arithmetic reads the figure, AmountCell reads the rest.
const amt = (v) => toNumber(splitEnvelopeValue(v).value);
const fig = (v) => splitEnvelopeValue(v).value;

// Row-level envelope facets apply to that row's amount cells; a side-level envelope-shaped
// value wins over the row's. `label` names the figure when the row hasn't.
function sideEnvelope(r, side) {
  const v = r[side];
  if (v === "" || v == null) return undefined;   // the blank side states nothing
  const own = splitEnvelopeValue(v).envelope;
  const base = r.envelope;
  if (!own && !base) return undefined;
  const merged = { ...base, ...own };
  if (!merged.label) merged.label = `${r.ref || r.date} · ${r.account || r.memo || side}`;
  return merged;
}

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.ldg-wrap{overflow-x:auto;border:1px solid var(--border,#D2CDC3);
  border-radius:var(--radius-chip,2px);background:var(--bg-light,#F8F7F3);}
.ldg-wrap:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.ldg{width:100%;min-width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.ldg thead th{padding:9px 12px;text-align:left;white-space:nowrap;position:sticky;top:0;
  background:var(--bg-medium,#EBE8E1);z-index:1;cursor:pointer;user-select:none;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#606870);
  border-bottom:1px solid var(--border-strong,#AAA49A);border-right:1px solid var(--border-divider,#E2DED5);}
.ldg thead th:hover{color:var(--text-primary,#20252B);}
.ldg thead th:focus-visible{outline:var(--focus-ring);outline-offset:-2px;color:var(--text-primary,#20252B);}
.ldg thead th:last-child{border-right:none;}
.ldg th.ldg--r{text-align:right;}
.ldg td{padding:11px 12px;white-space:nowrap;color:var(--text-primary,#20252B);
  border-top:1px solid var(--border,#D2CDC3);border-right:1px solid var(--border-divider,#E2DED5);
  vertical-align:baseline;height:40px;}
.ldg td:last-child{border-right:none;}
.ldg tbody tr:first-child td{border-top:none;}
.ldg td.ldg--r{text-align:right;}
.ldg tbody tr:hover{background:var(--bg-hover,#EDEFF0);}
.ldg tbody tr.ldg__row--click{cursor:pointer;}
.ldg tbody tr.ldg__row--on td{background:var(--bg-active,#E7EDF2);}
.ldg tbody tr.ldg__row--on td:first-child{box-shadow:inset 3px 0 0 var(--interaction-selected,#496C8F);}
.ldg tbody tr.ldg__row--pending td{background:var(--orange-a10,rgba(152,103,23,.07));}
.ldg tbody tr.ldg__row--void td{color:var(--text-disabled,#A4ABB2);text-decoration:line-through;}
.ldg tbody tr.ldg__row--void td .ldg__ref{color:var(--text-disabled,#A4ABB2);}
.ldg tbody tr.ldg__row--click:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.ldg__empty{padding:16px 12px;font-family:var(--font-body);font-size:12px;color:var(--text-muted,#606870);text-align:center;}
.ldg__date{color:var(--text-secondary,#535C65);}
.ldg__ref{color:var(--link,#2A5F96);}
.ldg__chip{display:inline-block;margin-left:7px;font-family:var(--font-body);font-size:9px;
  font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;border:1px solid;
  border-radius:var(--radius-chip,2px);padding:0 5px;line-height:1.5;vertical-align:1px;}
.ldg__chip--pending{background:var(--orange-a10,rgba(152,103,23,.10));border-color:var(--orange,#986717);color:var(--orange-dim,#724D11);}
.ldg__chip--void{background:var(--red-a10,rgba(181,75,66,.10));border-color:var(--red,#B54B42);color:var(--red-dim,#883832);}
.ldg__memo{font-family:var(--font-body);color:var(--text-secondary,#535C65);
  white-space:normal;min-width:160px;}
.ldg__acct{color:var(--text-primary,#20252B);}
.ldg__open td{background:var(--bg-medium,#EBE8E1);color:var(--text-muted,#606870);}
.ldg__open .ldg__memo{font-style:italic;}
.ldg tfoot td{padding:9px 12px;background:var(--bg-medium,#EBE8E1);font-weight:600;
  border-top:2px solid var(--border-strong,#AAA49A);color:var(--text-primary,#20252B);}
.ldg tfoot td.ldg--r{text-align:right;}
.ldg__foot-label{font-family:var(--font-body);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-secondary,#535C65);}
.ldg__foot-note{font-weight:400;color:var(--text-muted,#606870);margin-left:8px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "ledger");
  el.textContent = css;
  document.head.appendChild(el);
}

function compareRows(a, b, key) {
  if (key === "debit" || key === "credit") return (amt(a[key]) || 0) - (amt(b[key]) || 0);
  if (key === "balance") return (a._bal ?? 0) - (b._bal ?? 0);
  const av = (a[key] ?? "").toString();
  const bv = (b[key] ?? "").toString();
  return av.localeCompare(bv, undefined, { numeric: true });
}

// Module-scope so the <th> keeps its identity (and keyboard focus) across re-renders and sorts.
function Head({ k, children, right, sortKey, sortDir, onSort }) {
  return (
    <th className={right ? "ldg--r" : undefined} scope="col" tabIndex={0} onClick={() => onSort(k)}
      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onSort(k); } }}
      aria-sort={sortKey === k ? (sortDir === 1 ? "ascending" : "descending") : "none"}>
      {children} {sortKey === k && (sortDir === 1 ? "\u2191" : "\u2193")}
    </th>
  );
}

export function LedgerTable({
  rows = [],
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

  // Running balance is chronological (source order); void rows contribute nothing. It is only
  // known from the opening balance or the first explicit row balance onward — earlier rows show a dash.
  let bal = isFinite(toNumber(open)) ? toNumber(open) : 0;
  let canCompute = isFinite(toNumber(open));
  const computed = rows.map((r, i) => {
    const isVoid = r.status === "void";
    const d = isVoid ? 0 : amt(r.debit) || 0;
    const c = isVoid ? 0 : amt(r.credit) || 0;
    const delta = normalSide === "credit" ? c - d : d - c;
    if (!isVoid && r.balance != null && isFinite(amt(r.balance))) {
      bal = amt(r.balance);
      canCompute = true;
    } else if (canCompute) {
      bal += delta;
    }
    return { ...r, _bal: bal, _hasBal: canCompute, _void: isVoid, _i: i };
  });

  let display = computed;
  if (sortKey && !onSort) {
    display = [...computed].sort((a, b) => sortDir * compareRows(a, b, sortKey));
  }

  const live = computed.filter((r) => !r._void);
  const totalD = live.reduce((a, r) => a + (amt(r.debit) || 0), 0);
  const totalC = live.reduce((a, r) => a + (amt(r.credit) || 0), 0);
  const voided = computed.length - live.length;

  const hp = { sortKey, sortDir, onSort: handleSort };

  return (
    <div className="ldg-wrap" tabIndex={0} role="region" aria-label={caption || "General ledger"}>
      <table className="ldg">
        <thead>
          <tr>
            <Head k="date" {...hp}>Date</Head>
            <Head k="ref" {...hp}>Ref</Head>
            {showAccount && <Head k="account" {...hp}>Account</Head>}
            <Head k="memo" {...hp}>Description</Head>
            <Head k="debit" right {...hp}>Debit</Head>
            <Head k="credit" right {...hp}>Credit</Head>
            <Head k="balance" right {...hp}>Balance</Head>
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
          {display.length === 0 && (
            <tr><td className="ldg__empty" colSpan={showAccount ? 7 : 6}>No journal entries</td></tr>
          )}
          {display.map((r) => {
            const status = r.status && r.status !== "posted" ? r.status : null;
            const cls = [
              status ? `ldg__row--${status}` : "",
              onRowClick ? "ldg__row--click" : "",
              selectedIndex === r._i ? "ldg__row--on" : "",
            ].filter(Boolean).join(" ") || undefined;
            return (
              <tr key={r._i} className={cls} tabIndex={onRowClick ? 0 : undefined}
                onKeyDown={onRowClick ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onRowClick(rows[r._i], r._i); } } : undefined}
                onClick={onRowClick ? () => onRowClick(rows[r._i], r._i) : undefined}>
                <td className="ldg__date">{r.date}</td>
                <td>
                  <span className="ldg__ref">{r.ref}</span>
                  {status && <span className={`ldg__chip ldg__chip--${status}`}>{status}</span>}
                </td>
                {showAccount && <td className="ldg__acct">{r.account}</td>}
                <td className="ldg__memo">{r.memo}</td>
                <td className="ldg--r">
                  <AmountCell value={fig(r.debit)} currency={currency} zeroDash
                    mode={r._void ? "muted" : undefined} envelope={sideEnvelope(r, "debit")} />
                </td>
                <td className="ldg--r">
                  <AmountCell value={fig(r.credit)} currency={currency} zeroDash
                    mode={r._void ? "muted" : undefined} envelope={sideEnvelope(r, "credit")} />
                </td>
                <td className="ldg--r">
                  {r._void || !r._hasBal
                    ? <span style={{ color: "var(--text-disabled,#A4ABB2)" }}>&mdash;</span>
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
