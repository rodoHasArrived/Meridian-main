import { ArrowUpRight, CheckCircle, FlaskConical, Layers3, ShieldAlert, TimerReset, XCircle } from "lucide-react";
import { useMemo, useState } from "react";
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
import { approvePromotion, evaluatePromotion, rejectPromotion } from "@/lib/api";
import { cn } from "@/lib/utils";
import type { PromotionDecisionResult, PromotionEvaluationResult, ResearchRunRecord, ResearchWorkspaceResponse } from "@/types";

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

export function ResearchScreen({ data }: ResearchScreenProps) {
  const [selectedRun, setSelectedRun] = useState<ResearchRunRecord | null>(null);
  const [promotion, setPromotion] = useState<PromotionState>({
    phase: "idle",
    evaluation: null,
    decision: null,
    error: null
  });

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
    setPromotion({ phase: "idle", evaluation: null, decision: null, error: null });
  }

  function handleDialogClose(open: boolean) {
    if (!open) {
      setSelectedRun(null);
      setPromotion({ phase: "idle", evaluation: null, decision: null, error: null });
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
    setPromotion((prev) => ({ ...prev, phase: "rejecting", error: null }));
    try {
      const result = await rejectPromotion(selectedRun.id, "Rejected by operator.");
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

      <EntityDataTable rows={data.runs} onSelectRun={handleSelectRun} />

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

              {/* Promotion panel */}
              <PromotionPanel
                phase={promotion.phase}
                evaluation={promotion.evaluation}
                decision={promotion.decision}
                error={promotion.error}
                runStatus={selectedRun.status}
                onEvaluate={handleEvaluatePromotion}
                onApprove={handleApprovePromotion}
                onReject={handleRejectPromotion}
              />
            </div>
          </DialogContent>
        </Dialog>
      ) : null}
    </div>
  );
}

interface PromotionPanelProps {
  phase: PromotionPhase;
  evaluation: PromotionEvaluationResult | null;
  decision: PromotionDecisionResult | null;
  error: string | null;
  runStatus: ResearchRunRecord["status"];
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
  onEvaluate,
  onApprove,
  onReject
}: PromotionPanelProps) {
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
              <Button size="sm" variant="outline" onClick={onReject} disabled={isLoading}>
                <XCircle className="mr-2 h-4 w-4" />
                Reject
              </Button>
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

