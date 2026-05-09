import { Link } from "react-router-dom";
import { Activity, AlertCircle, CheckCircle2, LineChart, Plus, RefreshCw, Trash2 } from "lucide-react";
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
import { useWatchlistScreenViewModel, type WatchlistRowViewModel } from "@/screens/watchlist-screen.view-model";

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
            <label htmlFor={vm.inputId} className="sr-only">Add symbol</label>
            <Input
              id={vm.inputId}
              placeholder="Add symbols (e.g. MSFT, SPY)"
              value={vm.pendingSymbol}
              onChange={(event) => vm.setPendingSymbol(event.target.value)}
              autoComplete="off"
              spellCheck={false}
              error={vm.submitFeedback?.tone === "danger"}
              disabled={vm.submitting}
              aria-describedby={vm.inputHelpId}
            />
            <Button
              type="submit"
              variant="default"
              disabled={vm.addDisabled}
              disabledReason={vm.addDisabledReason}
              busy={vm.submitting}
              busyLabel="Adding..."
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
              busyLabel="Refreshing..."
            >
              <RefreshCw className={`h-4 w-4 ${vm.refreshing ? "animate-spin" : ""}`} aria-hidden="true" />
              <span className="ml-1.5">{vm.refreshButtonLabel}</span>
            </Button>
          </form>
          <p id="add-symbol-help" className="mt-2 text-xs text-muted-foreground">
            {vm.inputHelpText}
          </p>
          {vm.submitFeedback ? (
            <p
              id="add-symbol-feedback"
              role={vm.submitFeedback.tone === "success" ? "status" : "alert"}
              className={`mt-2 flex items-center gap-1.5 text-xs ${feedbackTextClass[vm.submitFeedback.tone]}`}
            >
              <FeedbackIcon className="h-3.5 w-3.5" aria-hidden="true" />
              {vm.submitFeedback.message}
            </p>
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
            }
          />
          {vm.listState === "error" ? (
            <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
              {vm.listDescription}
            </p>
          ) : vm.listState === "loading" ? (
            <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-4 py-3 text-sm text-muted-foreground">
              {vm.listDescription}
            </p>
          ) : (
            <>
              {vm.loadError ? (
                <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                  {vm.loadError}
                </p>
              ) : null}
              <p
                role={vm.quoteStatusTone === "danger" ? "alert" : "status"}
                className={`rounded-md border px-4 py-3 text-sm ${quoteStatusClass[vm.quoteStatusTone]}`}
              >
                {vm.quoteStatusLabel}
              </p>
              <DenseDataTable
                columns={buildColumns(vm.removeSymbol)}
                rows={vm.rows}
                getRowId={(row) => row.symbol}
                getRowAriaLabel={(row) => row.ariaLabel}
                emptyText={vm.listDescription}
                ariaLabel={vm.tableLabel}
                caption={vm.tableCaption}
              />
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

function buildColumns(removeSymbol: (symbol: string) => Promise<void>): DenseDataTableColumn<WatchlistRowViewModel>[] {
  return [
    {
      id: "symbol",
      label: "Symbol",
      className: "font-mono font-semibold text-foreground",
      render: (row) => row.symbol
    },
    {
      id: "status",
      label: "Status",
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
      render: (row) => <span className={lastToneClass[row.lastTone]}>{row.lastPriceLabel}</span>
    },
    {
      id: "spread",
      label: "Spread",
      align: "right",
      className: "font-mono text-muted-foreground",
      render: (row) => row.spreadLabel
    },
    {
      id: "quote-age",
      label: "Quote age",
      className: "text-muted-foreground",
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
          <Button asChild variant="outline" size="sm">
            <Link to={row.quoteHref} aria-label={row.quoteAriaLabel}>
              <LineChart className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="ml-1">Quote</span>
            </Link>
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={row.isRemoving}
            disabledReason={row.removeDisabledReason}
            onClick={() => void removeSymbol(row.symbol)}
            aria-label={row.removeAriaLabel}
          >
            <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
            <span className="ml-1">{row.removeLabel}</span>
          </Button>
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
