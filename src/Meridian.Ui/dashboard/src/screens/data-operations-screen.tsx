import { DatabaseZap, Download, RadioTower, RefreshCcw } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { useLocation } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import type { DataOperationsWorkspaceResponse } from "@/types";

interface DataOperationsScreenProps {
  data: DataOperationsWorkspaceResponse | null;
}

const providerTone: Record<DataOperationsWorkspaceResponse["providers"][number]["status"], string> = {
  Healthy: "text-success",
  Warning: "text-warning",
  Degraded: "text-danger"
};

const workstreamCopy: Record<string, { title: string; description: string }> = {
  providers: {
    title: "Provider health focus",
    description: "Live provider heartbeat, latency, and capability health stay visible while the broader workstation shell remains responsive."
  },
  backfills: {
    title: "Backfill queue focus",
    description: "Prioritize queue movement, long-running replay jobs, and review-required gaps before they spill into research workflows."
  },
  exports: {
    title: "Export delivery focus",
    description: "Monitor operational export targets and handoff readiness without leaving the workstation shell."
  }
};

export function DataOperationsScreen({ data }: DataOperationsScreenProps) {
  const { pathname } = useLocation();
  const workstream = useMemo(() => {
    if (pathname.includes("/providers")) {
      return "providers";
    }

    if (pathname.includes("/backfills")) {
      return "backfills";
    }

    if (pathname.includes("/exports")) {
      return "exports";
    }

    return "providers";
  }, [pathname]);
  const [selectedBackfillJobId, setSelectedBackfillJobId] = useState<string | null>(null);
  const selectedBackfill = data?.backfills.find((item) => item.jobId === selectedBackfillJobId) ?? data?.backfills[0] ?? null;

  useEffect(() => {
    if (!data || selectedBackfillJobId || data.backfills.length === 0) {
      return;
    }

    setSelectedBackfillJobId(data.backfills[0].jobId);
  }, [data, selectedBackfillJobId]);

  if (!data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Loading Data Operations</CardTitle>
          <CardDescription>Waiting for provider, backfill, and export summaries from the workstation bootstrap payload.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const focus = workstreamCopy[workstream];

  return (
    <div className="space-y-8">
      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => (
          <MetricCard key={metric.id} {...metric} />
        ))}
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
        <Card>
          <CardHeader>
            <div className="eyebrow-label">Operations Lane</div>
            <CardTitle className="flex items-center gap-2">
              <RefreshCcw className="h-5 w-5 text-primary" />
              {focus.title}
            </CardTitle>
            <CardDescription>{focus.description}</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-3">
            <OperationsHighlight
              icon={RadioTower}
              title="Provider state"
              description="Keep providers observable with latency, capability, and narrative health notes."
            />
            <OperationsHighlight
              icon={DatabaseZap}
              title="Replay pressure"
              description="Backfill summaries keep replay risk visible before it blocks research or trading workflows."
            />
            <OperationsHighlight
              icon={Download}
              title="Export handoff"
              description="Operational exports remain visible so governance and downstream consumers are not surprised."
            />
          </CardContent>
        </Card>

        <Card className="bg-panel-strong text-slate-50">
          <CardHeader>
            <div className="eyebrow-label">Route Context</div>
            <CardTitle>Current workstream</CardTitle>
            <CardDescription className="text-slate-300">
              Deep links under <code className="rounded bg-white/10 px-1 py-0.5">{pathname}</code> reuse the same prefetched summary payload.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3 text-sm text-slate-200">
            <ContextRow label="Providers tracked" value={String(data.providers.length)} />
            <ContextRow label="Backfill jobs" value={String(data.backfills.length)} />
            <ContextRow label="Export jobs" value={String(data.exports.length)} />
          </CardContent>
        </Card>
      </section>

      {workstream === "backfills" && selectedBackfill ? (
        <section className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <DatabaseZap className="h-4 w-4 text-primary" />
                Backfill queue detail
              </CardTitle>
              <CardDescription>Select a queued job to inspect its operational detail panel.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              {data.backfills.map((backfill) => (
                <button
                  key={backfill.jobId}
                  type="button"
                  onClick={() => setSelectedBackfillJobId(backfill.jobId)}
                  className={cn(
                    "w-full rounded-xl border px-4 py-4 text-left transition-colors",
                    backfill.jobId === selectedBackfill.jobId
                      ? "border-primary/50 bg-primary/10"
                      : "border-border/70 bg-secondary/30 hover:bg-secondary/45"
                  )}
                >
                  <div className="flex items-center justify-between gap-3">
                    <div className="font-semibold">{backfill.jobId}</div>
                    <div className="font-mono text-xs uppercase tracking-[0.16em] text-primary">{backfill.status}</div>
                  </div>
                  <div className="mt-2 text-sm text-muted-foreground">{backfill.scope}</div>
                  <div className="mt-3 flex items-center justify-between gap-4 text-sm">
                    <span className="text-muted-foreground">{backfill.provider}</span>
                    <span className="font-mono">{backfill.progress}</span>
                  </div>
                </button>
              ))}
            </CardContent>
          </Card>

          <Card className="bg-panel-strong text-slate-50">
            <CardHeader>
              <div className="eyebrow-label">Backfill Detail</div>
              <CardTitle>{selectedBackfill.jobId}</CardTitle>
              <CardDescription className="text-slate-300">{selectedBackfill.scope}</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4 text-sm">
              <DetailRow label="Provider" value={selectedBackfill.provider} />
              <DetailRow label="Status" value={selectedBackfill.status} />
              <DetailRow label="Progress" value={selectedBackfill.progress} />
              <DetailRow label="Last updated" value={selectedBackfill.updatedAt} />
              <div className="rounded-xl bg-white/10 p-4 text-slate-200">
                {buildBackfillNarrative(selectedBackfill)}
              </div>
              <div className="flex gap-3">
                <Button variant="secondary">Inspect checkpoints</Button>
                <Button variant="outline" className="border-white/20 bg-transparent text-slate-50 hover:bg-white/10">
                  Review queue prerequisites
                </Button>
              </div>
            </CardContent>
          </Card>
        </section>
      ) : null}

      <section className="grid gap-4 xl:grid-cols-3">
        <Card className="xl:col-span-1">
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <RadioTower className="h-4 w-4 text-primary" />
              Provider health
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {data.providers.map((provider) => (
              <div key={provider.provider} className="rounded-xl border border-border/70 bg-secondary/30 p-4">
                <div className="flex items-center justify-between gap-3">
                  <div className="font-semibold">{provider.provider}</div>
                  <div className={cn("font-mono text-xs uppercase tracking-[0.16em]", providerTone[provider.status])}>{provider.status}</div>
                </div>
                <div className="mt-2 text-sm text-muted-foreground">{provider.capability}</div>
                <div className="mt-3 flex items-center justify-between gap-4 text-sm">
                  <span className="text-muted-foreground">Latency</span>
                  <span className="font-mono">{provider.latency}</span>
                </div>
                <div className="mt-3 text-sm leading-6 text-foreground">{provider.note}</div>
              </div>
            ))}
          </CardContent>
        </Card>

        <Card className="xl:col-span-1">
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <DatabaseZap className="h-4 w-4 text-primary" />
              Backfill queue
            </CardTitle>
          </CardHeader>
          <CardContent>
            <CompactTable
              columns={["Job", "Scope", "Provider", "Status", "Progress", "Updated"]}
              rows={data.backfills.map((backfill) => [
                backfill.jobId,
                backfill.scope,
                backfill.provider,
                backfill.status,
                backfill.progress,
                backfill.updatedAt
              ])}
            />
          </CardContent>
        </Card>

        <Card className="xl:col-span-1">
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Download className="h-4 w-4 text-primary" />
              Recent exports
            </CardTitle>
          </CardHeader>
          <CardContent>
            <CompactTable
              columns={["Export", "Profile", "Target", "Status", "Rows", "Updated"]}
              rows={data.exports.map((item) => [
                item.exportId,
                item.profile,
                item.target,
                item.status,
                item.rows,
                item.updatedAt
              ])}
            />
          </CardContent>
        </Card>
      </section>
    </div>
  );
}

function OperationsHighlight({
  icon: Icon,
  title,
  description
}: {
  icon: typeof RefreshCcw;
  title: string;
  description: string;
}) {
  return (
    <div className="rounded-xl border border-border/70 bg-secondary/35 p-4">
      <Icon className="mb-3 h-5 w-5 text-primary" />
      <div className="font-semibold">{title}</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{description}</p>
    </div>
  );
}

function ContextRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-lg bg-white/10 px-3 py-2">
      <span className="text-slate-300">{label}</span>
      <span className="font-mono text-slate-50">{value}</span>
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

function buildBackfillNarrative(backfill: DataOperationsWorkspaceResponse["backfills"][number]) {
  if (backfill.status === "Running") {
    return `Replay is currently advancing for ${backfill.scope}. Keep provider latency and downstream export pressure in view until the queue clears.`;
  }

  if (backfill.status === "Review") {
    return `This job is waiting on operator review before it can be promoted as complete. Check the final checkpoint, symbol coverage, and export dependencies.`;
  }

  return `This job is queued behind currently active operational work. Confirm provider readiness and symbol coverage before it becomes the next replay candidate.`;
}

function CompactTable({ columns, rows }: { columns: string[]; rows: string[][] }) {
  return (
    <div className="overflow-x-auto rounded-xl border border-border/70">
      <table className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
        <thead className="bg-secondary/30">
          <tr>
            {columns.map((column) => (
              <th key={column} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-border/50">
          {rows.map((row, rowIndex) => (
            <tr key={`row-${rowIndex}`} className="bg-background/20">
              {row.map((value, valueIndex) => (
                <td key={`cell-${rowIndex}-${valueIndex}`} className="px-3 py-2 font-mono text-foreground">
                  {value}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
