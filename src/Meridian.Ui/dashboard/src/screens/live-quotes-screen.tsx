import { Link, useSearchParams } from "react-router-dom";
import {
  AlertCircle,
  ArrowDown,
  ArrowUp,
  CheckCircle2,
  ListPlus,
  LineChart,
  RefreshCw,
  Search,
  Send,
  TrendingUp
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { HistoricalChartCard } from "@/components/meridian/historical-chart";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { getLiveOrderbook, getLiveQuote, getLiveTrades, submitOrder } from "@/lib/api";
import {
  computeIntradayMetrics,
  useLiveQuotesScreenViewModel,
  type LiveQuotesBboPanelViewModel,
  type LiveQuotesDepthLadderViewModel,
  type LiveQuotesPanelState,
  type LiveQuotesPriceChartViewModel,
  type LiveQuotesSparklineViewModel,
  type LiveQuotesTradeRowViewModel,
  type QuickTradeTicketViewModel
} from "@/screens/live-quotes-screen.view-model";

export { computeIntradayMetrics };

export function LiveQuotesScreen() {
  const [searchParams, setSearchParams] = useSearchParams();
  const vm = useLiveQuotesScreenViewModel(
    { getLiveQuote, getLiveTrades, getLiveOrderbook, submitOrder },
    {
      routeSymbol: searchParams.get("symbol") ?? "",
      setRouteSymbol: (symbol) => setSearchParams({ symbol }, { replace: true })
    }
  );
  const { activeSymbol, lookup: symbolLookupVm, market: marketVm, quickTrade } = vm;

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="eyebrow-label">Data Lane</div>
          <CardTitle className="flex items-center gap-2">
            <LineChart className="h-5 w-5 text-primary" />
            Live quotes & order book
          </CardTitle>
          <CardDescription>
            Look up live bid/ask, recent trades, and L2 depth for any subscribed symbol. Refreshes every {vm.pollIntervalSecondsLabel}s.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form
            onSubmit={vm.submitLookup}
            className="flex flex-col gap-3 sm:flex-row sm:items-center"
            aria-label={symbolLookupVm.formLabel}
          >
            <label htmlFor={symbolLookupVm.inputId} className="sr-only">{symbolLookupVm.inputLabel}</label>
            <Input
              id={symbolLookupVm.inputId}
              placeholder={symbolLookupVm.inputPlaceholder}
              value={vm.symbolInput}
              onChange={(event) => vm.setSymbolInput(event.target.value)}
              leadingIcon={<Search className="h-4 w-4" />}
              autoComplete="off"
              spellCheck={false}
              error={symbolLookupVm.inputInvalid}
              aria-describedby={symbolLookupVm.statusId}
            />
            <div className="flex items-center gap-2">
              <Button
                type="submit"
                variant="default"
                disabled={symbolLookupVm.command.disabled}
                disabledReason={symbolLookupVm.command.disabledReason}
                aria-label={symbolLookupVm.command.ariaLabel}
              >
                {symbolLookupVm.command.label}
              </Button>
              <Button asChild variant="outline" size="sm">
                <Link to="/data/watchlist" aria-label="Open symbol watchlist">
                  <ListPlus className="h-4 w-4" aria-hidden="true" />
                  <span className="ml-1.5">Watchlist</span>
                </Link>
              </Button>
              {vm.refreshCommand ? (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => void vm.refreshMarketData()}
                  aria-label={vm.refreshCommand.ariaLabel}
                  disabled={vm.refreshCommand.disabled}
                  disabledReason={vm.refreshCommand.disabledReason}
                  busy={vm.refreshCommand.busy}
                  busyLabel="Refreshing..."
                >
                  <RefreshCw className={`h-4 w-4 ${vm.refreshCommand.busy ? "animate-spin" : ""}`} aria-hidden="true" />
                  <span className="ml-1.5">{vm.refreshCommand.label}</span>
                </Button>
              ) : null}
            </div>
          </form>
          <p
            id={symbolLookupVm.statusId}
            role={symbolLookupVm.status.role}
            aria-live={symbolLookupVm.status.role === "status" ? "polite" : undefined}
            className={`mt-2 text-xs ${symbolLookupVm.status.toneClass}`}
          >
            {symbolLookupVm.status.message}
          </p>
          {activeSymbol ? (
            <div className="mt-4 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <span className="font-semibold text-foreground">{activeSymbol}</span>
              {marketVm.venueLabel ? <Badge variant="outline">{marketVm.venueLabel}</Badge> : null}
              {marketVm.stale ? <Badge variant="warning">Stale</Badge> : null}
              <span>Last update {marketVm.lastUpdateLabel}</span>
            </div>
          ) : null}
        </CardContent>
      </Card>

      {!activeSymbol ? (
        <Card>
          <CardContent className="py-10 text-center text-sm text-muted-foreground">
            Enter a symbol above to see live BBO, recent trades, and L2 depth.
          </CardContent>
        </Card>
      ) : (
        <>
        <HistoricalChartCard symbol={activeSymbol} />
        <PriceChartCard
          viewModel={marketVm.priceChart}
        />
        <div className="grid gap-4 lg:grid-cols-[1fr_1fr]">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Best bid / offer</CardTitle>
              <CardDescription>Click bid or ask to seed the trade ticket</CardDescription>
            </CardHeader>
            <CardContent>
              {!marketVm.quoteState.showData || marketVm.bboPanels.length === 0 ? (
                <PanelStateMessage state={marketVm.quoteState} />
              ) : (
                <div className="space-y-3">
                  <PanelStateMessage state={marketVm.quoteState} />
                  <div className="grid gap-4 sm:grid-cols-2">
                    {marketVm.bboPanels.map((panel) => (
                      <BboPanel
                        key={panel.id}
                        panel={panel}
                        icon={panel.id === "bid" ? <ArrowDown className="h-4 w-4" aria-hidden="true" /> : <ArrowUp className="h-4 w-4" aria-hidden="true" />}
                        onSeed={() => quickTrade.seedTicket(panel.seedSide, panel.price)}
                      />
                    ))}
                    {marketVm.quoteMetrics.map((metric) => (
                      <MetricRow key={metric.id} label={metric.label} value={metric.value} />
                    ))}
                  </div>
                </div>
              )}
            </CardContent>
          </Card>

          <QuickTradeCard
            symbol={activeSymbol}
            vm={quickTrade}
          />

          <Card>
            <CardHeader>
              <CardTitle className="text-base">Order book (L2)</CardTitle>
              <CardDescription>{marketVm.orderbookDescription}</CardDescription>
            </CardHeader>
            <CardContent>
              {!marketVm.orderbookState.showData || !marketVm.orderbook ? (
                <PanelStateMessage state={marketVm.orderbookState} />
              ) : (
                <div className="space-y-3">
                  <PanelStateMessage state={marketVm.orderbookState} />
                  <DepthLadder
                    ladder={marketVm.depthLadder}
                    onSeedBuy={(price) => quickTrade.seedTicket("Buy", price)}
                    onSeedSell={(price) => quickTrade.seedTicket("Sell", price)}
                  />
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-base">Recent trades</CardTitle>
              <CardDescription>{marketVm.tradesDescription}</CardDescription>
            </CardHeader>
            <CardContent>
              {!marketVm.tradesState.showData ? (
                <PanelStateMessage state={marketVm.tradesState} />
              ) : (
                <div className="space-y-3">
                  <PanelStateMessage state={marketVm.tradesState} />
                  <TradesTable trades={marketVm.tradeDisplayRows} />
                </div>
              )}
            </CardContent>
          </Card>
        </div>
        </>
      )}
    </div>
  );
}

function MetricRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between border-b border-border/40 py-2 text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </div>
  );
}

function PanelStateMessage({ state }: { state: LiveQuotesPanelState }) {
  if (!state.message) {
    return null;
  }

  return (
    <p
      role={state.role}
      aria-live={state.role === "status" ? "polite" : undefined}
      className={`rounded-md border px-3 py-2 text-sm ${state.toneClass}`}
    >
      {state.message}
    </p>
  );
}

interface BboPanelProps {
  panel: LiveQuotesBboPanelViewModel;
  icon: React.ReactNode;
  onSeed?: () => void;
}

function BboPanel({ panel, icon, onSeed }: BboPanelProps) {
  const toneClass = panel.tone === "positive"
    ? "border-positive/30 bg-positive/5 text-positive"
    : "border-danger/30 bg-danger/5 text-danger";
  const interactive = typeof onSeed === "function" && Number.isFinite(panel.price) && panel.price > 0;
  const interactiveClass = interactive
    ? "cursor-pointer hover:bg-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
    : "";

  if (!interactive) {
    return (
      <div className={`rounded-md border px-3 py-3 text-left ${toneClass}`}>
        <div className="flex items-center gap-1.5 text-xs uppercase tracking-wide">
          {icon}
          <span>{panel.label}</span>
        </div>
        <div className="mt-2 font-mono text-2xl text-foreground">{panel.priceLabel}</div>
        <div className="mt-1 text-xs text-muted-foreground">{panel.sizeLabel}</div>
      </div>
    );
  }

  return (
    <button
      type="button"
      onClick={onSeed}
      aria-label={panel.seedLabel}
      className={`rounded-md border px-3 py-3 text-left transition-colors ${toneClass} ${interactiveClass}`}
    >
      <div className="flex items-center gap-1.5 text-xs uppercase tracking-wide">
        {icon}
        <span>{panel.label}</span>
      </div>
      <div className="mt-2 font-mono text-2xl text-foreground">{panel.priceLabel}</div>
      <div className="mt-1 text-xs text-muted-foreground">{panel.sizeLabel}</div>
    </button>
  );
}

interface DepthLadderProps {
  ladder: LiveQuotesDepthLadderViewModel;
  onSeedBuy?: (price: number) => void;
  onSeedSell?: (price: number) => void;
}

function DepthLadder({ ladder, onSeedBuy, onSeedSell }: DepthLadderProps) {
  return (
    <div className="grid grid-cols-2 gap-2 font-mono text-xs">
      <div>
        <div className="mb-1 flex justify-between text-muted-foreground">
          <span>Bid size</span>
          <span>Price</span>
        </div>
        {ladder.bids.map((level) => (
          <button
            type="button"
            key={level.id}
            onClick={() => onSeedSell?.(level.price)}
            aria-label={level.seedLabel}
            className="relative flex w-full justify-between rounded-sm px-2 py-1 text-left transition-colors hover:bg-positive/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          >
            <span
              aria-hidden="true"
              className="absolute inset-y-0 right-0 rounded-sm bg-positive/15"
              style={{ width: level.barWidth }}
            />
            <span className="relative">{level.sizeLabel}</span>
            <span className="relative text-positive">{level.priceLabel}</span>
          </button>
        ))}
      </div>
      <div>
        <div className="mb-1 flex justify-between text-muted-foreground">
          <span>Price</span>
          <span>Ask size</span>
        </div>
        {ladder.asks.map((level) => (
          <button
            type="button"
            key={level.id}
            onClick={() => onSeedBuy?.(level.price)}
            aria-label={level.seedLabel}
            className="relative flex w-full justify-between rounded-sm px-2 py-1 text-left transition-colors hover:bg-danger/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          >
            <span
              aria-hidden="true"
              className="absolute inset-y-0 left-0 rounded-sm bg-danger/15"
              style={{ width: level.barWidth }}
            />
            <span className="relative text-danger">{level.priceLabel}</span>
            <span className="relative">{level.sizeLabel}</span>
          </button>
        ))}
      </div>
    </div>
  );
}

interface QuickTradeCardProps {
  symbol: string;
  vm: QuickTradeTicketViewModel;
}

function QuickTradeCard({ symbol, vm }: QuickTradeCardProps) {
  const { ticket, submitCommand, status } = vm;
  const statusToneClass = quickTicketStatusClass[status.tone];
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <TrendingUp className="h-4 w-4 text-primary" aria-hidden="true" />
          Quick trade
        </CardTitle>
        <CardDescription>
          Submit a paper or live order for {symbol}. Click the bid or ask above to seed the price.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={vm.submitTicket} className="space-y-3" aria-describedby={status.id}>
          <div className="grid gap-2 sm:grid-cols-2">
            <div className="flex flex-col gap-1">
              <label htmlFor="quick-ticket-side" className="text-xs uppercase tracking-wide text-muted-foreground">Side</label>
              <Select
                id="quick-ticket-side"
                value={ticket.side}
                onChange={(event) => vm.updateField("side", event.target.value as "Buy" | "Sell")}
                className={vm.sideToneClass}
                aria-label="Order side"
              >
                <option value="Buy">Buy</option>
                <option value="Sell">Sell</option>
              </Select>
            </div>
            <div className="flex flex-col gap-1">
              <label htmlFor="quick-ticket-type" className="text-xs uppercase tracking-wide text-muted-foreground">Type</label>
              <Select
                id="quick-ticket-type"
                value={ticket.type}
                onChange={(event) => vm.updateField("type", event.target.value as "Market" | "Limit")}
                aria-label="Order type"
              >
                <option value="Limit">Limit</option>
                <option value="Market">Market</option>
              </Select>
            </div>
          </div>

          <div className="grid gap-2 sm:grid-cols-2">
            <div className="flex flex-col gap-1">
              <label htmlFor="quick-ticket-quantity" className="text-xs uppercase tracking-wide text-muted-foreground">Quantity</label>
              <Input
                id="quick-ticket-quantity"
                type="number"
                inputMode="numeric"
                min={1}
                step={1}
                placeholder="100"
                value={ticket.quantity}
                onChange={(event) => vm.updateField("quantity", event.target.value)}
                autoComplete="off"
                spellCheck={false}
                error={vm.quantityInvalid}
                aria-label="Order quantity in shares"
                aria-describedby={status.id}
              />
            </div>
            <div className="flex flex-col gap-1">
              <label htmlFor="quick-ticket-price" className="text-xs uppercase tracking-wide text-muted-foreground">
                {ticket.type === "Market" ? "Price (market)" : "Limit price"}
              </label>
              <Input
                id="quick-ticket-price"
                type="number"
                inputMode="decimal"
                min={0}
                step="0.01"
                placeholder={ticket.type === "Market" ? "Best available" : "0.00"}
                value={ticket.type === "Market" ? "" : ticket.limitPrice}
                onChange={(event) => vm.updateField("limitPrice", event.target.value)}
                disabled={vm.priceDisabled}
                autoComplete="off"
                spellCheck={false}
                error={vm.priceInvalid}
                aria-label="Limit price"
                aria-describedby={status.id}
              />
            </div>
          </div>

          <Button
            type="submit"
            variant={submitCommand.variant}
            className="w-full"
            disabled={submitCommand.disabled}
            disabledReason={submitCommand.disabledReason}
            busy={submitCommand.busy}
            busyLabel={submitCommand.busyLabel}
            aria-label={submitCommand.ariaLabel}
          >
            <Send className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">{submitCommand.label}</span>
          </Button>

          <div
            id={status.id}
            role={status.role}
            aria-live="polite"
            className={`flex min-h-[1.25rem] items-center gap-1.5 text-xs ${statusToneClass}`}
          >
            {status.showSuccessIcon ? <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" /> : null}
            {status.showErrorIcon ? <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" /> : null}
            <span>{status.message}</span>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

const quickTicketStatusClass = {
  default: "text-muted-foreground",
  success: "text-positive",
  danger: "text-danger"
} as const;

function TradesTable({ trades }: { trades: LiveQuotesTradeRowViewModel[] }) {
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full text-sm">
        <thead>
          <tr className="border-b border-border/60 text-left text-xs uppercase tracking-wide text-muted-foreground">
            <th className="px-2 py-2 font-medium">Time</th>
            <th className="px-2 py-2 font-medium text-right">Price</th>
            <th className="px-2 py-2 font-medium text-right">Size</th>
            <th className="px-2 py-2 font-medium">Aggressor</th>
            <th className="px-2 py-2 font-medium">Venue</th>
          </tr>
        </thead>
        <tbody className="font-mono">
          {trades.map((trade) => (
              <tr key={trade.id} className="border-b border-border/30">
                <td className="px-2 py-1.5">{trade.timeLabel}</td>
                <td className="px-2 py-1.5 text-right">{trade.priceLabel}</td>
                <td className="px-2 py-1.5 text-right">{trade.sizeLabel}</td>
                <td className={`px-2 py-1.5 ${tradeAggressorClass[trade.aggressorTone]}`}>{trade.aggressorLabel}</td>
                <td className="px-2 py-1.5 text-muted-foreground">{trade.venueLabel}</td>
              </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

const tradeAggressorClass = {
  positive: "text-positive",
  negative: "text-danger",
  muted: "text-muted-foreground"
} as const;

interface PriceChartCardProps {
  viewModel: LiveQuotesPriceChartViewModel;
}

function PriceChartCard({ viewModel }: PriceChartCardProps) {
  const toneClass = chartChangeToneClass[viewModel.changeTone];

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-1 sm:flex-row sm:items-end sm:justify-between">
          <div>
            <div className="eyebrow-label">Recent price action</div>
            <CardTitle className="flex items-center gap-2 text-base">
              <LineChart className="h-4 w-4 text-primary" aria-hidden="true" />
              {viewModel.title}
            </CardTitle>
            <CardDescription>
              {viewModel.description}
            </CardDescription>
          </div>
          <div className="flex flex-col items-start gap-0.5 sm:items-end" aria-live="polite">
            <span className="font-mono text-2xl text-foreground">{viewModel.lastPriceLabel}</span>
            <span className={`font-mono text-xs ${toneClass}`}>{viewModel.changeLabel}</span>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        {viewModel.statusMessage ? (
          <p
            role={viewModel.statusRole}
            aria-live={viewModel.statusRole === "status" ? "polite" : undefined}
            className={viewModel.statusRole === "alert" ? "text-sm text-danger" : "text-sm text-muted-foreground"}
          >
            {viewModel.statusMessage}
          </p>
        ) : (
          <div className="space-y-3">
            {viewModel.sparkline ? <PriceSparkline sparkline={viewModel.sparkline} /> : null}
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
              {viewModel.stats.map((stat) => (
                <ChartStat key={stat.id} label={stat.label} value={stat.value} />
              ))}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

const chartChangeToneClass = {
  positive: "text-positive",
  negative: "text-danger",
  default: "text-foreground"
} as const;

function ChartStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-border/60 bg-secondary/25 px-2.5 py-1.5">
      <div className="text-[10px] uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-0.5 font-mono text-sm text-foreground">{value}</div>
    </div>
  );
}

function PriceSparkline({ sparkline }: { sparkline: LiveQuotesSparklineViewModel }) {
  return (
    <svg
      viewBox={sparkline.viewBox}
      preserveAspectRatio="none"
      className="block h-44 w-full overflow-visible"
      role="img"
      aria-label={sparkline.ariaLabel}
    >
      <line
        x1={sparkline.guideStartX}
        x2={sparkline.guideEndX}
        y1={sparkline.highGuideY}
        y2={sparkline.highGuideY}
        stroke="currentColor"
        strokeOpacity="0.15"
        strokeDasharray="4 4"
      />
      <line
        x1={sparkline.guideStartX}
        x2={sparkline.guideEndX}
        y1={sparkline.lowGuideY}
        y2={sparkline.lowGuideY}
        stroke="currentColor"
        strokeOpacity="0.15"
        strokeDasharray="4 4"
      />
      <path d={sparkline.areaPath} fill={sparkline.strokeToken} fillOpacity="0.12" stroke="none" />
      <polyline
        fill="none"
        stroke={sparkline.strokeToken}
        strokeWidth="1.75"
        strokeLinejoin="round"
        strokeLinecap="round"
        points={sparkline.points}
      />
      <circle
        cx={sparkline.lastPoint.x}
        cy={sparkline.lastPoint.y}
        r="3.25"
        fill={sparkline.strokeToken}
        stroke="currentColor"
        strokeOpacity="0.4"
        strokeWidth="1"
      />
      <text
        x={sparkline.labelX}
        y={sparkline.highLabelY}
        textAnchor="end"
        fontFamily="IBM Plex Mono, ui-monospace"
        fontSize="10"
        fill="currentColor"
        fillOpacity="0.55"
      >
        {sparkline.highLabel}
      </text>
      <text
        x={sparkline.labelX}
        y={sparkline.lowLabelY}
        textAnchor="end"
        fontFamily="IBM Plex Mono, ui-monospace"
        fontSize="10"
        fill="currentColor"
        fillOpacity="0.55"
      >
        {sparkline.lowLabel}
      </text>
    </svg>
  );
}
