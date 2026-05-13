import type { ReactNode } from "react";
import {
  AlarmClock,
  Bell,
  BellOff,
  BellRing,
  Check,
  CheckCheck,
  LineChart,
  Plus,
  RotateCcw,
  Trash2,
  X
} from "lucide-react";
import { Link, useSearchParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { MetricCard } from "@/components/meridian/metric-card";
import { usePriceAlerts } from "@/lib/price-alerts/service";
import type { PriceAlertCondition, PriceAlertField } from "@/lib/price-alerts/types";
import {
  PRICE_ALERT_CONDITION_OPTIONS,
  PRICE_ALERT_FIELD_OPTIONS,
  usePriceAlertsScreenViewModel,
  type PriceAlertsScreenViewModel,
  type PriceAlertRowAction,
  type PriceAlertRowViewModel,
  type PriceAlertTriggerRowViewModel
} from "@/screens/price-alerts-screen.view-model";

export function PriceAlertsScreen() {
  const alerts = usePriceAlerts();
  const [searchParams, setSearchParams] = useSearchParams();
  const vm = usePriceAlertsScreenViewModel({
    alerts,
    seededSymbol: searchParams.get("symbol"),
    onSeededSymbolConsumed: () => {
      const next = new URLSearchParams(searchParams);
      next.delete("symbol");
      setSearchParams(next, { replace: true });
    }
  });

  const triggerColumns: DenseDataTableColumn<PriceAlertTriggerRowViewModel>[] = [
    {
      id: "trigger",
      label: "Trigger",
      render: (row) => (
        <span className="block min-w-0">
          <span className="block font-semibold text-foreground">{row.symbol}</span>
          <span className="mt-1 block break-words text-[11px] text-muted-foreground">{row.conditionLabel}</span>
        </span>
      )
    },
    {
      id: "status",
      label: "Status",
      render: (row) => <Badge variant={row.statusVariant} dot>{row.statusLabel}</Badge>
    },
    {
      id: "price",
      label: "Price",
      align: "right",
      render: (row) => <span>{row.priceLabel}</span>
    },
    {
      id: "fired",
      label: "Fired",
      render: (row) => <span className="text-muted-foreground">{row.triggeredAtLabel}</span>
    },
    {
      id: "note",
      label: "Note",
      render: (row) => <span className="block max-w-[18rem] truncate text-muted-foreground">{row.noteLabel}</span>
    },
    {
      id: "actions",
      label: "Actions",
      render: (row) => (
        <div className="flex flex-wrap justify-end gap-2">
          <Button asChild variant="outline" size="sm">
            <Link to={row.quoteHref} aria-label={row.quoteAriaLabel}>
              <LineChart className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="ml-1">Quote</span>
            </Link>
          </Button>
          {row.acknowledgeAction ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => vm.acknowledgeTrigger(row.id)}
              aria-label={row.acknowledgeAction.ariaLabel}
            >
              <Check className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="ml-1">{row.acknowledgeAction.label}</span>
            </Button>
          ) : null}
        </div>
      )
    }
  ];

  const alertColumns: DenseDataTableColumn<PriceAlertRowViewModel>[] = [
    {
      id: "alert",
      label: "Alert",
      render: (row) => (
        <span className="block min-w-0">
          <span className="block font-semibold text-foreground">{row.symbol}</span>
          <span className="mt-1 block break-words text-[11px] text-muted-foreground">{row.conditionLabel}</span>
        </span>
      )
    },
    {
      id: "status",
      label: "Status",
      render: (row) => <Badge variant={row.statusVariant} dot>{row.statusLabel}</Badge>
    },
    {
      id: "last",
      label: "Last seen",
      render: (row) => <span className="block min-w-[12rem] text-muted-foreground">{row.lastObservedLabel}</span>
    },
    {
      id: "note",
      label: "Note",
      render: (row) => <span className="block max-w-[18rem] truncate text-muted-foreground">{row.noteLabel}</span>
    },
    {
      id: "actions",
      label: "Actions",
      render: (row) => (
        <div className="flex flex-wrap justify-end gap-2">
          <Button asChild variant="outline" size="sm">
            <Link to={row.quoteHref} aria-label={row.quoteAriaLabel}>
              <LineChart className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="ml-1">Quote</span>
            </Link>
          </Button>
          <AlertActionButton action={row.primaryAction} onClick={() => handleAlertAction(vm, row.primaryAction, row.id)} />
          <AlertActionButton action={row.pauseAction} onClick={() => handleAlertAction(vm, row.pauseAction, row.id)} />
          <AlertActionButton action={row.deleteAction} onClick={() => handleAlertAction(vm, row.deleteAction, row.id)} />
        </div>
      )
    }
  ];

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="eyebrow-label">Data Lane</div>
          <CardTitle className="flex items-center gap-2">
            <BellRing className="h-5 w-5 text-primary" aria-hidden="true" />
            Price alerts
          </CardTitle>
          <CardDescription>
            {vm.heroDescription}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 sm:grid-cols-3">
            {vm.summaryMetrics.map((metric) => <MetricCard key={metric.id} {...metric} />)}
          </div>
          {vm.pollErrorPanel ? (
            <p role={vm.pollErrorPanel.role} aria-live={vm.pollErrorPanel.ariaLive} className={vm.pollErrorPanel.className}>
              {vm.pollErrorPanel.text}
            </p>
          ) : null}
          {vm.notificationPanel ? (
            <div
              id={vm.notificationPanel.id}
              role={vm.notificationPanel.role}
              className={`mt-3 flex flex-wrap items-center justify-between gap-2 rounded-md border px-3 py-2 text-sm ${
                vm.notificationPanel.tone === "warning"
                  ? "border-warning/30 bg-warning/10 text-warning"
                  : "border-border/70 bg-secondary/25 text-muted-foreground"
              }`}
            >
              <span>{vm.notificationPanel.text}</span>
              {vm.notificationPanel.action ? (
              <Button type="button" variant="outline" size="sm" onClick={() => void vm.requestNotifications()} aria-label={vm.notificationPanel.action.ariaLabel}>
                <Bell className="h-3.5 w-3.5" aria-hidden="true" />
                <span className="ml-1.5">{vm.notificationPanel.action.label}</span>
              </Button>
              ) : null}
            </div>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">New alert</CardTitle>
          <CardDescription>Pick a symbol, condition, and threshold. The alert fires once and then waits to be reset.</CardDescription>
        </CardHeader>
        <CardContent>
          <form
            onSubmit={(event) => {
              event.preventDefault();
              vm.submit();
            }}
            className="grid gap-3 md:grid-cols-[1fr_1.4fr_1fr_auto] md:items-end"
            aria-label="Create price alert"
          >
            <FormField label="Symbol" htmlFor="price-alert-symbol" error={vm.form.symbol ? vm.validation.symbolError : null}>
              <Input
                id="price-alert-symbol"
                value={vm.form.symbol}
                onChange={(event) => vm.setSymbol(event.target.value)}
                placeholder="e.g. AAPL"
                autoComplete="off"
                spellCheck={false}
                error={Boolean(vm.form.symbol && vm.validation.symbolError)}
              />
            </FormField>
            <FormField label="Condition" htmlFor="price-alert-condition">
              <div className="grid grid-cols-[1.4fr_0.9fr] gap-2">
                <Select
                  id="price-alert-condition"
                  value={vm.form.condition}
                  onChange={(event) => vm.setCondition(event.target.value as PriceAlertCondition)}
                >
                  {PRICE_ALERT_CONDITION_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </Select>
                <Select
                  id="price-alert-field"
                  value={vm.form.field}
                  onChange={(event) => vm.setField(event.target.value as PriceAlertField)}
                  aria-label="Price field"
                >
                  {PRICE_ALERT_FIELD_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </Select>
              </div>
            </FormField>
            <FormField label="Threshold" htmlFor="price-alert-threshold" error={vm.form.threshold ? vm.validation.thresholdError : null}>
              <Input
                id="price-alert-threshold"
                type="text"
                inputMode="decimal"
                value={vm.form.threshold}
                onChange={(event) => vm.setThreshold(event.target.value)}
                placeholder="e.g. 200.50"
                error={Boolean(vm.form.threshold && vm.validation.thresholdError)}
                autoComplete="off"
                spellCheck={false}
              />
            </FormField>
            <Button
              type="submit"
              disabled={vm.submitAction.disabled}
              disabledReason={vm.submitAction.disabledReason}
              aria-label={vm.submitAction.ariaLabel}
            >
              <Plus className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">Add alert</span>
            </Button>
          </form>
          <FormField label="Note (optional)" htmlFor="price-alert-note" className="mt-3">
            <Input
              id="price-alert-note"
              value={vm.form.note}
              onChange={(event) => vm.setNote(event.target.value)}
              placeholder="e.g. Watch for earnings call"
              maxLength={120}
              autoComplete="off"
            />
          </FormField>
          {vm.submitFeedback ? (
            <p role="status" aria-live="polite" aria-label={vm.submitFeedback.ariaLabel} className="mt-3 flex items-center gap-1.5 text-xs text-success">
              <Check className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{vm.submitFeedback.text}</span>
            </p>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <div>
              <CardTitle className="text-base">{vm.triggerSection.title}</CardTitle>
              <CardDescription>{vm.triggerSection.summary}</CardDescription>
            </div>
            {vm.triggerSection.hasRows ? (
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={vm.acknowledgeAllTriggers}
                  disabled={vm.triggerSection.acknowledgeAllAction.disabled}
                  disabledReason={vm.triggerSection.acknowledgeAllAction.disabledReason}
                  aria-label={vm.triggerSection.acknowledgeAllAction.ariaLabel}
                >
                  <CheckCheck className="h-3.5 w-3.5" aria-hidden="true" />
                  <span className="ml-1.5">{vm.triggerSection.acknowledgeAllAction.label}</span>
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={vm.clearTriggers}
                  disabled={vm.triggerSection.clearHistoryAction.disabled}
                  disabledReason={vm.triggerSection.clearHistoryAction.disabledReason}
                  aria-label={vm.triggerSection.clearHistoryAction.ariaLabel}
                >
                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                  <span className="ml-1.5">{vm.triggerSection.clearHistoryAction.label}</span>
                </Button>
              </div>
            ) : null}
          </div>
        </CardHeader>
        <CardContent>
          {vm.triggerSection.hasRows ? (
            <DenseDataTable
              columns={triggerColumns}
              rows={vm.triggerSection.rows}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.rowAriaLabel}
              emptyText={vm.triggerSection.emptyText}
              ariaLabel={vm.triggerSection.tableLabel}
              caption={vm.triggerSection.caption}
            />
          ) : (
            <p className="rounded-md border border-dashed border-border/70 bg-secondary/15 px-3 py-4 text-sm text-muted-foreground">
              {vm.triggerSection.emptyText}
            </p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">{vm.alertSection.title}</CardTitle>
          <CardDescription>{vm.alertSection.summary}</CardDescription>
        </CardHeader>
        <CardContent>
          {vm.alertSection.hasRows ? (
            <DenseDataTable
              columns={alertColumns}
              rows={vm.alertSection.rows}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.rowAriaLabel}
              emptyText={vm.alertSection.emptyText}
              ariaLabel={vm.alertSection.tableLabel}
              caption={vm.alertSection.caption}
            />
          ) : (
            <p className="rounded-md border border-dashed border-border/70 bg-secondary/15 px-3 py-4 text-sm text-muted-foreground">
              {vm.alertSection.emptyText}
            </p>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function AlertActionButton({ action, onClick }: { action: PriceAlertRowAction; onClick: () => void }) {
  const Icon = action.id === "snooze"
    ? AlarmClock
    : action.id === "wake" || action.id === "resume"
      ? Bell
      : action.id === "reset"
        ? RotateCcw
        : action.id === "pause"
          ? BellOff
          : Trash2;

  return (
    <Button type="button" variant="outline" size="sm" onClick={onClick} aria-label={action.ariaLabel}>
      <Icon className="h-3.5 w-3.5" aria-hidden="true" />
      <span className="ml-1">{action.label}</span>
    </Button>
  );
}

function handleAlertAction(vm: PriceAlertsScreenViewModel, action: PriceAlertRowAction, alertId: string) {
  switch (action.id) {
    case "wake":
      vm.wakeAlert(alertId);
      break;
    case "snooze":
      vm.snoozeAlert(alertId);
      break;
    case "reset":
      vm.resetAlert(alertId);
      break;
    case "pause":
    case "resume":
      vm.toggleAlert(alertId);
      break;
    case "delete":
      vm.deleteAlert(alertId);
      break;
  }
}

interface FormFieldProps {
  label: string;
  htmlFor: string;
  error?: string | null;
  children: ReactNode;
  className?: string;
}

function FormField({ label, htmlFor, error, children, className }: FormFieldProps) {
  return (
    <div className={`flex flex-col gap-1 ${className ?? ""}`}>
      <label htmlFor={htmlFor} className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </label>
      {children}
      {error ? (
        <p role="alert" className="text-[11px] text-danger">
          <X className="-mt-0.5 mr-1 inline h-3 w-3" aria-hidden="true" />
          {error}
        </p>
      ) : null}
    </div>
  );
}
