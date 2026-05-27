import { Link } from "react-router-dom";
import { Activity, AlertCircle, CheckCircle2, EyeOff, Eye, LineChart, Plus, RefreshCw, Settings, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { MetricCard } from "@/components/meridian/metric-card";
import { DenseDataTable, type DenseDataTableColumn, ToolbarStrip } from "@/components/meridian/ui-kit-primitives";
import {
  addSymbol as addSymbolApi,
  bulkAddSymbols,
  getLiveQuotesSnapshot,
  getSymbols,
  getSymbolsStatistics,
  removeSymbol as removeSymbolApi
} from "@/lib/api";
import { cn } from "@/lib/utils";
import {
  useWatchlistScreenViewModel,
  type WatchlistDetailFieldTone,
  type WatchlistRowViewModel,
  type WatchlistSelectedDetail,
  type WatchlistSortColumn
} from "@/screens/watchlist-screen.view-model";

export function WatchlistScreen() {
  const vm = useWatchlistScreenViewModel({
    getSymbols,
    getSymbolsStatistics,
    getLiveQuotesSnapshot,
    addSymbol: addSymbolApi,
    bulkAddSymbols,
    removeSymbol: removeSymbolApi
  });
  const FeedbackIcon = vm.submitFeedback?.tone === "success" ? CheckCircle2 : AlertCircle;

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="eyebrow-label">Data Lane</div>
          <CardTitle className="flex items-center gap-2">
            <Activity className="h-5 w-5 text-primary" />
            Symbol watchlist
          </CardTitle>
          <CardDescription>
            Add, remove, and monitor symbols subscribed to the live data pipeline. Open a symbol to view live quotes.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            {vm.stats.map((stat) => (
              <MetricCard key={stat.id} {...stat} />
            ))}
          </div>

          <form
            onSubmit={(event) => void vm.addPendingSymbol(event)}
            className="mt-5 flex flex-col gap-2 sm:flex-row sm:items-center"
            aria-label={vm.formLabel}
          >
            <label htmlFor={vm.addSymbolField.id} className="sr-only">{vm.addSymbolField.label}</label>
            <Input
              id={vm.addSymbolField.id}
              placeholder={vm.addSymbolField.placeholder}
              value={vm.pendingSymbol}
              onChange={(event) => vm.setPendingSymbol(event.target.value)}
              autoComplete="off"
              spellCheck={false}
              error={vm.addSymbolField.invalid}
              disabled={vm.addSymbolField.disabled}
              aria-describedby={vm.addSymbolField.describedBy}
              aria-errormessage={vm.addSymbolField.errorMessageId}
            />
            <Button
              type="submit"
              variant="default"
              disabled={vm.addDisabled}
              disabledReason={vm.addDisabledReason}
              busy={vm.submitting}
              busyLabel={vm.addButtonLabel}
              aria-label={vm.addButtonAriaLabel}
            >
              <Plus className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">{vm.addButtonLabel}</span>
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => void vm.refresh()}
              aria-label={vm.refreshButtonAriaLabel}
              disabled={vm.refreshDisabled}
              busy={vm.refreshing}
              busyLabel={vm.refreshButtonLabel}
            >
              <RefreshCw className={`h-4 w-4 ${vm.refreshing ? "animate-spin" : ""}`} aria-hidden="true" />
              <span className="ml-1.5">{vm.refreshButtonLabel}</span>
            </Button>
          </form>
          <p id={vm.addSymbolField.helperId} className="mt-2 text-xs text-muted-foreground">
            {vm.addSymbolField.helperText}
          </p>
          <div className="mt-4 flex flex-wrap items-center gap-2" aria-label={vm.starterPackGroupLabel}>
            <span className="text-xs font-semibold uppercase tracking-normal text-muted-foreground">{vm.starterPackEyebrow}</span>
            {vm.starterPacks.map((pack) => (
              <Button
                key={pack.id}
                type="button"
                variant="outline"
                size="sm"
                className="max-w-full flex-wrap justify-start"
                onClick={() => void vm.applyStarterPack(pack.id)}
                disabled={pack.disabled}
                disabledReason={pack.disabledReason}
                busy={pack.busy}
                busyLabel={pack.busyLabel}
                aria-label={pack.ariaLabel}
              >
                <Plus className="h-3.5 w-3.5" aria-hidden="true" />
                <span>{pack.label}</span>
                <span className="break-all text-left font-mono text-[11px] font-normal text-muted-foreground">{pack.symbolsLabel}</span>
              </Button>
            ))}
          </div>
          {vm.submitFeedback ? (
            <div
              id={vm.addSymbolField.feedbackId}
              role={vm.addSymbolField.feedbackRole}
              className={`mt-2 flex flex-wrap items-center gap-2 text-xs ${feedbackTextClass[vm.submitFeedback.tone]}`}
            >
              <span className="inline-flex min-w-0 items-center gap-1.5">
                <FeedbackIcon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                <span>{vm.submitFeedback.message}</span>
              </span>
              {vm.submitFeedback.details && vm.submitFeedback.details.length > 0 ? (
                <ul className="w-full list-disc space-y-1 pl-5 text-xs leading-5">
                  {vm.submitFeedback.details.map((detail) => (
                    <li key={detail}>{detail}</li>
                  ))}
                </ul>
              ) : null}
              {vm.submitFeedback.providerSetupHandoff ? (
                <Button asChild variant="outline" size="sm">
                  <Link to={vm.submitFeedback.providerSetupHandoff.href} aria-label={vm.submitFeedback.providerSetupHandoff.ariaLabel}>
                    <Settings className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>{vm.submitFeedback.providerSetupHandoff.label}</span>
                  </Link>
                </Button>
              ) : null}
              {vm.submitFeedback.nextActionHandoff ? (
                <Button asChild variant="outline" size="sm">
                  <Link to={vm.submitFeedback.nextActionHandoff.href} aria-label={vm.submitFeedback.nextActionHandoff.ariaLabel}>
                    <LineChart className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>{vm.submitFeedback.nextActionHandoff.label}</span>
                  </Link>
                </Button>
              ) : null}
            </div>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Subscribed symbols</CardTitle>
          <CardDescription>
            {vm.listDescription}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <ToolbarStrip
            items={vm.toolbarItems}
            ariaLabel="Symbol watchlist status"
            right={
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  type="button"
                  variant={vm.staleFilterCommand.pressed ? "secondary" : "outline"}
                  size="sm"
                  onClick={() => vm.setHideStale(!vm.hideStale)}
                  disabled={vm.staleFilterCommand.disabled}
                  disabledReason={vm.staleFilterCommand.disabledReason}
                  aria-pressed={vm.staleFilterCommand.pressed}
                  aria-label={vm.staleFilterCommand.ariaLabel}
                >
                  {vm.staleFilterCommand.pressed ? (
                    <EyeOff className="h-3.5 w-3.5" aria-hidden="true" />
                  ) : (
                    <Eye className="h-3.5 w-3.5" aria-hidden="true" />
                  )}
                  <span className="ml-1">{vm.staleFilterCommand.label}</span>
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => void vm.refreshQuotes()}
                  disabled={vm.quoteRefreshCommand.disabled}
                  disabledReason={vm.quoteRefreshCommand.disabledReason}
                  busy={vm.quoteRefreshCommand.busy}
                  busyLabel={vm.quoteRefreshCommand.label}
                  aria-label={vm.quoteRefreshCommand.ariaLabel}
                >
                  <RefreshCw className={`h-3.5 w-3.5 ${vm.quoteRefreshCommand.busy ? "animate-spin" : ""}`} aria-hidden="true" />
                  <span className="ml-1">{vm.quoteRefreshCommand.label}</span>
                </Button>
              </div>
            }
          />
          {vm.listState === "error" ? (
            <div
              role="alert"
              className="flex flex-col gap-3 rounded-md border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger sm:flex-row sm:items-center sm:justify-between"
            >
              <span>{vm.listDescription}</span>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="w-fit bg-background/50"
                onClick={() => void vm.refresh()}
                disabled={vm.listRetryCommand.disabled}
                disabledReason={vm.listRetryCommand.disabledReason}
                busy={vm.listRetryCommand.busy}
                busyLabel={vm.listRetryCommand.label}
                aria-label={vm.listRetryCommand.ariaLabel}
              >
                <RefreshCw className={`h-3.5 w-3.5 ${vm.listRetryCommand.busy ? "animate-spin" : ""}`} aria-hidden="true" />
                <span className="ml-1">{vm.listRetryCommand.label}</span>
              </Button>
            </div>
          ) : vm.listState === "loading" ? (
            <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-4 py-3 text-sm text-muted-foreground">
              {vm.listDescription}
            </p>
          ) : (
            <>
              {vm.loadError ? (
                <div role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  <div>{vm.loadError.summary}</div>
                  {vm.loadError.details.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                      {vm.loadError.details.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                </div>
              ) : null}
              <div
                role={vm.quoteStatusTone === "danger" ? "alert" : "status"}
                className={`flex flex-col gap-3 rounded-md border px-4 py-3 text-sm sm:flex-row sm:items-center sm:justify-between ${quoteStatusClass[vm.quoteStatusTone]}`}
              >
                <div className="min-w-0">
                  <div>{vm.quoteStatusLabel}</div>
                  {vm.quoteStatusDetails.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                      {vm.quoteStatusDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                </div>
                {vm.quoteProviderSetupHandoff ? (
                  <Button asChild variant="outline" size="sm" className="w-fit bg-background/40">
                    <Link to={vm.quoteProviderSetupHandoff.href} aria-label={vm.quoteProviderSetupHandoff.ariaLabel}>
                      <Settings className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>{vm.quoteProviderSetupHandoff.label}</span>
                    </Link>
                  </Button>
                ) : null}
              </div>
              <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(280px,0.42fr)]">
                <DenseDataTable
                  columns={buildColumns(vm.selectSymbol, vm.selectedSymbol, vm.detailPanelId, vm.removeSymbol)}
                  rows={vm.rows}
                  getRowId={(row) => row.symbol}
                  getRowAriaLabel={(row) => row.ariaLabel}
                  getRowAriaControls={() => vm.detailPanelId}
                  getRowAriaExpanded={(row) => row.symbol === vm.selectedRowId}
                  getRowSelectAriaLabel={(row) => row.rowSelectAriaLabel}
                  onRowSelect={(row) => vm.selectSymbol(row.symbol)}
                  selectedRowId={vm.selectedRowId}
                  emptyText={vm.listDescription}
                  ariaLabel={vm.tableLabel}
                  caption={vm.tableCaption}
                  sort={vm.sort}
                  onToggleSort={(columnId) => vm.toggleSort(columnId as WatchlistSortColumn)}
                />
                <WatchlistDetailPanel
                  title={vm.detailPanelTitle}
                  description={vm.detailPanelDescription}
                  emptyText={vm.detailPanelEmptyText}
                  id={vm.detailPanelId}
                  ariaLabel={vm.detailPanelAriaLabel}
                  detail={vm.selectedDetail}
                />
              </div>
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

const feedbackTextClass = {
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
} as const;

const quoteStatusClass = {
  default: "border-border/70 bg-secondary/25 text-muted-foreground",
  warning: "border-warning/30 bg-warning/10 text-warning",
  danger: "border-danger/30 bg-danger/10 text-danger"
} as const;

const detailFieldToneClass: Record<WatchlistDetailFieldTone, string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
};

function buildColumns(
  selectSymbol: (symbol: string) => void,
  selectedSymbol: string | null,
  detailPanelId: string,
  removeSymbol: (symbol: string) => Promise<void>
): DenseDataTableColumn<WatchlistRowViewModel>[] {
  return [
    {
      id: "symbol",
      label: "Symbol",
      className: "font-mono font-semibold text-foreground",
      sortable: true,
      render: (row) => row.symbol
    },
    {
      id: "status",
      label: "Status",
      sortable: true,
      render: (row) => <Badge variant={row.statusVariant} dot>{row.status}</Badge>
    },
    {
      id: "bid",
      label: "Bid x size",
      align: "right",
      className: "font-mono",
      render: (row) => row.bidLabel
    },
    {
      id: "ask",
      label: "Ask x size",
      align: "right",
      className: "font-mono",
      render: (row) => row.askLabel
    },
    {
      id: "last",
      label: "Last",
      align: "right",
      className: `font-mono`,
      sortable: true,
      render: (row) => <span className={lastToneClass[row.lastTone]}>{row.lastPriceLabel}</span>
    },
    {
      id: "change",
      label: "Chg",
      align: "right",
      className: "font-mono",
      render: (row) => <span className={detailFieldToneClass[row.changeTone]}>{row.changeLabel}</span>
    },
    {
      id: "change-percent",
      label: "Chg%",
      align: "right",
      className: "font-mono",
      sortable: true,
      render: (row) => <span className={detailFieldToneClass[row.changeTone]}>{row.changePercentLabel}</span>
    },
    {
      id: "day-range",
      label: "Day H/L",
      align: "right",
      className: "font-mono text-muted-foreground",
      render: (row) => row.dayRangeLabel
    },
    {
      id: "spread",
      label: "Spread",
      align: "right",
      className: "font-mono text-muted-foreground",
      sortable: true,
      render: (row) => row.spreadLabel
    },
    {
      id: "quote-age",
      label: "Quote age",
      className: "text-muted-foreground",
      sortable: true,
      render: (row) => <span className={row.quoteStale ? "text-warning" : undefined}>{row.quoteAgeLabel}</span>
    },
    {
      id: "provider",
      label: "Provider",
      className: "text-muted-foreground",
      render: (row) => row.providerLabel
    },
    {
      id: "actions",
      label: "Actions",
      align: "right",
      render: (row) => (
        <div className="flex justify-end gap-1.5">
          <Button
            type="button"
            variant={selectedSymbol === row.symbol ? "secondary" : "outline"}
            size="sm"
            aria-pressed={selectedSymbol === row.symbol}
            aria-controls={detailPanelId}
            aria-expanded={selectedSymbol === row.symbol}
            aria-label={row.inspectAriaLabel}
            onClick={() => selectSymbol(row.symbol)}
          >
            <Eye className="h-3.5 w-3.5" aria-hidden="true" />
            <span className="ml-1">{row.inspectLabel}</span>
          </Button>
          <Button asChild variant="outline" size="sm">
            <Link to={row.quoteHref} aria-label={row.quoteAriaLabel}>
              <LineChart className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="ml-1">Quote</span>
            </Link>
          </Button>
          <Button
            type="button"
            variant={row.removeButtonVariant}
            size="sm"
            disabled={row.isRemoving}
            disabledReason={row.removeDisabledReason}
            busy={row.isRemoving}
            busyLabel={row.removeLabel}
            onClick={() => void removeSymbol(row.symbol)}
            aria-label={row.removeAriaLabel}
            aria-describedby={row.removeStatusId ?? undefined}
          >
            <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
            <span className="ml-1">{row.removeLabel}</span>
          </Button>
          {row.removeStatusLabel ? (
            <span
              id={row.removeStatusId ?? undefined}
              role="status"
              aria-live="polite"
              className={cn(
                "inline-flex min-h-8 items-center rounded-sm border px-2 font-mono text-[10px] uppercase tracking-[0.12em]",
                removeStatusToneClass[row.removeStatusTone]
              )}
            >
              {row.removeStatusLabel}
            </span>
          ) : null}
        </div>
      )
    }
  ];
}

const lastToneClass = {
  success: "text-success",
  danger: "text-danger",
  default: "text-foreground"
} as const;

const removeStatusToneClass = {
  warning: "border-warning/35 bg-warning/10 text-warning",
  danger: "border-danger/35 bg-danger/10 text-danger"
} as const;

function WatchlistDetailPanel({
  title,
  description,
  emptyText,
  id,
  ariaLabel,
  detail
}: {
  title: string;
  description: string;
  emptyText: string;
  id: string;
  ariaLabel: string;
  detail: WatchlistSelectedDetail | null;
}) {
  return (
    <aside
      id={id}
      role="complementary"
      aria-label={ariaLabel}
      aria-live="polite"
      className="row-detail-panel h-fit min-w-0"
    >
      <div className="head">{title}</div>
      <div className="body">
        {detail ? (
          <div role="region" aria-label={detail.regionLabel} className="space-y-3">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <h3 className="font-mono text-base font-semibold text-foreground">{detail.title}</h3>
                <p className="mt-1 break-words font-mono text-xs text-muted-foreground">{detail.subtitle}</p>
              </div>
              <Badge variant={detail.statusVariant} aria-label={detail.statusAriaLabel}>
                {detail.statusLabel}
              </Badge>
            </div>
            <p className="text-sm leading-6 text-muted-foreground">{detail.description}</p>
            <dl className="grid gap-2 sm:grid-cols-2 xl:grid-cols-1 2xl:grid-cols-2">
              {detail.fields.map((field) => (
                <div key={field.label} className="rounded-sm border border-border/60 bg-background/35 px-2.5 py-2">
                  <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{field.label}</dt>
                  <dd className={`mt-1 break-words font-mono text-xs ${detailFieldToneClass[field.tone]}`}>
                    {field.value}
                  </dd>
                </div>
              ))}
            </dl>
            <Button asChild variant="outline" size="sm" className="bg-background/40">
              <Link to={detail.quoteActionHref} aria-label={detail.quoteActionAriaLabel}>
                <LineChart className="h-3.5 w-3.5" aria-hidden="true" />
                <span>{detail.quoteActionLabel}</span>
              </Link>
            </Button>
          </div>
        ) : (
          <div role="status" className="rounded-md border border-dashed border-border/70 bg-secondary/20 px-3 py-3">
            <div className="text-sm font-semibold text-foreground">{description}</div>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">{emptyText}</p>
          </div>
        )}
      </div>
    </aside>
  );
}
