// Meridian tax-lot table — cost-basis lots with holding-period classification and unrealized (or
// realized) P&L. Each lot: acquisition date + days held, quantity, cost basis, market value (or
// proceeds), and gain/loss with a percent, plus a short/long-term badge derived from days held vs.
// `longTermDays` (amber short-term / steel-blue long-term). The footer rolls up basis, value, and
// total gain. All figures mono and tabular; P&L is red/green. Flat Concrete grid.
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";

export interface TaxLot {
  id?: string | number;
  /** Acquisition date (ISO "2025-02-14"). Drives days-held and the holding-period badge. */
  acquired: string;
  /** Instrument symbol — shown only when `showSymbol` is set. */
  symbol?: string;
  quantity: number | string;
  costBasis: number | string;
  /** Current market value — used when mode = "unrealized". */
  marketValue?: number | string;
  /** Sale proceeds — used when mode = "realized". */
  proceeds?: number | string;
  /** Override computed holding days (e.g. server-supplied). */
  daysHeld?: number;
  /** Force the holding-period classification instead of deriving it. */
  term?: "short" | "long";
}

export interface TaxLotTableProps {
  lots: TaxLot[];
  /** @default "USD" */
  currency?: string;
  /** ISO date the holding period and market value are measured at. @default now */
  asOf?: string;
  /** Days held at or above which a lot is long-term. @default 365 */
  longTermDays?: number;
  /** "unrealized" uses marketValue; "realized" uses proceeds. @default "unrealized" */
  mode?: "unrealized" | "realized";
  /** Include a Symbol column (cross-instrument lot list). @default false */
  showSymbol?: boolean;
  /** Accessible caption / region label. */
  caption?: string;
}

let injected = false;
function inject(): void {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.tlt-wrap{overflow-x:auto;border:1px solid var(--border,#CBD3DC);
  border-radius:var(--radius-chip,2px);background:var(--bg-light,#FFFFFF);}
.tlt{width:100%;min-width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data,monospace);font-size:12px;}
.tlt thead th{padding:9px 12px;text-align:left;white-space:nowrap;position:sticky;top:0;z-index:1;
  background:var(--bg-medium,#EBEFF4);
  font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border-strong,#99A5B2);border-right:1px solid var(--border-divider,#CBD3DC);}
.tlt thead th:last-child{border-right:none;}
.tlt th.tlt--r{text-align:right;}
.tlt td{padding:7px 12px;white-space:nowrap;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#CBD3DC);border-right:1px solid var(--border-divider,#CBD3DC);
  vertical-align:middle;font-variant-numeric:tabular-nums;}
.tlt td:last-child{border-right:none;}
.tlt tbody tr:first-child td{border-top:none;}
.tlt td.tlt--r{text-align:right;}
.tlt tbody tr:hover{background:var(--bg-hover,#F3F6F9);}
.tlt__date{color:var(--text-secondary,#4D5967);}
.tlt__sym{font-weight:600;color:var(--text-primary,#22272E);}
.tlt__days{color:var(--text-muted,#59636F);font-size:11px;}
.tlt__hp{display:inline-flex;align-items:center;gap:5px;border:1px solid;border-radius:var(--radius-chip,2px);
  padding:2px 7px;font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;white-space:nowrap;line-height:1.3;}
.tlt__hp--short{background:var(--orange-a10,rgba(138,82,14,.10));border-color:var(--orange,#8A520E);color:var(--orange-dim,#683E0B);}
.tlt__hp--long{background:var(--blue-a10,rgba(47,111,143,.10));border-color:var(--accent,#2F6F8F);color:var(--accent,#2F6F8F);}
.tlt__hp__dot{height:5px;width:5px;border-radius:50%;background:currentColor;flex:0 0 auto;}
.tlt__pct{font-family:var(--font-data,monospace);font-size:10.5px;margin-left:7px;}
.tlt tfoot td{padding:9px 12px;background:var(--bg-medium,#EBEFF4);font-weight:600;
  border-top:2px solid var(--border-strong,#99A5B2);color:var(--text-primary,#22272E);}
.tlt tfoot td.tlt--r{text-align:right;}
.tlt__foot-lbl{font-family:var(--font-body,inherit);font-size:11px;font-variant:all-small-caps;letter-spacing:.03em;color:var(--text-secondary,#4D5967);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "taxlot");
  el.textContent = css;
  document.head.appendChild(el);
}

const DAY = 86400000;
function daysHeld(acquired: string, asOf?: string): number | null {
  const a = Date.parse(acquired);
  const b = asOf ? Date.parse(asOf) : Date.now();
  if (!Number.isFinite(a) || !Number.isFinite(b)) return null;
  return Math.max(0, Math.floor((b - a) / DAY));
}
function fmtDays(d: number | null): string {
  if (d == null) return "";
  if (d < 365) return `${d}d`;
  const y = Math.floor(d / 365);
  const r = d % 365;
  return r >= 30 ? `${y}y ${Math.floor(r / 30)}m` : `${y}y`;
}
function fmtQty(q: number | string): string {
  const n = toNumber(q);
  if (!Number.isFinite(n)) return String(q);
  return n.toLocaleString("en-US", { maximumFractionDigits: 4 });
}

export function TaxLotTable({
  lots,
  currency = "USD",
  asOf,
  longTermDays = 365,
  mode = "unrealized",
  showSymbol = false,
  caption
}: TaxLotTableProps) {
  inject();
  const valueHead = mode === "realized" ? "Proceeds" : "Market value";
  const gainHead = mode === "realized" ? "Realized P&L" : "Unrealized P&L";

  const computed = lots.map((l) => {
    const basis = toNumber(l.costBasis);
    const value = toNumber(mode === "realized" ? l.proceeds : l.marketValue);
    const gain = Number.isFinite(basis) && Number.isFinite(value) ? value - basis : NaN;
    const pct = Number.isFinite(gain) && basis !== 0 ? (gain / Math.abs(basis)) * 100 : NaN;
    const held = l.daysHeld != null ? l.daysHeld : daysHeld(l.acquired, asOf);
    const long = l.term ? l.term === "long" : held != null && held >= longTermDays;
    return { ...l, basis, value, gain, pct, held, long };
  });

  const tBasis = computed.reduce((a, r) => a + (Number.isFinite(r.basis) ? r.basis : 0), 0);
  const tValue = computed.reduce((a, r) => a + (Number.isFinite(r.value) ? r.value : 0), 0);
  const tGain = tValue - tBasis;

  return (
    <div className="tlt-wrap" role="region" aria-label={caption || "Tax lots"}>
      <table className="tlt">
        <thead>
          <tr>
            <th>Acquired</th>
            {showSymbol && <th>Symbol</th>}
            <th>Holding period</th>
            <th className="tlt--r">Quantity</th>
            <th className="tlt--r">Cost basis</th>
            <th className="tlt--r">{valueHead}</th>
            <th className="tlt--r">{gainHead}</th>
          </tr>
        </thead>
        <tbody>
          {computed.map((r, i) => (
            <tr key={r.id ?? i}>
              <td className="tlt__date">
                {r.acquired}
                {r.held != null && <span className="tlt__days">{"  ·  "}{fmtDays(r.held)}</span>}
              </td>
              {showSymbol && <td className="tlt__sym">{r.symbol}</td>}
              <td>
                <span className={`tlt__hp ${r.long ? "tlt__hp--long" : "tlt__hp--short"}`}>
                  <span className="tlt__hp__dot" aria-hidden="true" />
                  {r.long ? "Long-term" : "Short-term"}
                </span>
              </td>
              <td className="tlt--r">{fmtQty(r.quantity)}</td>
              <td className="tlt--r">
                <AmountCell value={r.basis} currency={currency} />
              </td>
              <td className="tlt--r">
                <AmountCell value={r.value} currency={currency} />
              </td>
              <td className="tlt--r">
                <AmountCell value={r.gain} currency={currency} mode="pnl" signed zeroDash />
                {Number.isFinite(r.pct) && (
                  <span
                    className="tlt__pct"
                    style={{
                      color:
                        r.gain < 0
                          ? "var(--red-dim, #8C2F40)"
                          : r.gain > 0
                            ? "var(--green-dim, #10663F)"
                            : "var(--text-muted, #59636F)"
                    }}
                  >
                    {r.gain >= 0 ? "+" : "−"}
                    {Math.abs(r.pct).toFixed(2)}%
                  </span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td className="tlt__foot-lbl" colSpan={showSymbol ? 4 : 3}>
              {computed.length} {computed.length === 1 ? "lot" : "lots"}
            </td>
            <td className="tlt--r">
              <AmountCell value={tBasis} currency={currency} strong />
            </td>
            <td className="tlt--r">
              <AmountCell value={tValue} currency={currency} strong />
            </td>
            <td className="tlt--r">
              <AmountCell value={tGain} currency={currency} mode="pnl" signed strong />
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}

TaxLotTable.displayName = "TaxLotTable";
