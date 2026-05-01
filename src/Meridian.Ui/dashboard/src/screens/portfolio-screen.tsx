import { BriefcaseBusiness, LineChart, Wallet } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { MetricCard } from "@/components/meridian/metric-card";
import { cn } from "@/lib/utils";
import { buildPortfolioScreenViewModel } from "@/screens/portfolio-screen.view-model";
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

const cashFlowBorderClass = {
  default: "border-border/70",
  success: "border-success/30",
  warning: "border-warning/30",
  danger: "border-danger/30"
} as const;

const modeVariant: Record<string, "paper" | "live" | "outline"> = {
  paper: "paper",
  live: "live",
  backtest: "outline"
};

export function PortfolioScreen({ trading, research, governance }: PortfolioScreenProps) {
  const vm = buildPortfolioScreenViewModel({ trading, research, governance });

  return (
    <div className="space-y-8">
      {trading?.metrics ? (
        <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          {trading.metrics.map((metric) => (
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

      <Card>
        <CardHeader>
          <div className="eyebrow-label">Portfolio Lane</div>
          <CardTitle className="flex items-center gap-2">
            <Wallet className="h-5 w-5 text-primary" />
            Open positions
          </CardTitle>
          <CardDescription>
            Current open positions from the active paper session with exposure and unrealized P&amp;L.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {vm.hasPositions ? (
            <div className="overflow-x-auto rounded-xl border border-border/70">
              <table
                className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm"
                aria-label="Open positions"
              >
                <thead className="bg-secondary/30">
                  <tr>
                    {["Symbol", "Side", "Qty", "Avg", "Mark", "Unrealized P&L", "Exposure"].map((col) => (
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
                  {vm.positionRows.map((row) => (
                    <tr key={row.symbol} aria-label={row.ariaLabel} className="bg-background/20">
                      <td className="px-3 py-2 font-mono font-semibold text-foreground">{row.symbol}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{row.side}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{row.quantity}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{row.avgPrice}</td>
                      <td className="px-3 py-2 font-mono text-foreground">{row.markPrice}</td>
                      <td className={cn("px-3 py-2 font-mono font-semibold", pnlToneClass[row.pnlTone])}>
                        {row.unrealizedPnl}
                      </td>
                      <td className="px-3 py-2 font-mono text-foreground">{row.exposure}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <p className="py-4 text-center text-sm text-muted-foreground">{vm.positionEmptyText}</p>
          )}
        </CardContent>
      </Card>

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
                        <Badge variant={modeVariant[row.mode] ?? "outline"}>{row.mode}</Badge>
                      </td>
                      <td className="px-3 py-2 text-foreground">{row.status}</td>
                      <td
                        className={cn(
                          "px-3 py-2 font-mono font-semibold",
                          row.pnl?.startsWith("+")
                            ? "text-success"
                            : row.pnl?.startsWith("-")
                              ? "text-danger"
                              : "text-foreground"
                        )}
                      >
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
