// Meridian financial-statement table — grouped sections (P&L, balance sheet, cash flow) with
// indented line items, per-section subtotals, and a strong grand total. Supports one or two
// amount columns for period comparison. Accounting negatives in parentheses. Light theme.
import React from "react";
import { AmountCell } from "./AmountCell";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.stm-wrap{overflow-x:auto;border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-chip,4px);background:var(--bg-light,#fff);}
.stm{width:100%;border-collapse:separate;border-spacing:0;font-family:var(--font-data);font-size:12px;}
.stm thead th{padding:9px 14px;white-space:nowrap;background:var(--bg-medium,#F5F7FA);
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#6E7781);
  border-bottom:1px solid var(--border,#D7DCE2);text-align:right;}
.stm thead th.stm--l{text-align:left;}
.stm__section td{padding:10px 14px 5px;font-family:var(--font-body);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.05em;color:var(--text-secondary,#4D5967);
  border-top:1px solid var(--border,#D7DCE2);}
.stm__section--first td{border-top:none;}
.stm__item td{padding:8px 14px;color:var(--text-primary,#22272E);}
.stm__item td.stm--l{color:var(--text-secondary,#4D5967);}
.stm__item td.stm--num{text-align:right;}
.stm__item--muted td.stm--l{color:var(--text-muted,#6E7781);}
.stm__sub td{padding:10px 14px;font-weight:600;border-top:1px solid var(--border,#D7DCE2);
  background:var(--card-surface-raised,#FAFBFC);}
.stm__sub td.stm--l{font-family:var(--font-body);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-primary,#22272E);}
.stm__sub td.stm--num{text-align:right;}
.stm__total td{padding:10px 14px;font-weight:600;background:var(--bg-medium,#F5F7FA);
  border-top:2px solid var(--border-strong,#AAB4BF);
  border-bottom:3px double var(--border-strong,#AAB4BF);}
.stm__total td.stm--l{font-family:var(--font-body);font-variant:all-small-caps;letter-spacing:.04em;
  font-size:12px;color:var(--text-primary,#22272E);}
.stm__total td.stm--num{text-align:right;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "statement");
  el.textContent = css;
  document.head.appendChild(el);
}

export function StatementTable({
  sections,
  total,
  columns,                 // [{ key, label }] — defaults to one unlabeled "value" column
  currency = "USD",
  parens = true,
  pnl = false,             // color amounts as gains/losses
}) {
  inject();
  const cols = columns && columns.length ? columns : [{ key: "value", label: "" }];

  const valuesOf = (obj) => {
    if (obj == null) return cols.map(() => null);
    if (obj.values) return cols.map((c) => obj.values[c.key]);
    return cols.map(() => obj.value); // single value broadcast (1-col case)
  };

  return (
    <div className="stm-wrap" role="table" aria-label="Financial statement">
      <table className="stm">
        <thead>
          <tr>
            <th className="stm--l">&nbsp;</th>
            {cols.map((c) => (
              <th key={c.key}>{c.label}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {sections.map((sec, si) => (
            <React.Fragment key={si}>
              {sec.label && (
                <tr className={`stm__section${si === 0 ? " stm__section--first" : ""}`}>
                  <td colSpan={cols.length + 1}>{sec.label}</td>
                </tr>
              )}
              {(sec.rows || []).map((row, ri) => (
                <tr key={ri} className={`stm__item${row.muted ? " stm__item--muted" : ""}`}>
                  <td className="stm--l" style={{ paddingLeft: 14 + (row.indent || 0) * 16 }}>
                    {row.label}
                  </td>
                  {valuesOf(row).map((v, vi) => (
                    <td key={vi} className="stm--num">
                      {v == null || v === ""
                        ? <span style={{ color: "var(--text-disabled,#9AA4AF)" }}>&mdash;</span>
                        : <AmountCell value={v} currency={currency} parens={parens} mode={pnl ? "pnl" : "plain"} />}
                    </td>
                  ))}
                </tr>
              ))}
              {sec.subtotal && (
                <tr className="stm__sub">
                  <td className="stm--l">{sec.subtotal.label}</td>
                  {valuesOf(sec.subtotal).map((v, vi) => (
                    <td key={vi} className="stm--num">
                      <AmountCell value={v} currency={currency} parens={parens} strong mode={pnl ? "pnl" : "plain"} />
                    </td>
                  ))}
                </tr>
              )}
            </React.Fragment>
          ))}
        </tbody>
        {total && (
          <tfoot>
            <tr className="stm__total">
              <td className="stm--l">{total.label}</td>
              {valuesOf(total).map((v, vi) => (
                <td key={vi} className="stm--num">
                  <AmountCell value={v} currency={currency} parens={parens} strong mode={pnl ? "pnl" : "plain"} />
                </td>
              ))}
            </tr>
          </tfoot>
        )}
      </table>
    </div>
  );
}
