// Meridian financial-statement table — P&L, balance sheet, or cash-flow layout: grouped sections
// with indented line items, per-section subtotals, and a strong double-ruled grand total. Supports
// one or two amount columns for period comparison. Negatives render in accounting parentheses.
import { Fragment } from "react";
import { AmountCell } from "./AmountCell";

export interface StatementValue {
  label: string;
  /** Single amount (when there's one column). */
  value?: number | string;
  /** Per-column amounts, keyed by `columns[].key` (comparison statements). */
  values?: Record<string, number | string>;
  /** Indent level for nested line items. @default 0 */
  indent?: number;
  /** Render the label muted (informational sub-lines). */
  muted?: boolean;
}

export interface StatementSection {
  /** Small-caps section header (e.g. "Revenue", "Operating expenses"). Optional. */
  label?: string;
  rows: StatementValue[];
  /** Section subtotal row (top border, bold). */
  subtotal?: StatementValue;
}

export interface StatementColumn {
  key: string;
  label: string;
}

export interface StatementTableProps {
  sections: StatementSection[];
  /** Grand-total row — double-ruled, bold. */
  total?: StatementValue;
  /** Amount columns. Omit for a single unlabeled column. */
  columns?: StatementColumn[];
  /** @default "USD" */
  currency?: string;
  /** Negatives in parentheses. @default true */
  parens?: boolean;
  /** Color amounts as gains/losses (negative red / positive green). @default false */
  pnl?: boolean;
}

let injected = false;
function inject(): void {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.stm-wrap{overflow-x:auto;border:1px solid var(--border,#CBD3DC);
  border-radius:var(--radius-chip,2px);background:var(--bg-light,#FFFFFF);}
.stm{width:100%;border-collapse:separate;border-spacing:0;font-family:var(--font-data,monospace);font-size:12px;}
.stm thead th{padding:9px 14px;white-space:nowrap;background:var(--bg-medium,#EBEFF4);
  font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border,#CBD3DC);text-align:right;}
.stm thead th.stm--l{text-align:left;}
.stm__section td{padding:10px 14px 5px;font-family:var(--font-body,inherit);font-size:10px;font-weight:600;
  font-variant:all-small-caps;letter-spacing:.05em;color:var(--text-secondary,#4D5967);
  border-top:1px solid var(--border,#CBD3DC);}
.stm__section--first td{border-top:none;}
.stm__item td{padding:9px 14px;color:var(--text-primary,#22272E);height:40px;}
.stm__item td.stm--l{color:var(--text-secondary,#4D5967);}
.stm__item td.stm--num{text-align:right;}
.stm__item--muted td.stm--l{color:var(--text-muted,#59636F);}
.stm__sub td{padding:10px 14px;font-weight:600;border-top:1px solid var(--border,#CBD3DC);
  background:var(--card-surface-raised,#F3F6F9);}
.stm__sub td.stm--l{font-family:var(--font-body,inherit);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-primary,#22272E);}
.stm__sub td.stm--num{text-align:right;}
.stm__total td{padding:10px 14px;font-weight:600;background:var(--bg-medium,#EBEFF4);
  border-top:2px solid var(--border-strong,#99A5B2);
  border-bottom:3px double var(--border-strong,#99A5B2);}
.stm__total td.stm--l{font-family:var(--font-body,inherit);font-variant:all-small-caps;letter-spacing:.04em;
  font-size:12px;color:var(--text-primary,#22272E);}
.stm__total td.stm--num{text-align:right;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "statement");
  el.textContent = css;
  document.head.appendChild(el);
}

export function StatementTable({ sections, total, columns, currency = "USD", parens = true, pnl = false }: StatementTableProps) {
  inject();
  const cols: StatementColumn[] = columns && columns.length ? columns : [{ key: "value", label: "" }];

  const valuesOf = (obj: StatementValue | undefined | null): Array<number | string | undefined> => {
    if (obj == null) return cols.map(() => undefined);
    if (obj.values) return cols.map((c) => obj.values![c.key]);
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
            <Fragment key={si}>
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
                      {v == null || v === "" ? (
                        <span style={{ color: "var(--text-disabled, #889099)" }}>—</span>
                      ) : (
                        <AmountCell value={v} currency={currency} parens={parens} mode={pnl ? "pnl" : "plain"} />
                      )}
                    </td>
                  ))}
                </tr>
              ))}
              {sec.subtotal && (
                <tr className="stm__sub">
                  <td className="stm--l">{sec.subtotal.label}</td>
                  {valuesOf(sec.subtotal).map((v, vi) => (
                    <td key={vi} className="stm--num">
                      <AmountCell value={v ?? ""} currency={currency} parens={parens} strong mode={pnl ? "pnl" : "plain"} />
                    </td>
                  ))}
                </tr>
              )}
            </Fragment>
          ))}
        </tbody>
        {total && (
          <tfoot>
            <tr className="stm__total">
              <td className="stm--l">{total.label}</td>
              {valuesOf(total).map((v, vi) => (
                <td key={vi} className="stm--num">
                  <AmountCell value={v ?? ""} currency={currency} parens={parens} strong mode={pnl ? "pnl" : "plain"} />
                </td>
              ))}
            </tr>
          </tfoot>
        )}
      </table>
    </div>
  );
}

StatementTable.displayName = "StatementTable";
