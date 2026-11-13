// Meridian tie-out panel — a compact proof that two independently sourced values reconcile.
// Dashboard-native, read-only port of the design system's `accounting/TieOutPanel`. It separates
// the gross difference, the explained reconciling items, and the unexplained residual, so a
// tie-out never reads as successful merely because the net difference is small.
//
// Rules kept from the reference:
//  - A missing input is not a zero. A source with no value — or one whose condition is missing,
//    pending, stale, not applicable or exception — makes the panel indeterminate: the gross
//    difference and the residual print as a dash and every unusable input is named.
//  - A caller-declared status cannot outrank a missing input.
//  - "Within tolerance" is not success, and items marked `explained: false` are listed but never
//    summed into the explanation.
//  - Every status carries a mark and a word; colour is never the only signal.
//
// Deliberate differences, each stricter than the reference:
//  - A declared status is honoured only when it is at least as strict as the arithmetic, so a
//    caller can report open breaks behind a zero net difference but cannot declare a residual
//    away. The residual reading follows the panel status, so the two cannot disagree.
//  - The explained total prints as a dash when one of its items has no amount.
//
// Not ported, because no workstation endpoint backs them: accept / resolve / escalate / reopen /
// add-item / link-evidence / export actions and the acceptance-reason form, the reviewer and
// evidence footer, the `secondary` measure, and the legacy `adjustments` alias. The "Cannot tie
// out" block therefore omits the reference's sentence about acceptance and resolution.
import { useId, useState, type HTMLAttributes, type ReactNode } from "react";
import { AmountCell } from "./AmountCell";
import { formatMoney } from "./money";
import {
  proveTieOut,
  roundMoney,
  TIE_OUT_STATUS_MARK,
  TIE_OUT_STATUS_TEXT,
  usableAmount,
  type TieOutCondition,
  type TieOutStatus
} from "./proof-arithmetic";
import { StatusBadge, type DesignSystemSeverity } from "@/design-system/status";
import { pluralizeCount } from "@/lib/format";

export type { TieOutCondition, TieOutStatus } from "./proof-arithmetic";

export type TieOutType =
  | "one-to-one"
  | "many-to-one"
  | "one-to-many"
  | "rollforward"
  | "quantity-and-value"
  | "count-and-amount"
  | "multi-basis";

export interface TieOutSource {
  label: string;
  /** The stated value. `null` / omitted means the source is not loaded — never zero. */
  value?: number | null;
  condition?: TieOutCondition;
  /** Display text for the condition when the screen already has its own word for it. */
  conditionLabel?: string;
  /** How many records stand behind the value. */
  count?: number;
}

export interface TieOutItem {
  /** Stable key. Required: two items may share a label. */
  id: string;
  label: string;
  /** Signed adjustment applied to source A. `null` means the amount is not known. */
  value: number | null;
  condition?: TieOutCondition;
  /** Display text for the condition when the screen already has its own word for it. */
  conditionLabel?: string;
  evidence?: ReactNode;
  /** `false` marks an item as identified but not yet substantiated; it is listed, not summed. */
  explained?: boolean;
}

export interface TieOutPanelProps extends Omit<HTMLAttributes<HTMLElement>, "title"> {
  /** @default "Tie-out proof" */
  title?: ReactNode;
  context?: ReactNode;
  /** @default "one-to-one" */
  type?: TieOutType;
  sourceA: TieOutSource;
  sourceB: TieOutSource;
  items?: TieOutItem[];
  /** @default 0 */
  tolerance?: number;
  /** @default "USD" */
  currency?: string;
  /** @default "currency" */
  notation?: "currency" | "number";
  /** Decimal places. @default 2 */
  digits?: number;
  /** @default false */
  defaultExpanded?: boolean;
  /** Controlled disclosure state for the reconciling items. */
  expanded?: boolean;
  onToggleDetail?: (open: boolean) => void;
  /**
   * Declared status. Honoured only when it is at least as strict as the arithmetic; it can never
   * outrank a missing input.
   */
  status?: TieOutStatus;
  /** Why the declared status applies. Shown as the proof note when the declared status is the one in force. */
  statusNote?: string;
  /** Heading level for the title, so the panel fits the host screen's outline. @default 3 */
  headingLevel?: 2 | 3 | 4 | 5 | 6;
}

const TYPE_LABEL: Record<TieOutType, string> = {
  "one-to-one": "One-to-one",
  "many-to-one": "Many-to-one",
  "one-to-many": "One-to-many",
  rollforward: "Rollforward",
  "quantity-and-value": "Quantity and value",
  "count-and-amount": "Count and amount",
  "multi-basis": "Multi-basis"
};

const CONDITION_GLYPH: Record<TieOutCondition, string> = {
  confirmed: "●",
  provisional: "◐",
  estimated: "○",
  stale: "◔",
  overridden: "◆",
  missing: "■",
  na: "—",
  pending: "◇",
  exception: "▲",
  reconciled: "✓"
};

const CONDITION_WORD: Record<TieOutCondition, string> = {
  confirmed: "Confirmed",
  provisional: "Provisional",
  estimated: "Estimated",
  stale: "Stale",
  overridden: "Overridden",
  missing: "Missing",
  na: "Not applicable",
  pending: "Pending",
  exception: "Exception",
  reconciled: "Reconciled"
};

const STATUS_SEVERITY: Record<TieOutStatus, DesignSystemSeverity> = {
  tied: "ready",
  tolerance: "review",
  exception: "blocked",
  indeterminate: "blocked"
};

const RESIDUAL_TONE: Record<Exclude<TieOutStatus, "indeterminate">, string> = {
  tied: "pass",
  tolerance: "review",
  exception: "exception"
};

let injected = false;
function inject(): void {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-tie{border:1px solid var(--border-color,#D2CDC3);background:var(--bg-light,#F8F7F3);font-family:var(--font-body,inherit);font-size:12px;color:var(--text-secondary,#535C65);text-align:left;}
.mds-tie__head{display:flex;flex-wrap:wrap;align-items:flex-start;gap:8px 10px;padding:10px 12px;border-bottom:1px solid var(--border-divider,#E2DED5);}
.mds-tie__title{margin:0;font-size:14px;font-weight:600;line-height:1.3;color:var(--text-primary,#20252B);}
.mds-tie__context{margin:2px 0 0;font-size:12px;line-height:1.45;color:var(--text-muted,#606870);}
.mds-tie__tags{display:flex;flex-direction:column;align-items:flex-end;gap:3px;margin-left:auto;}
.mds-tie__type{font-family:var(--font-data,monospace);font-size:12px;text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);}
.mds-tie__rows{margin:0;padding:6px 12px;}
.mds-tie__row{display:grid;grid-template-columns:1fr auto;gap:10px;align-items:baseline;min-height:28px;border-bottom:1px solid var(--border-divider,#E2DED5);font-size:12px;}
.mds-tie__row:last-child{border-bottom:0;}
.mds-tie__row--rule{border-bottom-color:var(--border-strong,#AAA49A);}
.mds-tie__row dt{margin:0;font-weight:400;}
.mds-tie__row dd{margin:0;}
.mds-tie__srclabel{display:flex;flex-wrap:wrap;align-items:baseline;gap:2px 6px;}
.mds-tie__cond{font-family:var(--font-data,monospace);font-size:12px;color:var(--text-muted,#606870);}
.mds-tie__value{font-family:var(--font-data,monospace);font-size:12px;font-variant-numeric:tabular-nums;white-space:nowrap;color:var(--text-primary,#20252B);}
.mds-tie__toggle{display:inline-flex;align-items:center;gap:5px;min-height:24px;background:none;border:0;padding:0 2px;font:inherit;text-align:left;color:var(--interaction-link,#2A5F96);cursor:pointer;}
.mds-tie__toggle:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:2px;}
.mds-tie__items{border-bottom:1px solid var(--border-divider,#E2DED5);overflow-x:auto;}
.mds-tie__items:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px;}
.mds-tie__table{width:100%;min-width:420px;border-collapse:collapse;font-size:12px;}
.mds-tie__table th{padding:4px 8px;text-align:left;font-family:var(--font-data,monospace);font-size:12px;font-weight:400;text-transform:uppercase;letter-spacing:.04em;color:var(--text-muted,#606870);border-bottom:1px solid var(--border-divider,#E2DED5);}
.mds-tie__table td{padding:5px 8px;vertical-align:baseline;border-bottom:1px solid var(--border-divider,#E2DED5);color:var(--text-secondary,#535C65);}
.mds-tie__table th:first-child,.mds-tie__table td:first-child{padding-left:26px;}
.mds-tie__table tbody tr:last-child td{border-bottom:0;}
.mds-tie__table .mds-tie__num{text-align:right;}
.mds-tie__item--unexplained td{background:var(--state-danger-bg,rgba(181,75,66,.10));}
.mds-tie__proof{display:grid;grid-template-columns:1fr auto;gap:12px;align-items:center;padding:10px 12px;border-top:1px solid var(--border-strong,#AAA49A);background:var(--card-surface-raised,var(--bg-light,#F8F7F3));}
.mds-tie__proof-title{display:block;font-size:12px;font-weight:600;color:var(--text-primary,#20252B);}
.mds-tie__proof-note{display:block;margin-top:2px;font-size:12px;line-height:1.45;color:var(--text-muted,#606870);}
.mds-tie__residual{display:inline-grid;justify-items:end;text-align:right;font-family:var(--font-data,monospace);font-variant-numeric:tabular-nums;}
.mds-tie__residual-result{font-size:12px;font-weight:600;line-height:16px;white-space:nowrap;}
.mds-tie__residual--pass .mds-tie__residual-result{color:var(--state-healthy-fg,#225840);}
.mds-tie__residual--review .mds-tie__residual-result{color:var(--state-warn-fg,#724D11);}
.mds-tie__residual--exception .mds-tie__residual-result{color:var(--state-danger-fg,#883832);}
.mds-tie__block{margin:0;padding:8px 12px;border-top:1px solid var(--border-divider,#E2DED5);background:var(--state-danger-bg,rgba(181,75,66,.10));font-size:12px;line-height:1.5;color:var(--text-primary,#20252B);}
.mds-tie__block b{font-weight:600;}
.mds-tie__sr{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0;}
@media (max-width:560px){.mds-tie__proof{grid-template-columns:1fr;}.mds-tie__residual{justify-items:start;text-align:left;}.mds-tie__tags{align-items:flex-start;margin-left:0;}}
@media (forced-colors:active){.mds-tie,.mds-tie__block{border-color:CanvasText;}}
`;
  const el = document.createElement("style");
  el.setAttribute("data-meridian-component", "tie-out-panel");
  el.textContent = css;
  document.head.appendChild(el);
}

function ConditionTag({ condition, label }: { condition?: TieOutCondition; label?: string }) {
  if (!condition && !label) return null;
  return (
    <span className="mds-tie__cond">
      {condition ? <span aria-hidden="true">{CONDITION_GLYPH[condition]} </span> : null}
      {label ?? (condition ? CONDITION_WORD[condition] : "")}
    </span>
  );
}

function Dash({ meaning }: { meaning: string }) {
  return (
    <>
      <span aria-hidden="true">—</span>
      <span className="mds-tie__sr">{meaning}</span>
    </>
  );
}

export function TieOutPanel({
  title = "Tie-out proof",
  context,
  type = "one-to-one",
  sourceA,
  sourceB,
  items = [],
  tolerance = 0,
  currency = "USD",
  notation = "currency",
  digits = 2,
  defaultExpanded = false,
  expanded: expandedProp,
  onToggleDetail,
  status: declaredStatus,
  statusNote,
  headingLevel = 3,
  className,
  ...rest
}: TieOutPanelProps) {
  inject();
  const uid = useId();
  const titleId = `${uid}-title`;
  const itemsId = `${uid}-items`;
  const [openState, setOpenState] = useState(defaultExpanded);
  const open = expandedProp !== undefined ? expandedProp : openState;
  const toggle = () => {
    if (expandedProp === undefined) setOpenState((value) => !value);
    onToggleDetail?.(!open);
  };

  const proof = proveTieOut({ sourceA, sourceB, items, tolerance, status: declaredStatus });
  const { status } = proof;
  const indeterminate = status === "indeterminate";
  const amountCurrency = notation === "currency" ? currency : undefined;
  const Heading = `h${headingLevel}` as "h2" | "h3" | "h4" | "h5" | "h6";

  // The caller's note explains the caller's status. When the arithmetic or a missing input has
  // overruled that status, the note no longer describes what is on screen.
  const declaredInForce = declaredStatus !== undefined && declaredStatus === status && !indeterminate;
  const defaultNote: Record<TieOutStatus, string> = {
    tied: proof.explainedCount > 0
      ? "Difference fully explained by the reconciling items above."
      : "The two sources agree. No reconciling item is needed.",
    tolerance: "Residual is below tolerance but is not explained. Classify it or accept it with a reason.",
    exception: proof.derivedStatus === "exception"
      ? "Residual exceeds tolerance and is unexplained."
      : "Reported as an exception although the amounts net within tolerance.",
    indeterminate: "No residual can be computed while an input is unavailable. An unavailable input is not a zero."
  };
  const proofNote = declaredInForce && statusNote ? statusNote : defaultNote[status];

  // The residual reading (mark, tone and words) follows the panel status so the two never disagree.
  const tolAbs = Math.abs(tolerance);
  const residual = proof.residual === null ? null : roundMoney(proof.residual, digits, "half-even");
  const toleranceText = tolAbs > 0
    ? `${formatMoney(tolAbs, { currency: amountCurrency, decimals: digits })} tolerance`
    : "tolerance";
  const residualTone = status === "indeterminate" ? null : RESIDUAL_TONE[status];
  let residualResult = "";
  if (residual !== null) {
    const magnitude = Math.abs(residual);
    if (status === "exception") {
      residualResult = magnitude > tolAbs ? `Outside ${toleranceText}` : "Not tied";
    } else if (status === "tolerance") {
      residualResult = `${magnitude >= tolAbs * 0.8 ? "Near" : "Within"} ${toleranceText}`;
    } else if (status === "tied") {
      residualResult = tolAbs > 0 ? `Within ${toleranceText}` : magnitude === 0 ? "Agrees exactly" : "Within tolerance";
    }
  }

  const renderSource = (source: TieOutSource, value: number | null, fallbackLabel: string, rule: boolean) => (
    <div className={rule ? "mds-tie__row mds-tie__row--rule" : "mds-tie__row"}>
      <dt className="mds-tie__srclabel">
        <span>{source?.label || fallbackLabel}</span>
        <ConditionTag condition={source?.condition} label={source?.conditionLabel} />
        {source?.count != null ? (
          <span className="mds-tie__cond">{pluralizeCount(source.count, "record", { localizeCount: true })}</span>
        ) : null}
      </dt>
      <dd className="mds-tie__value">
        {value === null ? <Dash meaning="no value" /> : <AmountCell value={value} currency={amountCurrency} decimals={digits} />}
      </dd>
    </div>
  );

  return (
    <section
      className={`mds-tie mds-tie--${status}${className ? ` ${className}` : ""}`}
      aria-labelledby={titleId}
      data-meridian-component="TieOutPanel"
      data-tie-out-status={status}
      {...rest}
    >
      <header className="mds-tie__head">
        <div style={{ minWidth: 0 }}>
          <Heading id={titleId} className="mds-tie__title">{title}</Heading>
          {context ? <p className="mds-tie__context">{context}</p> : null}
        </div>
        <div className="mds-tie__tags">
          <StatusBadge
            status={STATUS_SEVERITY[status]}
            dot={false}
            label={<><span aria-hidden="true">{TIE_OUT_STATUS_MARK[status]} </span>{TIE_OUT_STATUS_TEXT[status]}</>}
          />
          <span className="mds-tie__type">{TYPE_LABEL[type]}</span>
        </div>
      </header>

      <dl className="mds-tie__rows">
        {renderSource(sourceA, proof.sourceAValue, "Source A", false)}
        {renderSource(sourceB, proof.sourceBValue, "Source B", true)}
        <div className="mds-tie__row">
          <dt>Gross difference</dt>
          <dd className="mds-tie__value">
            {proof.grossDifference === null
              ? <Dash meaning="not computable" />
              : <AmountCell value={proof.grossDifference} currency={amountCurrency} decimals={digits} signed />}
          </dd>
        </div>
        <div className="mds-tie__row mds-tie__row--rule">
          <dt>
            {items.length > 0 ? (
              <button type="button" className="mds-tie__toggle" onClick={toggle} aria-expanded={open} aria-controls={itemsId}>
                <span aria-hidden="true">{open ? "▾" : "▸"}</span>
                <span>
                  Explained · {proof.explainedCount} reconciling item{proof.explainedCount === 1 ? "" : "s"}
                  {proof.unexplainedItemCount > 0 ? ` · ${proof.unexplainedItemCount} identified, not explained` : ""}
                </span>
              </button>
            ) : "Explained"}
          </dt>
          <dd className="mds-tie__value">
            {proof.explainedTotal === null
              ? <Dash meaning="not computable" />
              : <AmountCell value={proof.explainedTotal} currency={amountCurrency} decimals={digits} signed />}
          </dd>
        </div>
      </dl>

      {items.length > 0 ? (
        <div
          id={itemsId}
          className="mds-tie__items"
          hidden={!open}
          role="region"
          aria-label="Reconciling items"
          tabIndex={open ? 0 : undefined}
        >
          {open ? (
            <table className="mds-tie__table">
              <thead>
                <tr>
                  <th scope="col">Item</th>
                  <th scope="col" className="mds-tie__num">Amount</th>
                  <th scope="col">Condition</th>
                  <th scope="col">Evidence</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => {
                  const value = usableAmount(item.value);
                  const condition = item.condition ?? (item.explained === false ? "exception" : "confirmed");
                  return (
                    <tr key={item.id} className={item.explained === false ? "mds-tie__item--unexplained" : undefined}>
                      <td>{item.label}</td>
                      <td className="mds-tie__num mds-tie__value">
                        {value === null
                          ? <Dash meaning="no amount" />
                          : <AmountCell value={value} currency={amountCurrency} decimals={digits} signed />}
                      </td>
                      <td><ConditionTag condition={condition} label={item.conditionLabel} /></td>
                      <td>{item.evidence || "No evidence"}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          ) : null}
        </div>
      ) : null}

      <div className="mds-tie__proof">
        <div>
          <span className="mds-tie__proof-title">Unexplained residual</span>
          <span className="mds-tie__proof-note">{proofNote}</span>
        </div>
        {residual === null || residualTone === null ? (
          <span className="mds-tie__value"><Dash meaning="Residual not computable — an input is missing" /></span>
        ) : (
          <span className={`mds-tie__residual mds-tie__residual--${residualTone}`}>
            <AmountCell value={residual} currency={amountCurrency} decimals={digits} parens strong />
            <span className="mds-tie__residual-result">
              <span aria-hidden="true">{TIE_OUT_STATUS_MARK[status]} </span>
              {residualResult}
            </span>
          </span>
        )}
      </div>

      {indeterminate ? (
        <p className="mds-tie__block">
          <b>Cannot tie out.</b> {proof.gaps.join(" · ")}. Supply the input, or record it as a reconciling item with its own evidence.
        </p>
      ) : null}
    </section>
  );
}

TieOutPanel.displayName = "TieOutPanel";
