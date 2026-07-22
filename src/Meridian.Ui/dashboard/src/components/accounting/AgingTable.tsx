import { AmountCell } from "./AmountCell";
import { sumAmounts, toNumber } from "./money";

export interface AgingTableRow {
  id: string;
  name: string;
  ref?: string | null;
  amounts: Array<number | string | null | undefined>;
}

export interface AgingTableProps {
  rows: AgingTableRow[];
  buckets?: string[];
  selectedRowId?: string | null;
  currency?: string;
  caption?: string;
  warnFrom?: number;
  onRowSelect?: (row: AgingTableRow) => void;
}

let injected = false;
function inject(): void {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.agt-wrap{overflow-x:auto;border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-chip,2px);background:var(--bg-light,#FFFFFF);}
.agt{width:100%;min-width:720px;border-collapse:separate;border-spacing:0;font-family:var(--font-data,monospace);font-size:12px;}
.agt th,.agt td{text-align:right;white-space:nowrap;}
.agt th.agt--l,.agt td.agt--l{text-align:left;}
.agt thead th{padding:9px 12px;position:sticky;top:0;background:var(--bg-medium,#EBEFF4);z-index:1;font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-muted,#59636F);border-bottom:1px solid var(--border-strong,#99A5B2);border-right:1px solid var(--border-divider,#CBD3DC);}
.agt thead th:last-child,.agt td:last-child{border-right:none;}
.agt td{padding:10px 12px;color:var(--text-primary,#22272E);border-top:1px solid var(--border,#CBD3DC);border-right:1px solid var(--border-divider,#CBD3DC);vertical-align:top;}
.agt tbody tr:first-child td{border-top:none;}
.agt__name{font-family:var(--font-body,inherit);font-weight:600;}
.agt__ref{color:var(--text-secondary,#4D5967);}
.agt__row--click{cursor:pointer;}
.agt__row--click:hover{background:var(--bg-hover,#F3F6F9);}
.agt__row--click:focus-visible{outline:2px solid var(--accent,#2F6F8F);outline-offset:-2px;}
.agt__row--on td{background:color-mix(in srgb,var(--accent,#2F6F8F) 8%,transparent);}
.agt--warn{background:color-mix(in srgb,var(--orange,#8A520E) 8%,transparent);}
.agt--late{background:color-mix(in srgb,var(--red,#BA3F55) 8%,transparent);}
.agt tfoot td{padding:10px 12px;background:var(--bg-medium,#EBEFF4);font-weight:700;border-top:2px solid var(--border-strong,#99A5B2);color:var(--text-primary,#22272E);}
.agt__foot-label{font-family:var(--font-body,inherit);font-variant:all-small-caps;letter-spacing:.03em;font-size:11px;color:var(--text-secondary,#4D5967);}
.agt__share{display:block;margin-top:2px;font-family:var(--font-body,inherit);font-size:10px;font-weight:500;color:var(--text-muted,#59636F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-meridian-component", "aging-table");
  el.textContent = css;
  document.head.appendChild(el);
}

export function AgingTable({
  rows,
  buckets = ["Current", "1-30", "31-60", "61-90", "90+"],
  selectedRowId = null,
  currency = "USD",
  caption = "Aging schedule",
  warnFrom = 2,
  onRowSelect
}: AgingTableProps) {
  inject();
  const showRef = rows.some((row) => row.ref != null && row.ref !== "");
  const lastBucketIndex = buckets.length - 1;
  const columnTotals = buckets.map((_, bucketIndex) => sumAmounts(rows.map((row) => row.amounts[bucketIndex])));
  const grandTotal = sumAmounts(columnTotals);

  const cellClass = (bucketIndex: number, value: number | string | null | undefined) => {
    const amount = toNumber(value);
    if (!Number.isFinite(amount) || amount === 0) return undefined;
    if (bucketIndex === lastBucketIndex) return "agt--late";
    if (bucketIndex >= warnFrom) return "agt--warn";
    return undefined;
  };

  return (
    <div className="agt-wrap" role="region" aria-label={caption}>
      <table className="agt">
        <thead>
          <tr>
            <th className="agt--l" scope="col">Counterparty</th>
            {showRef ? <th className="agt--l" scope="col">Ref</th> : null}
            {buckets.map((bucket) => <th key={bucket} scope="col">{bucket}</th>)}
            <th scope="col">Total</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => {
            const selected = row.id === selectedRowId;
            return (
              <tr
                key={row.id}
                className={[
                  onRowSelect ? "agt__row--click" : "",
                  selected ? "agt__row--on" : ""
                ].filter(Boolean).join(" ") || undefined}
                tabIndex={onRowSelect ? 0 : undefined}
                aria-selected={selected || undefined}
                onClick={onRowSelect ? () => onRowSelect(row) : undefined}
                onKeyDown={onRowSelect ? (event) => {
                  if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    onRowSelect(row);
                  }
                } : undefined}
              >
                <td className="agt--l agt__name">{row.name}</td>
                {showRef ? <td className="agt--l agt__ref">{row.ref}</td> : null}
                {buckets.map((bucket, bucketIndex) => (
                  <td key={bucket} className={cellClass(bucketIndex, row.amounts[bucketIndex])}>
                    <AmountCell value={toNumber(row.amounts[bucketIndex]) || 0} currency={currency} zeroDash />
                  </td>
                ))}
                <td>
                  <AmountCell value={sumAmounts(row.amounts)} currency={currency} strong />
                </td>
              </tr>
            );
          })}
        </tbody>
        <tfoot>
          <tr>
            <td className="agt--l agt__foot-label" colSpan={showRef ? 2 : 1}>Totals</td>
            {columnTotals.map((total, bucketIndex) => (
              <td key={buckets[bucketIndex]}>
                <AmountCell value={total} currency={currency} zeroDash strong />
                {grandTotal > 0 ? (
                  <span className="agt__share">{((total / grandTotal) * 100).toFixed(1)}%</span>
                ) : null}
              </td>
            ))}
            <td>
              <AmountCell value={grandTotal} currency={currency} strong />
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}

AgingTable.displayName = "AgingTable";
