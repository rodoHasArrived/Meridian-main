// Meridian income schedule — the synchronized period schedule that replaces a
// generic chart for coupon accrual, premium amortization / discount accretion,
// investment income, and the book-value it rolls to. Selecting a period is a
// committed selection: it drives the journal preview, the rollforward, and the
// evidence rail. Arithmetic comes from money.js (effectiveYieldSchedule) so the
// table and the consumer agree to the cent.
import React from "react";
import { formatMoney } from "../core/format.js";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-isch{width:100%;border-collapse:separate;border-spacing:0;font-family:var(--font-body);
  font-size:12px;color:var(--text-secondary);}
.mds-isch th,.mds-isch td{padding:0 10px;height:30px;border-bottom:1px solid var(--border-divider);
  white-space:nowrap;}
.mds-isch thead th{position:sticky;top:0;z-index:1;background:var(--bg-medium);
  font-size:var(--type-label,10px);font-weight:var(--weight-semibold,600);font-variant:all-small-caps;
  letter-spacing:.05em;color:var(--text-muted);text-align:right;border-bottom:1px solid var(--border-strong);}
.mds-isch thead th:first-child{text-align:left;}
.mds-isch td{font-family:var(--font-data);font-variant-numeric:tabular-nums;text-align:right;
  color:var(--text-primary);}
.mds-isch td:first-child{font-family:var(--font-body);text-align:left;color:var(--text-secondary);}
.mds-isch tbody tr{cursor:pointer;}
.mds-isch tbody tr:hover td{background:var(--bg-hover);}
.mds-isch tbody tr.is-sel td{background:var(--viz-select-surface);}
.mds-isch tbody tr.is-sel td:first-child{box-shadow:inset 3px 0 0 var(--viz-select-rail);}
.mds-isch tbody tr:focus-visible{outline:2px solid var(--border-focus);outline-offset:-2px;}
.mds-isch tfoot td{font-weight:var(--weight-semibold,600);color:var(--text-primary);
  border-top:1px solid var(--border-strong);border-bottom:0;background:var(--bg-light);}
.mds-isch__st{display:inline-flex;align-items:center;gap:5px;font-family:var(--font-body);font-size:11px;}
.mds-isch__st i{font-style:normal;font-size:8px;}
.mds-isch__wrap{width:100%;overflow:auto;border:1px solid var(--border);border-radius:var(--radius-chip,2px);
  background:var(--bg-light);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "income-schedule");
  el.textContent = css;
  document.head.appendChild(el);
}

const STATES = {
  posted:    { label: "Posted",    tone: "confirmed",  glyph: "●" },
  accrued:   { label: "Accrued",   tone: "confirmed",  glyph: "●" },
  scheduled: { label: "Scheduled", tone: "pending",    glyph: "◇" },
  draft:     { label: "Draft",     tone: "provisional", glyph: "◐" },
  estimated: { label: "Estimated", tone: "estimated",  glyph: "○" },
  reversed:  { label: "Reversed",  tone: "exception",  glyph: "▲" },
  overridden:{ label: "Overridden", tone: "overridden", glyph: "◆" },
};

export function IncomeSchedule({
  rows = [], currency = "USD", selectedId, onSelect, showTotals = true, maxHeight = 260,
  couponLabel = "Coupon accrual", accretionLabel = "Discount accretion",
  incomeLabel = "Investment income", bookLabel = "Ending book value",
  className = "", ...rest
}) {
  inject();
  const money = (v) => formatMoney(v, { decimals: 2, parens: true, zeroDash: true });
  const total = (k) => rows.reduce((a, r) => a + (Number(r[k]) || 0), 0);
  return (
    <div className={`mds-isch__wrap${className ? " " + className : ""}`} style={{ maxHeight }} {...rest}>
      <table className="mds-isch">
        <thead>
          <tr>
            <th scope="col">Period</th>
            <th scope="col">{couponLabel}</th>
            <th scope="col">{accretionLabel}</th>
            <th scope="col">{incomeLabel}</th>
            <th scope="col">{bookLabel}</th>
            <th scope="col" style={{ textAlign: "left" }}>State</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r, i) => {
            const id = r.id || r.period || i;
            const st = STATES[String(r.state || "scheduled").toLowerCase()] || STATES.scheduled;
            return (
              <tr
                key={id}
                className={selectedId === id ? "is-sel" : ""}
                tabIndex={onSelect ? 0 : -1}
                onClick={() => onSelect && onSelect(id)}
                onKeyDown={(e) => { if (onSelect && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); onSelect(id); } }}
                aria-selected={selectedId === id}
              >
                <td>{r.period}</td>
                <td>{money(r.coupon)}</td>
                <td>{money(r.accretion)}</td>
                <td>{money(r.income)}</td>
                <td>{money(r.closingBook ?? r.endingBook)}</td>
                <td style={{ textAlign: "left" }}>
                  <span className="mds-isch__st" style={{ color: `var(--cond-${st.tone}-fg)` }}>
                    <i aria-hidden="true">{st.glyph}</i>{r.stateLabel || st.label}
                  </span>
                </td>
              </tr>
            );
          })}
        </tbody>
        {showTotals && rows.length ? (
          <tfoot>
            <tr>
              <td style={{ fontFamily: "var(--font-body)" }}>Total · {rows.length} periods</td>
              <td>{money(total("coupon"))}</td>
              <td>{money(total("accretion"))}</td>
              <td>{money(total("income"))}</td>
              <td>{money(rows[rows.length - 1].closingBook ?? rows[rows.length - 1].endingBook)}</td>
              <td style={{ textAlign: "left", fontFamily: "var(--font-body)", fontWeight: 400, color: "var(--text-muted)", fontSize: 11 }}>{currency}</td>
            </tr>
          </tfoot>
        ) : null}
      </table>
    </div>
  );
}
