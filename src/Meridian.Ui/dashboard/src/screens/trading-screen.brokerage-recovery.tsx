import { Link } from "react-router-dom";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { AlertTriangle, ArrowRight, RotateCcw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { EmptyState } from "@/components/data/empty-state";
import { useOpenScopePicker } from "@/lib/operating-scope/scope-picker-context";
import type { DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import { formatCurrency } from "@/lib/format";
import type { TradingBrokerageRecoveryRun, TradingExecutionReconciliationBreak, TradingOperatorReadiness } from "@/types";

type RecoveryBreak = TradingExecutionReconciliationBreak & { rowId: string };

const discrepancyColumns: DenseDataTableColumn<RecoveryBreak>[] = [
  { id: "symbol", label: "Symbol / discrepancy", render: (row) => <><span className="font-mono font-semibold">{row.symbol ?? "Account"}</span><p className="mt-1 text-xs text-muted-foreground whitespace-normal">{row.description}</p></> },
  { id: "order", label: "Order identity", render: (row) => <dl className="font-mono text-xs"><dt className="text-muted-foreground">Local</dt><dd>{row.localOrderId ?? "Unavailable"}</dd><dt className="mt-1 text-muted-foreground">Broker</dt><dd>{row.brokerOrderId ?? "Unavailable"}</dd><dt className="mt-1 text-muted-foreground">Client</dt><dd>{row.clientOrderId ?? "Unavailable"}</dd></dl> },
  { id: "local", label: "Retained local state", render: (row) => <span className="font-mono whitespace-normal">{row.localValue ?? "Unavailable"}</span> },
  { id: "broker", label: "Broker state", render: (row) => <span className="font-mono whitespace-normal">{row.brokerValue ?? "Unavailable"}</span> }
];

const runColumns: DenseDataTableColumn<TradingBrokerageRecoveryRun>[] = [
  { id: "run", label: "Strategy run", render: (row) => <span className="font-mono">{row.runId}</span> },
  { id: "strategy", label: "Strategy", render: (row) => row.strategyId },
  { id: "status", label: "Status", render: (row) => row.status },
  { id: "impact", label: "Recovery impact", render: (row) => <span className="whitespace-normal">{row.detail}</span> }
];

function timestamp(value: string | null | undefined) {
  if (!value || !Number.isFinite(Date.parse(value))) return "Unavailable";
  return `${new Date(value).toISOString().replace("T", " ").replace(/\.\d{3}Z$/, " UTC")}`;
}

export function BrokerageRecoveryPanel({ readiness, fundAccountId, brokerageEnvironment, busy, error, onRecover, variant = "full", reviewHref }: {
  readiness: TradingOperatorReadiness | null;
  fundAccountId?: string;
  /**
   * The trading read model's `brokerage.environment`, which the server writes as `"live"` or
   * `"paper"`. With no fund account in scope and any other non-blank value, the panel shows a
   * neutral setup state instead of the recovery block. Missing, blank or `"live"` keeps the block
   * (fail closed).
   *
   * This is not a deployment signal. The server takes it from the strategy run it prefers (a paper
   * run first) and writes `"paper"` when no run resolves; it does not read the brokerage gateway
   * or `LiveExecutionEnabled`. So a live-enabled host with no runs, or with any paper run, also
   * gets the setup state. That state unblocks nothing (it still says live trading stays off); it
   * only shows a lower severity. Follow-up: the read model should derive the value from the
   * gateway configuration and send null when it cannot tell.
   */
  brokerageEnvironment?: string | null;
  busy: boolean;
  error: string | null;
  onRecover: () => Promise<void>;
  /**
   * `full` is the recovery workspace (Trading overview). `row` is for routes whose job is
   * something else: one blocked-state line that links to the full panel, and nothing at all
   * once evidence is reconciled (the data-confidence card already names the broker posture).
   */
  variant?: "full" | "row";
  /** Where the row's Review link opens the full panel. Required for the row variant. */
  reviewHref?: string;
}) {
  const openScopePicker = useOpenScopePicker();
  const suppliedRecovery = readiness?.brokerageRecovery;
  const scopeMatches = Boolean(fundAccountId && suppliedRecovery?.fundAccountId?.toLowerCase() === fundAccountId.toLowerCase());
  const recovery = scopeMatches ? suppliedRecovery : null;
  const portfolio = recovery?.portfolio;
  const reconciliation = scopeMatches ? readiness?.executionReconciliation : null;
  const blocked = busy || Boolean(error) || !recovery || recovery.status !== "Ready"
    || !portfolio?.isComplete || !portfolio.isFresh || !portfolio.isConsistent
    || reconciliation?.status !== "Ready" || !reconciliation.brokerConnected || !reconciliation.brokerHealthy
    || reconciliation.breakCount !== 0 || reconciliation.breaks.length !== 0 || recovery.blockingReasons.length !== 0;
  const environment = brokerageEnvironment?.trim().toLowerCase() ?? "";
  // Choosing an account is setup, not recovery. Fail closed when the read model reports live or
  // nothing; see the prop's note on how the server derives the value.
  const needsAccountSetup = !fundAccountId && !error && environment !== "" && environment !== "live";
  const discrepancies = (reconciliation?.breaks ?? []).map((item, index) => ({ ...item, rowId: `${item.kind}:${index}` }));
  const reasons = [...new Set([...(recovery?.blockingReasons ?? []), ...(portfolio?.warnings ?? [])])];
  const money = (value: number | null | undefined) => portfolio?.currency
    ? `${formatCurrency(value, { currency: portfolio.currency, fallback: "Unavailable" })} (${portfolio.currency})`
    : "Unavailable — currency unverified";

  const blockedDetail = error ? `Synchronization unavailable: ${error}` : busy
    ? "Checking portfolio and broker orders. Trading remains blocked until reconciliation completes."
    : !fundAccountId ? "Select a fund account to load its brokerage recovery evidence."
      : !recovery ? "Current account recovery evidence is unavailable. Synchronize and reconcile before trading."
        : recovery.detail;

  if (variant === "row") {
    if (!blocked) {
      return null;
    }
    const reviewLink = reviewHref ? (
      <Link
        to={reviewHref}
        className="inline-flex shrink-0 items-center gap-1 font-semibold text-primary underline-offset-2 hover:underline"
        aria-label="Review brokerage recovery"
      >
        Review
        <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
      </Link>
    ) : null;
    if (needsAccountSetup) {
      // Setup, not a failure: neutral border, no alert icon, and choosing the account as the one
      // next step when the shell can open the scope picker.
      return (
        <section
          role="region"
          aria-label="Brokerage recovery"
          aria-busy={busy}
          className="flex min-w-0 items-center gap-3 rounded-[2px] border border-l-[3px] border-border bg-card px-3 py-2 text-xs"
        >
          <p role="status" aria-label="Brokerage recovery readiness" aria-live="polite" className="min-w-0 flex-1 text-muted-foreground">
            No fund account in scope · brokerage recovery is checked per account; live trading stays off until one is chosen.
          </p>
          {openScopePicker ? (
            <Button size="sm" variant="outline" onClick={openScopePicker}>Choose account</Button>
          ) : reviewLink}
        </section>
      );
    }
    return (
      <section
        role="region"
        aria-label="Brokerage recovery"
        aria-busy={busy}
        className="flex min-w-0 items-center gap-3 rounded-[2px] border border-l-[3px] border-border border-l-danger bg-card px-3 py-2 text-xs"
      >
        <AlertTriangle className="h-3.5 w-3.5 shrink-0 text-danger" aria-hidden="true" />
        {/* Same status text and live region as the full panel, so the blocked state is announced
            identically wherever it is shown. */}
        <p role="status" aria-label="Brokerage recovery readiness" aria-live="polite" className="min-w-0 flex-1">
          <span className="font-semibold text-danger">Live trading blocked — recovery required</span>
          <span className="text-muted-foreground"> · {blockedDetail}</span>
        </p>
        {reviewLink}
      </section>
    );
  }

  return (
    <Card id="brokerage-recovery" role="region" aria-label="Brokerage recovery" aria-busy={busy}>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle>Brokerage recovery</CardTitle>
            <CardDescription className="mt-1">Account portfolio and retained execution evidence from shared reconciliation.</CardDescription>
          </div>
          {/* With no account there is nothing to synchronize: the one next step is choosing it. */}
          {fundAccountId ? (
            <Button size="sm" variant="outline" disabled={busy} onClick={() => void onRecover()}>
              <RotateCcw aria-hidden="true" className="mr-2 h-3.5 w-3.5" />
              {busy ? "Reconciling…" : "Synchronize and reconcile"}
            </Button>
          ) : null}
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* With no account and a reported non-live environment the empty state below is the
            status: nothing has failed, so no danger banner above it. */}
        {needsAccountSetup ? null : (
          <StatusBanner tone={blocked ? "danger" : "success"} role="status" aria-label="Brokerage recovery readiness" aria-live="polite"
            title={blocked ? "Live trading blocked — recovery required" : "Brokerage evidence reconciled"}
            detail={blockedDetail} />
        )}
        {reasons.length > 0 ? <ul aria-label="Recovery blockers" className="list-disc space-y-1 pl-5 text-xs text-danger">{reasons.map((reason) => <li key={reason}>{reason}</li>)}</ul> : null}
        {!fundAccountId ? (
          // One explanation and one next step, instead of a grid of "Unavailable" facts.
          <EmptyState
            icon="inbox"
            title="Choose a fund account"
            detail="Recovery evidence is per account: its broker portfolio, retained orders and affected strategy runs load once a fund account is in scope. Live trading stays off until then."
            action={openScopePicker ? "Choose account" : undefined}
            onAction={openScopePicker ?? undefined}
            role={needsAccountSetup ? "status" : undefined}
            ariaLabel={needsAccountSetup ? "Brokerage recovery readiness" : undefined}
            compact
          />
        ) : !recovery && !busy && !error ? null : (
        <TechnicalDetails label="Brokerage recovery evidence">
        <dl className="grid gap-x-5 gap-y-3 text-xs sm:grid-cols-2 xl:grid-cols-4">
          <RecoveryFact label="Fund account" value={fundAccountId ?? "Not selected"} />
          <RecoveryFact label="Broker account" value={recovery ? `${recovery.providerId ?? "Unknown provider"} · ${recovery.externalAccountId ?? "Unlinked"}` : "Unavailable"} />
          <RecoveryFact label="Connection health" value={!reconciliation ? "Unknown" : !reconciliation.brokerConnected ? "Disconnected" : !portfolio?.isFresh || busy || error ? "Unverified · last observed connected" : reconciliation.brokerHealthy ? "Connected · healthy" : "Connected · degraded"} />
          <RecoveryFact label="Portfolio evidence" value={!portfolio ? "Unavailable" : `${portfolio.isComplete ? "Complete" : "Incomplete"} · ${portfolio.isFresh ? "Fresh" : "Stale"} · ${portfolio.isConsistent ? "Consistent" : "Inconsistent"}`} />
          <RecoveryFact label="Latest sync attempt" value={timestamp(portfolio?.lastAttemptedAt)} />
          <RecoveryFact label="Last successful sync" value={timestamp(portfolio?.lastSuccessfulAt)} />
          <RecoveryFact label="Portfolio observed" value={timestamp(portfolio?.observedAt)} />
          <RecoveryFact label="Orders reconciled" value={timestamp(reconciliation?.reconciledAt)} />
          <RecoveryFact label="Cash" value={money(portfolio?.cash)} />
          <RecoveryFact label="Buying power" value={money(portfolio?.buyingPower)} />
          <RecoveryFact label="Portfolio value" value={money(portfolio?.portfolioValue)} />
          <RecoveryFact label="Holdings / matched open orders" value={`${portfolio?.positionCount ?? "Unknown"} / ${reconciliation?.matchedOpenOrderCount ?? "Unknown"}`} />
        </dl>

        <div>
          <h4 className="mb-2 text-xs font-semibold">Local orders versus broker state</h4>
          <TableWorkbench<RecoveryBreak> workbenchId="trading-broker-order-discrepancies" sourceKey={`${fundAccountId}:${reconciliation?.reconciledAt ?? "unavailable"}`}
            sourceDescription="All local/broker order discrepancies in the selected account reconciliation. An empty list does not establish recovery."
            completePopulation={Boolean(reconciliation) && scopeMatches && !busy && !error}
            getSearchText={(row) => `${row.symbol ?? ""} ${row.description} ${row.localOrderId ?? ""} ${row.brokerOrderId ?? ""}`}
            columns={discrepancyColumns.map((column) => ({ ...column, exportValue: (row: RecoveryBreak) => column.id === "symbol" ? `${row.symbol ?? "Account"}: ${row.description}` : column.id === "order" ? [row.localOrderId, row.brokerOrderId, row.clientOrderId].filter(Boolean).join(" | ") : column.id === "local" ? row.localValue : row.brokerValue }))} rows={discrepancies} getRowId={(row) => row.rowId}
            ariaLabel="Local and broker order discrepancies"
            emptyText={reconciliation?.status === "Ready" && !blocked ? "No unresolved order discrepancies." : "Order comparison is not verified. An empty result does not establish recovery."} />
        </div>
        <div>
          <h4 className="mb-2 text-xs font-semibold">Affected strategy runs</h4>
          <TableWorkbench<TradingBrokerageRecoveryRun> workbenchId="trading-broker-affected-runs" sourceKey={`${fundAccountId}:${reconciliation?.reconciledAt ?? "unavailable"}`}
            sourceDescription="All affected strategy runs in the selected account's retained brokerage recovery snapshot."
            completePopulation={Boolean(recovery) && scopeMatches && !busy && !error} getSearchText={(row) => `${row.runId} ${row.strategyId} ${row.status} ${row.detail}`}
            columns={runColumns.map((column) => ({ ...column, exportValue: (row: TradingBrokerageRecoveryRun) => column.id === "run" ? row.runId : column.id === "strategy" ? row.strategyId : column.id === "status" ? row.status : row.detail }))} rows={recovery?.affectedRuns ?? []} getRowId={(row) => row.runId}
            ariaLabel="Affected strategy runs" emptyText={recovery ? "No retained strategy runs reported for this account." : "Affected runs unavailable until account evidence is loaded."} />
        </div>
        <p className="text-xs text-muted-foreground">Synchronization checks broker truth and retained orders. It does not resubmit orders or resume strategy runs. Live operation also requires all trading readiness gates.</p>
        </TechnicalDetails>
        )}
      </CardContent>
    </Card>
  );
}

function RecoveryFact({ label, value }: { label: string; value: string }) {
  return <div className="min-w-0"><dt className="text-muted-foreground">{label}</dt><dd className="mt-1 break-words font-mono text-foreground">{value}</dd></div>;
}
