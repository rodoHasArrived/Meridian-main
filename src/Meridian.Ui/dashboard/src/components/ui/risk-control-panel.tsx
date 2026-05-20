import { RefreshCcw } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { useRiskControlPanelViewModel } from "@/components/ui/risk-control-panel.view-model";

const toneClass = {
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
} as const;

export function RiskControlPanel() {
  const vm = useRiskControlPanelViewModel();

  return (
    <Card data-testid="risk-control-panel" className="panel-surface">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle>Risk control panel</CardTitle>
            <CardDescription>{vm.overallSummary}</CardDescription>
          </div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => void vm.refresh()}
            busy={vm.refreshAction.busy}
            busyLabel={vm.refreshAction.busyLabel}
            disabled={vm.refreshAction.disabled}
            disabledReason={vm.refreshAction.disabledReason}
            aria-label={vm.refreshAction.ariaLabel}
          >
            <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
            {vm.refreshAction.label}
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <span id="risk-control-status" className="sr-only" aria-live="polite">{vm.statusAnnouncement}</span>
        {vm.error ? (
          <div role="alert" className="rounded-md border border-danger/35 bg-danger/10 px-3 py-2.5 text-sm text-danger">
            <p>{vm.error.summary}</p>
            {vm.error.details.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5 text-danger/90">
                {vm.error.details.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
          </div>
        ) : null}
        {!vm.error && vm.statusMessage ? (
          <div
            role={vm.statusRole}
            className={cn(
              "rounded-md border px-3 py-2.5 text-sm",
              vm.statusTone === "success"
                ? "border-success/35 bg-success/10 text-success"
                : vm.statusTone === "danger"
                  ? "border-danger/35 bg-danger/10 text-danger"
                  : "border-border/70 bg-secondary/25 text-muted-foreground"
            )}
          >
            {vm.statusMessage}
          </div>
        ) : null}
        <div className="space-y-2">
          <h4 className="text-xs font-semibold uppercase text-muted-foreground">{vm.rowsLabel}</h4>
          {vm.rows.map((row) => (
            <article key={row.ruleName} className="rounded-md border border-border/70 bg-background/35 p-3">
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
          {vm.rows.length === 0 && (
            <p role={vm.loading ? "status" : undefined} className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
              {vm.emptyRowsText}
            </p>
          )}
        </div>

        <div className="rounded-md border border-border/70 bg-background/35 p-3">
          <label htmlFor={vm.drawdownField.id} className="text-sm font-semibold">
            {vm.drawdownField.label}
          </label>
          <div className="mt-2 flex flex-col gap-2 sm:flex-row sm:items-start">
            <div className="min-w-0 flex-1">
              <Input
                id={vm.drawdownField.id}
                aria-label={vm.drawdownField.label}
                aria-describedby={vm.drawdownField.describedBy}
                value={vm.drawdownField.value}
                onChange={(event) => vm.setDrawdownPercent(event.target.value)}
                placeholder={vm.drawdownField.placeholder}
                disabled={vm.drawdownField.disabled}
                title={vm.drawdownField.disabledReason ?? undefined}
                error={vm.drawdownField.error}
              />
              <p
                id={vm.drawdownField.helpId}
                className={cn("mt-1 text-xs leading-5", vm.drawdownField.error ? "text-danger" : "text-muted-foreground")}
              >
                {vm.drawdownField.helpText}
              </p>
            </div>
            <Button
              type="button"
              onClick={() => void vm.saveDrawdownThreshold()}
              busy={vm.saveAction.busy}
              busyLabel={vm.saveAction.busyLabel}
              disabled={vm.saveAction.disabled}
              disabledReason={vm.saveAction.disabledReason}
              aria-label={vm.saveAction.ariaLabel}
              className="sm:mt-0"
            >
              {vm.saveAction.label}
            </Button>
          </div>
        </div>

        <div className="rounded-md border border-border/70 bg-background/35 p-3">
          <h4 className="font-semibold">{vm.timelineLabel}</h4>
          <ul className="mt-2 space-y-1 text-sm text-muted-foreground">
            {vm.violationTimeline.slice(0, 10).map((item) => (
              <li key={item.id}>
                <span className="font-medium text-foreground">{item.ruleName}:</span> {item.message}
              </li>
            ))}
            {vm.violationTimeline.length === 0 && <li>{vm.emptyTimelineText}</li>}
          </ul>
        </div>
      </CardContent>
    </Card>
  );
}
