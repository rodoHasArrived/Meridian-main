import { BriefcaseBusiness, LineChart, Wallet } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { MetricCard } from "@/components/meridian/metric-card";
import { cn } from "@/lib/utils";
import { usePortfolioScreenViewModel } from "@/screens/portfolio-screen.view-model";
import type {
  GovernanceWorkspaceResponse,
  ResearchWorkspaceResponse,
  TradingWorkspaceResponse
} from "@/types";

interface PortfolioScreenProps {
  trading: TradingWorkspaceResponse | null;
  research: ResearchWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
}

const pnlToneClass = {
  success: "text-success",
  danger: "text-danger",
  default: "text-foreground"
} as const;

const detailFieldToneClass = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
} as const;

const cashFlowBorderClass = {
  default: "border-border/70",
  success: "border-success/30",
  warning: "border-warning/30",
  danger: "border-danger/30"
} as const;

export function PortfolioScreen({ trading, research, governance }: PortfolioScreenProps) {
  const vm = usePortfolioScreenViewModel({ trading, research, governance });

  return (
    <div className="space-y-8">
      {vm.metricsFromTrading ? (
        <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          {vm.metricCards.map((metric) => (
            <MetricCard key={metric.id} {...metric} />
          ))}
        </section>
      ) : (
        <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          {vm.fallbackStats.map((stat) => (
            <Card key={stat.label}>
              <CardContent className="pt-5 pb-4">
                <div className="text-xs text-muted-foreground mb-2">{stat.label}</div>
                <p className="text-2xl font-semibold tabular-nums text-foreground">{stat.value}</p>
              </CardContent>
            </Card>
          ))}
        </section>
      )}

      <section className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
        <Card>
          <CardHeader>
            <div className="eyebrow-label">Portfolio Lane</div>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <Wallet className="h-5 w-5 text-primary" />
                  Open positions
                </CardTitle>
                <CardDescription className="mt-2">
                  Current open positions from the active paper session with exposure and unrealized P&amp;L.
                </CardDescription>
              </div>
              <Badge variant="outline" aria-label={vm.positionCountLabel}>
                {vm.positionCountLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent>
            {vm.hasPositions ? (
              <div className="overflow-x-auto rounded-lg border border-border/70">
                <table
                  className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm"
                  aria-label={vm.positionListLabel}
                >
                  <caption className="sr-only">
                    Select a position to update the holding detail panel.
                  </caption>
                  <thead className="bg-secondary/30">
                    <tr>
                      <th className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                        Symbol
                      </th>
                      <th className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                        Side
                      </th>
                      {["Qty", "Avg", "Mark", "Unrealized P&L", "Exposure"].map((col) => (
                        <th
                          key={col}
                          className="px-3 py-2 text-right font-semibold uppercase tracking-[0.14em] text-muted-foreground"
                        >
                          {col}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border/50">
                    {vm.positionRows.map((row) => (
                      <tr
                        key={row.id}
                        aria-label={row.ariaLabel}
                        aria-selected={row.isSelected}
                        className={cn(
                          "bg-background/20 transition-colors",
                          row.isSelected && "bg-primary/10"
                        )}
                      >
                        <td className="px-3 py-2">
                          <Button
                            type="button"
                            size="sm"
                            variant={row.isSelected ? "secondary" : "ghost"}
                            aria-pressed={row.isSelected}
                            aria-controls={vm.positionDetailId}
                            aria-label={row.selectAriaLabel}
                            onClick={() => vm.selectPosition(row.id)}
                            className="justify-start px-2 font-mono font-semibold"
                          >
                            {row.symbol}
                          </Button>
                        </td>
                        <td className="px-3 py-2 font-mono text-foreground">{row.side}</td>
                        <td className="px-3 py-2 text-right font-mono text-foreground">{row.quantity}</td>
                        <td className="px-3 py-2 text-right font-mono text-foreground">{row.avgPrice}</td>
                        <td className="px-3 py-2 text-right font-mono text-foreground">{row.markPrice}</td>
                        <td className={cn("px-3 py-2 text-right font-mono font-semibold", pnlToneClass[row.pnlTone])}>
                          {row.unrealizedPnl}
                        </td>
                        <td className="px-3 py-2 text-right font-mono text-foreground">{row.exposure}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <div
                role="status"
                className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning"
              >
                {vm.positionEmptyText}
              </div>
            )}
          </CardContent>
        </Card>

        <aside
          id={vm.positionDetailId}
          aria-live="polite"
          aria-label={vm.selectedPosition?.ariaLabel ?? "Portfolio holding detail"}
          className={cn(
            "h-fit min-w-0 overflow-hidden rounded-lg border bg-background/35 p-4",
            vm.selectedPosition
              ? cashFlowBorderClass[vm.selectedPosition.statusTone]
              : "border-border/70"
          )}
        >
          {vm.selectedPosition ? (
            <>
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="eyebrow-label">{vm.selectedPosition.statusTitle}</div>
                  <h3 className="mt-2 text-base font-semibold text-foreground">{vm.selectedPosition.title}</h3>
                  <p className="mt-1 font-mono text-xs text-muted-foreground">{vm.selectedPosition.subtitle}</p>
                </div>
                <Badge variant={vm.selectedPosition.statusTone === "default" ? "outline" : vm.selectedPosition.statusTone}>
                  Detail
                </Badge>
              </div>
              <p className="mt-3 text-sm leading-6 text-muted-foreground">{vm.selectedPosition.statusDetail}</p>
              <dl className="mt-4 grid gap-2">
                {vm.selectedPosition.fields.map((field) => (
                  <div
                    key={field.label}
                    className="grid grid-cols-[minmax(0,0.7fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2"
                  >
                    <dt className="text-xs text-muted-foreground">{field.label}</dt>
                    <dd className={cn("text-right font-mono text-xs", detailFieldToneClass[field.tone])}>
                      {field.value}
                    </dd>
                  </div>
                ))}
              </dl>
            </>
          ) : (
            <div role="status" className="text-sm leading-6 text-muted-foreground">
              <div className="eyebrow-label">No holding selected</div>
              <p className="mt-2">{vm.positionEmptyText}</p>
            </div>
          )}
        </aside>
      </section>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <LineChart className="h-4 w-4 text-primary" />
            Run-linked equity
          </CardTitle>
          <CardDescription>
            Strategy runs contributing to portfolio equity state. Promote runs to paper to connect execution evidence.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {vm.hasRuns ? (
            <div className="overflow-x-auto rounded-xl border border-border/70">
              <table
                className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm"
                aria-label="Run-linked equity"
              >
                <thead className="bg-secondary/30">
                  <tr>
                    {["Strategy", "Mode", "Status", "P&L", "Sharpe", "Promotion"].map((col) => (
                      <th
                        key={col}
                        className="px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground"
                      >
                        {col}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/50">
                  {vm.runRows.map((row) => (
                    <tr key={row.id} className="bg-background/20">
                      <td className="px-3 py-2 font-semibold text-foreground">{row.strategyName}</td>
                      <td className="px-3 py-2">
                        <Badge variant={row.modeBadgeVariant}>{row.mode}</Badge>
                      </td>
                      <td className="px-3 py-2 text-foreground">{row.status}</td>
                      <td className={cn("px-3 py-2 font-mono font-semibold", pnlToneClass[row.pnlTone])}>
                        {row.pnl}
                      </td>
                      <td className="px-3 py-2 font-mono text-foreground">{row.sharpe}</td>
                      <td className="px-3 py-2 text-muted-foreground">{row.promotionState ?? "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <p className="py-4 text-center text-sm text-muted-foreground">{vm.runEmptyText}</p>
          )}
        </CardContent>
      </Card>

      {vm.cashFlowSummary ? (
        <Card className={cn("border", cashFlowBorderClass[vm.cashFlowTone])}>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <BriefcaseBusiness className="h-4 w-4 text-primary" />
              Cash-flow posture
            </CardTitle>
            <CardDescription>{vm.cashFlowSummary}</CardDescription>
          </CardHeader>
          {vm.cashVarianceLabel ? (
            <CardContent>
              <div className="rounded-lg border border-border/70 bg-secondary/25 px-4 py-3 text-sm">
                <span className="text-muted-foreground">Net cash variance: </span>
                <span className="font-mono font-semibold text-foreground">{vm.cashVarianceLabel}</span>
              </div>
            </CardContent>
          ) : null}
        </Card>
      ) : null}
    </div>
  );
}
