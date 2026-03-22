import { FlaskConical, Layers3, ShieldAlert, TimerReset } from "lucide-react";
import { useMemo, useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { EntityDataTable } from "@/components/meridian/entity-data-table";
import { MetricCard } from "@/components/meridian/metric-card";
import { RunStatusBadge } from "@/components/meridian/run-status-badge";
import type { ResearchRunRecord, ResearchWorkspaceResponse } from "@/types";

interface ResearchScreenProps {
  data: ResearchWorkspaceResponse | null;
}

export function ResearchScreen({ data }: ResearchScreenProps) {
  const [selectedRun, setSelectedRun] = useState<ResearchRunRecord | null>(null);

  const highlights = useMemo(
    () => [
      {
        title: "Run comparison readiness",
        description: "6 backtests are now normalized onto the shared strategy-run model and can be promoted into Trading.",
        icon: Layers3
      },
      {
        title: "Safety signal",
        description: "Paper/live state styling is active in badges and review surfaces so risky promotions stay obvious.",
        icon: ShieldAlert
      },
      {
        title: "Operator pace",
        description: "Research filters and keyboard navigation are tuned for dense workstation workflows, not marketing layouts.",
        icon: TimerReset
      }
    ],
    []
  );

  if (!data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Loading research workspace</CardTitle>
          <CardDescription>Waiting for workstation bootstrap data from the Meridian host.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <div className="space-y-8">
      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => (
          <MetricCard key={metric.id} {...metric} />
        ))}
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.45fr_0.85fr]">
        <Card>
          <CardHeader>
            <div className="eyebrow-label">Research Lane</div>
            <CardTitle className="flex items-center gap-2">
              <FlaskConical className="h-5 w-5 text-primary" />
              Backtest studio lane
            </CardTitle>
            <CardDescription>
              First vertical slice for the workstation migration: metrics, filters, and run drill-in all live in one shell.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-3">
            {highlights.map((highlight) => (
              <div key={highlight.title} className="rounded-xl border border-border/70 bg-secondary/35 p-4">
                <highlight.icon className="mb-3 h-5 w-5 text-primary" />
                <div className="font-semibold">{highlight.title}</div>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{highlight.description}</p>
              </div>
            ))}
          </CardContent>
        </Card>

        <Card className="bg-panel-strong text-slate-50">
          <CardHeader>
            <div className="eyebrow-label">Migration Notes</div>
            <CardTitle>Workstation delivery notes</CardTitle>
            <CardDescription className="text-slate-300">
              The React shell lives under <code className="rounded bg-white/10 px-1 py-0.5">/workstation</code> and leaves the legacy root dashboard untouched.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4 text-sm leading-6 text-slate-200">
            <div className="rounded-xl bg-white/10 p-4">
              Command palette, focus-trapped drawers, semantic mode badges, and dense comparison tables are all source-owned Meridian wrappers.
            </div>
            <div className="rounded-xl bg-white/10 p-4">
              React Aria remains reserved for future accessibility-heavy widgets if Radix/shadcn primitives stop being enough.
            </div>
          </CardContent>
        </Card>
      </section>

      <EntityDataTable rows={data.runs} onSelectRun={setSelectedRun} />

      {selectedRun ? (
        <Dialog open={selectedRun !== null} onOpenChange={(open) => !open && setSelectedRun(null)}>
          <DialogContent aria-describedby={undefined}>
            <DialogHeader>
              <DialogTitle>{selectedRun.strategyName}</DialogTitle>
              <DialogDescription id="run-detail-description">
                Review strategy-run details, environment mode, and promotion risk before moving into a later workflow.
              </DialogDescription>
            </DialogHeader>

            <div className="mt-6 space-y-6">
              <div className="flex flex-wrap items-center justify-between gap-4">
                <RunStatusBadge status={selectedRun.status} mode={selectedRun.mode} />
                <div className="text-sm text-muted-foreground">Last updated {selectedRun.lastUpdated}</div>
              </div>

              <dl className="grid gap-4 sm:grid-cols-2">
                <Stat label="Engine" value={selectedRun.engine} />
                <Stat label="Dataset" value={selectedRun.dataset} />
                <Stat label="Run window" value={selectedRun.window} />
                <Stat label="P&L" value={selectedRun.pnl} />
                <Stat label="Sharpe" value={selectedRun.sharpe} />
                <Stat label="Run ID" value={selectedRun.id} />
              </dl>

              <div className="rounded-xl border border-border/80 bg-secondary/30 p-4">
                <div className="text-sm font-semibold uppercase tracking-[0.16em] text-muted-foreground">Operator notes</div>
                <p className="mt-3 text-sm leading-6 text-foreground">{selectedRun.notes}</p>
              </div>
            </div>
          </DialogContent>
        </Dialog>
      ) : null}
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-border/70 bg-secondary/30 p-4">
      <dt className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">{label}</dt>
      <dd className="mt-2 font-mono text-sm font-semibold text-foreground">{value}</dd>
    </div>
  );
}
