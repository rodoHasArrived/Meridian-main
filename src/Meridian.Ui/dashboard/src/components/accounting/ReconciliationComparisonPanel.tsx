import { useRef, useState, type Ref } from "react";
import { AmountCell } from "./AmountCell";
import { SeverityBadge } from "@/components/operations";
import { cn } from "@/lib/utils";
import type {
  ReconciliationComparisonViewState,
  ReconciliationLineItemViewModel
} from "@/screens/accounting-screen.view-model";

const reconciliationComparisonToneClass: Record<ReconciliationLineItemViewModel["statusTone"], string> = {
  success: "is-matched",
  warning: "is-timing",
  danger: "is-break"
};

type ReconciliationComparisonSide = "statement" | "ledger";

type ReconciliationComparisonSelection = { matchKey: string; side: ReconciliationComparisonSide } | null;

function parseAmountLabel(value: string): number | string {
  const trimmed = value.trim();
  if (!trimmed) return value;
  const negative = /^\(.+\)$/.test(trimmed) || trimmed.startsWith("-");
  const numeric = Number(trimmed.replace(/[()$,\s]/g, "").replace(/^-/, ""));
  if (!Number.isFinite(numeric)) return value;
  return negative ? -numeric : numeric;
}

function ReconciliationComparisonPane({
  heading,
  ariaLabel,
  side,
  lines,
  selection,
  onSelect,
  scrollRef,
  onScroll
}: {
  heading: string;
  ariaLabel: string;
  side: ReconciliationComparisonSide;
  lines: ReconciliationLineItemViewModel[];
  selection: ReconciliationComparisonSelection;
  onSelect: (matchKey: string, side: ReconciliationComparisonSide) => void;
  scrollRef?: Ref<HTMLDivElement>;
  onScroll?: () => void;
}) {
  const { matched, timing, breaks } = lines.reduce(
    (counts, line) => {
      if (line.statusTone === "success") counts.matched++;
      else if (line.statusTone === "warning") counts.timing++;
      else if (line.statusTone === "danger") counts.breaks++;
      return counts;
    },
    { matched: 0, timing: 0, breaks: 0 }
  );

  return (
    <div className="reconcile-pane">
      <div className="reconcile-pane-head">
        <span className="reconcile-pane-title">{heading}</span>
        <span className="reconcile-pane-chips">
          <SeverityBadge status="ready" dot={false} label={`${matched} matched`} />
          {timing > 0 ? <SeverityBadge status="action" dot={false} label={`${timing} timing`} /> : null}
          {breaks > 0 ? <SeverityBadge status="blocked" dot={false} label={`${breaks} break${breaks > 1 ? "s" : ""}`} /> : null}
        </span>
      </div>
      <div className="reconcile-pane-scroll" ref={scrollRef} onScroll={onScroll}>
        <table className="reconcile-table" aria-label={ariaLabel}>
          <thead>
            <tr>
              <th scope="col">{side === "statement" ? "Custodian / source" : "Ledger entry"}</th>
              <th scope="col" className="num">{side === "statement" ? "Statement" : "Ledger"}</th>
            </tr>
          </thead>
          <tbody>
            {lines.length === 0 ? (
              <tr>
                <td colSpan={2} className="reconcile-empty">No reconciliation items to display</td>
              </tr>
            ) : null}
            {lines.map((line) => {
              const isSelected = selection?.matchKey === line.matchKey && selection.side === side;
              const isCrossLit = selection?.matchKey === line.matchKey && selection.side !== side;
              return (
                <tr
                  key={line.id}
                  className={cn(
                    reconciliationComparisonToneClass[line.statusTone],
                    isSelected && "is-selected",
                    isCrossLit && "is-cross-lit"
                  )}
                  tabIndex={0}
                  aria-selected={isSelected}
                  aria-label={`${line.title} - ${line.statusLabel}`}
                  onClick={() => onSelect(line.matchKey, side)}
                  onKeyDown={(event) => {
                    if (event.key === "Enter" || event.key === " ") {
                      event.preventDefault();
                      onSelect(line.matchKey, side);
                    }
                  }}
                >
                  <td>
                    <div className="reconcile-row-title">{line.title}</div>
                    <div className="reconcile-row-meta">{line.meta}</div>
                  </td>
                  <td className="num">
                    <AmountCell value={parseAmountLabel(line.amountLabel)} currency="USD" parens zeroDash />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export function ReconciliationComparisonPanel({ view }: { view: ReconciliationComparisonViewState }) {
  const [selection, setSelection] = useState<ReconciliationComparisonSelection>(null);
  const statementScrollRef = useRef<HTMLDivElement>(null);
  const ledgerScrollRef = useRef<HTMLDivElement>(null);

  const handleSelect = (matchKey: string, side: ReconciliationComparisonSide) => {
    setSelection((previous) =>
      previous && previous.matchKey === matchKey && previous.side === side ? null : { matchKey, side }
    );
  };

  const syncScroll = (source: ReconciliationComparisonSide) => {
    const from = (source === "statement" ? statementScrollRef : ledgerScrollRef).current;
    const to = (source === "statement" ? ledgerScrollRef : statementScrollRef).current;
    if (from && to && to.scrollTop !== from.scrollTop) {
      to.scrollTop = from.scrollTop;
    }
  };

  return (
    <section className="accounting-reference-panel" data-appearance="light" aria-label={view.ariaLabel}>
      <div className="accounting-reference-heading">
        <div className="min-w-0">
          <p className="accounting-reference-kicker">{view.title}</p>
          <p className="accounting-reference-subtitle">{view.subtitle}</p>
        </div>
        <div className="accounting-reference-badges" aria-label="Reconciliation match status">
          <span className="accounting-reference-badge accounting-reference-badge-success">{view.matchedBadgeLabel}</span>
          <span className="accounting-reference-badge accounting-reference-badge-warning">{view.openBadgeLabel}</span>
        </div>
      </div>

      <div className="accounting-reconciliation-split">
        <ReconciliationComparisonPane
          heading={view.statementHeading}
          ariaLabel={`${view.statementHeading} reconciliation lines`}
          side="statement"
          lines={view.statementLines}
          selection={selection}
          onSelect={handleSelect}
          scrollRef={statementScrollRef}
          onScroll={() => syncScroll("statement")}
        />
        <ReconciliationComparisonPane
          heading={view.ledgerHeading}
          ariaLabel={`${view.ledgerHeading} reconciliation lines`}
          side="ledger"
          lines={view.ledgerLines}
          selection={selection}
          onSelect={handleSelect}
          scrollRef={ledgerScrollRef}
          onScroll={() => syncScroll("ledger")}
        />
      </div>

      <div className="accounting-balance-strip">
        <div>
          <span>Statement balance</span>
          <strong>{view.statementBalanceLabel}</strong>
        </div>
        <div>
          <span>Ledger balance</span>
          <strong>{view.ledgerBalanceLabel}</strong>
        </div>
        <div className={cn("accounting-reference-balance-badge", view.varianceTone === "success" ? "is-balanced" : "is-out")}>
          <span aria-hidden="true" />
          {view.varianceLabel}
        </div>
      </div>
    </section>
  );
}

ReconciliationComparisonPanel.displayName = "ReconciliationComparisonPanel";
