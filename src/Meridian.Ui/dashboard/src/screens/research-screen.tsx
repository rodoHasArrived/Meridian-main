import { ArrowUpRight, CheckCircle, FlaskConical, GitCompare, History, Layers3, ShieldAlert, TimerReset, XCircle } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { EntityDataTable } from "@/components/meridian/entity-data-table";
import { MetricCard } from "@/components/meridian/metric-card";
import { RunStatusBadge } from "@/components/meridian/run-status-badge";
import { EquityCurveChart } from "@/components/meridian/equity-curve-chart";
import { approvePromotion, compareRuns, diffRuns, evaluatePromotion, getPromotionHistory, getRunAttribution, getRunEquityCurve, getRunFills, rejectPromotion } from "@/lib/api";
import { cn } from "@/lib/utils";
import type { EquityCurveSummary, MetricsDiff, ParameterDiff, PositionDiffEntry, PromotionDecisionResult, PromotionEvaluationResult, PromotionRecord, ResearchRunRecord, ResearchWorkspaceResponse, RunAttributionSummary, RunComparisonRow, RunDiff, RunFillSummary, SecurityCoverageReference, SecurityCoverageSummary } from "@/types";

interface ResearchScreenProps {
  data: ResearchWorkspaceResponse | null;
}

type PromotionPhase = "idle" | "evaluating" | "evaluated" | "approving" | "approved" | "rejecting" | "rejected" | "error";

interface PromotionState {
  phase: PromotionPhase;
  evaluation: PromotionEvaluationResult | null;
  decision: PromotionDecisionResult | null;
  error: string | null;
}

type RunModeFilter = "all" | "backtest" | "paper" | "live";

const RUN_MODE_FILTERS: { value: RunModeFilter; label: string }[] = [
  { value: "all", label: "All" },
  { value: "backtest", label: "Backtest" },
  { value: "paper", label: "Paper" },
  { value: "live", label: "Live" }
];

export function ResearchScreen({ data }: ResearchScreenProps) {
  const [selectedRun, setSelectedRun] = useState<ResearchRunRecord | null>(null);
  const [promotion, setPromotion] = useState<PromotionState>({
    phase: "idle",
    evaluation: null,
    decision: null,
    error: null
  });
  const [rejectReason, setRejectReason] = useState("");

  // --- Run type filter ---
  const [modeFilter, setModeFilter] = useState<RunModeFilter>("all");

  const filteredRuns = useMemo(() => {
    if (!data || modeFilter === "all") return data?.runs ?? [];
    return data.runs.filter((run) => run.mode === modeFilter);
  }, [data, modeFilter]);

  // --- Multi-run comparison ---
  const [comparisonRows, setComparisonRows] = useState<RunComparisonRow[] | null>(null);
  const [comparisonLoading, setComparisonLoading] = useState(false);
  const [comparisonError, setComparisonError] = useState<string | null>(null);
  const [selectedRunIds, setSelectedRunIds] = useState<Set<string>>(new Set());

  // --- Run diff ---
  const [runDiff, setRunDiff] = useState<RunDiff | null>(null);
  const [diffLoading, setDiffLoading] = useState(false);
  const [diffError, setDiffError] = useState<string | null>(null);
  const [showDiffPanel, setShowDiffPanel] = useState(false);

  // --- Promotion history ---
  const [promotionHistory, setPromotionHistory] = useState<PromotionRecord[] | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [showHistory, setShowHistory] = useState(false);

  // --- Run detail drill-in tabs ---
  type RunDetailTab = "overview" | "chart" | "attribution" | "fills";
  const [runDetailTab, setRunDetailTab] = useState<RunDetailTab>("overview");
  const [attribution, setAttribution] = useState<RunAttributionSummary | null>(null);
  const [attributionLoading, setAttributionLoading] = useState(false);
  const [fills, setFills] = useState<RunFillSummary | null>(null);
  const [fillsLoading, setFillsLoading] = useState(false);
  const [equityCurve, setEquityCurve] = useState<EquityCurveSummary | null>(null);
  const [equityCurveLoading, setEquityCurveLoading] = useState(false);

  useEffect(() => {
    if (!selectedRun || runDetailTab !== "chart") return;
    setEquityCurveLoading(true);
    getRunEquityCurve(selectedRun.id)
      .then(setEquityCurve)
      .catch((err: unknown) => {
        console.error("Failed to load equity curve", err);
        setEquityCurve(null);
      })
      .finally(() => setEquityCurveLoading(false));
  }, [selectedRun, runDetailTab]);

  useEffect(() => {
    if (!selectedRun || runDetailTab !== "attribution") return;
    setAttributionLoading(true);
    getRunAttribution(selectedRun.id)
      .then(setAttribution)
      .catch(() => setAttribution(null))
      .finally(() => setAttributionLoading(false));
  }, [selectedRun, runDetailTab]);

  useEffect(() => {
    if (!selectedRun || runDetailTab !== "fills") return;
    setFillsLoading(true);
    getRunFills(selectedRun.id)
      .then(setFills)
      .catch(() => setFills(null))
      .finally(() => setFillsLoading(false));
  }, [selectedRun, runDetailTab]);

  const highlights = useMemo(
    () => [
      {
        title: "Run comparison readiness",
        description: "Backtests normalized onto the shared strategy-run model can be promoted into Trading.",
        icon: Layers3
      },
      {
        title: "Promotion safety",
        description: "Paper/live state styling is active in badges and review surfaces so risky promotions stay visible.",
        icon: ShieldAlert
      },
      {
        title: "Operator pace",
        description: "Research filters and keyboard navigation are tuned for dense workstation workflows.",
        icon: TimerReset
      }
    ],
    []
  );

  function handleSelectRun(run: ResearchRunRecord) {
    setSelectedRun(run);
    setRejectReason("");
    setPromotion({ phase: "idle", evaluation: null, decision: null, error: null });
  }

  function handleDialogClose(open: boolean) {
    if (!open) {
      setSelectedRun(null);
      setRejectReason("");
      setPromotion({ phase: "idle", evaluation: null, decision: null, error: null });
      setRunDetailTab("overview");
      setAttribution(null);
      setFills(null);
      setEquityCurve(null);
    }
  }

  function handleToggleRunSelection(runId: string) {
    setSelectedRunIds((prev) => {
      const next = new Set(prev);
      if (next.has(runId)) {
        next.delete(runId);
      } else {
        next.add(runId);
      }
      return next;
    });
    // Reset comparison when selection changes
    setComparisonRows(null);
    setComparisonError(null);
    setRunDiff(null);
    setDiffError(null);
    setShowDiffPanel(false);
  }

  async function handleCompareRuns() {
    const ids = Array.from(selectedRunIds);
    if (ids.length < 2) return;
    setComparisonLoading(true);
    setComparisonError(null);
    setComparisonRows(null);
    try {
      const rows = await compareRuns(ids);
      setComparisonRows(rows);
    } catch (err) {
      setComparisonError(err instanceof Error ? err.message : "Comparison failed.");
    } finally {
      setComparisonLoading(false);
    }
  }

  async function handleDiffRuns() {
    const ids = Array.from(selectedRunIds);
    if (ids.length !== 2) return;
    setDiffLoading(true);
    setDiffError(null);
    setRunDiff(null);
    setShowDiffPanel(true);
    try {
      const diff = await diffRuns(ids[0], ids[1]);
      setRunDiff(diff);
    } catch (err) {
      setDiffError(err instanceof Error ? err.message : "Diff failed.");
    } finally {
      setDiffLoading(false);
    }
  }

  async function handleLoadHistory() {
    setHistoryLoading(true);
    setHistoryError(null);
    setShowHistory(true);
    try {
      const history = await getPromotionHistory();
      setPromotionHistory(history);
    } catch (err) {
      setHistoryError(err instanceof Error ? err.message : "Failed to load promotion history.");
    } finally {
      setHistoryLoading(false);
    }
  }

  async function handleEvaluatePromotion() {
    if (!selectedRun) return;
    setPromotion((prev) => ({ ...prev, phase: "evaluating", error: null }));
    try {
      const result = await evaluatePromotion(selectedRun.id);
      setPromotion({ phase: "evaluated", evaluation: result, decision: null, error: null });
    } catch (err) {
      setPromotion((prev) => ({
        ...prev,
        phase: "error",
        error: err instanceof Error ? err.message : "Promotion evaluation failed."
      }));
    }
  }

  async function handleApprovePromotion() {
    if (!selectedRun) return;
    setPromotion((prev) => ({ ...prev, phase: "approving", error: null }));
    try {
      const result = await approvePromotion(selectedRun.id);
      setPromotion((prev) => ({ ...prev, phase: "approved", decision: result }));
    } catch (err) {
      setPromotion((prev) => ({
        ...prev,
        phase: "error",
        error: err instanceof Error ? err.message : "Approval failed."
      }));
    }
  }

  async function handleRejectPromotion() {
    if (!selectedRun) return;
    const reason = rejectReason.trim() || "Rejected by operator.";
    setPromotion((prev) => ({ ...prev, phase: "rejecting", error: null }));
    try {
      const result = await rejectPromotion(selectedRun.id, reason);
      setPromotion((prev) => ({ ...prev, phase: "rejected", decision: result }));
    } catch (err) {
      setPromotion((prev) => ({
        ...prev,
        phase: "error",
        error: err instanceof Error ? err.message : "Rejection failed."
      }));
    }
  }

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
              Metrics, filters, and run drill-in with promotion evaluation wired to the backend workflow.
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
            <div className="eyebrow-label">Promotion Workflow</div>
            <CardTitle>Backtest → Paper → Live</CardTitle>
            <CardDescription className="text-slate-300">
              Select a completed run to evaluate it against promotion thresholds, then approve or reject directly from the detail panel.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4 text-sm leading-6 text-slate-200">
            <div className="rounded-xl bg-white/10 p-4">
              Evaluation calls <code className="rounded bg-white/10 px-1">/api/promotion/evaluate/{"{runId}"}</code> and surfaces sharpe ratio, drawdown, and return against configurable thresholds.
            </div>
            <div className="rounded-xl bg-white/10 p-4">
              Approved promotions create a new run record in the target mode and write an immutable audit trail entry.
            </div>
          </CardContent>
        </Card>
      </section>

      <section>
        <div className="mb-3 flex flex-wrap gap-2">
          {RUN_MODE_FILTERS.map((filter) => (
            <Button
              key={filter.value}
              size="sm"
              variant={modeFilter === filter.value ? "default" : "outline"}
              onClick={() => setModeFilter(filter.value)}
            >
              {filter.label}
              {filter.value !== "all" && data && (
                <span className="ml-1.5 rounded-full bg-background/20 px-1.5 py-0.5 text-[10px] font-mono">
                  {data.runs.filter((r) => r.mode === filter.value).length}
                </span>
              )}
            </Button>
          ))}
        </div>

        <EntityDataTable
          rows={filteredRuns}
          onSelectRun={handleSelectRun}
          selectedRunIds={selectedRunIds}
          onToggleSelection={handleToggleRunSelection}
        />
      </section>

      {selectedRun ? (
        <Dialog open={selectedRun !== null} onOpenChange={handleDialogClose}>
          <DialogContent aria-describedby={undefined} className="max-w-2xl">
            <DialogHeader>
              <DialogTitle>{selectedRun.strategyName}</DialogTitle>
              <DialogDescription id="run-detail-description">
                Review strategy-run details and evaluate for promotion to the next mode.
              </DialogDescription>
            </DialogHeader>

            <div className="mt-6 space-y-6">
              <div className="flex flex-wrap items-center justify-between gap-4">
                <RunStatusBadge status={selectedRun.status} mode={selectedRun.mode} />
                <div className="text-sm text-muted-foreground">Last updated {selectedRun.lastUpdated}</div>
              </div>

              {/* Tab bar */}
              <div className="flex gap-1 rounded-lg border border-border/60 bg-secondary/30 p-1">
                {(["overview", "chart", "attribution", "fills"] as const).map((tab) => (
                  <button
                    key={tab}
                    type="button"
                    onClick={() => setRunDetailTab(tab)}
                    className={cn(
                      "flex-1 rounded-md px-3 py-1.5 text-sm font-medium capitalize transition-colors",
                      runDetailTab === tab
                        ? "bg-background text-foreground shadow-sm"
                        : "text-muted-foreground hover:text-foreground"
                    )}
                  >
                    {tab}
                  </button>
                ))}
              </div>

              {runDetailTab === "chart" && (
                <div className="space-y-4">
                  {equityCurveLoading && <p className="text-sm text-muted-foreground">Loading equity curve…</p>}
                  {!equityCurveLoading && !equityCurve && (
                    <p className="text-sm text-muted-foreground">No equity curve data available for this run.</p>
                  )}
                  {equityCurve && <EquityCurveChart data={equityCurve} />}
                </div>
              )}

              {runDetailTab === "overview" && (
                <>
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

                  {selectedRun.securityCoverage ? (
                    <SecurityCoveragePanel coverage={selectedRun.securityCoverage} />
                  ) : null}

                  {/* Promotion panel */}
                  <PromotionPanel
                    phase={promotion.phase}
                    evaluation={promotion.evaluation}
                    decision={promotion.decision}
                    error={promotion.error}
                    runStatus={selectedRun.status}
                    rejectReason={rejectReason}
                    onRejectReasonChange={setRejectReason}
                    onEvaluate={handleEvaluatePromotion}
                    onApprove={handleApprovePromotion}
                    onReject={handleRejectPromotion}
                  />
                </>
              )}

              {runDetailTab === "attribution" && (
                <div className="space-y-4">
                  {attributionLoading && <p className="text-sm text-muted-foreground">Loading attribution…</p>}
                  {!attributionLoading && !attribution && (
                    <p className="text-sm text-muted-foreground">No attribution data available for this run.</p>
                  )}
                  {attribution && (
                    <>
                      <dl className="grid gap-3 sm:grid-cols-3">
                        <Stat label="Realized P&L" value={`$${attribution.totalRealizedPnl.toLocaleString(undefined, { maximumFractionDigits: 2 })}`} />
                        <Stat label="Unrealized P&L" value={`$${attribution.totalUnrealizedPnl.toLocaleString(undefined, { maximumFractionDigits: 2 })}`} />
                        <Stat label="Total Commissions" value={`$${attribution.totalCommissions.toLocaleString(undefined, { maximumFractionDigits: 2 })}`} />
                      </dl>
                      <div className="overflow-x-auto rounded-xl border border-border/70">
                        <table className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
                          <thead className="bg-secondary/30">
                            <tr>
                              {["Symbol", "Realized", "Unrealized", "Total P&L", "Trades", "Commissions"].map((col) => (
                                <th key={col} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">{col}</th>
                              ))}
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-border/50">
                            {attribution.bySymbol.map((row) => (
                              <tr key={row.symbol} className="bg-background/20">
                                <td className="px-3 py-2 font-mono font-semibold">{row.symbol}</td>
                                <td className={cn("px-3 py-2 font-mono", row.realizedPnl >= 0 ? "text-success" : "text-danger")}>${row.realizedPnl.toLocaleString(undefined, { maximumFractionDigits: 2 })}</td>
                                <td className={cn("px-3 py-2 font-mono", row.unrealizedPnl >= 0 ? "text-success" : "text-danger")}>${row.unrealizedPnl.toLocaleString(undefined, { maximumFractionDigits: 2 })}</td>
                                <td className={cn("px-3 py-2 font-mono font-semibold", row.totalPnl >= 0 ? "text-success" : "text-danger")}>${row.totalPnl.toLocaleString(undefined, { maximumFractionDigits: 2 })}</td>
                                <td className="px-3 py-2 font-mono text-muted-foreground">{row.tradeCount}</td>
                                <td className="px-3 py-2 font-mono text-muted-foreground">${row.commissions.toLocaleString(undefined, { maximumFractionDigits: 2 })}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </>
                  )}
                </div>
              )}

              {runDetailTab === "fills" && (
                <div className="space-y-4">
                  {fillsLoading && <p className="text-sm text-muted-foreground">Loading fills…</p>}
                  {!fillsLoading && !fills && (
                    <p className="text-sm text-muted-foreground">No fill data available for this run.</p>
                  )}
                  {fills && (
                    <>
                      <dl className="grid gap-3 sm:grid-cols-2">
                        <Stat label="Total fills" value={String(fills.totalFills)} />
                        <Stat label="Total commissions" value={`$${fills.totalCommissions.toLocaleString(undefined, { maximumFractionDigits: 2 })}`} />
                      </dl>
                      <div className="overflow-x-auto rounded-xl border border-border/70">
                        <table className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm">
                          <thead className="bg-secondary/30">
                            <tr>
                              {["Symbol", "Qty", "Price", "Commission", "Filled At"].map((col) => (
                                <th key={col} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">{col}</th>
                              ))}
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-border/50">
                            {fills.fills.slice(0, 50).map((fill) => (
                              <tr key={fill.fillId} className="bg-background/20">
                                <td className="px-3 py-2 font-mono font-semibold">{fill.symbol}</td>
                                <td className={cn("px-3 py-2 font-mono", fill.filledQuantity >= 0 ? "text-success" : "text-danger")}>{fill.filledQuantity}</td>
                                <td className="px-3 py-2 font-mono">${fill.fillPrice.toLocaleString(undefined, { maximumFractionDigits: 4 })}</td>
                                <td className="px-3 py-2 font-mono text-muted-foreground">${fill.commission.toLocaleString(undefined, { maximumFractionDigits: 2 })}</td>
                                <td className="px-3 py-2 font-mono text-muted-foreground">{new Date(fill.filledAt).toLocaleString()}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                        {fills.fills.length > 50 && (
                          <p className="px-3 py-2 text-xs text-muted-foreground">Showing first 50 of {fills.fills.length} fills.</p>
                        )}
                      </div>
                    </>
                  )}
                </div>
              )}
            </div>
          </DialogContent>
        </Dialog>
      ) : null}

      {/* --- Multi-run comparison section --- */}
      <section>
        <Card>
          <CardHeader>
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div>
                <div className="eyebrow-label">Run Comparison</div>
                <CardTitle className="flex items-center gap-2">
                  <GitCompare className="h-5 w-5 text-primary" />
                  Side-by-side run comparison
                </CardTitle>
                <CardDescription>
                  Select two or more runs from the table above and compare their metrics, fills, and promotion state.
                </CardDescription>
              </div>
              <div className="flex flex-wrap gap-2">
                {selectedRunIds.size >= 2 && (
                  <>
                    <Button size="sm" variant="outline" onClick={handleCompareRuns} disabled={comparisonLoading}>
                      {comparisonLoading ? "Loading…" : `Compare ${selectedRunIds.size} runs`}
                    </Button>
                    {selectedRunIds.size === 2 && (
                      <Button size="sm" variant="outline" onClick={handleDiffRuns} disabled={diffLoading}>
                        {diffLoading ? "Loading…" : "Diff 2 runs"}
                      </Button>
                    )}
                  </>
                )}
                <Button
                  size="sm"
                  variant="outline"
                  onClick={handleLoadHistory}
                  disabled={historyLoading}
                >
                  <History className="mr-2 h-4 w-4" />
                  {historyLoading ? "Loading…" : "Promotion history"}
                </Button>
              </div>
            </div>
          </CardHeader>

          {selectedRunIds.size === 0 && !comparisonRows && !showHistory && (
            <CardContent>
              <p className="text-sm text-muted-foreground py-2">
                Select runs from the table above to enable comparison and diff actions.
              </p>
            </CardContent>
          )}

          {selectedRunIds.size > 0 && !comparisonRows && !comparisonLoading && (
            <CardContent>
              <p className="text-sm text-muted-foreground py-2">
                {selectedRunIds.size} run{selectedRunIds.size !== 1 ? "s" : ""} selected.{" "}
                {selectedRunIds.size >= 2 ? "Click \u201cCompare\u201d to load side-by-side metrics." : "Select at least one more run to compare."}
              </p>
              <div className="mt-2 flex flex-wrap gap-2">
                {Array.from(selectedRunIds).map((id) => (
                  <span key={id} className="inline-flex items-center gap-1 rounded-full border border-primary/30 bg-primary/10 px-3 py-1 text-xs font-mono text-primary">
                    {id}
                    <button
                      type="button"
                      onClick={() => handleToggleRunSelection(id)}
                      className="ml-1 text-muted-foreground hover:text-destructive"
                    >
                      ×
                    </button>
                  </span>
                ))}
              </div>
            </CardContent>
          )}

          {comparisonError && (
            <CardContent>
              <div className="rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
                {comparisonError}
              </div>
            </CardContent>
          )}

          {comparisonRows && comparisonRows.length > 0 && (
            <CardContent>
              <div className="overflow-x-auto rounded-xl border border-border/70">
                <table className="min-w-full divide-y divide-border/60 text-left text-xs">
                  <thead className="bg-secondary/30">
                    <tr>
                      {["Strategy", "Mode", "Status", "Net P&L", "Return %", "Sharpe", "Max DD %", "Fills", "Promotion"].map((col) => (
                        <th key={col} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground whitespace-nowrap">
                          {col}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border/50">
                    {comparisonRows.map((row) => (
                      <tr key={row.runId} className="bg-background/20">
                        <td className="px-3 py-2 font-semibold text-foreground">{row.strategyName}</td>
                        <td className="px-3 py-2 font-mono capitalize text-muted-foreground">{row.mode}</td>
                        <td className="px-3 py-2 capitalize text-muted-foreground">{row.status}</td>
                        <td className={cn("px-3 py-2 font-mono", (row.netPnl ?? 0) >= 0 ? "text-success" : "text-danger")}>
                          {row.netPnl !== null ? `$${row.netPnl.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` : "—"}
                        </td>
                        <td className={cn("px-3 py-2 font-mono", (row.totalReturn ?? 0) >= 0 ? "text-success" : "text-danger")}>
                          {row.totalReturn !== null ? `${(row.totalReturn * 100).toFixed(2)}%` : "—"}
                        </td>
                        <td className="px-3 py-2 font-mono text-foreground">
                          {row.sharpeRatio !== null ? row.sharpeRatio.toFixed(3) : "—"}
                        </td>
                        <td className={cn("px-3 py-2 font-mono", (row.maxDrawdown ?? 0) < -0.05 ? "text-danger" : "text-muted-foreground")}>
                          {row.maxDrawdown !== null ? `${(row.maxDrawdown * 100).toFixed(1)}%` : "—"}
                        </td>
                        <td className="px-3 py-2 font-mono text-foreground">{row.fillCount}</td>
                        <td className="px-3 py-2 capitalize text-muted-foreground">
                          {row.promotionState === "None" ? "—" : row.promotionState.replace(/([A-Z])/g, " $1").trim()}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </CardContent>
          )}
        </Card>
      </section>

      {/* --- Run diff panel --- */}
      {showDiffPanel && (
        <section>
          <Card>
            <CardHeader>
              <div className="flex items-center justify-between gap-4">
                <div>
                  <div className="eyebrow-label">Run Diff</div>
                  <CardTitle className="text-base">Position &amp; parameter diff</CardTitle>
                  <CardDescription>
                    Changes in positions and parameters between the two selected runs.
                  </CardDescription>
                </div>
                <Button size="sm" variant="outline" onClick={() => { setShowDiffPanel(false); setRunDiff(null); }}>
                  Close
                </Button>
              </div>
            </CardHeader>
            <CardContent>
              {diffLoading && <p className="text-sm text-muted-foreground">Computing diff…</p>}
              {diffError && (
                <div className="rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
                  {diffError}
                </div>
              )}
              {runDiff && <RunDiffPanel diff={runDiff} />}
            </CardContent>
          </Card>
        </section>
      )}

      {/* --- Promotion history panel --- */}
      {showHistory && (
        <section>
          <Card>
            <CardHeader>
              <div className="flex items-center justify-between gap-4">
                <div>
                  <div className="eyebrow-label">Audit Trail</div>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <History className="h-4 w-4 text-primary" />
                    Promotion history
                  </CardTitle>
                  <CardDescription>
                    All approved Backtest → Paper → Live promotions with qualifying metrics and timestamps.
                  </CardDescription>
                </div>
                <Button size="sm" variant="outline" onClick={() => setShowHistory(false)}>
                  Close
                </Button>
              </div>
            </CardHeader>
            <CardContent>
              {historyLoading && <p className="text-sm text-muted-foreground">Loading history…</p>}
              {historyError && (
                <div className="rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
                  {historyError}
                </div>
              )}
              {promotionHistory !== null && promotionHistory.length === 0 && (
                <p className="text-sm text-muted-foreground py-4 text-center">No promotions recorded yet.</p>
              )}
              {promotionHistory && promotionHistory.length > 0 && (
                <div className="overflow-x-auto rounded-xl border border-border/70">
                  <table className="min-w-full divide-y divide-border/60 text-left text-xs">
                    <thead className="bg-secondary/30">
                      <tr>
                        {["Strategy", "From → To", "Sharpe", "Max DD %", "Return %", "Promoted At"].map((col) => (
                          <th key={col} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground whitespace-nowrap">
                            {col}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border/50">
                      {promotionHistory.map((record) => (
                        <tr key={record.promotionId} className="bg-background/20">
                          <td className="px-3 py-2 font-semibold text-foreground">{record.strategyName}</td>
                          <td className="px-3 py-2 text-muted-foreground capitalize">
                            {record.sourceRunType} → {record.targetRunType}
                          </td>
                          <td className="px-3 py-2 font-mono text-foreground">{record.qualifyingSharpe.toFixed(3)}</td>
                          <td className="px-3 py-2 font-mono text-muted-foreground">
                            {(record.qualifyingMaxDrawdownPercent * 100).toFixed(1)}%
                          </td>
                          <td className={cn("px-3 py-2 font-mono", record.qualifyingTotalReturn >= 0 ? "text-success" : "text-danger")}>
                            {(record.qualifyingTotalReturn * 100).toFixed(2)}%
                          </td>
                          <td className="px-3 py-2 text-muted-foreground">
                            {new Date(record.promotedAt).toLocaleString()}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </CardContent>
          </Card>
        </section>
      )}
    </div>
  );
}

interface RunDiffPanelProps {
  diff: RunDiff;
}

function RunDiffPanel({ diff }: RunDiffPanelProps) {
  const { addedPositions, removedPositions, modifiedPositions, parameterChanges, metrics } = diff;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-4 text-sm">
        <span className="font-semibold text-foreground">Base:</span>
        <span className="font-mono text-muted-foreground">{diff.baseStrategyName}</span>
        <span className="text-muted-foreground">vs.</span>
        <span className="font-semibold text-foreground">Target:</span>
        <span className="font-mono text-muted-foreground">{diff.targetStrategyName}</span>
      </div>

      {/* Metrics diff */}
      <div>
        <div className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground mb-3">Metrics delta</div>
        <div className="grid gap-3 sm:grid-cols-3">
          <MetricsDiffCard label="Net P&L delta" value={formatPnlDelta(metrics.netPnlDelta)} isPositive={metrics.netPnlDelta >= 0} />
          <MetricsDiffCard label="Return delta" value={`${(metrics.totalReturnDelta * 100).toFixed(2)}%`} isPositive={metrics.totalReturnDelta >= 0} />
          <MetricsDiffCard label="Fill count delta" value={metrics.fillCountDelta > 0 ? `+${metrics.fillCountDelta}` : metrics.fillCountDelta.toString()} isPositive={metrics.fillCountDelta >= 0} />
        </div>
      </div>

      {/* Position changes */}
      {(addedPositions.length + removedPositions.length + modifiedPositions.length) > 0 ? (
        <div>
          <div className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground mb-3">
            Position changes ({addedPositions.length + removedPositions.length + modifiedPositions.length})
          </div>
          <PositionDiffTable entries={[...addedPositions, ...modifiedPositions, ...removedPositions]} />
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">No position changes between these runs.</p>
      )}

      {/* Parameter changes */}
      {parameterChanges.length > 0 && (
        <div>
          <div className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground mb-3">
            Parameter changes ({parameterChanges.length})
          </div>
          <div className="overflow-x-auto rounded-xl border border-border/70">
            <table className="min-w-full divide-y divide-border/60 text-left text-xs">
              <thead className="bg-secondary/30">
                <tr>
                  {["Parameter", "Base value", "Target value"].map((col) => (
                    <th key={col} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                      {col}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-border/50">
                {parameterChanges.map((param: ParameterDiff) => (
                  <tr key={param.key} className="bg-background/20">
                    <td className="px-3 py-2 font-mono text-foreground">{param.key}</td>
                    <td className="px-3 py-2 font-mono text-muted-foreground">{param.baseValue ?? "—"}</td>
                    <td className="px-3 py-2 font-mono text-foreground">{param.targetValue ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

function SecurityCoveragePanel({ coverage }: { coverage: SecurityCoverageSummary }) {
  const unresolvedCount = coverage.portfolioMissing + coverage.ledgerMissing;
  const partialCount = coverage.portfolioPartial + coverage.ledgerPartial;

  return (
    <div className="rounded-xl border border-border/80 bg-secondary/30 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="text-sm font-semibold uppercase tracking-[0.16em] text-muted-foreground">Security Master coverage</div>
          <p className="mt-3 text-sm leading-6 text-foreground">{coverage.summary}</p>
        </div>
        <span className={cn("rounded-full px-2.5 py-1 text-xs font-mono uppercase", coverageBadgeTone(coverage.tone))}>
          {coverage.hasIssues ? "Review" : "Linked"}
        </span>
      </div>

      <div className="mt-4 grid gap-3 sm:grid-cols-3">
        <CoverageStat label="Resolved" value={String(coverage.portfolioResolved + coverage.ledgerResolved)} />
        <CoverageStat label="Partial" value={String(partialCount)} />
        <CoverageStat label="Unresolved" value={String(unresolvedCount)} />
      </div>

      {coverage.resolvedReferences.length > 0 && (
        <div className="mt-4 space-y-2">
          <div className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">Authoritative links</div>
          {coverage.resolvedReferences.slice(0, 3).map((reference) => (
            <SecurityReferenceRow key={`${reference.source}-${reference.symbol}-${reference.securityId ?? "none"}`} reference={reference} />
          ))}
        </div>
      )}

      {coverage.reviewReferences.length > 0 && (
        <div className="mt-4 space-y-2">
          <div className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">Needs review</div>
          {coverage.reviewReferences.slice(0, 3).map((reference) => (
            <SecurityReferenceRow key={`${reference.source}-${reference.symbol}-${reference.coverageStatus}`} reference={reference} />
          ))}
        </div>
      )}
    </div>
  );
}

function CoverageStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-background/50 p-3">
      <div className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">{label}</div>
      <div className="mt-2 font-mono text-sm text-foreground">{value}</div>
    </div>
  );
}

function SecurityReferenceRow({ reference }: { reference: SecurityCoverageReference }) {
  return (
    <div className="rounded-lg border border-border/70 bg-background/50 p-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <span className="font-semibold text-foreground">{reference.displayName}</span>
            <span className={cn("rounded-full px-2 py-0.5 text-[11px] font-mono uppercase", coverageBadgeTone(reference.coverageStatus === "Resolved" ? "success" : "warning"))}>
              {reference.coverageStatus}
            </span>
          </div>
          <div className="mt-1 flex flex-wrap gap-3 text-xs text-muted-foreground">
            <span>{reference.source}</span>
            <span className="font-mono">{reference.symbol}</span>
            {reference.assetClass ? <span>{reference.assetClass}</span> : null}
            {reference.subType ? <span>{reference.subType}</span> : null}
            {reference.currency ? <span>{reference.currency}</span> : null}
          </div>
          {reference.coverageReason ? <p className="mt-2 text-xs text-muted-foreground">{reference.coverageReason}</p> : null}
        </div>
        <Button asChild size="sm" variant="outline">
          <a href={reference.securityDetailUrl ?? buildSecurityMasterHref(reference.symbol)}>
            {reference.securityId ? "Open security" : "Search symbol"}
          </a>
        </Button>
      </div>
    </div>
  );
}

function PositionDiffTable({ entries }: { entries: PositionDiffEntry[] }) {
  return (
    <div className="overflow-x-auto rounded-xl border border-border/70">
      <table className="min-w-full divide-y divide-border/60 text-left text-xs">
        <thead className="bg-secondary/30">
          <tr>
            {["Symbol", "Change", "Base qty", "Target qty", "Base P&L", "Target P&L"].map((col) => (
              <th key={col} className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground whitespace-nowrap">
                {col}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-border/50">
          {entries.map((entry) => (
            <tr key={entry.symbol} className="bg-background/20">
              <td className="px-3 py-2 font-mono font-semibold text-foreground">{entry.symbol}</td>
              <td className={cn("px-3 py-2 font-semibold",
                entry.changeType === "Added" ? "text-success" :
                  entry.changeType === "Removed" ? "text-danger" : "text-warning")}>
                {entry.changeType}
              </td>
              <td className="px-3 py-2 font-mono text-muted-foreground">{entry.baseQuantity}</td>
              <td className="px-3 py-2 font-mono text-foreground">{entry.targetQuantity}</td>
              <td className={cn("px-3 py-2 font-mono", entry.basePnl >= 0 ? "text-success" : "text-danger")}>
                {formatPnlDelta(entry.basePnl)}
              </td>
              <td className={cn("px-3 py-2 font-mono", entry.targetPnl >= 0 ? "text-success" : "text-danger")}>
                {formatPnlDelta(entry.targetPnl)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function MetricsDiffCard({ label, value, isPositive }: { label: string; value: string; isPositive: boolean }) {
  return (
    <div className="rounded-lg border border-border/70 bg-secondary/30 p-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className={cn("mt-1 font-mono text-sm font-semibold", isPositive ? "text-success" : "text-danger")}>{value}</div>
    </div>
  );
}

function formatPnlDelta(value: number): string {
  const sign = value >= 0 ? "+" : "";
  return `${sign}$${Math.abs(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function coverageBadgeTone(tone: "default" | "success" | "warning" | "danger") {
  if (tone === "success") {
    return "bg-success/15 text-success";
  }

  if (tone === "warning") {
    return "bg-warning/15 text-warning";
  }

  if (tone === "danger") {
    return "bg-destructive/15 text-destructive";
  }

  return "bg-secondary text-muted-foreground";
}

function buildSecurityMasterHref(symbol: string) {
  return `/governance/security-master?query=${encodeURIComponent(symbol)}`;
}

interface PromotionPanelProps {
  phase: PromotionPhase;
  evaluation: PromotionEvaluationResult | null;
  decision: PromotionDecisionResult | null;
  error: string | null;
  runStatus: ResearchRunRecord["status"];
  rejectReason: string;
  onRejectReasonChange: (value: string) => void;
  onEvaluate: () => void;
  onApprove: () => void;
  onReject: () => void;
}

function PromotionPanel({
  phase,
  evaluation,
  decision,
  error,
  runStatus,
  rejectReason,
  onRejectReasonChange,
  onEvaluate,
  onApprove,
  onReject
}: PromotionPanelProps) {
  const [showRejectForm, setShowRejectForm] = useState(false);
  const canEvaluate = runStatus === "Completed" && (phase === "idle" || phase === "error");
  const isLoading = phase === "evaluating" || phase === "approving" || phase === "rejecting";

  return (
    <div className="rounded-xl border border-border/80 bg-secondary/20 p-4 space-y-4">
      <div className="flex items-center justify-between">
        <div className="text-sm font-semibold uppercase tracking-[0.16em] text-muted-foreground flex items-center gap-2">
          <ArrowUpRight className="h-4 w-4" />
          Promotion workflow
        </div>
        {canEvaluate ? (
          <Button size="sm" variant="outline" onClick={onEvaluate} disabled={isLoading}>
            Evaluate for promotion
          </Button>
        ) : phase === "idle" && runStatus !== "Completed" ? (
          <span className="text-xs text-muted-foreground">Run must be completed before evaluation.</span>
        ) : null}
      </div>

      {phase === "evaluating" && (
        <p className="text-sm text-muted-foreground">Evaluating against promotion thresholds…</p>
      )}

      {error && (
        <div className="rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {error}
        </div>
      )}

      {phase === "evaluated" && evaluation && (
        <div className="space-y-3">
          <div
            className={cn(
              "flex items-center gap-2 rounded-lg px-4 py-3 text-sm font-semibold",
              evaluation.isEligible
                ? "bg-success/10 text-success border border-success/30"
                : "bg-warning/10 text-warning border border-warning/30"
            )}
          >
            {evaluation.isEligible ? (
              <CheckCircle className="h-4 w-4 shrink-0" />
            ) : (
              <XCircle className="h-4 w-4 shrink-0" />
            )}
            {evaluation.reason}
          </div>

          <dl className="grid gap-3 sm:grid-cols-3 text-sm">
            <EvalStat label="Sharpe Ratio" value={evaluation.sharpeRatio.toFixed(3)} />
            <EvalStat label="Max Drawdown" value={`${(evaluation.maxDrawdownPercent * 100).toFixed(1)}%`} />
            <EvalStat label="Total Return" value={`${(evaluation.totalReturn * 100).toFixed(2)}%`} />
          </dl>

          {evaluation.targetMode && (
            <p className="text-xs text-muted-foreground">
              Target mode:{" "}
              <span className="font-semibold text-foreground capitalize">{evaluation.targetMode}</span>
            </p>
          )}

          {evaluation.isEligible && (
            <div className="flex gap-3 pt-2">
              <Button size="sm" onClick={onApprove} disabled={isLoading}>
                <CheckCircle className="mr-2 h-4 w-4" />
                Approve promotion
              </Button>
              <Button
                size="sm"
                variant="outline"
                onClick={() => setShowRejectForm((prev) => !prev)}
                disabled={isLoading}
              >
                <XCircle className="mr-2 h-4 w-4" />
                Reject
              </Button>
            </div>
          )}

          {!evaluation.isEligible && phase === "evaluated" && (
            <div className="flex gap-3 pt-2">
              <Button
                size="sm"
                variant="outline"
                onClick={() => setShowRejectForm((prev) => !prev)}
                disabled={isLoading}
              >
                <XCircle className="mr-2 h-4 w-4" />
                Record rejection
              </Button>
            </div>
          )}

          {showRejectForm && (
            <div className="mt-3 space-y-3 rounded-lg border border-border/60 bg-secondary/20 p-4">
              <label className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                Rejection reason
              </label>
              <textarea
                rows={2}
                placeholder="Describe why this run is being rejected (optional)…"
                value={rejectReason}
                onChange={(e) => onRejectReasonChange(e.target.value)}
                className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/40 resize-none"
              />
              <div className="flex gap-3">
                <Button size="sm" variant="outline" onClick={onReject} disabled={isLoading}>
                  {isLoading ? "Recording…" : "Confirm rejection"}
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => {
                    setShowRejectForm(false);
                    onRejectReasonChange("");
                  }}
                  disabled={isLoading}
                >
                  Cancel
                </Button>
              </div>
            </div>
          )}
        </div>
      )}

      {phase === "approved" && decision && (
        <div className="rounded-lg border border-success/30 bg-success/10 px-4 py-3 text-sm text-success space-y-1">
          <div className="font-semibold">Promotion approved</div>
          <div className="text-xs text-muted-foreground">{decision.reason}</div>
          {decision.newRunId && (
            <div className="text-xs font-mono text-muted-foreground">New run: {decision.newRunId}</div>
          )}
        </div>
      )}

      {phase === "rejected" && decision && (
        <div className="rounded-lg border border-muted/30 bg-secondary/30 px-4 py-3 text-sm text-muted-foreground">
          <div className="font-semibold text-foreground">Promotion rejected</div>
          <div>{decision.reason}</div>
        </div>
      )}

      {phase === "approving" && (
        <p className="text-sm text-muted-foreground">Approving promotion…</p>
      )}

      {phase === "rejecting" && (
        <p className="text-sm text-muted-foreground">Recording rejection…</p>
      )}
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

function EvalStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-secondary/30 p-3">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-1 font-mono text-sm font-semibold">{value}</dd>
    </div>
  );
}

