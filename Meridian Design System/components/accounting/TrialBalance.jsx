// Meridian trial balance — one row per account, net on its normal side, grouped into
// Assets / Liabilities / Equity / Revenue / Expenses sections with subtotals and a grand-total
// footer proving Σdebit = Σcredit. Pairs with Money.buildTrialBalance for raw postings.
import React from "react";
import { AmountCell } from "./AmountCell";
import { toNumber, accountNormalSide } from "./money";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.tbl-wrap{overflow-x:auto;border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-chip,2px);background:var(--bg-light,#fff);}
.tbl{width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.tbl thead th{padding:9px 12px;text-align:left;white-space:nowrap;position:sticky;top:0;
  background:var(--bg-medium,#F5F7FA);z-index:1;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border-strong,#AAB4BF);border-right:1px solid var(--border-divider,#DDE3EA);}
.tbl thead th:last-child{border-right:none;}
.tbl th.tbl--r,.tbl td.tbl--r{text-align:right;}
.tbl td{padding:9px 12px;white-space:nowrap;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#D7DCE2);border-right:1px solid var(--border-divider,#DDE3EA);
  vertical-align:baseline;}
.tbl td:last-child{border-right:none;}
.tbl tbody tr:hover td{background:var(--bg-hover,#F1F4F7);}
.tbl tbody tr.tbl__sec:hover td,.tbl tbody tr.tbl__sub:hover td{background:inherit;}
.tbl tbody tr.tbl__row--click{cursor:pointer;}
.tbl tbody tr.tbl__row--on td{background:var(--bg-active,#E6EEF5);}
.tbl tbody tr.tbl__row--on td:first-child{box-shadow:inset 3px 0 0 var(--accent,#2F6F8F);}
.tbl__sec td{background:var(--bg-medium,#F5F7FA)!important;font-family:var(--font-body);
  font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
  color:var(--text-secondary,#4D5967);padding:6px 12px;}
.tbl__sub td{background:var(--bg-light,#fff)!important;font-family:var(--font-body);
  font-size:11px;color:var(--text-muted,#59636F);border-top:1px solid var(--border-divider,#DDE3EA);}
.tbl__code{color:var(--text-muted,#59636F);}
.tbl__acct{font-family:var(--font-body);color:var(--text-primary,#22272E);}
.tbl tfoot td{padding:9px 12px;background:var(--bg-medium,#F5F7FA);font-weight:600;
  border-top:2px solid var(--border-strong,#AAB4BF);color:var(--text-primary,#22272E);}
.tbl__foot-label{font-family:var(--font-body);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-secondary,#4D5967);}
.tbl__foot-note{font-weight:400;color:var(--text-muted,#59636F);margin-left:8px;font-family:var(--font-body);font-size:11px;}
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
  const d = toNumber(row.debit);
  const c = toNumber(row.credit);
  if (isFinite(d) || isFinite(c)) return { debit: isFinite(d) ? d : 0, credit: isFinite(c) ? c : 0 };
  const b = toNumber(row.balance);
  if (!isFinite(b)) return { debit: 0, credit: 0 };
  const normal = accountNormalSide(row.type);
  const onNormal = b >= 0;
  const side = onNormal === (normal === "debit") ? "debit" : "credit";
  return { debit: side === "debit" ? Math.abs(b) : 0, credit: side === "credit" ? Math.abs(b) : 0 };
}

export function TrialBalance({
  rows,
  currency = "USD",
  grouped = true,
  caption,
  onRowClick,
  selectedIndex,
}) {
  inject();
  const showCode = rows.some((r) => r.code != null && r.code !== "");
  const cols = showCode ? 4 : 3;

  const computed = rows.map((r, i) => ({ ...r, ...sides(r), _i: i, _sec: sectionOf(r.type) }));
  const totalD = computed.reduce((a, r) => a + r.debit, 0);
  const totalC = computed.reduce((a, r) => a + r.credit, 0);
  const balanced = Math.abs(totalD - totalC) < 0.005;

  const groups = grouped
    ? SECTIONS.map((s) => [s, computed.filter((r) => r._sec === s)]).filter(([, rs]) => rs.length)
    : [[null, computed]];

  const Row = (r) => {
    const cls = [
      onRowClick ? "tbl__row--click" : "",
      selectedIndex === r._i ? "tbl__row--on" : "",
    ].filter(Boolean).join(" ") || undefined;
    return (
      <tr key={r._i} className={cls}
        onClick={onRowClick ? () => onRowClick(rows[r._i], r._i) : undefined}>
        {showCode && <td className="tbl__code">{r.code}</td>}
        <td className="tbl__acct">{r.account}</td>
        <td className="tbl--r"><AmountCell value={r.debit} currency={currency} zeroDash /></td>
        <td className="tbl--r"><AmountCell value={r.credit} currency={currency} zeroDash /></td>
      </tr>
    );
  };

  return (
    <div className="tbl-wrap" role="region" aria-label={caption || "Trial balance"}>
      <table className="tbl">
        <thead>
          <tr>
            {showCode && <th>Code</th>}
            <th>Account</th>
            <th className="tbl--r">Debit</th>
            <th className="tbl--r">Credit</th>
          </tr>
        </thead>
        <tbody>
          {groups.map(([sec, rs]) => (
            <React.Fragment key={sec || "all"}>
              {sec && (
                <tr className="tbl__sec"><td colSpan={cols}>{sec}</td></tr>
              )}
              {rs.map(Row)}
              {sec && rs.length > 1 && (
                <tr className="tbl__sub">
                  <td colSpan={cols - 2}>{sec} subtotal</td>
                  <td className="tbl--r">
                    <AmountCell value={rs.reduce((a, r) => a + r.debit, 0)} currency={currency} zeroDash mode="muted" />
                  </td>
                  <td className="tbl--r">
                    <AmountCell value={rs.reduce((a, r) => a + r.credit, 0)} currency={currency} zeroDash mode="muted" />
                  </td>
                </tr>
              )}
            </React.Fragment>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td className="tbl__foot-label" colSpan={cols - 2}>
              Totals
              {!balanced && (
                <span className="tbl__foot-note" style={{ color: "var(--red-dim,#8C2F40)" }}>
                  out of balance by {""}
                </span>
              )}
              {!balanced && (
                <AmountCell value={totalD - totalC} currency={currency} mode="pnl" parens />
              )}
            </td>
            <td className="tbl--r"><AmountCell value={totalD} currency={currency} strong /></td>
            <td className="tbl--r"><AmountCell value={totalC} currency={currency} strong /></td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
