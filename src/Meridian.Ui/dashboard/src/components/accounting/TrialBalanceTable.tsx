import { Fragment } from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";
import type { AccountingTrialBalanceRowViewModel } from "@/screens/accounting-screen.view-model";

export interface TrialBalanceTableProps {
  rows: AccountingTrialBalanceRowViewModel[];
  selectedRowId?: string | null;
  currency?: string;
  caption?: string;
  grouped?: boolean;
  onRowSelect?: (row: AccountingTrialBalanceRowViewModel) => void;
}

type TrialBalanceSection = "Assets" | "Liabilities" | "Equity" | "Revenue" | "Expenses" | "Other";

const SECTIONS: TrialBalanceSection[] = ["Assets", "Liabilities", "Equity", "Revenue", "Expenses", "Other"];

let injected = false;
function inject(): void {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.tbl-wrap{overflow-x:auto;border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-chip,2px);background:var(--bg-light,#FFFFFF);}
.tbl{width:100%;min-width:760px;border-collapse:separate;border-spacing:0;font-family:var(--font-data,monospace);font-size:12px;}
.tbl thead th{padding:9px 12px;text-align:left;white-space:nowrap;position:sticky;top:0;background:var(--bg-medium,#EBEFF4);z-index:1;font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);border-bottom:1px solid var(--border-strong,#99A5B2);border-right:1px solid var(--border-divider,#CBD3DC);}
.tbl thead th:last-child,.tbl td:last-child{border-right:none;}
.tbl th.tbl--r,.tbl td.tbl--r{text-align:right;}
.tbl td{padding:10px 12px;white-space:nowrap;color:var(--text-primary,#22272E);border-top:1px solid var(--border,#CBD3DC);border-right:1px solid var(--border-divider,#CBD3DC);vertical-align:top;}
.tbl tbody tr:first-child td{border-top:none;}
.tbl__sec td{background:var(--bg-medium,#EBEFF4);font-family:var(--font-body,inherit);font-size:11px;font-weight:700;font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-secondary,#4D5967);}
.tbl__acct{font-family:var(--font-body,inherit);white-space:normal;min-width:210px;}
.tbl__code{color:var(--text-secondary,#4D5967);}
.tbl__row--click{cursor:pointer;}
.tbl__row--click:hover{background:var(--bg-hover,#F3F6F9);}
.tbl__row--click:focus-visible{outline:2px solid var(--accent,#2F6F8F);outline-offset:-2px;}
.tbl__row--on td{background:color-mix(in srgb,var(--accent,#2F6F8F) 8%,transparent);}
.tbl__sub td{background:var(--bg-subtle,#F3F6F9);font-weight:600;color:var(--text-secondary,#4D5967);}
.tbl tfoot td{padding:10px 12px;background:var(--bg-medium,#EBEFF4);font-weight:700;border-top:2px solid var(--border-strong,#99A5B2);color:var(--text-primary,#22272E);}
.tbl__foot-label{font-family:var(--font-body,inherit);font-variant:all-small-caps;letter-spacing:.03em;font-size:11px;color:var(--text-secondary,#4D5967);}
.tbl__foot-note{margin-left:12px;font-size:11px;font-variant:normal;letter-spacing:0;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-meridian-component", "trial-balance-table");
  el.textContent = css;
  document.head.appendChild(el);
}

function sectionOf(row: AccountingTrialBalanceRowViewModel): TrialBalanceSection {
  const value = `${row.accountTypeLabel} ${row.accountType}`.toLowerCase();
  if (/asset/.test(value)) return "Assets";
  if (/liab/.test(value)) return "Liabilities";
  if (/equity|capital|retained|draw|dividend/.test(value)) return "Equity";
  if (/revenue|income|gain/.test(value)) return "Revenue";
  if (/expense|loss|cost/.test(value)) return "Expenses";
  return "Other";
}

function normalSide(row: AccountingTrialBalanceRowViewModel): "debit" | "credit" {
  const value = `${row.accountTypeLabel} ${row.accountType}`.toLowerCase();
  return /liab|equity|capital|revenue|income|gain/.test(value) ? "credit" : "debit";
}

function debitCredit(row: AccountingTrialBalanceRowViewModel): { debit: number; credit: number } {
  const balance = toNumber(row.balance);
  if (!Number.isFinite(balance) || balance === 0) {
    return { debit: 0, credit: 0 };
  }

  const side = balance >= 0
    ? normalSide(row)
    : normalSide(row) === "debit" ? "credit" : "debit";
  return side === "debit"
    ? { debit: Math.abs(balance), credit: 0 }
    : { debit: 0, credit: Math.abs(balance) };
}

export function TrialBalanceTable({
  rows,
  selectedRowId = null,
  currency = "USD",
  caption = "Trial balance",
  grouped = true,
  onRowSelect
}: TrialBalanceTableProps) {
  inject();
  const computedRows = rows.map((row) => ({ row, ...debitCredit(row), section: sectionOf(row) }));
  const totalDebits = computedRows.reduce((sum, row) => sum + row.debit, 0);
  const totalCredits = computedRows.reduce((sum, row) => sum + row.credit, 0);
  const balanced = Math.abs(totalDebits - totalCredits) < 0.005;
  const groups = grouped
    ? SECTIONS.map((section) => ({
        section,
        rows: computedRows.filter((row) => row.section === section)
      })).filter((group) => group.rows.length > 0)
    : [{ section: null, rows: computedRows }];

  return (
    <div className="tbl-wrap" role="region" aria-label={caption}>
      <table className="tbl">
        <thead>
          <tr>
            <th scope="col">Account</th>
            <th scope="col">Type</th>
            <th scope="col">Basis</th>
            <th scope="col" className="tbl--r">Debit</th>
            <th scope="col" className="tbl--r">Credit</th>
            <th scope="col" className="tbl--r">Entries</th>
          </tr>
        </thead>
        <tbody>
          {groups.map((group) => (
            <Fragment key={group.section ?? "all"}>
              {group.section ? (
                <tr className="tbl__sec">
                  <td colSpan={6}>{group.section}</td>
                </tr>
              ) : null}
              {group.rows.map(({ row, debit, credit }) => {
                const selected = row.rowId === selectedRowId;
                return (
                  <tr
                    key={row.rowId}
                    className={[
                      onRowSelect ? "tbl__row--click" : "",
                      selected ? "tbl__row--on" : ""
                    ].filter(Boolean).join(" ") || undefined}
                    tabIndex={onRowSelect ? 0 : undefined}
                    aria-selected={selected || undefined}
                    aria-controls={row.detailPanelId}
                    aria-expanded={row.isExpanded}
                    aria-label={row.ariaLabel}
                    onClick={onRowSelect ? () => onRowSelect(row) : undefined}
                    onKeyDown={onRowSelect ? (event) => {
                      if (event.key === "Enter" || event.key === " ") {
                        event.preventDefault();
                        onRowSelect(row);
                      }
                    } : undefined}
                  >
                    <td className="tbl__acct">
                      <span className="block font-semibold">{row.accountLabel}</span>
                      <span className="tbl__code mt-1 block font-mono text-[11px]">{row.financialAccountId ?? "Unassigned"}</span>
                      <span className="tbl__code mt-1 block break-words font-mono text-[11px]">{row.dimensionLabel}</span>
                    </td>
                    <td className="tbl__code">{row.accountTypeLabel}</td>
                    <td className="tbl__code">{row.basisLabel}</td>
                    <td className="tbl--r">
                      <AmountCell value={debit} currency={currency} zeroDash />
                    </td>
                    <td className="tbl--r">
                      <AmountCell value={credit} currency={currency} zeroDash />
                    </td>
                    <td className="tbl--r">{row.entryCountLabel}</td>
                  </tr>
                );
              })}
              {group.section && group.rows.length > 1 ? (
                <tr className="tbl__sub">
                  <td colSpan={3}>{group.section} subtotal</td>
                  <td className="tbl--r">
                    <AmountCell value={group.rows.reduce((sum, row) => sum + row.debit, 0)} currency={currency} zeroDash mode="muted" />
                  </td>
                  <td className="tbl--r">
                    <AmountCell value={group.rows.reduce((sum, row) => sum + row.credit, 0)} currency={currency} zeroDash mode="muted" />
                  </td>
                  <td />
                </tr>
              ) : null}
            </Fragment>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td className="tbl__foot-label" colSpan={3}>
              Totals
              {!balanced ? (
                <span className="tbl__foot-note text-danger">
                  out of balance by{" "}
                  <AmountCell value={totalDebits - totalCredits} currency={currency} mode="pnl" parens />
                </span>
              ) : null}
            </td>
            <td className="tbl--r">
              <AmountCell value={totalDebits} currency={currency} strong />
            </td>
            <td className="tbl--r">
              <AmountCell value={totalCredits} currency={currency} strong />
            </td>
            <td />
          </tr>
        </tfoot>
      </table>
    </div>
  );
}

TrialBalanceTable.displayName = "TrialBalanceTable";
