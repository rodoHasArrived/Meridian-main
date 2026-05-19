import { useEffect, useMemo, useState } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { getRiskRuleConfig, getRiskRules, updateRiskRuleConfig } from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import { cn } from "@/lib/utils";
import { buildRiskControlPanelViewModel } from "@/components/ui/risk-control-panel.view-model";
import type { RiskRuleConfig, RiskRuleStatus } from "@/types";

const toneClass = {
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
} as const;

export function RiskControlPanel() {
  const [statuses, setStatuses] = useState<RiskRuleStatus[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const [drawdownPercent, setDrawdownPercent] = useState<string>("");

  const vm = useMemo(() => buildRiskControlPanelViewModel(statuses), [statuses]);

  useEffect(() => {
    let cancelled = false;
    async function load(): Promise<void> {
      setLoading(true);
      setError(null);
      try {
        const [rules, drawdownConfig] = await Promise.all([
          getRiskRules(),
          getRiskRuleConfig("DrawdownCircuitBreaker")
        ]);
        if (!cancelled) {
          setStatuses(rules);
          setDrawdownPercent(formatDrawdown(drawdownConfig));
        }
      } catch (loadError) {
        if (!cancelled) {
          setError(describeApiError(loadError, "Failed to load risk controls."));
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleDrawdownSave(): Promise<void> {
    if (!drawdownPercent.trim()) {
      return;
    }

    const parsed = Number(drawdownPercent);
    if (!Number.isFinite(parsed) || parsed <= 0) {
      setError({ summary: "Drawdown threshold must be a positive number.", details: [] });
      return;
    }

    setError(null);
    try {
      await updateRiskRuleConfig("DrawdownCircuitBreaker", {
        maxDrawdownPercent: parsed,
        reason: "Updated from risk control panel."
      });
      const refreshed = await getRiskRules();
      setStatuses(refreshed);
    } catch (updateError) {
      setError(describeApiError(updateError, "Failed to update risk rule config."));
    }
  }

  return (
    <Card data-testid="risk-control-panel">
      <CardHeader>
        <CardTitle>Risk control panel</CardTitle>
        <CardDescription>{vm.overallSummary}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {error ? (
          <div role="alert" className="rounded-md border border-danger/35 bg-danger/10 px-3 py-2.5 text-sm text-danger">
            <p>{error.summary}</p>
            {error.details.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5 text-danger/90">
                {error.details.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
          </div>
        ) : null}
        <div className="space-y-2">
          {vm.rows.map((row) => (
            <article key={row.ruleName} className="rounded-lg border border-border/70 p-3">
              <div className="flex items-center justify-between gap-3">
                <h4 className="font-semibold">{row.ruleName}</h4>
                <span className={cn("text-xs font-semibold uppercase tracking-wide", toneClass[row.tone])}>
                  {row.state}
                </span>
              </div>
              <p className="text-sm text-muted-foreground">{row.summary}</p>
              <p className="mt-1 text-xs text-muted-foreground">
                Threshold: {row.threshold} · Current: {row.currentValue}
              </p>
            </article>
          ))}
          {!loading && vm.rows.length === 0 && (
            <p className="text-sm text-muted-foreground">No risk rules are currently available.</p>
          )}
        </div>

        <div className="rounded-lg border border-border/70 p-3">
          <h4 className="font-semibold">Drawdown threshold</h4>
          <div className="mt-2 flex items-center gap-2">
            <Input
              aria-label="Drawdown threshold percent"
              value={drawdownPercent}
              onChange={(event) => setDrawdownPercent(event.target.value)}
              placeholder="5"
            />
            <Button type="button" onClick={() => void handleDrawdownSave()}>
              Save
            </Button>
          </div>
        </div>

        <div className="rounded-lg border border-border/70 p-3">
          <h4 className="font-semibold">Rule violation timeline</h4>
          <ul className="mt-2 space-y-1 text-sm text-muted-foreground">
            {vm.violationTimeline.slice(0, 10).map((item) => (
              <li key={item.id}>
                <span className="font-medium text-foreground">{item.ruleName}:</span> {item.message}
              </li>
            ))}
            {vm.violationTimeline.length === 0 && <li>No recent violations recorded.</li>}
          </ul>
        </div>
      </CardContent>
    </Card>
  );
}

function formatDrawdown(config: RiskRuleConfig | null | undefined): string {
  if (!config || typeof config.maxDrawdownPercent !== "number" || Number.isNaN(config.maxDrawdownPercent)) {
    return "";
  }

  return config.maxDrawdownPercent.toString();
}
