// Meridian general-ledger / journal table — double-entry rows with Debit / Credit columns and a
// running Balance, plus a totals footer that proves Σdebit = Σcredit (the footer balance turns to
// P&L color when the two sides disagree). Flat Concrete grid: white paper, small-caps muted
// headers, hairline rows, mono tabular figures.
import { useState, type ThHTMLAttributes } from "react";
import type { AriaAttributes, KeyboardEvent } from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";

export interface LedgerRow {
  date: string;
  /** Document / journal reference (rendered in accent). */
  ref?: string;
  /** Posting description. */
  memo?: string;
  /** Account name/code — shown only when `showAccount` is set. */
  account?: string;
  /** Debit amount (number or money-ish string). Blank side renders as a dash. */
  debit?: number | string;
  /** Credit amount. */
  credit?: number | string;
  /** Explicit running balance. Omit to auto-compute from `opening` + normal-side deltas. */
  balance?: number | string;
}

export interface LedgerTableProps {
  rows: LedgerRow[];
  /** @default "USD" */
  currency?: string;
  /** Opening balance — renders a leading muted row and seeds the running balance. */
  opening?: number | string;
  /** Show an Account column (journal spanning multiple accounts). @default false */
  showAccount?: boolean;
  /** Which side increases the running balance. @default "debit" */
  normalSide?: "debit" | "credit";
  /** Accessible caption / region label. */
  caption?: string;
  /** Fires when a sortable header is clicked. */
  onSort?: (key: string, dir: number) => void;
}

type SortKey = "date" | "ref" | "account" | "memo" | "debit" | "credit" | "balance";

let injected = false;
function inject(): void {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.ldg-wrap{overflow-x:auto;border:1px solid var(--border,#CBD3DC);
  border-radius:var(--radius-chip,2px);background:var(--bg-light,#FFFFFF);}
.ldg{width:100%;min-width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data,monospace);font-size:12px;}
.ldg thead th{padding:9px 12px;text-align:left;white-space:nowrap;position:sticky;top:0;
  background:var(--bg-medium,#EBEFF4);z-index:1;
  font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border-strong,#99A5B2);border-right:1px solid var(--border-divider,#CBD3DC);}
.ldg thead th:last-child{border-right:none;}
.ldg th.ldg--r{text-align:right;}
.ldg td{padding:11px 12px;white-space:nowrap;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#CBD3DC);border-right:1px solid var(--border-divider,#CBD3DC);
  vertical-align:baseline;height:40px;}
.ldg td:last-child{border-right:none;}
.ldg tbody tr:first-child td{border-top:none;}
.ldg td.ldg--r{text-align:right;}
.ldg tbody tr:hover{background:var(--bg-hover,#F3F6F9);}
.ldg__date{color:var(--text-secondary,#4D5967);}
.ldg__ref{color:var(--accent,#2F6F8F);}
.ldg__memo{font-family:var(--font-body,inherit);color:var(--text-secondary,#4D5967);
  white-space:normal;min-width:160px;}
.ldg__acct{color:var(--text-primary,#22272E);}
.ldg__open td{background:var(--bg-medium,#EBEFF4);color:var(--text-muted,#59636F);}
.ldg__open .ldg__memo{font-style:italic;}
.ldg tfoot td{padding:9px 12px;background:var(--bg-medium,#EBEFF4);font-weight:600;
  border-top:2px solid var(--border-strong,#99A5B2);color:var(--text-primary,#22272E);}
.ldg tfoot td.ldg--r{text-align:right;}
.ldg__foot-label{font-family:var(--font-body,inherit);font-variant:all-small-caps;letter-spacing:.03em;
  font-size:11px;color:var(--text-secondary,#4D5967);}
.ldg__sort{cursor:pointer;user-select:none;}
.ldg__sort:focus-visible{outline:2px solid var(--accent,#2F6F8F);outline-offset:-2px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "ledger");
  el.textContent = css;
  document.head.appendChild(el);
}

export function LedgerTable({
  rows,
  currency = "USD",
  opening,
  showAccount = false,
  normalSide = "debit",
  caption,
  onSort
}: LedgerTableProps) {
  inject();
  const [sortKey, setSortKey] = useState<SortKey | null>(null);
  const [sortDir, setSortDir] = useState(1);

  const handleSort = (key: SortKey) => {
    if (!onSort) return;
    const nextDir = sortKey === key ? (sortDir === 1 ? -1 : 1) : 1;
    setSortKey(key);
    setSortDir(nextDir);
    onSort(key, nextDir);
  };
  const handleSortKey = (key: SortKey) => (e: KeyboardEvent<HTMLTableHeaderCellElement>) => {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      handleSort(key);
    }
  };

  const openingNum = toNumber(opening);
  const hasOpening = Number.isFinite(openingNum);

  // Compute running balances when a row omits `balance`.
  let bal = hasOpening ? openingNum : 0;
  let canCompute = hasOpening;
  const computed = rows.map((r) => {
    const d = toNumber(r.debit) || 0;
    const c = toNumber(r.credit) || 0;
    const delta = normalSide === "credit" ? c - d : d - c;
    const explicit = toNumber(r.balance);
    if (r.balance != null && Number.isFinite(explicit)) {
      bal = explicit;
      canCompute = true;
    } else {
      if (canCompute) bal += delta;
    }
    return { ...r, _bal: bal, _hasBal: canCompute };
  });

  const totalD = rows.reduce((a, r) => a + (toNumber(r.debit) || 0), 0);
  const totalC = rows.reduce((a, r) => a + (toNumber(r.credit) || 0), 0);
  const imbalance = totalD - totalC;

  const caret = (key: SortKey) => (onSort && sortKey === key ? (sortDir === 1 ? " ↑" : " ↓") : "");
  const sortableProps = (key: SortKey): ThHTMLAttributes<HTMLTableCellElement> =>
    onSort
      ? {
          className: "ldg__sort",
          onClick: () => handleSort(key),
          onKeyDown: handleSortKey(key),
          tabIndex: 0,
          "aria-sort": (sortKey === key
            ? sortDir === 1
              ? "ascending"
              : "descending"
            : "none") as AriaAttributes["aria-sort"],
          scope: "col",
        }
      : { scope: "col" };

  return (
    <div className="ldg-wrap" role="region" aria-label={caption || "General ledger"}>
      <table className="ldg">
        <thead>
          <tr>
            <th {...sortableProps("date")}>
              Date{caret("date")}
            </th>
            <th {...sortableProps("ref")}>
              Ref{caret("ref")}
            </th>
            {showAccount && (
              <th {...sortableProps("account")}>
                Account{caret("account")}
              </th>
            )}
            <th {...sortableProps("memo")}>
              Description{caret("memo")}
            </th>
            <th {...sortableProps("debit")} className={`${onSort ? "ldg__sort " : ""}ldg--r`.trim()}>
              Debit{caret("debit")}
            </th>
            <th {...sortableProps("credit")} className={`${onSort ? "ldg__sort " : ""}ldg--r`.trim()}>
              Credit{caret("credit")}
            </th>
            <th {...sortableProps("balance")} className={`${onSort ? "ldg__sort " : ""}ldg--r`.trim()}>
              Balance{caret("balance")}
            </th>
          </tr>
        </thead>
        <tbody>
          {hasOpening && (
            <tr className="ldg__open">
              <td className="ldg__date" />
              <td />
              {showAccount && <td />}
              <td className="ldg__memo">Opening balance</td>
              <td className="ldg--r" />
              <td className="ldg--r" />
              <td className="ldg--r">
                <AmountCell value={openingNum} currency={currency} mode="muted" />
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
                <AmountCell value={r.debit ?? ""} currency={currency} zeroDash />
              </td>
              <td className="ldg--r">
                <AmountCell value={r.credit ?? ""} currency={currency} zeroDash />
              </td>
              <td className="ldg--r">
                {r._hasBal ? (
                  <AmountCell value={r._bal} currency={currency} parens />
                ) : (
                  <span style={{ color: "var(--text-disabled, #889099)" }}>—</span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td className="ldg__foot-label" colSpan={showAccount ? 4 : 3}>
              Totals
            </td>
            <td className="ldg--r">
              <AmountCell value={totalD} currency={currency} strong />
            </td>
            <td className="ldg--r">
              <AmountCell value={totalC} currency={currency} strong />
            </td>
            <td className="ldg--r">
              <AmountCell
                value={imbalance}
                currency={currency}
                mode={Math.abs(imbalance) < 0.005 ? "muted" : "pnl"}
                parens
                strong
                aria-label={Math.abs(imbalance) < 0.005 ? "Ledger balanced" : "Ledger imbalance"}
              />
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}

LedgerTable.displayName = "LedgerTable";
