import { AlertCircle, CheckCircle, CheckCircle2, DatabaseZap, Download, Filter, Plus, RadioTower, RefreshCcw, Search, Tag, XCircle } from "lucide-react";
import React, { useEffect, useMemo, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import {
  acknowledgeAnomaly,
  addSymbol,
  getBackfillProgress,
  getQualityAnomalies,
  getQualityDashboard,
  getSymbols,
  getSymbolsStatistics,
  previewBackfill,
  removeSymbol,
  triggerBackfill
} from "@/lib/api";
import { cn } from "@/lib/utils";
import type {
  BackfillProgressResponse,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  DataOperationsWorkspaceResponse,
  QualityAnomalyEntry,
  QualityDashboardResponse,
  SymbolRecord,
  SymbolStatistics
} from "@/types";

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
  },
  symbols: {
    title: "Symbol subscriptions",
    description: "Manage the active symbol universe: add, remove, or archive symbols and review per-symbol data coverage."
  },
  quality: {
    title: "Data quality monitoring",
    description: "Track completeness scores, data gaps, and anomalies across all monitored symbols."
  }
};

// --- Trigger backfill dialog state ---

type TriggerPhase = "idle" | "previewing" | "previewed" | "running" | "done" | "error";

interface TriggerState {
  phase: TriggerPhase;
  symbolsInput: string;
  provider: string;
  from: string;
  to: string;
  preview: BackfillTriggerResult | null;
  result: BackfillTriggerResult | null;
  error: string | null;
}

const INITIAL_TRIGGER: TriggerState = {
  phase: "idle",
  symbolsInput: "",
  provider: "",
  from: "",
  to: "",
  preview: null,
  result: null,
  error: null
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

    if (pathname.includes("/symbols")) {
      return "symbols";
    }

    if (pathname.includes("/quality")) {
      return "quality";
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

  // --- Trigger backfill state ---
  const [triggerOpen, setTriggerOpen] = useState(false);
  const [trigger, setTrigger] = useState<TriggerState>(INITIAL_TRIGGER);

  // --- Live progress polling ---
  const [progress, setProgress] = useState<BackfillProgressResponse | null>(null);
  const progressIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    if (trigger.phase !== "running") {
      if (progressIntervalRef.current) {
        clearInterval(progressIntervalRef.current);
        progressIntervalRef.current = null;
      }
      return;
    }

    progressIntervalRef.current = setInterval(() => {
      getBackfillProgress()
        .then(setProgress)
        .catch((err) => {
          console.warn("Backfill progress poll failed:", err instanceof Error ? err.message : err);
        });
    }, 2000);

    return () => {
      if (progressIntervalRef.current) {
        clearInterval(progressIntervalRef.current);
      }
    };
  }, [trigger.phase]);

  function openTrigger() {
    setTrigger(INITIAL_TRIGGER);
    setProgress(null);
    setTriggerOpen(true);
  }

  function closeTrigger() {
    if (trigger.phase === "running") return;
    setTriggerOpen(false);
    setTrigger(INITIAL_TRIGGER);
    setProgress(null);
  }

  function buildRequest(): BackfillTriggerRequest {
    return {
      provider: trigger.provider.trim() || null,
      symbols: parseSymbols(trigger.symbolsInput),
      from: trigger.from.trim() || null,
      to: trigger.to.trim() || null
    };
  }

  async function handlePreview() {
    setTrigger((prev) => ({ ...prev, phase: "previewing", error: null }));
    try {
      const preview = await previewBackfill(buildRequest());
      setTrigger((prev) => ({ ...prev, phase: "previewed", preview }));
    } catch (err) {
      setTrigger((prev) => ({
        ...prev,
        phase: "error",
        error: err instanceof Error ? err.message : "Preview failed."
      }));
    }
  }

  async function handleRun() {
    setTrigger((prev) => ({ ...prev, phase: "running", error: null }));
    try {
      const result = await triggerBackfill(buildRequest());
      setTrigger((prev) => ({ ...prev, phase: "done", result }));
    } catch (err) {
      setTrigger((prev) => ({
        ...prev,
        phase: "error",
        error: err instanceof Error ? err.message : "Backfill failed."
      }));
    }
  }

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
              <CardTitle className="flex items-center justify-between gap-2 text-base">
                <span className="flex items-center gap-2">
                  <DatabaseZap className="h-4 w-4 text-primary" />
                  Backfill queue detail
                </span>
                <Button size="sm" onClick={openTrigger}>
                  Trigger backfill
                </Button>
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
                <Button variant="secondary" onClick={openTrigger}>
                  Trigger new backfill
                </Button>
                <Button variant="outline" className="border-white/20 bg-transparent text-slate-50 hover:bg-white/10" onClick={openTrigger}>
                  Trigger backfill from this scope
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
            <CardTitle className="flex items-center justify-between gap-2 text-base">
              <span className="flex items-center gap-2">
                <DatabaseZap className="h-4 w-4 text-primary" />
                Backfill queue
              </span>
              <Button size="sm" variant="outline" onClick={openTrigger}>
                Trigger backfill
              </Button>
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

      <TriggerBackfillDialog
        open={triggerOpen}
        trigger={trigger}
        progress={progress}
        onClose={closeTrigger}
        onFieldChange={(field, value) =>
          setTrigger((prev) => ({ ...prev, [field]: value }))
        }
        onPreview={handlePreview}
        onRun={handleRun}
      />

      {workstream === "symbols" && <SymbolsPanel />}
      {workstream === "quality" && <QualityPanel />}
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

function parseSymbols(input: string): string[] {
  return input
    .split(/[\s,]+/)
    .map((s) => s.trim().toUpperCase())
    .filter(Boolean);
}

// --- Trigger Backfill Dialog ---

interface TriggerBackfillDialogProps {
  open: boolean;
  trigger: TriggerState;
  progress: BackfillProgressResponse | null;
  onClose: () => void;
  onFieldChange: (field: keyof Pick<TriggerState, "symbolsInput" | "provider" | "from" | "to">, value: string) => void;
  onPreview: () => void;
  onRun: () => void;
}

function TriggerBackfillDialog({
  open,
  trigger,
  progress,
  onClose,
  onFieldChange,
  onPreview,
  onRun
}: TriggerBackfillDialogProps) {
  const { phase, symbolsInput, provider, from, to, preview, result, error } = trigger;
  const busy = phase === "previewing" || phase === "running";
  const isDone = phase === "done";
  const canPreview = phase === "idle" || phase === "previewed" || phase === "error";
  const canRun = phase === "previewed";

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Trigger backfill</DialogTitle>
          <DialogDescription>
            Enter symbols, an optional provider, and an optional date range. Preview first to see what will be fetched before committing.
          </DialogDescription>
        </DialogHeader>

        {!isDone ? (
          <div className="space-y-4">
            <LabeledField label="Symbols (comma or space separated)">
              <input
                className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-primary/50"
                placeholder="AAPL MSFT SPY"
                value={symbolsInput}
                onChange={(e) => onFieldChange("symbolsInput", e.target.value)}
                disabled={busy}
              />
            </LabeledField>

            <LabeledField label="Provider (optional — leave blank for default chain)">
              <input
                className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-primary/50"
                placeholder="e.g. polygon"
                value={provider}
                onChange={(e) => onFieldChange("provider", e.target.value)}
                disabled={busy}
              />
            </LabeledField>

            <div className="grid grid-cols-2 gap-3">
              <LabeledField label="From (YYYY-MM-DD)">
                <input
                  type="date"
                  className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-primary/50"
                  value={from}
                  onChange={(e) => onFieldChange("from", e.target.value)}
                  disabled={busy}
                />
              </LabeledField>
              <LabeledField label="To (YYYY-MM-DD)">
                <input
                  type="date"
                  className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-primary/50"
                  value={to}
                  onChange={(e) => onFieldChange("to", e.target.value)}
                  disabled={busy}
                />
              </LabeledField>
            </div>

            {error ? (
              <div className="flex items-center gap-2 rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
                <XCircle className="h-4 w-4 shrink-0" />
                {error}
              </div>
            ) : null}

            {preview && phase === "previewed" ? (
              <div className="rounded-lg border border-primary/30 bg-primary/10 p-4 text-sm space-y-2">
                <div className="font-semibold text-primary">Preview — {preview.provider}</div>
                <div className="text-muted-foreground">Symbols: <span className="font-mono">{preview.symbols.join(", ")}</span></div>
                {preview.from ? <div className="text-muted-foreground">From: <span className="font-mono">{preview.from}</span></div> : null}
                {preview.to ? <div className="text-muted-foreground">To: <span className="font-mono">{preview.to}</span></div> : null}
                <div className="text-muted-foreground">Estimated bars: <span className="font-mono">{preview.barsWritten.toLocaleString()}</span></div>
              </div>
            ) : null}

            {phase === "running" && progress ? (
              <div className="rounded-lg border border-border/70 bg-secondary/30 p-4 text-sm space-y-2">
                <div className="font-semibold">Running — {progress.provider ?? "…"}</div>
                {progress.symbols.map((entry) => (
                  <div key={entry.symbol} className="flex items-center justify-between gap-2 font-mono text-xs">
                    <span>{entry.symbol}</span>
                    <span>{entry.completed ? "✓" : `${entry.barsWritten.toLocaleString()} bars`}</span>
                  </div>
                ))}
              </div>
            ) : null}

            <div className="flex justify-end gap-3 pt-2">
              <Button variant="outline" onClick={onClose} disabled={busy}>
                Cancel
              </Button>
              {canPreview ? (
                <Button variant="secondary" onClick={onPreview} disabled={busy || !symbolsInput.trim()}>
                  {busy ? "Previewing…" : "Preview"}
                </Button>
              ) : null}
              {canRun ? (
                <Button onClick={onRun} disabled={busy}>
                  {busy ? "Running…" : "Run backfill"}
                </Button>
              ) : null}
            </div>
          </div>
        ) : (
          <div className="space-y-4">
            {result?.success ? (
              <div className="flex flex-col gap-2 rounded-lg border border-success/30 bg-success/10 px-4 py-4 text-sm text-success">
                <div className="flex items-center gap-2 font-semibold">
                  <CheckCircle className="h-4 w-4 shrink-0" />
                  Backfill complete — {result.provider}
                </div>
                <div>Symbols: <span className="font-mono">{result.symbols.join(", ")}</span></div>
                <div>Bars written: <span className="font-mono">{result.barsWritten.toLocaleString()}</span></div>
              </div>
            ) : (
              <div className="flex items-center gap-2 rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
                <XCircle className="h-4 w-4 shrink-0" />
                {result?.error ?? "Backfill failed."}
              </div>
            )}
            <div className="flex justify-end pt-2">
              <Button variant="outline" onClick={onClose}>Close</Button>
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

function LabeledField({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1.5">
      <label className="text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">{label}</label>
      {children}
    </div>
  );
}

// --- Symbols Panel ---

const symbolStatusBadgeClass: Record<SymbolRecord["status"], string> = {
  Active: "border-success/40 text-success",
  Monitored: "border-blue-400/40 text-blue-400",
  Archived: "border-border text-muted-foreground",
  Error: "border-danger/40 text-danger"
};

function SymbolsPanel() {
  const [symbols, setSymbols] = useState<SymbolRecord[] | null>(null);
  const [stats, setStats] = useState<SymbolStatistics | null>(null);
  const [loading, setLoading] = useState(true);
  const [searchQuery, setSearchQuery] = useState("");
  const [addOpen, setAddOpen] = useState(false);
  const [addSymbolInput, setAddSymbolInput] = useState("");
  const [addProvider, setAddProvider] = useState("");
  const [addBusy, setAddBusy] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);
  const [removeTarget, setRemoveTarget] = useState<string | null>(null);

  async function loadData() {
    setLoading(true);
    try {
      const [syms, st] = await Promise.all([getSymbols(), getSymbolsStatistics()]);
      setSymbols(syms);
      setStats(st);
    } catch {
      setSymbols([]);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void loadData(); }, []);

  const filtered = useMemo(() => {
    if (!symbols) return [];
    const q = searchQuery.trim().toLowerCase();
    return q ? symbols.filter((s) => s.symbol.toLowerCase().includes(q)) : symbols;
  }, [symbols, searchQuery]);

  async function handleAdd() {
    if (!addSymbolInput.trim()) return;
    setAddBusy(true);
    setAddError(null);
    try {
      await addSymbol(addSymbolInput.trim().toUpperCase(), addProvider.trim() || undefined);
      setAddOpen(false);
      setAddSymbolInput("");
      setAddProvider("");
      await loadData();
    } catch (err) {
      setAddError(err instanceof Error ? err.message : "Failed to add symbol.");
    } finally {
      setAddBusy(false);
    }
  }

  async function handleRemove(symbol: string) {
    setRemoveTarget(symbol);
    try {
      await removeSymbol(symbol);
      await loadData();
    } catch {
      // ignore
    } finally {
      setRemoveTarget(null);
    }
  }

  return (
    <section className="space-y-4">
      {/* Stats row */}
      {stats && (
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
          <StatPill label="Total" value={stats.totalSymbols} />
          <StatPill label="Monitored" value={stats.monitoredSymbols} />
          <StatPill label="Archived" value={stats.archivedSymbols} />
          <StatPill label="Errors" value={stats.symbolsWithErrors} danger={stats.symbolsWithErrors > 0} />
          <StatPill label="Events (24h)" value={stats.totalEventsLast24h} />
        </div>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center justify-between gap-2 text-base">
            <span className="flex items-center gap-2">
              <Tag className="h-4 w-4 text-primary" />
              Symbol subscriptions
            </span>
            <div className="flex items-center gap-2">
              <Button variant="outline" size="sm" onClick={() => { void loadData(); }}>
                <RefreshCcw className="size-3.5 mr-1.5" />
                Refresh
              </Button>
              <Button size="sm" onClick={() => setAddOpen(true)}>
                <Plus className="size-3.5 mr-1.5" />
                Add symbol
              </Button>
            </div>
          </CardTitle>
          <CardDescription>
            Manage the active symbol universe. Symbols here receive live data and are eligible for backfill.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {/* Search */}
          <div className="relative mb-4">
            <Search className="absolute left-2.5 top-2.5 size-3.5 text-muted-foreground" />
            <Input
              placeholder="Filter symbols…"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="pl-8 h-8 text-sm"
            />
          </div>

          {loading ? (
            <p className="text-sm text-muted-foreground py-6 text-center">Loading symbols…</p>
          ) : filtered.length === 0 ? (
            <p className="text-sm text-muted-foreground py-6 text-center">
              {searchQuery ? "No symbols match your filter." : "No symbols subscribed. Add a symbol to get started."}
            </p>
          ) : (
            <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
                <thead>
                  <tr className="text-muted-foreground text-xs uppercase tracking-wide">
                    <th className="py-2 pr-4 font-medium">Symbol</th>
                    <th className="py-2 pr-4 font-medium">Status</th>
                    <th className="py-2 pr-4 font-medium">Provider</th>
                    <th className="py-2 pr-4 font-medium">Last Event</th>
                    <th className="py-2 pr-4 font-medium">Historical</th>
                    <th className="py-2 font-medium" />
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/40">
                  {filtered.map((sym) => (
                    <tr key={sym.symbol} className="group hover:bg-muted/30 transition-colors">
                      <td className="py-2 pr-4 font-mono font-medium">{sym.symbol}</td>
                      <td className="py-2 pr-4">
                        <Badge variant="outline" className={cn("text-xs", symbolStatusBadgeClass[sym.status])}>
                          {sym.status}
                        </Badge>
                      </td>
                      <td className="py-2 pr-4 text-muted-foreground">{sym.provider ?? "—"}</td>
                      <td className="py-2 pr-4 text-muted-foreground">
                        {sym.lastEventAt ? new Date(sym.lastEventAt).toLocaleTimeString() : "—"}
                      </td>
                      <td className="py-2 pr-4">
                        {sym.hasHistoricalData
                          ? <CheckCircle2 className="size-4 text-success" />
                          : <span className="text-muted-foreground text-xs">—</span>
                        }
                      </td>
                      <td className="py-2">
                        <Button
                          variant="ghost"
                          size="sm"
                          className="opacity-0 group-hover:opacity-100 transition-opacity text-danger hover:text-danger text-xs h-6 px-2"
                          disabled={removeTarget === sym.symbol}
                          onClick={() => { void handleRemove(sym.symbol); }}
                        >
                          {removeTarget === sym.symbol ? "Removing…" : "Remove"}
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Add symbol dialog */}
      <Dialog open={addOpen} onOpenChange={setAddOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Add symbol</DialogTitle>
            <DialogDescription>
              Enter a ticker symbol to subscribe to live data and enable backfill.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4 pt-2">
            <LabeledField label="Symbol">
              <Input
                placeholder="e.g. AAPL"
                value={addSymbolInput}
                onChange={(e) => setAddSymbolInput(e.target.value.toUpperCase())}
                className="font-mono"
              />
            </LabeledField>
            <LabeledField label="Preferred provider (optional)">
              <Input
                placeholder="e.g. alpaca"
                value={addProvider}
                onChange={(e) => setAddProvider(e.target.value)}
              />
            </LabeledField>
            {addError && (
              <p className="text-sm text-danger">{addError}</p>
            )}
            <div className="flex justify-end gap-3 pt-2">
              <Button variant="outline" onClick={() => setAddOpen(false)} disabled={addBusy}>Cancel</Button>
              <Button onClick={() => { void handleAdd(); }} disabled={addBusy || !addSymbolInput.trim()}>
                {addBusy ? "Adding…" : "Add symbol"}
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </section>
  );
}

function StatPill({ label, value, danger }: { label: string; value: number; danger?: boolean }) {
  return (
    <div className="rounded-lg border border-border/60 bg-muted/20 px-3 py-2.5 text-center">
      <p className={cn("text-lg font-semibold tabular-nums", danger && value > 0 ? "text-danger" : "text-foreground")}>
        {value.toLocaleString()}
      </p>
      <p className="text-xs text-muted-foreground mt-0.5">{label}</p>
    </div>
  );
}

// --- Quality Panel ---

const qualityHealthClass: Record<string, string> = {
  Healthy: "text-success",
  Warning: "text-warning",
  Critical: "text-danger"
};

function QualityPanel() {
  const [dashboard, setDashboard] = useState<QualityDashboardResponse | null>(null);
  const [anomalies, setAnomalies] = useState<QualityAnomalyEntry[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [ackBusy, setAckBusy] = useState<string | null>(null);

  async function loadData() {
    setLoading(true);
    try {
      const [dash, anoms] = await Promise.all([getQualityDashboard(), getQualityAnomalies()]);
      setDashboard(dash);
      setAnomalies(anoms);
    } catch {
      setDashboard(null);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void loadData(); }, []);

  async function handleAcknowledge(anomalyId: string) {
    setAckBusy(anomalyId);
    try {
      await acknowledgeAnomaly(anomalyId);
      setAnomalies((prev) => prev?.map((a) =>
        a.anomalyId === anomalyId ? { ...a, acknowledged: true } : a
      ) ?? null);
    } catch {
      // ignore
    } finally {
      setAckBusy(null);
    }
  }

  const unackAnomalies = anomalies?.filter((a) => !a.acknowledged) ?? [];

  return (
    <section className="space-y-4">
      {/* Score overview */}
      {dashboard && (
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <ScoreCard label="Overall Score" value={dashboard.overallScore} />
          <ScoreCard label="Completeness" value={dashboard.completenessScore} />
          <ScoreCard label="Freshness" value={dashboard.freshnessScore} />
          <ScoreCard label="Anomaly Rate" value={dashboard.anomalyRate} invert />
        </div>
      )}

      <div className="grid gap-4 xl:grid-cols-2">
        {/* Per-symbol health */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between gap-2 text-base">
              <span className="flex items-center gap-2">
                <Filter className="h-4 w-4 text-primary" />
                Symbol quality scores
              </span>
              <Button variant="outline" size="sm" onClick={() => { void loadData(); }}>
                <RefreshCcw className="size-3.5 mr-1.5" />
                Refresh
              </Button>
            </CardTitle>
            <CardDescription>Completeness and freshness per monitored symbol.</CardDescription>
          </CardHeader>
          <CardContent>
            {loading ? (
              <p className="text-sm text-muted-foreground py-6 text-center">Loading quality data…</p>
            ) : !dashboard || dashboard.symbols.length === 0 ? (
              <p className="text-sm text-muted-foreground py-6 text-center">No symbol quality data available.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
                  <thead>
                    <tr className="text-muted-foreground text-xs uppercase tracking-wide">
                      <th className="py-2 pr-4 font-medium">Symbol</th>
                      <th className="py-2 pr-4 font-medium">Health</th>
                      <th className="py-2 pr-4 font-medium">Completeness</th>
                      <th className="py-2 pr-4 font-medium">Freshness</th>
                      <th className="py-2 pr-4 font-medium">Gaps</th>
                      <th className="py-2 font-medium">Anomalies</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border/40">
                    {dashboard.symbols.map((sym) => (
                      <tr key={sym.symbol} className="hover:bg-muted/30 transition-colors">
                        <td className="py-2 pr-4 font-mono font-medium">{sym.symbol}</td>
                        <td className="py-2 pr-4">
                          <span className={cn("text-xs font-medium", qualityHealthClass[sym.health] ?? "text-foreground")}>
                            {sym.health}
                          </span>
                        </td>
                        <td className="py-2 pr-4 tabular-nums">{(sym.completenessScore * 100).toFixed(1)}%</td>
                        <td className="py-2 pr-4 tabular-nums">{(sym.freshnessScore * 100).toFixed(1)}%</td>
                        <td className={cn("py-2 pr-4 tabular-nums", sym.gapCount > 0 && "text-warning")}>{sym.gapCount}</td>
                        <td className={cn("py-2 tabular-nums", sym.anomalyCount > 0 && "text-danger")}>{sym.anomalyCount}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </CardContent>
        </Card>

        {/* Unacknowledged anomalies */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <AlertCircle className="h-4 w-4 text-warning" />
              Unacknowledged anomalies
              {unackAnomalies.length > 0 && (
                <Badge variant="outline" className="border-warning/40 text-warning text-xs ml-1">
                  {unackAnomalies.length}
                </Badge>
              )}
            </CardTitle>
            <CardDescription>Anomalies that require operator attention.</CardDescription>
          </CardHeader>
          <CardContent>
            {loading ? (
              <p className="text-sm text-muted-foreground py-6 text-center">Loading anomalies…</p>
            ) : unackAnomalies.length === 0 ? (
              <div className="flex flex-col items-center gap-2 py-6 text-center">
                <CheckCircle2 className="size-6 text-success" />
                <p className="text-sm text-muted-foreground">No unacknowledged anomalies.</p>
              </div>
            ) : (
              <ul className="space-y-2">
                {unackAnomalies.map((anomaly) => (
                  <li
                    key={anomaly.anomalyId}
                    className="flex items-start gap-3 rounded-lg border border-border/50 p-3"
                  >
                    <AlertCircle className="size-4 text-warning shrink-0 mt-0.5" />
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-medium font-mono">{anomaly.symbol}</p>
                      <p className="text-xs text-muted-foreground mt-0.5">
                        {anomaly.anomalyType} · {anomaly.message}
                      </p>
                      <p className="text-xs text-muted-foreground mt-0.5">
                        {new Date(anomaly.detectedAt).toLocaleString()}
                      </p>
                    </div>
                    <Button
                      variant="outline"
                      size="sm"
                      className="shrink-0 text-xs h-7"
                      disabled={ackBusy === anomaly.anomalyId}
                      onClick={() => { void handleAcknowledge(anomaly.anomalyId); }}
                    >
                      {ackBusy === anomaly.anomalyId ? "Acking…" : "Acknowledge"}
                    </Button>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      </div>

      {/* Data gaps */}
      {dashboard && dashboard.recentGaps.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Recent data gaps</CardTitle>
            <CardDescription>Open data gaps detected across monitored symbols.</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
                <thead>
                  <tr className="text-muted-foreground text-xs uppercase tracking-wide">
                    <th className="py-2 pr-4 font-medium">Symbol</th>
                    <th className="py-2 pr-4 font-medium">Provider</th>
                    <th className="py-2 pr-4 font-medium">From</th>
                    <th className="py-2 pr-4 font-medium">To</th>
                    <th className="py-2 pr-4 font-medium">Est. Bars</th>
                    <th className="py-2 font-medium">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/40">
                  {dashboard.recentGaps.map((gap, i) => (
                    <tr key={i} className="hover:bg-muted/30 transition-colors">
                      <td className="py-2 pr-4 font-mono font-medium">{gap.symbol}</td>
                      <td className="py-2 pr-4 text-muted-foreground">{gap.provider}</td>
                      <td className="py-2 pr-4 text-muted-foreground">{new Date(gap.from).toLocaleDateString()}</td>
                      <td className="py-2 pr-4 text-muted-foreground">{new Date(gap.to).toLocaleDateString()}</td>
                      <td className="py-2 pr-4 tabular-nums">{gap.estimatedBars.toLocaleString()}</td>
                      <td className="py-2">
                        <Badge
                          variant="outline"
                          className={cn("text-xs", gap.status === "Open" ? "border-warning/40 text-warning" : "border-success/40 text-success")}
                        >
                          {gap.status}
                        </Badge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>
      )}
    </section>
  );
}

function ScoreCard({ label, value, invert }: { label: string; value: number; invert?: boolean }) {
  const pct = Math.round(value * 100);
  const tone = invert
    ? (pct > 5 ? "text-danger" : pct > 2 ? "text-warning" : "text-success")
    : (pct >= 90 ? "text-success" : pct >= 70 ? "text-warning" : "text-danger");

  return (
    <div className="rounded-lg border border-border/60 bg-muted/20 px-3 py-2.5 text-center">
      <p className={cn("text-2xl font-semibold tabular-nums", tone)}>{pct}%</p>
      <p className="text-xs text-muted-foreground mt-0.5">{label}</p>
    </div>
  );
}
