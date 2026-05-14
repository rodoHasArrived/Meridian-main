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
  usePriceAlertsScreenViewModel,
  type PriceAlertDetailAction,
  type PriceAlertDetailEmptyState,
  type PriceAlertDetailField,
  type PriceAlertsScreenViewModel,
  type PriceAlertRowAction,
  type PriceAlertRowDetailViewModel,
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
          <AlertActionButton
            action={row.deleteAction}
            onClick={() => handleAlertAction(vm, row.deleteAction, row.id)}
            describedBy={row.deleteConfirmationStatusId}
          />
          {row.deleteConfirmationStatus ? (
            <p
              id={row.deleteConfirmationStatusId ?? undefined}
              role="status"
              aria-live="polite"
              className="basis-full text-right text-[11px] leading-4 text-warning"
            >
              {row.deleteConfirmationStatus}
            </p>
          ) : null}
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
            <FormField
              label={vm.fields.symbol.label}
              htmlFor={vm.fields.symbol.id}
              helperId={vm.fields.symbol.helperId}
              helperText={vm.fields.symbol.helperText}
              error={vm.fields.symbol.errorText}
              errorId={vm.fields.symbol.errorId}
            >
              <Input
                id={vm.fields.symbol.id}
                value={vm.form.symbol}
                onChange={(event) => vm.setSymbol(event.target.value)}
                placeholder="e.g. AAPL"
                autoComplete="off"
                spellCheck={false}
                error={vm.fields.symbol.invalid}
                aria-describedby={vm.fields.symbol.describedBy}
                aria-errormessage={vm.fields.symbol.errorMessageId}
              />
            </FormField>
            <FormField label="Condition" htmlFor="price-alert-condition">
              <div className="grid grid-cols-[1.4fr_0.9fr] gap-2">
                <Select
                  id="price-alert-condition"
                  value={vm.form.condition}
                  onChange={(event) => vm.setCondition(event.target.value as PriceAlertCondition)}
                  aria-describedby={vm.conditionHelpId}
                >
                  {vm.conditionOptions.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </Select>
                <Select
                  id="price-alert-field"
                  value={vm.form.field}
                  onChange={(event) => vm.setField(event.target.value as PriceAlertField)}
                  aria-label={vm.fieldSelectAriaLabel}
                  aria-describedby={vm.fieldHelpId}
                >
                  {vm.fieldOptions.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </Select>
              </div>
              <div className="grid gap-1 text-[11px] leading-4 text-muted-foreground">
                <p id={vm.conditionHelpId}>{vm.conditionHelpText}</p>
                <p id={vm.fieldHelpId}>{vm.fieldHelpText}</p>
              </div>
            </FormField>
            <FormField
              label={vm.fields.threshold.label}
              htmlFor={vm.fields.threshold.id}
              helperId={vm.fields.threshold.helperId}
              helperText={vm.fields.threshold.helperText}
              error={vm.fields.threshold.errorText}
              errorId={vm.fields.threshold.errorId}
            >
              <Input
                id={vm.fields.threshold.id}
                type="text"
                inputMode="decimal"
                value={vm.form.threshold}
                onChange={(event) => vm.setThreshold(event.target.value)}
                placeholder="e.g. 200.50"
                error={vm.fields.threshold.invalid}
                aria-describedby={vm.fields.threshold.describedBy}
                aria-errormessage={vm.fields.threshold.errorMessageId}
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
            <div
              role="status"
              aria-live="polite"
              aria-label={vm.submitFeedback.ariaLabel}
              className="mt-3 flex flex-wrap items-center gap-2 text-xs text-success"
            >
              <span className="flex min-w-0 items-center gap-1.5">
                <Check className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                <span>{vm.submitFeedback.text}</span>
              </span>
              <Button asChild variant="outline" size="sm">
                <Link to={vm.submitFeedback.action.href} aria-label={vm.submitFeedback.action.ariaLabel}>
                  <LineChart className="h-3.5 w-3.5" aria-hidden="true" />
                  <span className="ml-1">{vm.submitFeedback.action.label}</span>
                </Link>
              </Button>
            </div>
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
            <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(260px,0.42fr)]">
              <DenseDataTable
                columns={triggerColumns}
                rows={vm.triggerSection.rows}
                getRowId={(row) => row.id}
                getRowAriaLabel={(row) => row.rowAriaLabel}
                getRowSelectAriaLabel={(row) => row.rowSelectAriaLabel}
                getRowAriaControls={(row) => row.detailPanelId}
                getRowAriaExpanded={(row) => row.expanded}
                onRowSelect={(row) => vm.selectTrigger(row.id)}
                selectedRowId={vm.triggerSection.selectedRowId}
                emptyText={vm.triggerSection.emptyText}
                ariaLabel={vm.triggerSection.tableLabel}
                caption={vm.triggerSection.caption}
              />
              <PriceAlertDetailPanel
                id={vm.triggerSection.detailPanelId}
                detail={vm.triggerSection.selectedDetail}
                emptyState={vm.triggerSection.detailEmptyState}
                onAction={(action, sourceId) => handleDetailAction(vm, action, sourceId)}
              />
            </div>
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
            <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(260px,0.42fr)]">
              <DenseDataTable
                columns={alertColumns}
                rows={vm.alertSection.rows}
                getRowId={(row) => row.id}
                getRowAriaLabel={(row) => row.rowAriaLabel}
                getRowSelectAriaLabel={(row) => row.rowSelectAriaLabel}
                getRowAriaControls={(row) => row.detailPanelId}
                getRowAriaExpanded={(row) => row.expanded}
                getRowClassName={(row) => row.rowClassName}
                onRowSelect={(row) => vm.selectAlert(row.id)}
                selectedRowId={vm.alertSection.selectedRowId}
                emptyText={vm.alertSection.emptyText}
                ariaLabel={vm.alertSection.tableLabel}
                caption={vm.alertSection.caption}
              />
              <PriceAlertDetailPanel
                id={vm.alertSection.detailPanelId}
                detail={vm.alertSection.selectedDetail}
                emptyState={vm.alertSection.detailEmptyState}
                onAction={(action, sourceId) => handleDetailAction(vm, action, sourceId)}
              />
            </div>
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

function PriceAlertDetailPanel({
  id,
  detail,
  emptyState,
  onAction
}: {
  id: string;
  detail: PriceAlertRowDetailViewModel | null;
  emptyState: PriceAlertDetailEmptyState;
  onAction: (action: PriceAlertDetailAction, sourceId: string) => void;
}) {
  return (
    <aside
      id={id}
      className="row-detail-panel h-fit min-w-0"
      role={detail ? "region" : "status"}
      aria-label={detail?.ariaLabel ?? emptyState.ariaLabel}
    >
      <div className="head">{detail?.eyebrow ?? "Price alert detail"}</div>
      <div className="body">
        {detail ? (
          <div className="space-y-3">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <h3 className="break-words text-sm font-semibold text-foreground">{detail.title}</h3>
                <p className="mt-1 break-words font-mono text-xs text-muted-foreground">{detail.subtitle}</p>
                <p className="mt-2 text-xs leading-5 text-muted-foreground">{detail.description}</p>
              </div>
              <Badge variant={detail.statusVariant} dot={detail.statusVariant !== "outline"}>
                {detail.statusLabel}
              </Badge>
            </div>
            <dl className="grid gap-2">
              {detail.fields.map((field) => (
                <PriceAlertDetailFieldRow key={field.id} field={field} />
              ))}
            </dl>
            <div className="flex flex-wrap items-center gap-2 pt-1">
              <Button asChild variant="outline" size="sm">
                <Link to={detail.quoteHref} aria-label={detail.quoteAriaLabel}>
                  <LineChart className="h-3.5 w-3.5" aria-hidden="true" />
                  <span className="ml-1">Quote</span>
                </Link>
              </Button>
              {detail.action ? (
                <DetailActionButton
                  action={detail.action}
                  onClick={() => onAction(detail.action!, detail.sourceId)}
                />
              ) : null}
            </div>
          </div>
        ) : (
          <div className="space-y-2">
            <h3 className="text-sm font-semibold text-foreground">{emptyState.title}</h3>
            <p className="text-xs leading-5 text-muted-foreground">{emptyState.description}</p>
          </div>
        )}
      </div>
    </aside>
  );
}

function PriceAlertDetailFieldRow({ field }: { field: PriceAlertDetailField }) {
  return (
    <div className="rounded-md border border-border/70 bg-background/45 px-3 py-2">
      <dt className="font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{field.label}</dt>
      <dd className={`mt-1 break-words font-mono text-xs ${detailFieldToneClass[field.tone]}`}>{field.value}</dd>
    </div>
  );
}

function AlertActionButton({
  action,
  onClick,
  describedBy
}: {
  action: PriceAlertRowAction;
  onClick: () => void;
  describedBy?: string | null;
}) {
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
    <Button
      type="button"
      variant="outline"
      size="sm"
      onClick={onClick}
      aria-label={action.ariaLabel}
      aria-describedby={describedBy ?? undefined}
    >
      <Icon className="h-3.5 w-3.5" aria-hidden="true" />
      <span className="ml-1">{action.label}</span>
    </Button>
  );
}

function DetailActionButton({ action, onClick }: { action: PriceAlertDetailAction; onClick: () => void }) {
  const Icon = action.id === "acknowledge"
    ? Check
    : action.id === "snooze"
      ? AlarmClock
      : action.id === "wake" || action.id === "resume"
        ? Bell
        : action.id === "reset"
          ? RotateCcw
          : action.id === "pause"
            ? BellOff
            : Trash2;

  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      onClick={onClick}
      disabled={action.disabled}
      disabledReason={action.disabledReason}
      aria-label={action.ariaLabel}
    >
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

function handleDetailAction(vm: PriceAlertsScreenViewModel, action: PriceAlertDetailAction, sourceId: string) {
  if (action.kind === "trigger") {
    vm.acknowledgeTrigger(sourceId);
    return;
  }

  switch (action.id) {
    case "wake":
      vm.wakeAlert(sourceId);
      break;
    case "snooze":
      vm.snoozeAlert(sourceId);
      break;
    case "reset":
      vm.resetAlert(sourceId);
      break;
    case "pause":
    case "resume":
      vm.toggleAlert(sourceId);
      break;
    case "delete":
      vm.deleteAlert(sourceId);
      break;
    case "acknowledge":
      break;
  }
}

const detailFieldToneClass: Record<PriceAlertDetailField["tone"], string> = {
  default: "text-foreground",
  muted: "text-muted-foreground",
  warning: "text-warning",
  success: "text-success"
};

interface FormFieldProps {
  label: string;
  htmlFor: string;
  helperId?: string;
  helperText?: string;
  error?: string | null;
  errorId?: string;
  children: ReactNode;
  className?: string;
}

function FormField({ label, htmlFor, helperId, helperText, error, errorId, children, className }: FormFieldProps) {
  return (
    <div className={`flex flex-col gap-1 ${className ?? ""}`}>
      <label htmlFor={htmlFor} className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </label>
      {children}
      {helperText ? (
        <p id={helperId} className="text-[11px] leading-4 text-muted-foreground">
          {helperText}
        </p>
      ) : null}
      {error ? (
        <p id={errorId} role="alert" className="text-[11px] leading-4 text-danger">
          <X className="-mt-0.5 mr-1 inline h-3 w-3" aria-hidden="true" />
          {error}
        </p>
      ) : null}
    </div>
  );
}
