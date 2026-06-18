// Meridian reconciliation panel — two-sided statement vs. ledger comparison rendered as proper
// multi-column data tables (Date · Reference · Memo · [Category] · Amount), with a shared toolbar
// for status filtering + free-text search and click-to-sort column headers. A matched/open rail
// colours each row; a summary bar totals each side (on the FULL data set, not the filtered view)
// and flags the difference. Light institutional theme.
import React from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.rec{border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-card,8px);
  background:var(--bg-light,#fff);overflow:hidden;box-shadow:var(--shadow-card,0 1px 1px rgba(0,0,0,.08));}
.rec__toolbar{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;
  padding:8px 12px;background:var(--bg-medium,#F5F7FA);border-bottom:1px solid var(--border,#D7DCE2);}
.rec__seg{display:inline-flex;border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-chip,4px);overflow:hidden;background:var(--bg-light,#fff);}
.rec__seg button{appearance:none;border:none;background:transparent;cursor:pointer;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
  color:var(--text-muted,#6E7781);padding:4px 10px;line-height:1.4;border-left:1px solid var(--border,#D7DCE2);}
.rec__seg button:first-child{border-left:none;}
.rec__seg button[aria-pressed="true"]{background:var(--bg-medium,#F5F7FA);color:var(--text-primary,#22272E);box-shadow:inset 0 -2px 0 var(--accent,#0A5E3E);}
.rec__searchwrap{position:relative;display:flex;align-items:center;}
.rec__search{font-family:var(--font-body);font-size:12px;color:var(--text-primary,#22272E);
  border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-chip,4px);background:var(--bg-light,#fff);
  padding:4px 9px 4px 24px;width:170px;outline:none;}
.rec__search:focus{border-color:var(--accent,#0A5E3E);box-shadow:0 0 0 2px var(--green-a10,rgba(10,94,62,.12));}
.rec__searchicon{position:absolute;left:8px;width:11px;height:11px;color:var(--text-muted,#6E7781);pointer-events:none;}
.rec__cols{display:grid;grid-template-columns:1fr 1fr;}
.rec__side{min-width:0;display:flex;flex-direction:column;}
.rec__side + .rec__side{border-left:1px solid var(--border,#D7DCE2);}
.rec__head{display:flex;align-items:center;justify-content:space-between;gap:8px;
  padding:9px 12px;background:var(--bg-medium,#F5F7FA);border-bottom:1px solid var(--border,#D7DCE2);}
.rec__title{font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#6E7781);}
.rec__counts{display:flex;gap:6px;}
.rec__chip{font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.02em;border:1px solid;border-radius:var(--radius-chip,4px);padding:1px 6px;line-height:1.4;white-space:nowrap;}
.rec__chip--ok{background:var(--green-a10,rgba(22,136,95,.10));border-color:var(--green,#16885F);color:var(--green-dim,#126C4D);}
.rec__chip--open{background:var(--orange-a10,rgba(183,121,31,.10));border-color:var(--orange,#B7791F);color:var(--orange-dim,#946216);}
.rec__tablewrap{overflow-x:auto;}
.rec__table{width:100%;border-collapse:collapse;table-layout:auto;}
.rec__table th{position:sticky;top:0;background:var(--bg-light,#fff);text-align:left;
  font-family:var(--font-body);font-size:9px;font-weight:600;font-variant:all-small-caps;letter-spacing:.04em;
  color:var(--text-muted,#6E7781);padding:6px 10px;border-bottom:1px solid var(--border,#D7DCE2);
  white-space:nowrap;cursor:pointer;user-select:none;}
.rec__table th:hover{color:var(--text-primary,#22272E);}
.rec__table th.is-sorted{color:var(--accent,#0A5E3E);}
.rec__table th.num{text-align:right;}
.rec__caret{display:inline-block;width:0;height:0;margin-left:4px;vertical-align:middle;}
.rec__caret--asc{border-left:3px solid transparent;border-right:3px solid transparent;border-bottom:4px solid currentColor;}
.rec__caret--desc{border-left:3px solid transparent;border-right:3px solid transparent;border-top:4px solid currentColor;}
.rec__table td{font-family:var(--font-body);font-size:12px;color:var(--text-primary,#22272E);
  padding:6px 10px;border-top:1px solid var(--border,#D7DCE2);vertical-align:baseline;white-space:nowrap;}
.rec__table td.mono{font-family:var(--font-data);font-size:11px;font-variant-numeric:tabular-nums;color:var(--text-muted,#6E7781);}
.rec__table td.memo{white-space:normal;max-width:0;width:99%;}
.rec__table td.num{text-align:right;}
.rec__table tbody tr{position:relative;}
.rec__table tbody tr td:first-child{position:relative;padding-left:13px;}
.rec__table tbody tr td:first-child::before{content:"";position:absolute;left:0;top:0;bottom:0;width:3px;}
.rec__table tr.matched td:first-child::before{background:var(--green,#16885F);}
.rec__table tr.open td:first-child::before{background:var(--orange,#B7791F);}
.rec__table tr.open td{background:var(--orange-a10,rgba(183,121,31,.06));}
.rec__status-pill{display:inline-block;width:7px;height:7px;border-radius:50%;}
.rec__empty{padding:16px 12px;font-family:var(--font-body);font-size:12px;color:var(--text-muted,#6E7781);text-align:center;}
.rec__summary{display:grid;grid-template-columns:1fr 1fr auto;gap:12px;align-items:center;
  padding:10px 14px;border-top:2px solid var(--border-strong,#AAB4BF);background:var(--bg-medium,#F5F7FA);}
.rec__sumcell{display:flex;flex-direction:column;gap:2px;min-width:0;}
.rec__sumlabel{font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#6E7781);}
.rec__statusbox{display:flex;align-items:center;gap:7px;justify-self:end;
  font-family:var(--font-body);font-size:11px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;border:1px solid;border-radius:var(--radius-chip,4px);padding:4px 10px;}
.rec__statusbox--bal{background:var(--green-a10,rgba(22,136,95,.10));border-color:var(--green,#16885F);color:var(--green-dim,#126C4D);}
.rec__statusbox--out{background:var(--red-a10,rgba(186,63,85,.10));border-color:var(--red,#BA3F55);color:var(--red-dim,#983244);}
.rec__dot{height:6px;width:6px;border-radius:50%;background:currentColor;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "reconcile");
  el.textContent = css;
  document.head.appendChild(el);
}

function compareItems(a, b, key) {
  if (key === "amount") return (toNumber(a.amount) || 0) - (toNumber(b.amount) || 0);
  if (key === "status") return (a.matched ? 1 : 0) - (b.matched ? 1 : 0);
  const av = (a[key] ?? "").toString();
  const bv = (b[key] ?? "").toString();
  return av.localeCompare(bv, undefined, { numeric: true });
}

function SortHead({ col, sort, onSort }) {
  const active = sort.key === col.key;
  return (
    <th
      className={`${col.num ? "num" : ""} ${active ? "is-sorted" : ""}`.trim()}
      onClick={() => onSort(col.key)}
      aria-sort={active ? (sort.dir > 0 ? "ascending" : "descending") : "none"}
      scope="col"
    >
      {col.label}
      {active && <span className={`rec__caret rec__caret--${sort.dir > 0 ? "asc" : "desc"}`} aria-hidden="true" />}
    </th>
  );
}

function Side({ side, columns, currency, status, query, sort, onSort }) {
  const items = side.items || [];
  const matched = items.filter((i) => i.matched).length;
  const open = items.length - matched;

  let rows = items;
  if (status === "matched") rows = rows.filter((i) => i.matched);
  else if (status === "open") rows = rows.filter((i) => !i.matched);

  const q = query.trim().toLowerCase();
  if (q) {
    rows = rows.filter((i) =>
      columns.some((c) => (i[c.key] ?? "").toString().toLowerCase().includes(q))
    );
  }

  if (sort.key) {
    rows = [...rows].sort((a, b) => sort.dir * compareItems(a, b, sort.key));
  }

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
              {columns.map((c) => <SortHead key={c.key} col={c} sort={sort} onSort={onSort} />)}
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 && (
              <tr><td className="rec__empty" colSpan={columns.length}>No matching items</td></tr>
            )}
            {rows.map((it, i) => (
              <tr key={it.id ?? i} className={it.matched ? "matched" : "open"}>
                {columns.map((c) =>
                  c.amount ? (
                    <td key={c.key} className="num">
                      <AmountCell value={it.amount} currency={currency} parens />
                    </td>
                  ) : (
                    <td key={c.key} className={`${c.mono ? "mono" : ""} ${c.key === "memo" ? "memo" : ""}`.trim()}>
                      {it[c.key] ?? ""}
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

const STATUS_TABS = [
  { key: "all", label: "All" },
  { key: "matched", label: "Matched" },
  { key: "open", label: "Open" },
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
}) {
  inject();
  const [status, setStatus] = React.useState("all");
  const [query, setQuery] = React.useState("");
  const [sort, setSort] = React.useState({ key: null, dir: 1 });

  const onSort = (key) =>
    setSort((s) =>
      s.key !== key ? { key, dir: 1 } : s.dir > 0 ? { key, dir: -1 } : { key: null, dir: 1 }
    );

  // Resolve columns: explicit prop, else default set + Category only if present in the data.
  const allItems = [...(left.items || []), ...(right.items || [])];
  const hasCategory = allItems.some((i) => i.category != null && i.category !== "");
  const cols =
    columns || [
      { key: "date", label: "Date", mono: true },
      { key: "ref", label: "Reference", mono: true },
      { key: "memo", label: "Memo" },
      ...(hasCategory ? [{ key: "category", label: "Category" }] : []),
      { key: "amount", label: "Amount", num: true, amount: true },
    ];

  const sb = statementBalance != null ? toNumber(statementBalance)
    : (left.items || []).reduce((a, i) => a + (toNumber(i.amount) || 0), 0);
  const bb = bookBalance != null ? toNumber(bookBalance)
    : (right.items || []).reduce((a, i) => a + (toNumber(i.amount) || 0), 0);
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
          ) : <span />}
          {searchable && (
            <div className="rec__searchwrap">
              <svg className="rec__searchicon" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true">
                <circle cx="7" cy="7" r="4.5" /><line x1="11" y1="11" x2="14.5" y2="14.5" />
              </svg>
              <input
                className="rec__search"
                type="search"
                placeholder="Filter memo, ref…"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                aria-label="Filter line items"
              />
            </div>
          )}
        </div>
      )}
      <div className="rec__cols">
        <Side side={left} columns={cols} currency={currency} status={status} query={query} sort={sort} onSort={onSort} />
        <Side side={right} columns={cols} currency={currency} status={status} query={query} sort={sort} onSort={onSort} />
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
        <div className={`rec__statusbox ${balanced ? "rec__statusbox--bal" : "rec__statusbox--out"}`}>
          <span className="rec__dot" aria-hidden="true" />
          {balanced
            ? "Reconciled"
            : <>Out by <AmountCell value={Math.abs(diff)} currency={currency} strong style={{ color: "inherit" }} /></>}
        </div>
      </div>
    </div>
  );
}
