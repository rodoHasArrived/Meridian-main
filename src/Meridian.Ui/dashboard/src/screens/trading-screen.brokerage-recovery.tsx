import { TechnicalDetails } from "@/components/ui/technical-details";
import { RotateCcw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
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

export function BrokerageRecoveryPanel({ readiness, fundAccountId, busy, error, onRecover, compact = false }: {
  readiness: TradingOperatorReadiness | null;
  fundAccountId?: string;
  busy: boolean;
  error: string | null;
  onRecover: () => Promise<void>;
  compact?: boolean;
}) {
  const suppliedRecovery = readiness?.brokerageRecovery;
  const scopeMatches = Boolean(fundAccountId && suppliedRecovery?.fundAccountId?.toLowerCase() === fundAccountId.toLowerCase());
  const recovery = scopeMatches ? suppliedRecovery : null;
  const portfolio = recovery?.portfolio;
  const reconciliation = scopeMatches ? readiness?.executionReconciliation : null;
  const blocked = busy || Boolean(error) || !recovery || recovery.status !== "Ready"
    || !portfolio?.isComplete || !portfolio.isFresh || !portfolio.isConsistent
    || reconciliation?.status !== "Ready" || !reconciliation.brokerConnected || !reconciliation.brokerHealthy
    || reconciliation.breakCount !== 0 || reconciliation.breaks.length !== 0 || recovery.blockingReasons.length !== 0;
  const discrepancies = (reconciliation?.breaks ?? []).map((item, index) => ({ ...item, rowId: `${item.kind}:${index}` }));
  const reasons = [...new Set([...(recovery?.blockingReasons ?? []), ...(portfolio?.warnings ?? [])])];
  const money = (value: number | null | undefined) => portfolio?.currency
    ? `${formatCurrency(value, { currency: portfolio.currency, fallback: "Unavailable" })} (${portfolio.currency})`
    : "Unavailable — currency unverified";

  const evidence = (<TechnicalDetails label="Brokerage recovery evidence">
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
        </TechnicalDetails>);
  const statusDetail = error ? `Synchronization unavailable: ${error}` : busy
    ? "Checking portfolio and broker orders; trading remains blocked."
    : !fundAccountId ? "Select a fund account to load brokerage evidence."
      : !recovery ? "Current account recovery evidence is unavailable."
        : recovery.detail;
  if (compact) return <section id="brokerage-recovery" role="region" aria-label="Brokerage recovery" aria-busy={busy}
    className={`flex flex-wrap items-center justify-between gap-x-3 gap-y-1 border-l-2 px-3 py-1.5 ${blocked ? "border-danger bg-danger/5" : "border-success bg-success/5"}`}>
    <p role="status" aria-label="Brokerage recovery readiness" aria-live="polite" className="min-w-0 flex-1 text-xs leading-5">
      <strong className={blocked ? "text-danger" : "text-success"}>{blocked ? "Live trading blocked — recovery required" : "Brokerage evidence reconciled"}</strong>
      <span className="ml-2 text-muted-foreground">{statusDetail}</span>
    </p>
    <Button size="sm" variant="outline" disabled={busy || !fundAccountId} disabledReason={!fundAccountId ? "Select an operating fund account before synchronizing." : busy ? "Reconciliation is in progress." : null} onClick={() => void onRecover()}>{busy ? "Reconciling…" : "Synchronize and reconcile"}</Button>
    <details className="text-xs open:basis-full"><summary className="cursor-pointer">Recovery evidence{reasons.length ? ` · ${reasons.length} blocker(s)` : ""}</summary>
      {reasons.length > 0 ? <ul aria-label="Recovery blockers" className="mt-2 list-disc pl-5 text-danger">{reasons.map((reason) => <li key={reason}>{reason}</li>)}</ul> : null}
      {evidence}
    </details>
  </section>;

  return (
    <Card id="brokerage-recovery" role="region" aria-label="Brokerage recovery" aria-busy={busy}>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle>Brokerage recovery</CardTitle>
            <CardDescription className="mt-1">Account portfolio and retained execution evidence from shared reconciliation.</CardDescription>
          </div>
          <Button size="sm" variant="outline" disabled={busy || !fundAccountId} onClick={() => void onRecover()}>
            <RotateCcw aria-hidden="true" className="mr-2 h-3.5 w-3.5" />
            {busy ? "Reconciling…" : "Synchronize and reconcile"}
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <StatusBanner tone={blocked ? "danger" : "success"} role="status" aria-label="Brokerage recovery readiness" aria-live="polite"
          title={blocked ? "Live trading blocked — recovery required" : "Brokerage evidence reconciled"}
          detail={error ? `Synchronization unavailable: ${error}` : busy
            ? "Checking portfolio and broker orders. Trading remains blocked until reconciliation completes."
            : !fundAccountId ? "Select a fund account to load its brokerage recovery evidence."
              : !recovery ? "Current account recovery evidence is unavailable. Synchronize and reconcile before trading."
                : recovery.detail} />
        {reasons.length > 0 ? <ul aria-label="Recovery blockers" className="list-disc space-y-1 pl-5 text-xs text-danger">{reasons.map((reason) => <li key={reason}>{reason}</li>)}</ul> : null}
        {evidence}
      </CardContent>
    </Card>
  );
}

function RecoveryFact({ label, value }: { label: string; value: string }) {
  return <div className="min-w-0"><dt className="text-muted-foreground">{label}</dt><dd className="mt-1 break-words font-mono text-foreground">{value}</dd></div>;
}
