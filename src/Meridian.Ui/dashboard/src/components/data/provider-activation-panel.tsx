import { useCallback, useEffect, useId, useRef, useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { apiGetJson, apiPostJson } from "@/lib/api/http-client";
import { describeApiError, isApiError } from "@/lib/api-errors";
import { formatTimestamp } from "@/lib/format";
import { useOnGlobalRefresh } from "@/lib/global-refresh";

type ActivationState = "active" | "saved-pending" | "disabled" | "failed-to-start" | "unavailable";

interface ProviderActivationEntry {
  providerId: string;
  surface: "streaming" | "backfill" | "module";
  state: ActivationState;
  activeSinceUtc?: string | null;
  reason?: string | null;
}

interface ProviderActivationResponse {
  providers: ProviderActivationEntry[];
  canRestart: boolean;
  pendingPaths: string[];
  restartDetail?: string | null;
  runtimeSessionId?: string | null;
  isReady: boolean;
}

const stateLabels = {
  active: { label: "Active", tone: "success" },
  "saved-pending": { label: "Saved, pending", tone: "warning" },
  disabled: { label: "Disabled", tone: "outline" },
  "failed-to-start": { label: "Failed to start", tone: "danger" },
  unavailable: { label: "Unavailable", tone: "outline" }
} as const;

const surfaceLabels = { streaming: "Streaming", backfill: "Backfill", module: "Module" } as const;

const activationColumns: DenseDataTableColumn<ProviderActivationEntry>[] = [
  { id: "provider", label: "Provider", rowHeader: true, render: (row) => row.providerId },
  { id: "surface", label: "Surface", render: (row) => surfaceLabels[row.surface] ?? row.surface },
  {
    id: "activation",
    label: "State",
    render: (row) => {
      const { label, tone } = stateLabels[row.state] ?? stateLabels.unavailable;
      return <Badge variant={tone}>{label}</Badge>;
    }
  },
  {
    id: "since",
    label: "Active since",
    render: (row) => row.activeSinceUtc ? formatTimestamp(row.activeSinceUtc) : "—"
  },
  { id: "reason", label: "Detail", render: (row) => row.reason ?? "—" }
];

export function ProviderActivationPanel({
  refreshKey,
  showRows = true,
  onRestartObserved
}: {
  refreshKey?: string | number;
  showRows?: boolean;
  onRestartObserved?: (pendingPaths: readonly string[]) => void;
}) {
  const [activation, setActivation] = useState<ProviderActivationResponse | null>(null);
  const [readError, setReadError] = useState<string | null>(null);
  const [restartMessage, setRestartMessage] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [restarting, setRestarting] = useState(false);
  const refreshRequest = useRef<AbortController | null>(null);
  const restartRequest = useRef<AbortController | null>(null);
  const dialogTitle = useId();
  const dialogDescription = useId();

  const refresh = useCallback(async () => {
    if (restartRequest.current) return;
    refreshRequest.current?.abort();
    const request = new AbortController();
    refreshRequest.current = request;
    try {
      const response = await readActivation(request.signal);
      if (!request.signal.aborted) {
        setActivation(response);
        setReadError(null);
        if (!response.canRestart || response.pendingPaths.length === 0)
          setConfirmOpen(false);
      }
    } catch (error) {
      if (!request.signal.aborted) {
        setActivation(null);
        setConfirmOpen(false);
        setReadError(describeApiError(error, "Provider activation is unavailable.").summary);
      }
    }
  }, []);

  useOnGlobalRefresh(refresh);
  useEffect(() => {
    void refresh();
    window.addEventListener("meridian:configuration-saved", refresh);
    return () => {
      window.removeEventListener("meridian:configuration-saved", refresh);
      refreshRequest.current?.abort();
    };
  }, [refresh, refreshKey]);
  useEffect(() => () => restartRequest.current?.abort(), []);

  async function restartHost() {
    if (!activation?.canRestart || !activation.runtimeSessionId || restartRequest.current) return;
    const previousSession = activation.runtimeSessionId;
    refreshRequest.current?.abort();
    const request = new AbortController();
    restartRequest.current = request;
    setConfirmOpen(false);
    setRestarting(true);
    setRestartMessage("Requesting restart…");
    try {
      try {
        await apiPostJson("/api/providers/restart", undefined, {
          signal: AbortSignal.any([request.signal, AbortSignal.timeout(10_000)]),
          allowDevelopmentFallback: false
        });
      } catch (error) {
        if (request.signal.aborted || (isApiError(error) && error.status >= 400 && error.status < 500))
          throw error;
        // A disconnect during the response can mean the host accepted the request and exited.
      }
      setRestartMessage("Waiting for Meridian to become ready.");
      const next = await waitForRestart(previousSession, request.signal);
      if (next) {
        setActivation(next);
        setReadError(null);
        setRestartMessage("Meridian restarted and is ready.");
        onRestartObserved?.(next.pendingPaths);
      } else if (!request.signal.aborted) {
        setRestartMessage("A new ready session has yet to respond. Check the Meridian launcher or refresh the page.");
      }
    } catch (error) {
      if (!request.signal.aborted)
        setRestartMessage(describeApiError(error, "Could not confirm the restart request.").summary);
    } finally {
      restartRequest.current = null;
      if (!request.signal.aborted)
        setRestarting(false);
    }
  }

  const pendingPaths = activation?.pendingPaths ?? [];
  const restartNotice = (
    <div className="space-y-2">
      {restartMessage ? <p role="status" className="text-sm text-muted-foreground">{restartMessage}</p> : null}
      {pendingPaths.length > 0 && !restarting ? (
        activation?.canRestart ? (
          <Button variant="outline" size="sm" onClick={() => setConfirmOpen(true)}>
            Restart host to apply {pendingPaths.length} pending {pendingPaths.length === 1 ? "change" : "changes"}
          </Button>
        ) : <p className="text-sm text-muted-foreground">{activation?.restartDetail}</p>
      ) : null}
      {!showRows && readError ? <p className="text-sm text-muted-foreground">{readError}</p> : null}
    </div>
  );

  return (
    <>
      {showRows ? (
        <Card>
          <CardHeader><CardTitle>Provider activation</CardTitle></CardHeader>
          <CardContent className="space-y-3">
            {readError ? (
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-sm text-muted-foreground">{readError}</span>
                <Button size="sm" variant="outline" onClick={() => void refresh()}>Retry</Button>
              </div>
            ) : activation ? (
              <DenseDataTable
                columns={activationColumns}
                rows={activation.providers}
                getRowId={(row) => `${row.surface}:${row.providerId}`}
                ariaLabel="Provider activation"
                emptyText="The host reported no provider activation evidence."
              />
            ) : <p className="text-sm text-muted-foreground">Reading provider activation…</p>}
            {restartNotice}
          </CardContent>
        </Card>
      ) : restartNotice}
      <Dialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <DialogContent aria-labelledby={dialogTitle} aria-describedby={dialogDescription}>
          <DialogHeader>
            <DialogTitle id={dialogTitle}>Restart Meridian</DialogTitle>
            <DialogDescription id={dialogDescription}>
              This restarts the host for all connected workstations and applies these saved changes.
            </DialogDescription>
          </DialogHeader>
          <ul className="max-h-48 list-disc space-y-1 overflow-auto pl-5 font-mono text-xs">
            {pendingPaths.map((path) => <li className="break-all" key={path}>{path}</li>)}
          </ul>
          <div className="flex justify-end gap-2 pt-4">
            <Button variant="outline" onClick={() => setConfirmOpen(false)}>Cancel</Button>
            <Button onClick={() => void restartHost()}>Restart host</Button>
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
}

function readActivation(signal: AbortSignal) {
  return apiGetJson<ProviderActivationResponse>("/api/providers/activation", {
    signal,
    allowDevelopmentFallback: false
  });
}

async function waitForRestart(previousSession: string, signal: AbortSignal) {
  const deadline = Date.now() + 180_000;
  while (!signal.aborted && Date.now() < deadline) {
    await new Promise<void>((resolve) => setTimeout(resolve, 1_500));
    if (signal.aborted) return null;
    try {
      const next = await readActivation(AbortSignal.any([signal, AbortSignal.timeout(5_000)]));
      if (next.isReady && next.runtimeSessionId && next.runtimeSessionId !== previousSession)
        return next;
    } catch {
      // The previous host may be draining or the new host may still be starting.
    }
  }
  return null;
}
