// Meridian rollforward trace — opening balance, the movements that explain the change, closing
// balance, and the proof that they agree. Dashboard-native port of the design system's
// `accounting/RollforwardTrace`: the component sums the movements itself and says "Out by …"
// when the stated closing does not tie, instead of quietly rendering both numbers.
//
// What the port keeps: the opening → movements → closing layout, the sign carried by the
// operator column (amounts print as absolute values), the zero dash, half-even rounding, the
// proof strip and its wording, and resolve-to links that render only when they can resolve.
//
// What the port adds, each to close a gap the reference leaves open:
//  - a missing opening or movement amount makes the proof indeterminate; it is never read as zero;
//  - a derived opening is labelled "(derived)" and the derivation is stated beside the proof;
//  - figures that agree by construction are reported as "Not proved", never as a passed proof;
//  - a missing amount prints in words, so it cannot be mistaken for the zero dash, and the zero
//    dash is spoken as "zero";
//  - the operator sign is spoken to assistive technology, which cannot see the glyph column.
//
// Not ported: the per-movement `tone` tint, which depends on data-condition tokens the dashboard
// does not define.
import type { HTMLAttributes } from "react";
import { AmountCell } from "./AmountCell";
import { formatMoney } from "./money";
import { proveRollforward, rollforwardVerdict, usableAmount, type RollforwardVerdict } from "./proof-arithmetic";

export type RollforwardOperator = "+" | "−" | "±";

export interface RollforwardMovement {
  /** Stable key. Required: two movements may share a label. */
  id: string;
  label: string;
  /** Signed amount; the sign drives the +/−/± operator column. `null` means no amount is known. */
  value: number | null;
  /** Force the operator glyph. */
  op?: RollforwardOperator;
  /** Mono qualifier after the label — method, rate, period. */
  note?: string;
  /** Resolve-to link text; renders a button only when paired with `onOpen`. */
  linkLabel?: string;
  onOpen?: () => void;
  /** Set false to print 0.00 instead of the zero dash. */
  emptyDash?: boolean;
}

export interface RollforwardTraceProps extends Omit<HTMLAttributes<HTMLDivElement>, "children"> {
  /** @default "Opening book value" */
  openingLabel?: string;
  /** Opening balance. `null` when it is not known — the trace then says it cannot be proved. */
  opening: number | null;
  movements: RollforwardMovement[];
  /** @default "Closing book value" */
  closingLabel?: string;
  /**
   * Stated closing balance. Omit to use opening + movements. `null` means a closing was expected
   * and is not available — the trace then says it cannot be proved rather than agreeing with itself.
   */
  closing?: number | null;
  currency?: string;
  /** @default 2 */
  decimals?: number;
  /** The tie-out strip. Leave on — the proof is the point. @default true */
  showProof?: boolean;
  /**
   * Say how the opening was derived when it is not an independently stated balance. When set,
   * the opening row reads "(derived)" and this sentence is printed under the proof.
   */
  openingDerivation?: string;
  /**
   * Say why agreement would prove nothing, when opening, movements and closing are not
   * independently stated (one net movement that is the closing restated, for example). When set
   * and the arithmetic agrees, the strip reads "Not proved — …" with this sentence instead of
   * "Opening + movements = closing". An out-by or a missing amount is still reported as such.
   */
  unsupportedReason?: string;
}

/** Visible wording for an amount that is not known. Never a dash: the dash means zero. */
export const ROLLFORWARD_MISSING_BALANCE_TEXT = "Not available";
export const ROLLFORWARD_MISSING_MOVEMENT_TEXT = "No amount";

const OPERATOR_WORD: Record<RollforwardOperator, string> = {
  "+": "plus",
  "−": "less",
  "±": "no change"
};

let injected = false;
function inject(): void {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-rf{width:100%;font-family:var(--font-body,inherit);font-size:12px;color:var(--text-secondary,#535C65);text-align:left;}
.mds-rf__rows{margin:0;padding:0;}
.mds-rf__row{display:flex;align-items:baseline;gap:12px;min-height:26px;padding:3px 0;border-bottom:1px solid var(--border-divider,#E2DED5);}
.mds-rf__row--open{border-bottom:1px solid var(--border-strong,#AAA49A);}
.mds-rf__row--close{border-top:1px solid var(--border-strong,#AAA49A);border-bottom:0;margin-top:2px;padding-top:7px;}
.mds-rf__lbl{flex:1;min-width:0;display:flex;flex-wrap:wrap;align-items:baseline;gap:2px 7px;margin:0;font-weight:400;}
.mds-rf__op{font-family:var(--font-data,monospace);color:var(--text-muted,#606870);width:10px;flex:0 0 10px;text-align:center;}
.mds-rf__txt{min-width:0;overflow-wrap:anywhere;}
.mds-rf__row--open .mds-rf__txt,.mds-rf__row--close .mds-rf__txt{font-weight:600;color:var(--text-primary,#20252B);}
.mds-rf__val{margin:0;text-align:right;white-space:nowrap;font-family:var(--font-data,monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B);}
.mds-rf__val--missing{font-family:var(--font-body,inherit);color:var(--text-muted,#606870);}
.mds-rf__link{font:inherit;font-size:12px;min-height:24px;background:none;border:0;padding:0 2px;cursor:pointer;color:var(--interaction-link,#2A5F96);text-decoration:underline;text-underline-offset:2px;white-space:nowrap;}
.mds-rf__link:focus-visible{outline:2px solid var(--interaction-focus,#2D6FB3);outline-offset:2px;}
.mds-rf__note{font-family:var(--font-data,monospace);font-size:12px;color:var(--text-muted,#606870);}
.mds-rf__proof{display:flex;align-items:baseline;gap:8px;margin:8px 0 0;padding:5px 8px;border:1px solid var(--state-healthy-bd,rgba(46,118,85,.32));background:var(--state-healthy-bg,rgba(46,118,85,.10));border-radius:var(--radius-chip,2px);font-size:12px;line-height:1.45;color:var(--state-healthy-fg,#225840);}
.mds-rf__proof--off{border-color:var(--state-danger-bd,rgba(181,75,66,.35));background:var(--state-danger-bg,rgba(181,75,66,.10));color:var(--state-danger-fg,#883832);}
.mds-rf__proof--unsupported{border-color:var(--state-warn-bd,rgba(152,103,23,.38));background:var(--state-warn-bg,rgba(152,103,23,.11));color:var(--state-warn-fg,#724D11);}
.mds-rf__basis{margin:6px 0 0;font-size:12px;line-height:1.5;color:var(--text-muted,#606870);}
.mds-rf__sr{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0;}
@media (forced-colors:active){.mds-rf__proof{border-color:CanvasText;}}
`;
  const el = document.createElement("style");
  el.setAttribute("data-meridian-component", "rollforward-trace");
  el.textContent = css;
  document.head.appendChild(el);
}

const PROOF_CLASS: Record<RollforwardVerdict, string> = {
  tied: "mds-rf__proof",
  unsupported: "mds-rf__proof mds-rf__proof--unsupported",
  out: "mds-rf__proof mds-rf__proof--off",
  indeterminate: "mds-rf__proof mds-rf__proof--off"
};

const PROOF_MARK: Record<RollforwardVerdict, string> = {
  tied: "✓",
  unsupported: "◇",
  out: "▲",
  indeterminate: "■"
};

/** A missing amount, in words. */
function MissingValue({ text }: { text: string }) {
  return <dd className="mds-rf__val mds-rf__val--missing">{text}</dd>;
}

/** The zero dash, with the word a screen reader would otherwise not get. */
function ZeroDash() {
  return (
    <dd className="mds-rf__val">
      <span aria-hidden="true">—</span>
      <span className="mds-rf__sr">zero</span>
    </dd>
  );
}

/** An opening or closing balance: missing in words, zero as a spoken dash, else the amount. */
function BalanceValue({ value, currency, decimals }: { value: number | null; currency?: string; decimals: number }) {
  if (value === null) return <MissingValue text={ROLLFORWARD_MISSING_BALANCE_TEXT} />;
  if (value === 0) return <ZeroDash />;
  return <AmountCell as="dd" className="mds-rf__val" value={value} currency={currency} decimals={decimals} parens strong />;
}

export function RollforwardTrace({
  openingLabel = "Opening book value",
  opening,
  movements,
  closingLabel = "Closing book value",
  closing,
  currency,
  decimals = 2,
  showProof = true,
  openingDerivation,
  unsupportedReason,
  className,
  ...rest
}: RollforwardTraceProps) {
  inject();
  const proof = proveRollforward({ openingLabel, opening, movements, closingLabel, closing, decimals });
  const verdict = rollforwardVerdict(proof, { independent: !unsupportedReason });
  const openingValue = usableAmount(opening);
  const movementCount = movements.length;

  return (
    <div
      className={className ? `mds-rf ${className}` : "mds-rf"}
      data-meridian-component="RollforwardTrace"
      data-rollforward-verdict={verdict}
      {...rest}
    >
      <dl className="mds-rf__rows">
        <div className="mds-rf__row mds-rf__row--open">
          <dt className="mds-rf__lbl">
            <span className="mds-rf__op" aria-hidden="true" />
            <span className="mds-rf__txt">{openingLabel}</span>
            {openingDerivation ? <span className="mds-rf__note">(derived)</span> : null}
          </dt>
          <BalanceValue value={openingValue} currency={currency} decimals={decimals} />
        </div>
        {movements.map((movement) => {
          const value = usableAmount(movement.value);
          const op: RollforwardOperator = movement.op ?? (value === null || value === 0 ? "±" : value > 0 ? "+" : "−");
          return (
            <div className="mds-rf__row" key={movement.id}>
              <dt className="mds-rf__lbl">
                {value === null ? (
                  <span className="mds-rf__op" aria-hidden="true" />
                ) : (
                  <>
                    <span className="mds-rf__op" aria-hidden="true">{op}</span>
                    <span className="mds-rf__sr">{OPERATOR_WORD[op]}</span>
                  </>
                )}
                <span className="mds-rf__txt">{movement.label}</span>
                {movement.note ? <span className="mds-rf__note">{movement.note}</span> : null}
                {movement.linkLabel && movement.onOpen ? (
                  <button
                    type="button"
                    className="mds-rf__link"
                    aria-label={`${movement.linkLabel}: ${movement.label}`}
                    onClick={movement.onOpen}
                  >
                    {movement.linkLabel}
                  </button>
                ) : null}
              </dt>
              {value === null ? (
                <MissingValue text={ROLLFORWARD_MISSING_MOVEMENT_TEXT} />
              ) : value === 0 && movement.emptyDash !== false ? (
                <ZeroDash />
              ) : (
                <AmountCell as="dd" className="mds-rf__val" value={Math.abs(value)} currency={currency} decimals={decimals} parens />
              )}
            </div>
          );
        })}
        <div className="mds-rf__row mds-rf__row--close">
          <dt className="mds-rf__lbl">
            <span className="mds-rf__op" aria-hidden="true" />
            <span className="mds-rf__txt">{closingLabel}</span>
          </dt>
          <BalanceValue value={proof.statedClosing} currency={currency} decimals={decimals} />
        </div>
      </dl>
      {showProof ? (
        <p className={PROOF_CLASS[verdict]}>
          <span aria-hidden="true">{PROOF_MARK[verdict]}</span>
          <span>
            {verdict === "tied"
              ? `Opening + movements = closing · ${movementCount} movement${movementCount === 1 ? "" : "s"}`
              : verdict === "unsupported"
                ? `Not proved — ${unsupportedReason}`
                : verdict === "out"
                  ? `Out by ${formatMoney(Math.abs(proof.difference ?? 0), { currency, decimals, parens: false })} — movements do not explain the closing balance`
                  : `Cannot prove the rollforward — ${proof.gaps.join(" · ")}. A missing amount is not a zero.`}
          </span>
        </p>
      ) : null}
      {openingDerivation ? <p className="mds-rf__basis">{openingDerivation}</p> : null}
    </div>
  );
}

RollforwardTrace.displayName = "RollforwardTrace";
