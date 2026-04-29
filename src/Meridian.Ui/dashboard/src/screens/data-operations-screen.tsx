import { DatabaseZap, Download, Play, RadioTower } from "lucide-react";
import { useLocation } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import { useDataOperationsViewModel } from "@/screens/data-operations-screen.view-model";
import type { BackfillTriggerResult, DataOperationsWorkspaceResponse } from "@/types";

interface DataOperationsScreenProps {
  data: DataOperationsWorkspaceResponse | null;
}

export function DataOperationsScreen({ data }: DataOperationsScreenProps) {
  const { pathname } = useLocation();
  const vm = useDataOperationsViewModel(data, pathname);

  if (!data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Loading Data Operations</CardTitle>
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
            <div className="eyebrow-label">Data Operations Lane</div>
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

        {vm.selectedBackfill && vm.selectedBackfillNarrative && (
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
        )}
      </section>

      <section className="grid gap-4 xl:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle>Provider health</CardTitle>
            <CardDescription>Current data-source posture.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {data.providers.map((provider) => (
              <div key={provider.provider} className="rounded-lg border border-border/70 p-3">
                <div className="flex items-center justify-between gap-3">
                  <span className="font-semibold">{provider.provider}</span>
                  <span className={cn("font-mono text-xs", provider.status === "Healthy" ? "text-success" : "text-warning")}>{provider.status}</span>
                </div>
                <p className="mt-2 text-sm text-muted-foreground">{provider.capability}</p>
                <p className="mt-1 text-xs text-muted-foreground">{provider.note}</p>
              </div>
            ))}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <div className="flex items-center justify-between gap-3">
              <div>
                <CardTitle>Backfill queue</CardTitle>
                <CardDescription>Run or inspect historical repairs.</CardDescription>
              </div>
              <Button size="sm" onClick={vm.openBackfillDialog}>
                Trigger backfill
              </Button>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {data.backfills.map((backfill) => (
              <button
                key={backfill.jobId}
                type="button"
                className={cn(
                  "w-full rounded-lg border px-3 py-3 text-left text-sm",
                  vm.selectedBackfill?.jobId === backfill.jobId ? "border-primary/50 bg-primary/10" : "border-border/70 bg-secondary/25"
                )}
                onClick={() => vm.selectBackfill(backfill.jobId)}
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="font-mono font-semibold">{backfill.jobId}</span>
                  <span>{backfill.progress}</span>
                </div>
                <p className="mt-1 text-muted-foreground">{backfill.scope}</p>
              </button>
            ))}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Recent exports</CardTitle>
            <CardDescription>Latest package and reporting outputs.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {data.exports.map((item) => (
              <div key={item.exportId} className="rounded-lg border border-border/70 p-3">
                <div className="flex items-center justify-between gap-3">
                  <span className="font-semibold">{item.profile}</span>
                  <span className="font-mono text-xs">{item.status}</span>
                </div>
                <p className="mt-1 text-sm text-muted-foreground">{item.target} · {item.rows}</p>
              </div>
            ))}
          </CardContent>
        </Card>
      </section>

      <Dialog open={vm.dialogOpen}>
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
          {vm.preview && <BackfillResult title={`Preview — ${vm.preview.provider}`} result={vm.preview} />}
          {vm.result && <BackfillResult title={`Backfill complete — ${vm.result.provider}`} result={vm.result} />}

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
    <div className="flex items-center justify-between gap-4 rounded-lg bg-white/10 px-3 py-2">
      <span className="text-slate-300">{label}</span>
      <span className="font-mono text-slate-50">{value}</span>
    </div>
  );
}

function BackfillResult({ title, result }: { title: string; result: BackfillTriggerResult }) {
  return (
    <div className="mt-4 rounded-lg border border-primary/30 bg-primary/10 p-3 text-sm">
      <div className="font-semibold">{title}</div>
      <div className="mt-2 grid grid-cols-2 gap-2 font-mono text-xs">
        <span>Symbols: {result.symbols.join(", ")}</span>
        <span>Bars: {result.barsWritten.toLocaleString()}</span>
      </div>
    </div>
  );
}
