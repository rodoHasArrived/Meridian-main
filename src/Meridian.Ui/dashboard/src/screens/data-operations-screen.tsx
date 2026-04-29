import { DatabaseZap, Download, Play, RadioTower } from "lucide-react";
import { useLocation } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import { workspaceForPath } from "@/lib/workspace";
import { useDataOperationsViewModel } from "@/screens/data-operations-screen.view-model";
import type { DataOperationsWorkspaceResponse } from "@/types";
import type { BackfillResultCardState, DataOperationsEmptyState } from "@/screens/data-operations-screen.view-model";

interface DataOperationsScreenProps {
  data: DataOperationsWorkspaceResponse | null;
}

export function DataOperationsScreen({ data }: DataOperationsScreenProps) {
  const { pathname } = useLocation();
  const workspace = workspaceForPath(pathname);
  const vm = useDataOperationsViewModel(data, pathname);

  if (!data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Loading {workspace.label}</CardTitle>
          <CardDescription>Waiting for provider posture and backfill queue state.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <div className="space-y-8">
      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => <MetricCard key={metric.id} {...metric} />)}
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.2fr_0.8fr]">
        <Card>
          <CardHeader>
            <div className="eyebrow-label">{workspace.label} Lane</div>
            <CardTitle className="flex items-center gap-2">
              <DatabaseZap className="h-5 w-5 text-primary" />
              {vm.workstream === "backfills" ? "Backfill queue focus" : "Provider health"}
            </CardTitle>
            <CardDescription>Provider posture, backfill execution, and export readiness for the web workstation.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-3">
            <DataHighlight icon={RadioTower} title="Provider health" description="Streaming and historical providers stay visible with latency and review notes." />
            <DataHighlight icon={Play} title="Backfill queue" description="Queued, running, and review-required backfills can be inspected before execution." />
            <DataHighlight icon={Download} title="Recent exports" description="Research packs and governed exports remain visible from the same surface." />
          </CardContent>
        </Card>

        {vm.selectedBackfill && vm.selectedBackfillNarrative ? (
          <Card className="bg-panel-strong text-slate-50">
            <CardHeader>
              <div className="eyebrow-label">Backfill Detail</div>
              <CardTitle>{vm.selectedBackfill.scope}</CardTitle>
              <CardDescription className="text-slate-300">{vm.selectedBackfillNarrative}</CardDescription>
            </CardHeader>
            <CardContent className="space-y-2 text-sm">
              <DetailRow label="Provider" value={vm.selectedBackfill.provider} />
              <DetailRow label="Status" value={vm.selectedBackfill.status} />
              <DetailRow label="Progress" value={vm.selectedBackfill.progress} />
            </CardContent>
          </Card>
        ) : vm.backfillDetailEmptyState ? (
          <Card>
            <CardHeader>
              <div className="eyebrow-label">Backfill Detail</div>
              <CardTitle>{vm.backfillDetailEmptyState.title}</CardTitle>
              <CardDescription>{vm.backfillDetailEmptyState.description}</CardDescription>
            </CardHeader>
          </Card>
        ) : null}
      </section>

      <section className="grid gap-4 xl:grid-cols-3">
        <Card aria-labelledby="data-provider-health-title">
          <CardHeader>
            <CardTitle id="data-provider-health-title">Provider health</CardTitle>
            <CardDescription>Current data-source posture.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {vm.providerSection.hasRows ? vm.providerSection.rows.map((provider) => (
              <div
                key={provider.provider}
                role="group"
                className={cn(
                  "rounded-lg border p-3",
                  provider.statusTone === "danger"
                    ? "border-danger/35 bg-danger/5"
                    : provider.statusTone === "warning"
                      ? "border-warning/35 bg-warning/5"
                      : "border-border/70 bg-secondary/20"
                )}
                aria-label={provider.ariaLabel}
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="font-semibold">{provider.provider}</span>
                  <span
                    className={cn(
                      "font-mono text-xs",
                      provider.statusTone === "danger"
                        ? "text-danger"
                        : provider.statusTone === "warning"
                          ? "text-warning"
                          : "text-success"
                    )}
                  >
                    {provider.status}
                  </span>
                </div>
                <p className="mt-2 text-sm text-muted-foreground">{provider.capability}</p>
                <p className="mt-1 text-xs text-muted-foreground">{provider.note}</p>
                <div className="mt-3 grid grid-cols-2 gap-2" aria-label={`${provider.provider} trust evidence`}>
                  {provider.trustFields.map((field) => (
                    <div key={field.id} className="rounded-md border border-border/60 bg-background/40 px-2.5 py-2">
                      <div className="eyebrow-label">{field.label}</div>
                      <div className="mt-1 truncate font-mono text-xs text-foreground">{field.value}</div>
                    </div>
                  ))}
                </div>
                <div className="mt-3 rounded-md border border-border/60 bg-background/40 px-3 py-2">
                  <div className="eyebrow-label">Recommended action</div>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{provider.recommendedActionText}</p>
                  <p className="mt-2 font-mono text-[11px] text-muted-foreground">Reason: {provider.reasonCodeText}</p>
                </div>
              </div>
            )) : (
              <EmptyState state={vm.providerSection.emptyState} />
            )}
          </CardContent>
        </Card>

        <Card aria-labelledby="data-backfill-queue-title">
          <CardHeader>
            <div className="flex items-center justify-between gap-3">
              <div>
                <CardTitle id="data-backfill-queue-title">Backfill queue</CardTitle>
                <CardDescription>Run or inspect historical repairs.</CardDescription>
              </div>
              <Button size="sm" onClick={vm.openBackfillDialog}>
                Trigger backfill
              </Button>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {vm.backfillSection.hasRows ? vm.backfillSection.rows.map((backfill) => (
              <button
                key={backfill.jobId}
                type="button"
                aria-label={backfill.ariaLabel}
                className={cn(
                  "w-full rounded-lg border px-3 py-3 text-left text-sm",
                  backfill.selected ? "border-primary/50 bg-primary/10" : "border-border/70 bg-secondary/25"
                )}
                onClick={() => vm.selectBackfill(backfill.jobId)}
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="font-mono font-semibold">{backfill.jobId}</span>
                  <span>{backfill.progress}</span>
                </div>
                <p className="mt-1 text-muted-foreground">{backfill.scope}</p>
              </button>
            )) : (
              <EmptyState state={vm.backfillSection.emptyState} />
            )}
          </CardContent>
        </Card>

        <Card aria-labelledby="data-recent-exports-title">
          <CardHeader>
            <CardTitle id="data-recent-exports-title">Recent exports</CardTitle>
            <CardDescription>Latest package and reporting outputs.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {vm.exportSection.hasRows ? vm.exportSection.rows.map((item) => (
              <div
                key={item.exportId}
                role="group"
                className={cn("rounded-md border p-3", exportToneClass[item.statusTone])}
                aria-label={item.ariaLabel}
              >
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <span className="font-semibold">{item.profile}</span>
                    <p className="mt-1 text-sm text-muted-foreground">{item.summaryText}</p>
                  </div>
                  <Badge variant={item.statusVariant} dot>{item.statusLabel}</Badge>
                </div>
                <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                  {item.detailFields.map((field) => (
                    <div key={field.id} className="rounded-md border border-border/60 bg-background/45 px-2.5 py-2">
                      <dt className="eyebrow-label">{field.label}</dt>
                      <dd className="mt-1 truncate font-mono text-xs text-foreground">{field.value}</dd>
                    </div>
                  ))}
                </dl>
                <div className="mt-3 rounded-md border border-border/60 bg-background/45 px-3 py-2">
                  <div className="eyebrow-label">Next action</div>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{item.actionText}</p>
                </div>
              </div>
            )) : (
              <EmptyState state={vm.exportSection.emptyState} />
            )}
          </CardContent>
        </Card>
      </section>

      <Dialog open={vm.dialogOpen} onOpenChange={(open) => { if (!open) vm.closeBackfillDialog(); }}>
        <DialogContent aria-labelledby="backfill-dialog-title" aria-describedby="backfill-dialog-description">
          <div className="flex items-start justify-between gap-4">
            <DialogHeader className="mb-0">
              <div className="eyebrow-label">Backfill</div>
              <DialogTitle id="backfill-dialog-title">Trigger backfill</DialogTitle>
              <DialogDescription id="backfill-dialog-description">
                Preview the request before writing historical bars.
              </DialogDescription>
            </DialogHeader>
            <Button variant="ghost" size="sm" onClick={vm.closeBackfillDialog} disabled={vm.busy}>Close</Button>
          </div>

          <div className="mt-5 grid gap-3">
            <label htmlFor="backfill-provider" className="grid gap-1 text-sm">
              Provider
              <input
                id="backfill-provider"
                className="rounded-md border border-border bg-background px-3 py-2"
                value={vm.form.provider}
                onChange={(event) => vm.updateBackfillForm("provider", event.target.value)}
              />
            </label>
            <label htmlFor="backfill-symbols" className="grid gap-1 text-sm">
              Symbols
              <input
                id="backfill-symbols"
                className="rounded-md border border-border bg-background px-3 py-2"
                placeholder="AAPL MSFT SPY"
                value={vm.form.symbols}
                aria-describedby="backfill-symbols-help backfill-form-feedback"
                aria-invalid={vm.validationError !== null}
                onChange={(event) => vm.updateBackfillForm("symbols", event.target.value)}
              />
              <span id="backfill-symbols-help" className="text-xs text-muted-foreground">{vm.symbolsHelpText}</span>
            </label>
            <div className="grid gap-3 md:grid-cols-2">
              <label htmlFor="backfill-from" className="grid gap-1 text-sm">
                From
                <input
                  id="backfill-from"
                  type="date"
                  className="rounded-md border border-border bg-background px-3 py-2"
                  value={vm.form.from}
                  onChange={(event) => vm.updateBackfillForm("from", event.target.value)}
                />
              </label>
              <label htmlFor="backfill-to" className="grid gap-1 text-sm">
                To
                <input
                  id="backfill-to"
                  type="date"
                  className="rounded-md border border-border bg-background px-3 py-2"
                  value={vm.form.to}
                  onChange={(event) => vm.updateBackfillForm("to", event.target.value)}
                />
              </label>
            </div>
          </div>

          {vm.feedbackText && (
            <div
              id="backfill-form-feedback"
              role="alert"
              className={cn(
                "mt-4 rounded-lg border px-3 py-2 text-sm",
                vm.feedbackTone === "warning"
                  ? "border-warning/40 bg-warning/10 text-warning"
                  : "border-danger/40 bg-danger/10 text-danger"
              )}
            >
              {vm.feedbackText}
            </div>
          )}
          <span className="sr-only" aria-live="polite">{vm.statusAnnouncement}</span>
          {vm.previewResultCard && <BackfillResultCard state={vm.previewResultCard} />}
          {vm.runResultCard && <BackfillResultCard state={vm.runResultCard} />}

          <div className="mt-5 flex justify-end gap-2">
            <Button variant="outline" onClick={() => void vm.previewBackfill()} disabled={!vm.canPreview}>
              {vm.previewButtonLabel}
            </Button>
            {vm.preview && (
              <Button onClick={() => void vm.runBackfill()} disabled={!vm.canRun}>
                {vm.runButtonLabel}
              </Button>
            )}
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function EmptyState({ state }: { state: DataOperationsEmptyState }) {
  return (
    <div role="status" className="rounded-lg border border-dashed border-border/80 bg-secondary/20 px-3 py-4">
      <div className="text-sm font-semibold">{state.title}</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{state.description}</p>
    </div>
  );
}

function DataHighlight({ icon: Icon, title, description }: { icon: typeof DatabaseZap; title: string; description: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-secondary/30 p-4">
      <Icon className="mb-3 h-5 w-5 text-primary" />
      <div className="font-semibold">{title}</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{description}</p>
    </div>
  );
}

function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-lg border border-border/70 bg-secondary/40 px-3 py-2">
      <span className="text-slate-300">{label}</span>
      <span className="font-mono text-slate-50">{value}</span>
    </div>
  );
}

const resultToneClass: Record<BackfillResultCardState["tone"], string> = {
  warning: "border-warning/35 bg-warning/10 text-warning",
  success: "border-success/35 bg-success/10 text-success",
  danger: "border-danger/35 bg-danger/10 text-danger"
};

const exportToneClass = {
  success: "border-success/30 bg-success/5",
  warning: "border-warning/30 bg-warning/5",
  paper: "border-paper/30 bg-paper/5"
} as const;

function BackfillResultCard({ state }: { state: BackfillResultCardState }) {
  return (
    <div
      role="status"
      aria-label={state.ariaLabel}
      className={cn("mt-4 rounded-md border p-3 text-sm", resultToneClass[state.tone])}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="font-semibold text-foreground">{state.title}</div>
        <div className="font-mono text-xs">{state.statusLabel}</div>
      </div>
      <dl className="mt-3 grid gap-2 sm:grid-cols-2">
        {state.rows.map((row) => (
          <div key={row.id} className="rounded-md border border-border/60 bg-background/45 px-2.5 py-2">
            <dt className="eyebrow-label">{row.label}</dt>
            <dd className="mt-1 font-mono text-xs text-foreground">{row.value}</dd>
          </div>
        ))}
      </dl>
      {state.errorText && <p className="mt-3 text-xs leading-5">{state.errorText}</p>}
    </div>
  );
}
