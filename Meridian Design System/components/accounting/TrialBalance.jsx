// Meridian trial balance — one row per account, net on its normal side, grouped into
// Assets / Liabilities / Equity / Revenue / Expenses sections with subtotals and a grand-total
// footer proving Σdebit = Σcredit. Pairs with Money.buildTrialBalance for raw postings.
import React from "react";
import { AmountCell, splitEnvelopeValue } from "./AmountCell";
import { toNumber } from "../core/format.js";
import { accountNormalSide } from "./money.js";
import { knownAmount, sumKnown, displayAmount, optionalSide } from "./knownAmounts.js";

// A side may arrive as a bare number or an envelope-shaped object — the proof arithmetic reads
// the figure, AmountCell renders the facets (guidelines/VALUE_ENVELOPE.md).
const fig = (v) => splitEnvelopeValue(v).value;

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.tbl-wrap:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.tbl-wrap{overflow-x:auto;border:1px solid var(--border,#D2CDC3);
  border-radius:var(--radius-chip,2px);background:var(--bg-light,#F8F7F3);}
.tbl{width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.tbl thead th{padding:9px 12px;text-align:left;white-space:nowrap;position:sticky;top:0;
  background:var(--bg-medium,#EBE8E1);z-index:1;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#606870);
  border-bottom:1px solid var(--border-strong,#AAA49A);border-right:1px solid var(--border-divider,#E2DED5);}
.tbl thead th:last-child{border-right:none;}
.tbl th.tbl--r,.tbl td.tbl--r{text-align:right;}
.tbl td{padding:9px 12px;white-space:nowrap;color:var(--text-primary,#20252B);
  border-top:1px solid var(--border,#D2CDC3);border-right:1px solid var(--border-divider,#E2DED5);
  vertical-align:baseline;}
.tbl td:last-child{border-right:none;}
.tbl tbody tr:hover td{background:var(--bg-hover,#EDEFF0);}
.tbl tbody tr.tbl__row--click{cursor:pointer;}
.tbl tbody tr.tbl__row--on td{background:var(--bg-active,#E7EDF2);}
.tbl tbody tr.tbl__row--on td:first-child{box-shadow:inset 3px 0 0 var(--interaction-selected,#496C8F);}
.tbl tbody tr.tbl__row--click:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.tbl__empty{padding:16px 12px;font-family:var(--font-body);font-size:12px;color:var(--text-muted,#606870);text-align:center;}
/* section + subtotal rows are structure, not data: state modifiers come after the hover rule
   and match its specificity, so they win by order — no !important needed. */
.tbl tbody tr.tbl__sec td,.tbl tbody tr.tbl__sec:hover td{background:var(--bg-medium,#EBE8E1);
  font-family:var(--font-body);
  font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
  color:var(--text-secondary,#535C65);padding:6px 12px;}
.tbl tbody tr.tbl__sub td,.tbl tbody tr.tbl__sub:hover td{background:var(--bg-light,#F8F7F3);
  font-family:var(--font-body);
  font-size:11px;color:var(--text-muted,#606870);border-top:1px solid var(--border-divider,#E2DED5);}
.tbl__code{color:var(--text-muted,#606870);}
.tbl__acct{font-family:var(--font-body);color:var(--text-primary,#20252B);}
.tbl tfoot td{padding:9px 12px;background:var(--bg-medium,#EBE8E1);font-weight:600;
  border-top:2px solid var(--border-strong,#AAA49A);color:var(--text-primary,#20252B);}
.tbl__foot-label{font-family:var(--font-body);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-secondary,#535C65);}
.tbl__foot-note{font-weight:400;color:var(--text-muted,#606870);margin-left:8px;font-family:var(--font-body);font-size:11px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "trialbalance");
  el.textContent = css;
  document.head.appendChild(el);
}

const SECTIONS = ["Assets", "Liabilities", "Equity", "Revenue", "Expenses", "Other"];
function sectionOf(type) {
  const t = String(type || "").toLowerCase();
  if (/contra.?asset/.test(t)) return "Assets";
  if (/contra.?(liab|equity)/.test(t)) return "Liabilities";
  if (/asset/.test(t)) return "Assets";
  if (/liab/.test(t)) return "Liabilities";
  if (/equity|capital|retained|draw|dividend/.test(t)) return "Equity";
  if (/revenue|income|gain/.test(t)) return "Revenue";
  if (/expense|loss|cost/.test(t)) return "Expenses";
  return "Other";
}

function sides(row) {
  const sideValue=key=>{
    const split=splitEnvelopeValue(row[key]);
    const facets={...row.envelope,...split.envelope};
    if(row[key]===undefined||(typeof row[key]==="string"&&row[key].trim()===""))return 0;
    return knownAmount({...facets,value:split.value});
  };
  const hasSide=row.debit!==undefined||row.credit!==undefined;
  if(hasSide)return{debit:sideValue("debit"),credit:sideValue("credit")};
  const split=splitEnvelopeValue(row.balance);
  const balance=knownAmount({...row.envelope,...split.envelope,value:split.value});
  if(balance===null)return{debit:null,credit:null};
  const normal=accountNormalSide(row.type);
  const side=(balance>=0)===(normal==="debit")?"debit":"credit";
  return{debit:side==="debit"?Math.abs(balance):0,credit:side==="credit"?Math.abs(balance):0};
}

export function TrialBalance({
  rows = [],
  currency = "USD",
  grouped = true,
  caption,
  onRowClick,
  selectedIndex,
  trailing,
  trailingLabel = "Assurance",
}) {
  inject();
  const showCode = rows.some((r) => r.code != null && r.code !== "");
  const cols = (showCode ? 4 : 3) + (trailing ? 1 : 0);

  const computed = rows.map((r, i) => ({ ...r, ...sides(r), _i: i, _sec: sectionOf(r.type) }));
  const totalD = sumKnown(computed.map(row=>row.debit));
  const totalC = sumKnown(computed.map(row=>row.credit));
  const complete=totalD!==null&&totalC!==null;
  const balanced = complete&&Math.abs(totalD-totalC)<0.005;

  const groups = grouped
    ? SECTIONS.map((s) => [s, computed.filter((r) => r._sec === s)]).filter(([, rs]) => rs.length)
    : [[null, computed]];

  const Row = (r) => {
    const env = r.envelope
      ? { ...r.envelope, label: r.envelope.label || r.account }
      : undefined;
    const cls = [
      onRowClick ? "tbl__row--click" : "",
      selectedIndex === r._i ? "tbl__row--on" : "",
    ].filter(Boolean).join(" ") || undefined;
    return (
      <tr key={r._i} className={cls} tabIndex={onRowClick ? 0 : undefined}
                onKeyDown={onRowClick ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onRowClick(rows[r._i], r._i); } } : undefined}
                onClick={onRowClick ? () => onRowClick(rows[r._i], r._i) : undefined}>
        {showCode && <td className="tbl__code">{r.code}</td>}
        <td className="tbl__acct">{r.account}</td>
        <td className="tbl--r"><AmountCell value={displayAmount(r.debit)} currency={currency} zeroDash envelope={r.debit ? env : undefined} /></td>
        <td className="tbl--r"><AmountCell value={displayAmount(r.credit)} currency={currency} zeroDash envelope={r.credit ? env : undefined} /></td>
        {trailing && <td className="tbl__trail">{trailing(rows[r._i], r._i)}</td>}
      </tr>
    );
  };

  return (
    <div className="tbl-wrap" tabIndex={0} role="region" aria-label={caption || "Trial balance"}>
      <table className="tbl">
        <thead>
          <tr>
            {showCode && <th scope="col">Code</th>}
            <th scope="col">Account</th>
            <th className="tbl--r" scope="col">Debit</th>
            <th className="tbl--r" scope="col">Credit</th>
            {trailing && <th scope="col">{trailingLabel}</th>}
          </tr>
        </thead>
        <tbody>
          {computed.length === 0 && (
            <tr><td className="tbl__empty" colSpan={cols}>No accounts</td></tr>
          )}
          {groups.map(([sec, rs]) => (
            <React.Fragment key={sec || "all"}>
              {sec && (
                <tr className="tbl__sec"><td colSpan={cols}>{sec}</td></tr>
              )}
              {rs.map(Row)}
              {sec && rs.length > 1 && (
                <tr className="tbl__sub">
                  <td colSpan={cols - 2 - (trailing ? 1 : 0)}>{sec} subtotal</td>
                  <td className="tbl--r">
                    <AmountCell value={displayAmount(sumKnown(rs.map(row=>row.debit)))} currency={currency} zeroDash mode="muted" />
                  </td>
                  <td className="tbl--r">
                    <AmountCell value={displayAmount(sumKnown(rs.map(row=>row.credit)))} currency={currency} zeroDash mode="muted" />
                  </td>
                  {trailing && <td />}
                </tr>
              )}
            </React.Fragment>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td className="tbl__foot-label" colSpan={cols - 2 - (trailing ? 1 : 0)}>
              Totals
              {!complete&&<span className="tbl__foot-note"> Input missing — balance cannot be proved</span>}
              {complete&&!balanced && (
                <span className="tbl__foot-note" style={{ color: "var(--red-dim,#883832)" }}>
                  out of balance by {""}
                </span>
              )}
              {complete&&!balanced && (
                <AmountCell value={totalD - totalC} currency={currency} mode="pnl" parens />
              )}
            </td>
            <td className="tbl--r"><AmountCell value={displayAmount(totalD)} currency={currency} strong /></td>
            <td className="tbl--r"><AmountCell value={displayAmount(totalC)} currency={currency} strong /></td>
            {trailing && <td />}
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
