import { useEffect, useState } from "react";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import { getLedgerPeriodAccountRunningBalance } from "@/lib/ledger-reports-api";
import type { LedgerAccountRunningBalance } from "@/types";

export interface LedgerAccountRunningBalancePanelProps {
  periodId: string;
  accountName: string;
  symbol?: string | null;
  financialAccountId?: string | null;
  /**
   * True when the trial balance shows this account on more than one row (one per dimension
   * scope). The postings route is not scoped by dimension, so the list and its ending balance
   * then cover every scope of the account, not only the selected row.
   */
  combinedAcrossScopes?: boolean;
  /** Formats an amount in the selected book's currency. */
  formatAmount: (value: number) => string;
}

type RunningBalanceState =
  | { status: "loading" }
  | { status: "error"; error: ApiErrorDisplay }
  | { status: "ready"; data: LedgerAccountRunningBalance };

/**
 * Account drill-down for the posted ledger: every posting to the selected trial-balance account
 * in the period, with the balance after each one. Reads the governed book of record. Postings are
 * selected by account, symbol and financial account, not by dimension scope; when the account has
 * several trial-balance rows the panel says the list combines them.
 */
export function LedgerAccountRunningBalancePanel({
  periodId,
  accountName,
  symbol = null,
  financialAccountId = null,
  combinedAcrossScopes = false,
  formatAmount
}: LedgerAccountRunningBalancePanelProps) {
  const [state, setState] = useState<RunningBalanceState>({ status: "loading" });

  useEffect(() => {
    let cancelled = false;
    setState({ status: "loading" });
    void (async () => {
      try {
        const data = await getLedgerPeriodAccountRunningBalance(periodId, {
          account: accountName,
          symbol,
          financialAccountId
        });
        if (!cancelled) {
          setState({ status: "ready", data });
        }
      } catch (error) {
        if (!cancelled) {
          setState({
            status: "error",
            error: describeApiError(error, "Account postings could not be loaded.")
          });
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [periodId, accountName, symbol, financialAccountId]);

  const title = `Postings to ${accountName}`;
  return (
    <section role="region" aria-label={title} className="row-detail-panel min-w-0">
      <div className="eyebrow-label">Account running balance</div>
      <h3 className="mt-1 text-sm font-semibold text-foreground">{title}</h3>
      {combinedAcrossScopes ? (
        <p className="mt-2 text-sm text-muted-foreground">
          This account appears on more than one trial-balance row. The postings and ending balance below cover all
          of its dimension scopes, not only the selected row.
        </p>
      ) : null}
      {state.status === "loading" ? (
        <p role="status" className="mt-2 text-sm text-muted-foreground">Loading postings…</p>
      ) : state.status === "error" ? (
        <div role="alert" className="mt-2 text-sm text-danger">
          <div className="font-semibold">{state.error.summary}</div>
          {state.error.details.map((detail) => (
            <div key={detail}>{detail}</div>
          ))}
        </div>
      ) : state.data.points.length === 0 ? (
        <p className="mt-2 text-sm text-muted-foreground">No postings to this account in the selected period.</p>
      ) : (
        <>
          <p className="mt-2 text-sm text-muted-foreground">
            {state.data.points.length} posting{state.data.points.length === 1 ? "" : "s"}; ending balance{" "}
            <span className="font-mono font-semibold text-foreground">{formatAmount(state.data.endingBalance)}</span>
          </p>
          <div className="mt-3 max-h-80 overflow-auto">
            <table className="w-full text-sm">
              <caption className="sr-only">{title}</caption>
              <thead>
                <tr className="text-left text-xs text-muted-foreground">
                  <th scope="col" className="py-1 pr-3 font-medium">Posted</th>
                  <th scope="col" className="py-1 pr-3 font-medium">Description</th>
                  <th scope="col" className="py-1 pr-3 text-right font-medium">Debit</th>
                  <th scope="col" className="py-1 pr-3 text-right font-medium">Credit</th>
                  <th scope="col" className="py-1 text-right font-medium">Balance</th>
                </tr>
              </thead>
              <tbody>
                {state.data.points.map((point) => (
                  <tr key={point.entryId} className="border-t border-border">
                    <td className="py-1 pr-3 font-mono text-xs">{point.timestamp.slice(0, 10)}</td>
                    <td className="py-1 pr-3">{point.description}</td>
                    <td className="py-1 pr-3 text-right font-mono">{point.debit ? formatAmount(point.debit) : ""}</td>
                    <td className="py-1 pr-3 text-right font-mono">{point.credit ? formatAmount(point.credit) : ""}</td>
                    <td className="py-1 text-right font-mono">{formatAmount(point.balance)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  );
}
