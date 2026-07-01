// Meridian reconciliation panel — a two-sided statement vs. ledger comparison rendered as proper
// multi-column data tables (Date · Reference · Memo · [Category] · Amount). A shared toolbar
// filters by match status (All / Matched / Open) and free-text searches across the visible
// columns; every column header is click-to-sort (asc → desc → unsorted). Matched rows carry a
// green wash, unmatched an amber wash. A summary bar totals each side over the FULL data set (not
// the filtered view) and flags the difference: "Reconciled" (green) within tolerance, "Out by …"
// (red) otherwise. Self-contained (native checkbox + search) so it has no cross-unit deps.
import { useState } from "react";
import type { KeyboardEvent } from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";

export interface ReconciliationItem {
  id?: string | number;
  date?: string;
  /** Document / transaction reference. */
  ref?: string;
  memo?: string;
  /** Optional classification — surfaces a "Category" column when any item sets it. */
  category?: string;
  amount: number | string;
  /** Whether this line has a counterpart on the other side. */
  matched?: boolean;
  [key: string]: unknown;
}

export interface ReconciliationSide {
  /** Side label (e.g. "Statement", "Ledger"). Also used in the summary bar. */
  title: string;
  items: ReconciliationItem[];
}

export interface ReconciliationColumn {
  /** Item field this column reads. */
  key: string;
  /** Column header label. */
  label: string;
  /** Render in the tabular mono/data font (dates, refs). */
  mono?: boolean;
  /** Right-align the header + cells (numeric columns). */
  num?: boolean;
  /** Render the cell through `AmountCell` with accounting formatting. */
  amount?: boolean;
}

export interface ReconciliationPanelProps {
  left: ReconciliationSide;
  right: ReconciliationSide;
  /**
   * Column definitions, applied to both sides. Defaults to Date · Reference · Memo · Amount,
   * with a Category column inserted automatically when any item carries `category`.
   */
  columns?: ReconciliationColumn[];
  /** @default "USD" */
  currency?: string;
  /** Explicit statement-side balance. Defaults to the sum of `left.items`. */
  statementBalance?: number | string;
  /** Explicit book-side balance. Defaults to the sum of `right.items`. */
  bookBalance?: number | string;
  /** Absolute difference treated as reconciled. @default 0.005 */
  tolerance?: number;
  /** Show the free-text search box. @default true */
  searchable?: boolean;
  /** Show the All / Matched / Open status filter. @default true */
  filterable?: boolean;
  /** Fires when a single item's match checkbox toggles. */
  onToggleItem?: (id: ReconciliationItem["id"], matched: boolean) => void;
  /** Fires when a side's header checkbox toggles all visible items. */
  onToggleAll?: (side: ReconciliationSide, matched: boolean) => void;
}

type Sort = { key: string | null; dir: number };
type StatusFilter = "all" | "matched" | "open";

let injected = false;
function inject(): void {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.rec{border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-card,2px);
  background:var(--bg-light,#FFFFFF);overflow:hidden;}
.rec__toolbar{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;
  padding:8px 12px;background:var(--bg-medium,#EBEFF4);border-bottom:1px solid var(--border,#CBD3DC);}
.rec__seg{display:inline-flex;border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-chip,2px);overflow:hidden;background:var(--bg-light,#FFFFFF);}
.rec__seg button{appearance:none;border:none;background:transparent;cursor:pointer;
  font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
  color:var(--text-muted,#59636F);padding:4px 10px;line-height:1.4;border-left:1px solid var(--border,#CBD3DC);}
.rec__seg button:first-child{border-left:none;}
.rec__seg button[aria-pressed="true"]{background:var(--bg-medium,#EBEFF4);color:var(--text-primary,#22272E);box-shadow:inset 0 -2px 0 var(--accent,#2F6F8F);}
.rec__search{font-family:var(--font-body,inherit);font-size:12px;color:var(--text-primary,#22272E);
  background:var(--bg-light,#FFFFFF);border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-button,2px);padding:4px 9px;line-height:1.4;min-width:180px;}
.rec__search:focus{outline:2px solid var(--accent,#2F6F8F);outline-offset:-1px;border-color:var(--accent,#2F6F8F);}
.rec__cols{display:grid;grid-template-columns:1fr 1fr;}
.rec__side{min-width:0;display:flex;flex-direction:column;}
.rec__side + .rec__side{border-left:1px solid var(--border,#CBD3DC);}
.rec__head{display:flex;align-items:center;justify-content:space-between;gap:8px;
  padding:9px 12px;background:var(--bg-medium,#EBEFF4);border-bottom:1px solid var(--border,#CBD3DC);}
.rec__title{font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);}
.rec__counts{display:flex;gap:6px;}
.rec__chip{font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.02em;border:1px solid;border-radius:var(--radius-chip,2px);padding:1px 6px;line-height:1.4;white-space:nowrap;}
.rec__chip--ok{background:var(--green-a10,rgba(22,136,95,.10));border-color:var(--green,#16885F);color:var(--green-dim,#10663F);}
.rec__chip--open{background:var(--orange-a10,rgba(138,82,14,.10));border-color:var(--orange,#8A520E);color:var(--orange-dim,#683E0B);}
.rec__tablewrap{overflow-x:auto;}
.rec__table{width:100%;border-collapse:collapse;table-layout:auto;}
.rec__table th{position:sticky;top:0;background:var(--bg-light,#FFFFFF);text-align:left;
  font-family:var(--font-body,inherit);font-size:9px;font-weight:600;font-variant:all-small-caps;letter-spacing:.04em;
  color:var(--text-muted,#59636F);padding:6px 10px;border-bottom:1px solid var(--border,#CBD3DC);
  white-space:nowrap;cursor:pointer;user-select:none;}
.rec__table th:hover{color:var(--text-primary,#22272E);}
.rec__table th.is-sorted{color:var(--accent,#2F6F8F);}
.rec__table th.num{text-align:right;}
.rec__caret{display:inline-block;width:0;height:0;margin-left:4px;vertical-align:middle;}
.rec__caret--asc{border-left:3px solid transparent;border-right:3px solid transparent;border-bottom:4px solid currentColor;}
.rec__caret--desc{border-left:3px solid transparent;border-right:3px solid transparent;border-top:4px solid currentColor;}
.rec__table td{font-family:var(--font-body,inherit);font-size:12px;color:var(--text-primary,#22272E);
  padding:6px 10px;border-top:1px solid var(--border,#CBD3DC);vertical-align:baseline;white-space:nowrap;}
.rec__table td.mono{font-family:var(--font-data,monospace);font-size:11px;font-variant-numeric:tabular-nums;color:var(--text-muted,#59636F);}
.rec__table td.memo{white-space:normal;max-width:0;width:99%;}
.rec__table td.num{text-align:right;}
.rec__table tr.matched td{background:var(--green-a10,rgba(22,136,95,.06));}
.rec__table tr.open td{background:var(--orange-a10,rgba(138,82,14,.06));}
.rec__empty{padding:16px 12px;font-family:var(--font-body,inherit);font-size:12px;color:var(--text-muted,#59636F);text-align:center;}
.rec__summary{display:grid;grid-template-columns:1fr 1fr auto;gap:12px;align-items:center;
  padding:10px 14px;border-top:2px solid var(--border-strong,#99A5B2);background:var(--bg-medium,#EBEFF4);}
.rec__sumcell{display:flex;flex-direction:column;gap:2px;min-width:0;}
.rec__sumlabel{font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);}
.rec__statusbox{display:flex;align-items:center;gap:7px;justify-self:end;
  font-family:var(--font-body,inherit);font-size:11px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;border:1px solid;border-radius:var(--radius-chip,2px);padding:4px 10px;}
.rec__statusbox--bal{background:var(--green-a10,rgba(22,136,95,.10));border-color:var(--green,#16885F);color:var(--green-dim,#10663F);}
.rec__statusbox--out{background:var(--red-a10,rgba(186,63,85,.10));border-color:var(--red,#BA3F55);color:var(--red-dim,#8C2F40);}
.rec__dot{height:6px;width:6px;border-radius:50%;background:currentColor;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "reconcile");
  el.textContent = css;
  document.head.appendChild(el);
}

function compareItems(a: ReconciliationItem, b: ReconciliationItem, key: string): number {
  if (key === "amount") return (toNumber(a.amount) || 0) - (toNumber(b.amount) || 0);
  if (key === "status") return (a.matched ? 1 : 0) - (b.matched ? 1 : 0);
  const an = toNumber(a[key]);
  const bn = toNumber(b[key]);
  if (Number.isFinite(an) || Number.isFinite(bn)) return (Number.isFinite(an) ? an : 0) - (Number.isFinite(bn) ? bn : 0);
  const av = String(a[key] ?? "");
  const bv = String(b[key] ?? "");
  return av.localeCompare(bv, undefined, { numeric: true });
}

interface SortHeadProps {
  col: ReconciliationColumn;
  sort: Sort;
  onSort: (key: string) => void;
}
function SortHead({ col, sort, onSort }: SortHeadProps) {
  const active = sort.key === col.key;
  const onKeyDown = (e: KeyboardEvent<HTMLTableHeaderCellElement>) => {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      onSort(col.key);
    }
  };
  return (
    <th
      className={`${col.num ? "num" : ""} ${active ? "is-sorted" : ""}`.trim()}
      onClick={() => onSort(col.key)}
      onKeyDown={onKeyDown}
      tabIndex={0}
      aria-sort={active ? (sort.dir > 0 ? "ascending" : "descending") : "none"}
      scope="col"
    >
      {col.label}
      {active && <span className={`rec__caret rec__caret--${sort.dir > 0 ? "asc" : "desc"}`} aria-hidden="true" />}
    </th>
  );
}

interface SideProps {
  side: ReconciliationSide;
  columns: ReconciliationColumn[];
  currency: string;
  status: StatusFilter;
  query: string;
  sort: Sort;
  onSort: (key: string) => void;
  onToggleItem?: ReconciliationPanelProps["onToggleItem"];
  onToggleAll?: ReconciliationPanelProps["onToggleAll"];
}
function Side({ side, columns, currency, status, query, sort, onSort, onToggleItem, onToggleAll }: SideProps) {
  const items = side.items || [];
  const matched = items.filter((i) => i.matched).length;
  const open = items.length - matched;

  let rows = items;
  if (status === "matched") rows = rows.filter((i) => i.matched);
  else if (status === "open") rows = rows.filter((i) => !i.matched);

  const q = query.trim().toLowerCase();
  if (q) {
    rows = rows.filter((i) => columns.some((c) => String(i[c.key] ?? "").toLowerCase().includes(q)));
  }

  if (sort.key) {
    const key = sort.key;
    rows = [...rows].sort((a, b) => sort.dir * compareItems(a, b, key));
  }

  const allMatched = rows.length > 0 && rows.every((i) => i.matched);

  return (
    <div className="rec__side">
      <div className="rec__head">
        <span className="rec__title">{side.title}</span>
        <span className="rec__counts">
          <span className="rec__chip rec__chip--ok">{matched} matched</span>
          {open > 0 && <span className="rec__chip rec__chip--open">{open} open</span>}
        </span>
      </div>
      <div className="rec__tablewrap">
        <table className="rec__table">
          <thead>
            <tr>
              <th style={{ width: 28, textAlign: "center" }}>
                <input
                  type="checkbox"
                  checked={allMatched}
                  aria-label={`Match all ${side.title} items`}
                  onChange={(e) => onToggleAll?.(side, e.target.checked)}
                />
              </th>
              {columns.map((c) => (
                <SortHead key={c.key} col={c} sort={sort} onSort={onSort} />
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 && (
              <tr>
                <td className="rec__empty" colSpan={columns.length + 1}>
                  No matching items
                </td>
              </tr>
            )}
            {rows.map((it, i) => (
              <tr key={it.id ?? i} className={it.matched ? "matched" : "open"}>
                <td style={{ textAlign: "center", paddingLeft: 6, paddingRight: 6 }}>
                  <input
                    type="checkbox"
                    checked={it.matched || false}
                    aria-label={`Match ${side.title} item ${it.ref ?? i + 1}`}
                    onChange={(e) => onToggleItem?.(it.id, e.target.checked)}
                  />
                </td>
                {columns.map((c) =>
                  c.amount ? (
                    <td key={c.key} className="num">
                      <AmountCell value={it[c.key] as number | string} currency={currency} parens />
                    </td>
                  ) : (
                    <td key={c.key} className={`${c.mono ? "mono" : ""} ${c.key === "memo" ? "memo" : ""}`.trim()}>
                      {String(it[c.key] ?? "")}
                    </td>
                  )
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

const STATUS_TABS: Array<{ key: StatusFilter; label: string }> = [
  { key: "all", label: "All" },
  { key: "matched", label: "Matched" },
  { key: "open", label: "Open" }
];

export function ReconciliationPanel({
  left,
  right,
  columns,
  currency = "USD",
  statementBalance,
  bookBalance,
  tolerance = 0.005,
  searchable = true,
  filterable = true,
  onToggleItem,
  onToggleAll
}: ReconciliationPanelProps) {
  inject();
  const [status, setStatus] = useState<StatusFilter>("all");
  const [query, setQuery] = useState("");
  const [sort, setSort] = useState<Sort>({ key: null, dir: 1 });

  const onSort = (key: string) =>
    setSort((s) => (s.key !== key ? { key, dir: 1 } : s.dir > 0 ? { key, dir: -1 } : { key: null, dir: 1 }));

  // Resolve columns: explicit prop, else default set + Category only if present in the data.
  const allItems = [...(left.items || []), ...(right.items || [])];
  const hasCategory = allItems.some((i) => i.category != null && i.category !== "");
  const cols: ReconciliationColumn[] =
    columns || [
      { key: "date", label: "Date", mono: true },
      { key: "ref", label: "Reference", mono: true },
      { key: "memo", label: "Memo" },
      ...(hasCategory ? [{ key: "category", label: "Category" }] : []),
      { key: "amount", label: "Amount", num: true, amount: true }
    ];

  const sb =
    statementBalance != null
      ? toNumber(statementBalance)
      : (left.items || []).reduce((a, i) => a + (toNumber(i.amount) || 0), 0);
  const bb =
    bookBalance != null ? toNumber(bookBalance) : (right.items || []).reduce((a, i) => a + (toNumber(i.amount) || 0), 0);
  const diff = sb - bb;
  const balanced = Math.abs(diff) <= tolerance;

  return (
    <div className="rec" role="group" aria-label="Reconciliation">
      {(filterable || searchable) && (
        <div className="rec__toolbar">
          {filterable ? (
            <div className="rec__seg" role="group" aria-label="Filter by status">
              {STATUS_TABS.map((t) => (
                <button key={t.key} type="button" aria-pressed={status === t.key} onClick={() => setStatus(t.key)}>
                  {t.label}
                </button>
              ))}
            </div>
          ) : (
            <span />
          )}
          {searchable && (
            <input
              className="rec__search"
              type="search"
              placeholder="Filter memo, ref…"
              aria-label="Filter reconciliation items"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          )}
        </div>
      )}
      <div className="rec__cols">
        <Side
          side={left}
          columns={cols}
          currency={currency}
          status={status}
          query={query}
          sort={sort}
          onSort={onSort}
          onToggleItem={onToggleItem}
          onToggleAll={onToggleAll}
        />
        <Side
          side={right}
          columns={cols}
          currency={currency}
          status={status}
          query={query}
          sort={sort}
          onSort={onSort}
          onToggleItem={onToggleItem}
          onToggleAll={onToggleAll}
        />
      </div>
      <div className="rec__summary">
        <div className="rec__sumcell">
          <span className="rec__sumlabel">{left.title} balance</span>
          <AmountCell value={sb} currency={currency} parens strong />
        </div>
        <div className="rec__sumcell">
          <span className="rec__sumlabel">{right.title} balance</span>
          <AmountCell value={bb} currency={currency} parens strong />
        </div>
        <div className={`rec__statusbox ${balanced ? "rec__statusbox--bal" : "rec__statusbox--out"}`} role="status">
          <span className="rec__dot" aria-hidden="true" />
          {balanced ? (
            "Reconciled"
          ) : (
            <>
              Out by <AmountCell value={Math.abs(diff)} currency={currency} strong style={{ color: "inherit" }} />
            </>
          )}
        </div>
      </div>
    </div>
  );
}

ReconciliationPanel.displayName = "ReconciliationPanel";
