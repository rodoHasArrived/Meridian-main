import { RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useRiskControlPanelViewModel } from "@/components/ui/risk-control-panel.view-model";

const toneMap: Record<"Healthy" | "Observe" | "Constrained", "success" | "warning" | "danger"> = {
  Healthy: "success",
  Observe: "warning",
  Constrained: "danger"
};

export function RiskControlPanel() {
  const vm = useRiskControlPanelViewModel();

  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <div className="eyebrow-label">Risk rule controls</div>
            <CardTitle>Active risk guardrails</CardTitle>
            <CardDescription>Inspect live rule posture and adjust thresholds inline.</CardDescription>
          </div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => { void vm.refresh(); }}
            disabled={vm.loading}
          >
            <RefreshCcw className="h-4 w-4" aria-hidden="true" />
            Refresh
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {vm.errorText ? (
          <p role="status" className="rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-sm text-warning">
            {vm.errorText}
          </p>
        ) : null}

        {vm.loading ? (
          <p className="text-sm text-muted-foreground">Loading risk rules…</p>
        ) : (
          vm.rows.map((row) => (
            <article key={row.status.ruleName} className="rounded-xl border border-border/70 bg-secondary/25 p-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <h4 className="text-sm font-semibold text-foreground">{row.status.displayName}</h4>
                  <p className="text-xs text-muted-foreground">{row.status.summary}</p>
                </div>
                <Badge variant={toneMap[row.status.state]}>{row.status.state}</Badge>
              </div>

              <dl className="mt-3 grid gap-2 text-xs text-muted-foreground sm:grid-cols-2">
                <div>
                  <dt>{row.status.currentValueLabel}</dt>
                  <dd className="font-mono text-foreground">{row.status.currentValue}</dd>
                </div>
                <div>
                  <dt>{row.status.thresholdLabel}</dt>
                  <dd className="font-mono text-foreground">{row.status.thresholdValue}</dd>
                </div>
              </dl>

              <div className="mt-3 grid gap-2 md:grid-cols-2">
                {Object.entries(row.draftConfig).map(([key, value]) => {
                  const inputId = `${row.status.ruleName}-${key}`;
                  return (
                    <div key={inputId} className="space-y-1">
                      <Label htmlFor={inputId} className="text-xs">{key}</Label>
                      <Input
                        id={inputId}
                        value={value}
                        onChange={(event) => vm.updateDraftValue(row.status.ruleName, key, event.target.value)}
                      />
                    </div>
                  );
                })}
              </div>

              <div className="mt-3 flex items-center justify-between gap-2">
                <span className="text-[11px] text-muted-foreground">
                  Updated by {row.config?.updatedBy ?? "system"} at {row.config?.updatedAt ?? "—"}
                </span>
                <Button
                  type="button"
                  size="sm"
                  onClick={() => { void vm.saveRuleConfig(row.status.ruleName); }}
                  disabled={row.saving}
                  busy={row.saving}
                  busyLabel="Saving…"
                >
                  Save
                </Button>
              </div>
            </article>
          ))
        )}
      </CardContent>
    </Card>
  );
}
