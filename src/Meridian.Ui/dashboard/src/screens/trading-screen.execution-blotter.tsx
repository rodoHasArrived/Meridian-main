/**
 * Broker-side execution blotter for Trading → Risk.
 *
 * The workstation's trading projection is Meridian's own view of the desk. These
 * endpoints are the execution gateway's: its gateway health, its account figures,
 * and the position book it will actually act on. Nothing in the workstation called
 * them, so an operator had no way to compare the two.
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import { Briefcase, RefreshCcw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { ApiError } from "@/lib/api-errors";
import {
  getExecutionAccountSnapshot,
  getExecutionBlotter,
  getExecutionGatewayHealth,
  upsizeExecutionPosition
} from "@/lib/api/execution-blotter.api";
import {
  buildExecutionBlotterMetrics,
  buildExecutionBlotterRow,
  buildExecutionProvenance,
  executionBlotterEmptyMessage,
  type ExecutionBlotterTone,
  type ExecutionReadState
} from "@/screens/trading-screen.execution-blotter.view-model";
import type {
  ExecutionAccountSnapshot,
  ExecutionBlotterSnapshot,
  ExecutionGatewayHealth
} from "@/types/execution-blotter.types";

/** The status these endpoints answer when their execution service is not registered. */
const SERVICE_INACTIVE_STATUS = 503;

export function ExecutionBlotterPanel() {
  const [health, setHealth] = useState<ExecutionGatewayHealth | null>(null);
  const [account, setAccount] = useState<ExecutionAccountSnapshot | null>(null);
  const [blotter, setBlotter] = useState<ExecutionBlotterSnapshot | null>(null);
  const [readState, setReadState] = useState<ExecutionReadState>("loading");
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [upsizeKey, setUpsizeKey] = useState<string | null>(null);
  const [upsizeQuantity, setUpsizeQuantity] = useState("");
  const [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    setReadState("loading");
    setError(null);
    const [healthResult, accountResult, blotterResult] = await Promise.allSettled([
      getExecutionGatewayHealth(),
      getExecutionAccountSnapshot(),
      getExecutionBlotter()
    ]);

    setHealth(healthResult.status === "fulfilled" ? healthResult.value : null);
    setAccount(accountResult.status === "fulfilled" ? accountResult.value : null);
    setBlotter(blotterResult.status === "fulfilled" ? blotterResult.value : null);

    if (blotterResult.status === "fulfilled") {
      setReadState("ready");
      return;
    }

    // A host without execution services answers 503 on every one of these reads.
    // Reporting that as a failure would send an operator chasing a broken gateway.
    if (isServiceInactive(blotterResult.reason)) {
      setReadState("inactive");
      return;
    }

    setReadState("error");
    setError(errorMessage(blotterResult.reason));
  }, []);

  useEffect(() => { void refresh(); }, [refresh]);

  const metrics = useMemo(() => buildExecutionBlotterMetrics(health, account), [health, account]);
  const provenance = useMemo(() => buildExecutionProvenance(blotter), [blotter]);
  const rows = useMemo(() => (blotter?.positions ?? []).map(buildExecutionBlotterRow), [blotter]);

  async function submitUpsize(positionKey: string) {
    const quantity = Number(upsizeQuantity);
    if (!Number.isFinite(quantity) || quantity <= 0) {
      setError("Enter a positive quantity to add to the position.");
      return;
    }

    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const result = await upsizeExecutionPosition({ positionKey, quantity });
      setNotice(`${result.status}: ${result.message}`);
      setUpsizeKey(null);
      setUpsizeQuantity("");
      await refresh();
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div className="eyebrow-label">Execution gateway</div>
            <CardTitle className="flex items-center gap-2">
              <Briefcase className="h-5 w-5 text-primary" />
              Execution blotter
            </CardTitle>
            <CardDescription>
              The position book the execution gateway will act on, with the gateway health and
              account figures behind it.
            </CardDescription>
          </div>
          <Button size="sm" variant="outline" onClick={() => void refresh()}>
            <RefreshCcw className="mr-2 h-4 w-4" />
            Refresh
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {error ? <StatusBanner tone="danger" title="Execution read needs attention" detail={error} /> : null}
        {notice ? <StatusBanner tone="success" title="Execution action accepted" detail={notice} /> : null}
        <StatusBanner tone={bannerTone(provenance.tone)} title={provenance.label} detail={provenance.detail} />

        <div className="grid gap-2 md:grid-cols-4" aria-label="Execution gateway posture">
          {metrics.map((metric) => (
            <div key={metric.id} className="rounded-[2px] border border-border bg-secondary/20 p-3">
              <div className="text-xs uppercase tracking-wide text-muted-foreground">{metric.label}</div>
              <div className={toneTextClassName(metric.tone)}>{metric.value}</div>
              <div className="mt-1 text-xs text-muted-foreground">{metric.detail}</div>
            </div>
          ))}
        </div>

        <table className="w-full text-sm" aria-label="Execution blotter positions">
          <thead>
            <tr className="text-left text-xs uppercase tracking-wide text-muted-foreground">
              <th className="py-2">Symbol</th>
              <th>Side</th>
              <th>Quantity</th>
              <th>Avg cost</th>
              <th>Mark</th>
              <th>Market value</th>
              <th>Unrealised</th>
              <th className="sr-only">Actions</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={8} className="py-4 text-center text-muted-foreground">
                  {executionBlotterEmptyMessage(readState)}
                </td>
              </tr>
            ) : rows.map((row) => (
              <tr key={row.positionKey} className="border-t border-border/60" aria-label={row.ariaLabel}>
                <td className="py-2">
                  <div className="font-mono text-xs">{row.symbol}</div>
                  <div className="text-xs text-muted-foreground">
                    {row.assetClass}{row.contractDetail ? ` · ${row.contractDetail}` : ""}
                  </div>
                </td>
                <td>{row.side}</td>
                <td className="font-mono text-xs">{row.quantity}</td>
                <td className="font-mono text-xs">{row.averageCostBasis}</td>
                <td className="font-mono text-xs">{row.marketPrice}</td>
                <td className="font-mono text-xs">{row.marketValue}</td>
                <td className={`font-mono text-xs ${toneInlineClassName(row.unrealisedTone)}`}>{row.unrealisedPnl}</td>
                <td className="text-right">
                  {row.canUpsize ? (
                    upsizeKey === row.positionKey ? (
                      <span className="flex items-center justify-end gap-2">
                        <Input
                          aria-label={`Quantity to add to ${row.symbol}`}
                          className="h-8 w-24"
                          value={upsizeQuantity}
                          onChange={(event) => setUpsizeQuantity(event.target.value)}
                        />
                        <Button size="sm" disabled={busy} onClick={() => void submitUpsize(row.positionKey)}>
                          Confirm
                        </Button>
                      </span>
                    ) : (
                      <Button size="sm" variant="outline" onClick={() => { setUpsizeKey(row.positionKey); setUpsizeQuantity(""); }}>
                        Upsize
                      </Button>
                    )
                  ) : null}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </CardContent>
    </Card>
  );
}

function isServiceInactive(reason: unknown): boolean {
  return reason instanceof ApiError && reason.status === SERVICE_INACTIVE_STATUS;
}

function bannerTone(tone: ExecutionBlotterTone): "success" | "warning" | "danger" | "info" {
  return tone === "default" ? "info" : tone;
}

function toneTextClassName(tone: ExecutionBlotterTone): string {
  switch (tone) {
    case "success":
      return "mt-1 text-xl font-semibold text-success";
    case "warning":
      return "mt-1 text-xl font-semibold text-warning";
    case "danger":
      return "mt-1 text-xl font-semibold text-danger";
    default:
      return "mt-1 text-xl font-semibold";
  }
}

function toneInlineClassName(tone: ExecutionBlotterTone): string {
  switch (tone) {
    case "success":
      return "text-success";
    case "danger":
      return "text-danger";
    default:
      return "";
  }
}

function errorMessage(reason: unknown): string {
  if (reason instanceof ApiError) {
    return reason.detail ?? reason.title ?? reason.message;
  }

  return reason instanceof Error ? reason.message : "The operation could not be completed.";
}
