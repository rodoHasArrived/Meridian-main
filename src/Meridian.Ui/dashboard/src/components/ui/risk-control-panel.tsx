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
    <Card
      data-testid="risk-control-panel"
      role="region"
      aria-label={vm.panelAriaLabel}
      aria-busy={vm.panelAriaBusy}
      className="panel-surface"
    >
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
          <div role="alert" className="rounded-[2px] border border-danger/35 bg-danger/10 px-3 py-2.5 text-sm text-danger">
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
              "rounded-[2px] border px-3 py-2.5 text-sm",
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
            <article key={row.ruleName} className="rounded-[2px] border border-border/70 bg-background/35 p-3">
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
            <p role={vm.loading ? "status" : undefined} className="rounded-[2px] border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
              {vm.emptyRowsText}
            </p>
          )}
        </div>

        <div className="rounded-[2px] border border-border/70 bg-background/35 p-3">
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

        <div className="rounded-[2px] border border-border/70 bg-background/35 p-3">
          <h4 className="text-sm font-semibold">Fat-finger limits</h4>
          <div className="mt-2 space-y-2">
            <div>
              <label htmlFor={vm.fatFingerQuantityField.id} className="text-sm font-semibold">
                {vm.fatFingerQuantityField.label}
              </label>
              <div className="mt-1 min-w-0">
                <Input
                  id={vm.fatFingerQuantityField.id}
                  aria-label={vm.fatFingerQuantityField.label}
                  aria-describedby={vm.fatFingerQuantityField.describedBy}
                  value={vm.fatFingerQuantityField.value}
                  onChange={(event) => vm.setFatFingerQuantity(event.target.value)}
                  placeholder={vm.fatFingerQuantityField.placeholder}
                  disabled={vm.fatFingerQuantityField.disabled}
                  title={vm.fatFingerQuantityField.disabledReason ?? undefined}
                  error={vm.fatFingerQuantityField.error}
                />
                <p
                  id={vm.fatFingerQuantityField.helpId}
                  className={cn("mt-1 text-xs leading-5", vm.fatFingerQuantityField.error ? "text-danger" : "text-muted-foreground")}
                >
                  {vm.fatFingerQuantityField.helpText}
                </p>
              </div>
            </div>
            <div>
              <label htmlFor={vm.fatFingerDeviationField.id} className="text-sm font-semibold">
                {vm.fatFingerDeviationField.label}
              </label>
              <div className="mt-1 flex flex-col gap-2 sm:flex-row sm:items-start">
                <div className="min-w-0 flex-1">
                  <Input
                    id={vm.fatFingerDeviationField.id}
                    aria-label={vm.fatFingerDeviationField.label}
                    aria-describedby={vm.fatFingerDeviationField.describedBy}
                    value={vm.fatFingerDeviationField.value}
                    onChange={(event) => vm.setFatFingerDeviation(event.target.value)}
                    placeholder={vm.fatFingerDeviationField.placeholder}
                    disabled={vm.fatFingerDeviationField.disabled}
                    title={vm.fatFingerDeviationField.disabledReason ?? undefined}
                    error={vm.fatFingerDeviationField.error}
                  />
                  <p
                    id={vm.fatFingerDeviationField.helpId}
                    className={cn("mt-1 text-xs leading-5", vm.fatFingerDeviationField.error ? "text-danger" : "text-muted-foreground")}
                  >
                    {vm.fatFingerDeviationField.helpText}
                  </p>
                </div>
                <Button
                  type="button"
                  onClick={() => void vm.saveFatFingerThresholds()}
                  busy={vm.saveFatFingerAction.busy}
                  busyLabel={vm.saveFatFingerAction.busyLabel}
                  disabled={vm.saveFatFingerAction.disabled}
                  disabledReason={vm.saveFatFingerAction.disabledReason}
                  aria-label={vm.saveFatFingerAction.ariaLabel}
                  className="sm:mt-0"
                >
                  {vm.saveFatFingerAction.label}
                </Button>
              </div>
            </div>
          </div>
        </div>

        <div className="rounded-[2px] border border-border/70 bg-background/35 p-3">
          <h4 className="text-sm font-semibold">Price collar</h4>
          <div className="mt-2">
            <label htmlFor={vm.priceCollarField.id} className="text-sm font-semibold">
              {vm.priceCollarField.label}
            </label>
            <div className="mt-1 flex flex-col gap-2 sm:flex-row sm:items-start">
              <div className="min-w-0 flex-1">
                <Input
                  id={vm.priceCollarField.id}
                  aria-label={vm.priceCollarField.label}
                  aria-describedby={vm.priceCollarField.describedBy}
                  value={vm.priceCollarField.value}
                  onChange={(event) => vm.setPriceCollar(event.target.value)}
                  placeholder={vm.priceCollarField.placeholder}
                  disabled={vm.priceCollarField.disabled}
                  title={vm.priceCollarField.disabledReason ?? undefined}
                  error={vm.priceCollarField.error}
                />
                <p
                  id={vm.priceCollarField.helpId}
                  className={cn("mt-1 text-xs leading-5", vm.priceCollarField.error ? "text-danger" : "text-muted-foreground")}
                >
                  {vm.priceCollarField.helpText}
                </p>
              </div>
              <Button
                type="button"
                onClick={() => void vm.savePriceCollarThreshold()}
                busy={vm.savePriceCollarAction.busy}
                busyLabel={vm.savePriceCollarAction.busyLabel}
                disabled={vm.savePriceCollarAction.disabled}
                disabledReason={vm.savePriceCollarAction.disabledReason}
                aria-label={vm.savePriceCollarAction.ariaLabel}
                className="sm:mt-0"
              >
                {vm.savePriceCollarAction.label}
              </Button>
            </div>
          </div>
        </div>

        <div className="rounded-[2px] border border-border/70 bg-background/35 p-3">
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
