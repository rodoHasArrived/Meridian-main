import { AlertTriangle, RefreshCcw, TimerReset } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import {
  useProviderAccountingPanel,
  type ProviderAccountingServices
} from "@/screens/data-screen.provider-accounting.view-model";

export function ProviderAccountingRegion({ services }: { services?: ProviderAccountingServices }) {
  const { panel, requestStatus, refresh } = useProviderAccountingPanel(services);
  const registrationVariant = panel.registrationTone === "danger"
    ? "danger"
    : panel.registrationTone === "warning" ? "warning" : "success";

  return (
    <section
      aria-labelledby="data-provider-accounting-title"
      className="workspace-region"
      data-testid="provider-accounting-region"
    >
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div className="eyebrow-label">Runtime evidence</div>
            <CardTitle id="data-provider-accounting-title" className="mt-2 flex items-center gap-2">
              <TimerReset className="h-5 w-5 text-primary" aria-hidden="true" />
              Provider registration and rate limits
            </CardTitle>
            <CardDescription className="mt-2">
              Current server-owned registration failures, request-window usage, reset posture, and retry guidance.
            </CardDescription>
          </div>
          <Button
            size="sm"
            variant="outline"
            onClick={() => void refresh()}
            disabled={requestStatus.inFlight}
            disabledReason={requestStatus.inFlight ? "Provider runtime evidence is already refreshing." : null}
            busy={requestStatus.inFlight}
            aria-label="Refresh provider registration and rate-limit evidence"
          >
            <RefreshCcw className={cn("h-3.5 w-3.5", requestStatus.inFlight && "animate-spin")} aria-hidden="true" />
            Refresh evidence
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div role="status" aria-live="polite" className="text-xs text-muted-foreground">
          {requestStatus.message}
        </div>

        <div className="rounded-md border border-border/70 bg-secondary/20 p-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <div className="text-sm font-semibold text-foreground">{panel.registrationTitle}</div>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">{panel.registrationSummary}</p>
            </div>
            <Badge variant={registrationVariant}>{panel.registrationTitle}</Badge>
          </div>

          {panel.registrationFailures.length > 0 ? (
            <ul className="mt-3 grid gap-2" aria-label="Provider registration failures">
              {panel.registrationFailures.map((failure) => (
                <li key={failure.id} className="rounded border border-danger/30 bg-danger/5 p-2 text-xs">
                  <div className="flex items-start gap-2 font-semibold text-danger">
                    <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                    <span>{failure.stage}: {failure.module}</span>
                  </div>
                  <div className="mt-1 font-mono text-foreground">{failure.subject}</div>
                  <div className="mt-1 text-muted-foreground">{failure.error}</div>
                </li>
              ))}
            </ul>
          ) : null}
        </div>

        <div>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <h3 className="text-sm font-semibold text-foreground">Current request windows</h3>
              <p className="mt-1 text-xs text-muted-foreground">{panel.rateLimitSummary}</p>
            </div>
            <Badge variant="outline">History unavailable</Badge>
          </div>

          {panel.rateLimits.length > 0 ? (
            <div className="mt-3 overflow-x-auto rounded-md border border-border/70">
              <TableWorkbench tableClassName={"w-full min-w-[920px] text-left text-xs"} maxVisibleRows={null}  workbenchId="data-provider-rate-limits" sourceKey={panel.rateLimits.map((row) => row.id).join("|")}
                sourceDescription="Complete current provider rate-limit snapshot; no history is inferred." completePopulation
                rows={panel.rateLimits} getRowId={(row) => row.id} getSearchText={(row) => `${row.provider} ${row.surface} ${row.status} ${row.failureReason}`}
                ariaLabel="Current provider rate-limit state" emptyText="No current rate-limit evidence."
                columns={[
                  { id: "provider", label: "Provider / surface", render: (row) => <><strong>{row.provider}</strong><span className="block text-muted-foreground">{row.surface}</span></>, exportValue: (row) => `${row.source.provider}; ${row.source.surface}` },
                  { id: "status", label: "Status", render: (row) => <Badge variant={row.statusTone}>{row.status}</Badge>, exportValue: (row) => row.source.status },
                  { id: "connection", label: "Connection / reachability", render: (row) => row.connectionPosture, exportValue: (row) => row.connectionPosture },
                  { id: "requests", label: "Requests", render: (row) => row.requestUsage, exportValue: (row) => row.source.requestsInWindow },
                  { id: "remaining", label: "Remaining", render: (row) => row.remaining, exportValue: (row) => row.source.remainingRequests },
                  { id: "reset", label: "Reset countdown", render: (row) => row.resetCountdown, exportValue: (row) => row.source.resetAt },
                  { id: "reason", label: "Failure / reason", render: (row) => row.failureReason, exportValue: (row) => row.source.reason },
                  { id: "retry", label: "Retry posture", render: (row) => row.retryPosture, exportValue: (row) => row.retryPosture }
                ]} />
            </div>
          ) : (
            <div className="mt-3 rounded-md border border-dashed border-border p-3 text-xs text-muted-foreground">
              No current provider rate-limit rows are available. Meridian will not infer request capacity from configuration alone.
            </div>
          )}
          <p className="mt-2 text-xs text-muted-foreground" aria-label="Provider rate-limit history posture">
            History: {panel.historyPosture}
          </p>
        </div>
      </CardContent>
    </section>
  );
}
