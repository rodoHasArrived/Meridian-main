import { useCallback, useEffect, useMemo, useState } from "react";
import { Activity, Power, RefreshCcw, RotateCcw } from "lucide-react";
import {
  getLatestRuntimeShutdownReceipt,
  getRuntimeLifecycle,
  getRuntimeShutdownOperation,
  requestRuntimeShutdown
} from "@/lib/api";
import type {
  LifecycleShutdownAccepted,
  LifecycleShutdownOperation,
  LifecycleShutdownReceipt,
  RuntimeLifecycleCheck,
  RuntimeLifecycleSnapshot
} from "@/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { cn } from "@/lib/utils";

type PendingAction = "shutdown" | "restart";

export function LifecycleControlPanel() {
  const [snapshot, setSnapshot] = useState<RuntimeLifecycleSnapshot | null>(null);
  const [latestReceipt, setLatestReceipt] = useState<LifecycleShutdownReceipt | null>(null);
  const [accepted, setAccepted] = useState<LifecycleShutdownAccepted | null>(null);
  const [operation, setOperation] = useState<LifecycleShutdownOperation | null>(null);
  const [requestedSessionId, setRequestedSessionId] = useState<string | null>(null);
  const [pendingAction, setPendingAction] = useState<PendingAction | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async (signal?: AbortSignal) => {
    const [lifecycleResult, receiptResult] = await Promise.allSettled([
      getRuntimeLifecycle({ signal }),
      getLatestRuntimeShutdownReceipt({ signal })
    ]);

    if (lifecycleResult.status === "rejected") {
      if (signal?.aborted) return;
      setError(errorMessage(lifecycleResult.reason, "Runtime lifecycle status is unavailable."));
      setLoading(false);
      return;
    }

    setSnapshot(lifecycleResult.value);
    setError(null);
    if (receiptResult.status === "fulfilled") setLatestReceipt(receiptResult.value);
    setLoading(false);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);
    const interval = window.setInterval(() => void refresh(controller.signal), 5_000);
    return () => {
      controller.abort();
      window.clearInterval(interval);
    };
  }, [refresh]);

  useEffect(() => {
    if (
      accepted
      && requestedSessionId
      && snapshot?.sessionId !== requestedSessionId
      && snapshot?.acceptingWork
    ) {
      setAccepted(null);
      setOperation(null);
      setRequestedSessionId(null);
    }
  }, [accepted, requestedSessionId, snapshot?.acceptingWork, snapshot?.sessionId]);

  const submitAction = async () => {
    if (!pendingAction) return;
    setSubmitting(true);
    setError(null);
    try {
      const result = await requestRuntimeShutdown({
        reason: pendingAction === "restart" ? "Restart" : "Operator",
        detail: pendingAction === "restart"
          ? "Restart requested from the browser lifecycle control panel."
          : "Shutdown requested from the browser lifecycle control panel.",
        requestedBy: "browser-workstation"
      });
      setRequestedSessionId(snapshot?.sessionId ?? null);
      setAccepted(result);
      setPendingAction(null);
      setSnapshot((current) => current ? { ...current, shutdownRequested: true, state: result.state } : current);
      try {
        setOperation(await getRuntimeShutdownOperation(result.operationUri));
      } catch {
        // The host can leave before the operation poll completes. The 202 response is
        // authoritative and the supervisor persists the terminal session receipt.
      }
    } catch (requestError) {
      setError(errorMessage(requestError, "The lifecycle request was not accepted."));
    } finally {
      setSubmitting(false);
    }
  };

  const readinessTone = lifecycleTone(snapshot?.readiness);
  const visibleChecks = useMemo(
    () => [...(snapshot?.checks ?? [])].sort((left, right) => checkRank(left) - checkRank(right)),
    [snapshot?.checks]
  );
  const controlsDisabled = submitting || Boolean(snapshot?.shutdownRequested) || Boolean(accepted);

  return (
    <Card id="lifecycle-control" role="region" aria-label="Meridian lifecycle control" className="panel-surface scroll-mt-6">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <div className="eyebrow-label">Runtime ownership</div>
            <CardTitle className="flex items-center gap-2 text-base">
              <Activity className="h-4 w-4 text-primary" aria-hidden="true" />
              Lifecycle control plane
            </CardTitle>
            <CardDescription className="mt-2">
              Inspect readiness and request a supervised restart or shutdown. Meridian drains work and stops its dedicated database in order.
            </CardDescription>
          </div>
          <Badge variant="outline" className={readinessTone.className}>{snapshot?.readiness ?? (loading ? "Loading" : "Unavailable")}</Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4" aria-label="Runtime lifecycle facts">
          <LifecycleFact label="State" value={snapshot?.state ?? "—"} />
          <LifecycleFact label="Active phase" value={snapshot?.activePhase ?? "—"} />
          <LifecycleFact label="Uptime" value={formatUptime(snapshot?.uptimeSeconds)} />
          <LifecycleFact label="Session" value={shortIdentifier(snapshot?.sessionId)} mono />
        </div>

        <div aria-live="polite" aria-atomic="true">
          {accepted ? (
            <div role="status" className="rounded-md border border-warning/35 bg-warning/10 px-4 py-3 text-sm text-foreground">
              <div className="font-semibold">{acceptedActionLabel(accepted, operation)}</div>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">
                Operation {shortIdentifier(accepted.operationId)} was accepted. The supervisor retains ownership while the host exits.
              </p>
            </div>
          ) : error ? (
            <div role="alert" className="rounded-md border border-danger/35 bg-danger/10 px-4 py-3 text-sm text-danger">{error}</div>
          ) : (
            <p className="text-xs leading-5 text-muted-foreground">
              {snapshot?.acceptingWork ? "The host is accepting operator work." : "The host is not accepting new operator work."}
            </p>
          )}
        </div>

        <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(260px,0.42fr)]">
          <div className="rounded-md border border-border/70 bg-background/35 px-4 py-3">
            <div className="text-xs font-semibold uppercase tracking-[0.12em] text-muted-foreground">Readiness checks</div>
            {visibleChecks.length > 0 ? (
              <ul className="mt-3 grid gap-2" aria-label="Runtime readiness checks">
                {visibleChecks.map((check) => <LifecycleCheckRow key={check.id} check={check} />)}
              </ul>
            ) : (
              <p className="mt-3 text-xs text-muted-foreground">No readiness evidence is available.</p>
            )}
          </div>
          <div className="rounded-md border border-border/70 bg-background/35 px-4 py-3">
            <div className="text-xs font-semibold uppercase tracking-[0.12em] text-muted-foreground">Last shutdown receipt</div>
            {latestReceipt ? (
              <dl className="mt-3 grid gap-2 text-xs">
                <ReceiptFact label="Outcome" value={latestReceipt.outcome} />
                <ReceiptFact label="Reason" value={latestReceipt.reason} />
                <ReceiptFact label="Completed" value={formatTimestamp(latestReceipt.completedAtUtc)} />
                <ReceiptFact label="Forced" value={latestReceipt.forcedTermination ? "Yes" : "No"} />
              </dl>
            ) : (
              <p className="mt-3 text-xs leading-5 text-muted-foreground">No prior host shutdown receipt is available.</p>
            )}
          </div>
        </div>

        <div className="flex flex-wrap justify-end gap-2">
          <Button type="button" variant="outline" size="sm" disabled={loading || submitting} onClick={() => void refresh()}>
            <RefreshCcw className={cn("h-3.5 w-3.5", loading && "animate-spin")} aria-hidden="true" />
            Refresh
          </Button>
          <Button type="button" variant="outline" size="sm" disabled={controlsDisabled} onClick={() => setPendingAction("restart")}>
            <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
            Restart Meridian
          </Button>
          <Button type="button" variant="destructive" size="sm" disabled={controlsDisabled} onClick={() => setPendingAction("shutdown")}>
            <Power className="h-3.5 w-3.5" aria-hidden="true" />
            Shut down Meridian
          </Button>
        </div>
      </CardContent>

      <Dialog open={pendingAction !== null} onOpenChange={(open) => { if (!open && !submitting) setPendingAction(null); }}>
        {pendingAction ? (
          <DialogContent
            className="max-w-md"
            aria-labelledby="lifecycle-confirmation-title"
            aria-describedby="lifecycle-confirmation-description"
          >
            <DialogHeader>
              <DialogTitle id="lifecycle-confirmation-title">{pendingAction === "restart" ? "Restart Meridian?" : "Shut down Meridian?"}</DialogTitle>
              <DialogDescription id="lifecycle-confirmation-description">
                {pendingAction === "restart"
                  ? "New work will stop, active work will drain, the dedicated database will stop, and the supervisor will start a new session."
                  : "New work will stop, active work will drain, and the supervisor will stop the host and its dedicated database."}
              </DialogDescription>
            </DialogHeader>
            <div className="flex flex-wrap justify-end gap-2">
              <Button type="button" variant="outline" disabled={submitting} onClick={() => setPendingAction(null)}>Cancel</Button>
              <Button
                type="button"
                variant={pendingAction === "shutdown" ? "destructive" : "default"}
                disabled={submitting}
                busy={submitting}
                busyLabel="Submitting lifecycle request"
                onClick={() => void submitAction()}
              >
                {pendingAction === "restart" ? "Confirm restart" : "Confirm shutdown"}
              </Button>
            </div>
          </DialogContent>
        ) : null}
      </Dialog>
    </Card>
  );
}

function LifecycleFact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-md border border-border/70 bg-background/35 px-3 py-2.5">
      <div className="text-[10px] uppercase tracking-[0.12em] text-muted-foreground">{label}</div>
      <div className={cn("mt-1 truncate text-sm font-semibold text-foreground", mono && "font-mono text-xs")}>{value}</div>
    </div>
  );
}

function LifecycleCheckRow({ check }: { check: RuntimeLifecycleCheck }) {
  const tone = checkTone(check.status);
  return (
    <li className="flex flex-col gap-1 rounded-md border border-border/60 px-3 py-2 sm:flex-row sm:items-start sm:justify-between">
      <div className="min-w-0">
        <div className="text-xs font-semibold text-foreground">{check.displayName}</div>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">{check.message}</p>
      </div>
      <Badge variant="outline" className={cn("shrink-0", tone.className)}>{check.status}</Badge>
    </li>
  );
}

function ReceiptFact({ label, value }: { label: string; value: string }) {
  return <div className="flex justify-between gap-3"><dt className="text-muted-foreground">{label}</dt><dd className="text-right font-medium text-foreground">{value}</dd></div>;
}

function lifecycleTone(readiness?: string) {
  if (readiness === "Ready") return { className: "border-success/35 bg-success/10 text-success" };
  if (readiness === "Degraded" || readiness === "Starting" || readiness === "Stopping") return { className: "border-warning/35 bg-warning/10 text-warning" };
  if (readiness === "Failed" || readiness === "NotReady") return { className: "border-danger/35 bg-danger/10 text-danger" };
  return { className: "border-border/70 text-muted-foreground" };
}

function checkTone(status: RuntimeLifecycleCheck["status"]) {
  if (status === "Passing") return { className: "border-success/35 bg-success/10 text-success" };
  if (status === "Degraded" || status === "Pending" || status === "Skipped") return { className: "border-warning/35 bg-warning/10 text-warning" };
  return { className: "border-danger/35 bg-danger/10 text-danger" };
}

function checkRank(check: RuntimeLifecycleCheck) {
  if (check.status === "Failing") return 0;
  if (check.status === "Degraded") return 1;
  if (check.status === "Pending") return 2;
  return 3;
}

function formatUptime(seconds?: number) {
  if (seconds === undefined || !Number.isFinite(seconds)) return "—";
  const wholeSeconds = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(wholeSeconds / 3600);
  const minutes = Math.floor((wholeSeconds % 3600) / 60);
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${minutes}m ${wholeSeconds % 60}s`;
}

function formatTimestamp(value: string) {
  const timestamp = new Date(value);
  return Number.isNaN(timestamp.valueOf()) ? value : timestamp.toLocaleString();
}

function shortIdentifier(value?: string | null) {
  if (!value) return "—";
  return value.length > 12 ? `${value.slice(0, 12)}…` : value;
}

function acceptedActionLabel(accepted: LifecycleShutdownAccepted, operation: LifecycleShutdownOperation | null) {
  const reason = operation?.reason;
  if (reason === "Restart") return "Restart accepted";
  return accepted.accepted ? "Shutdown accepted" : "Lifecycle request received";
}

function errorMessage(error: unknown, fallback: string) {
  if (error instanceof Error && error.message.trim()) return error.message;
  return fallback;
}
