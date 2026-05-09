import type { KeyboardEvent } from "react";
import { useEffect, useRef } from "react";
import { BriefcaseBusiness, LineChart, Network, Wallet } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { MetricCard } from "@/components/meridian/metric-card";
import { cn } from "@/lib/utils";
import {
  resolveBrokerageAccountFilterKeyCommand,
  usePortfolioScreenViewModel
} from "@/screens/portfolio-screen.view-model";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  GovernanceWorkspaceResponse,
  PortfolioWorkspaceResponse,
  ResearchWorkspaceResponse,
  TradingWorkspaceResponse
} from "@/types";

interface PortfolioScreenProps {
  portfolio?: PortfolioWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  research: ResearchWorkspaceResponse | null;
  governance: GovernanceWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
  brokeragePortfolio?: BrokerageHouseholdPortfolio | null;
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

export function PortfolioScreen({
  portfolio,
  trading,
  research,
  governance,
  brokerageConnection,
  brokeragePortfolio
}: PortfolioScreenProps) {
  const location = useLocation();
  const brokerageAccountButtonRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const shouldFocusBrokerageAccount = useRef(false);
  const vm = usePortfolioScreenViewModel({
    portfolio,
    trading,
    research,
    governance,
    brokerageConnection,
    brokeragePortfolio,
    pathname: location.pathname
  });

  useEffect(() => {
    if (!shouldFocusBrokerageAccount.current) {
      return;
    }

    shouldFocusBrokerageAccount.current = false;
    brokerageAccountButtonRefs.current[vm.selectedBrokerageAccountKey]?.focus();
  }, [vm.selectedBrokerageAccountKey]);

  function handleBrokerageAccountFilterKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const command = resolveBrokerageAccountFilterKeyCommand(event.key);
    if (!command) {
      return;
    }

    event.preventDefault();
    shouldFocusBrokerageAccount.current = true;
    vm.selectAdjacentBrokerageAccount(command);
  }

  return (
    <div className="space-y-8">
      <section
        role="region"
        aria-label="Portfolio workbench context"
        className="panel-surface-strong flex flex-wrap items-center justify-between gap-3 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">Portfolio lane</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            Execution-linked holdings
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            Holdings, run evidence, and cash posture stay aligned with the active paper workflow.
          </p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          {vm.headerChips.map((chip) => (
            <PortfolioChip key={chip.label} label={chip.label} value={chip.value} />
          ))}
        </div>
      </section>

      {vm.workflowTaskPanel ? (
        <section
          role="region"
          aria-label={vm.workflowTaskPanel.regionLabel}
          className={cn("panel-surface border p-4", cashFlowBorderClass[vm.workflowTaskPanel.statusTone])}
        >
          <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
            <div className="min-w-0">
              <div className="eyebrow-label">{vm.workflowTaskPanel.eyebrow}</div>
              <div className="mt-2 flex flex-wrap items-center gap-2">
                <h3 className="text-base font-semibold text-foreground">{vm.workflowTaskPanel.title}</h3>
                <Badge variant={workflowStatusVariant(vm.workflowTaskPanel.statusTone)}>
                  {vm.workflowTaskPanel.statusLabel}
                </Badge>
              </div>
              <p className="mt-2 max-w-4xl text-sm leading-6 text-muted-foreground">
                {vm.workflowTaskPanel.description}
              </p>
              <p className="mt-3 text-sm leading-6 text-foreground">{vm.workflowTaskPanel.selectedSummary}</p>
            </div>
            <div className="flex flex-wrap items-center gap-2 lg:justify-end">
              {vm.workflowTaskPanel.chips.map((chip) => (
                <PortfolioChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
            </div>
          </div>

          <div className="mt-4 grid gap-4 xl:grid-cols-[1fr_0.8fr]">
            <dl className="grid gap-2 sm:grid-cols-2">
              {vm.workflowTaskPanel.statusRows.map((field) => (
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
            <div className="rounded-lg border border-border/70 bg-background/20 p-3">
              <div className="eyebrow-label">Backend sources</div>
              <div className="mt-3 grid gap-2">
                {vm.workflowTaskPanel.backendLinks.map((link) => (
                  <a
                    key={link.id}
                    href={link.href}
                    aria-label={link.ariaLabel}
                    className="flex min-w-0 items-center justify-between gap-3 rounded-md border border-border/60 bg-secondary/20 px-3 py-2 text-xs transition-colors hover:border-primary/50 hover:bg-primary/10"
                  >
                    <span className="min-w-0 truncate font-medium text-foreground">{link.label}</span>
                    <span className="shrink-0 font-mono text-muted-foreground">
                      {link.method} {link.href}
                    </span>
                  </a>
                ))}
              </div>
            </div>
          </div>
        </section>
      ) : null}

      <Card className={cn("panel-surface border", cashFlowBorderClass[vm.brokerageConnectionTone])}>
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <div className="eyebrow-label">{vm.brokeragePanelEyebrow}</div>
              <CardTitle className="mt-2 flex items-center gap-2 text-base">
                <BriefcaseBusiness className="h-4 w-4 text-primary" />
                Live brokerage portfolio
              </CardTitle>
              <CardDescription>{vm.brokerageConnectionDetail}</CardDescription>
            </div>
            <Badge variant={vm.brokerageConnectionTone === "default" ? "outline" : vm.brokerageConnectionTone}>
              {vm.brokerageConnectionLabel}
            </Badge>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div
            className="flex flex-wrap items-center gap-2"
            role="group"
            aria-label={vm.brokerageAccountFilterLabel}
            onKeyDown={handleBrokerageAccountFilterKeyDown}
          >
            {vm.brokerageAccountOptions.map((option) => (
              <Button
                key={option.key}
                ref={(node) => {
                  brokerageAccountButtonRefs.current[option.key] = node;
                }}
                type="button"
                size="sm"
                variant={option.isSelected ? "secondary" : "outline"}
                aria-pressed={option.isSelected}
                aria-label={option.ariaLabel}
                tabIndex={option.tabIndex}
                onClick={() => vm.selectBrokerageAccount(option.key)}
              >
                {option.label}
              </Button>
            ))}
          </div>

          {vm.brokerageWarningRows.length > 0 ? (
            <div
              role="status"
              aria-label={vm.brokerageWarningCountLabel}
              className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning"
            >
              <div className="font-semibold text-warning">{vm.brokerageWarningCountLabel}</div>
              <ul className="mt-2 grid gap-2">
                {vm.brokerageWarningRows.map((warning) => (
                  <li key={warning.id} aria-label={warning.ariaLabel} className="grid gap-1 sm:grid-cols-[10rem_1fr]">
                    <span className="font-medium text-warning">{warning.label}</span>
                    <span className="text-warning/90">{warning.detail}</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          {vm.hasBrokerageAccounts ? (
            <div className="grid gap-3 md:grid-cols-3">
              {vm.brokerageAccountRows.map((account) => (
                <div key={account.id} className="rounded-lg border border-border/70 bg-secondary/20 px-4 py-3">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="text-sm font-semibold text-foreground">{account.kind}</div>
                      <div className="mt-1 truncate text-xs text-muted-foreground">{account.label}</div>
                    </div>
                    <Badge variant={account.health === "Healthy" ? "success" : account.health === "Failed" ? "danger" : "warning"}>
                      {account.health}
                    </Badge>
                  </div>
                  <dl className="mt-3 grid gap-2 text-xs">
                    <div className="flex items-center justify-between gap-3">
                      <dt className="text-muted-foreground">Equity</dt>
                      <dd className="font-mono text-foreground">{account.equity}</dd>
                    </div>
                    <div className="flex items-center justify-between gap-3">
                      <dt className="text-muted-foreground">Cash</dt>
                      <dd className="font-mono text-foreground">{account.cash}</dd>
                    </div>
                    <div className="flex items-center justify-between gap-3">
                      <dt className="text-muted-foreground">Synced</dt>
                      <dd className="font-mono text-foreground">{account.syncedAt}</dd>
                    </div>
                  </dl>
                  {account.hasWarning ? (
                    <div role="status" className="mt-3 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning">
                      {account.warningText}
                    </div>
                  ) : null}
                </div>
              ))}
            </div>
          ) : null}

          {vm.hasBrokeragePositions ? (
            <div className="data-grid-surface overflow-x-auto">
              <table
                className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm"
                aria-label={vm.brokeragePositionsTableLabel}
              >
                <thead className="bg-secondary/30">
                  <tr>
                    {["Account", "Symbol", "Qty", "Avg", "Mark", "Market value", "Unrealized P&L", "Coverage"].map((col) => (
                      <th
                        key={col}
                        className={cn(
                          "px-3 py-2 font-semibold uppercase tracking-[0.14em] text-muted-foreground",
                          ["Qty", "Avg", "Mark", "Market value", "Unrealized P&L"].includes(col) ? "text-right" : ""
                        )}
                      >
                        {col}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/50">
                  {vm.brokeragePositionRows.map((row) => (
                    <tr key={row.id} aria-label={row.ariaLabel} className="bg-background/20">
                      <td className="px-3 py-2">
                        <div className="font-semibold text-foreground">{row.accountKind}</div>
                        <div className="text-xs text-muted-foreground">{row.accountLabel}</div>
                      </td>
                      <td className="px-3 py-2 font-mono font-semibold text-foreground">{row.symbol}</td>
                      <td className="px-3 py-2 text-right font-mono text-foreground">{row.quantity}</td>
                      <td className="px-3 py-2 text-right font-mono text-foreground">{row.averagePrice}</td>
                      <td className="px-3 py-2 text-right font-mono text-foreground">{row.markPrice}</td>
                      <td className="px-3 py-2 text-right font-mono text-foreground">{row.marketValue}</td>
                      <td className={cn("px-3 py-2 text-right font-mono font-semibold", pnlToneClass[row.pnlTone])}>
                        {row.unrealizedPnl}
                      </td>
                      <td className="px-3 py-2 text-muted-foreground">{row.securityCoverage}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <div role="status" className="rounded-lg border border-border/70 bg-secondary/20 px-4 py-3 text-sm text-muted-foreground">
              {vm.brokerageEmptyText}
            </div>
          )}
        </CardContent>
      </Card>

      {vm.metricsFromTrading ? (
        <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          {vm.metricCards.map((metric) => (
            <MetricCard key={metric.id} {...metric} />
          ))}
        </section>
      ) : (
        <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          {vm.fallbackStats.map((stat) => (
            <MetricCard key={stat.id} {...stat} />
          ))}
        </section>
      )}

      <section className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
        <Card className="panel-surface">
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
            <div className="mb-4 flex flex-wrap items-center gap-2">
              <PortfolioChip label="Selected detail" value={vm.selectedPosition?.title ?? "None"} />
              <PortfolioChip label="Execution source" value={vm.positionSourceLabel} />
              <PortfolioChip label="Run evidence" value={vm.hasRuns ? `${vm.runRows.length} linked run${vm.runRows.length === 1 ? "" : "s"}` : "No linked runs"} />
            </div>
            {vm.hasPositions ? (
              <div className="data-grid-surface overflow-x-auto">
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
                          row.isSelected ? "bg-primary/10" : "hover:bg-secondary/20"
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
          role="complementary"
          aria-live="polite"
          aria-label={vm.selectedPosition?.ariaLabel ?? "Portfolio holding detail"}
          className={cn(
            "panel-surface h-fit min-w-0 overflow-hidden p-4",
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

      <Card className="panel-surface">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle className="flex items-center gap-2 text-base">
                <LineChart className="h-4 w-4 text-primary" />
                Run-linked equity
              </CardTitle>
              <CardDescription>
                Strategy runs contributing to portfolio equity state. Promote runs to paper to connect execution evidence.
              </CardDescription>
            </div>
            <div className="flex flex-wrap items-center justify-end gap-2">
              <PortfolioChip label="Runs" value={vm.runCountLabel} />
              <PortfolioChip label="Selected run" value={vm.selectedRun?.title ?? "None"} />
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {vm.hasRuns ? (
            <div className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
              <div className="data-grid-surface overflow-x-auto">
                <table
                  className="min-w-full divide-y divide-border/60 text-left text-xs sm:text-sm"
                  aria-label={vm.runListLabel}
                >
                  <caption className="sr-only">
                    Select a run to update the run evidence detail panel.
                  </caption>
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
                      <tr
                        key={row.id}
                        aria-label={row.ariaLabel}
                        aria-selected={row.isSelected}
                        className={cn(
                          "bg-background/20 transition-colors",
                          row.isSelected ? "bg-primary/10" : "hover:bg-secondary/20"
                        )}
                      >
                        <td className="px-3 py-2">
                          <Button
                            type="button"
                            size="sm"
                            variant={row.isSelected ? "secondary" : "ghost"}
                            aria-pressed={row.isSelected}
                            aria-controls={vm.runDetailId}
                            aria-label={row.selectAriaLabel}
                            onClick={() => vm.selectRun(row.id)}
                            className="justify-start px-2 text-left font-semibold"
                          >
                            {row.strategyName}
                          </Button>
                        </td>
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
              <aside
                id={vm.runDetailId}
                role="complementary"
                aria-live="polite"
                aria-label={vm.selectedRun?.ariaLabel ?? "Run evidence detail"}
                className={cn(
                  "panel-surface h-fit min-w-0 overflow-hidden p-4",
                  vm.selectedRun
                    ? cashFlowBorderClass[vm.selectedRun.statusTone]
                    : "border-border/70"
                )}
              >
                {vm.selectedRun ? (
                  <>
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <div className="eyebrow-label">{vm.selectedRun.statusTitle}</div>
                        <h3 className="mt-2 text-base font-semibold text-foreground">{vm.selectedRun.title}</h3>
                        <p className="mt-1 break-words font-mono text-xs text-muted-foreground">
                          {vm.selectedRun.subtitle}
                        </p>
                      </div>
                      <Badge variant={vm.selectedRun.statusBadgeVariant}>{vm.selectedRun.statusBadgeLabel}</Badge>
                    </div>
                    <p className="mt-3 text-sm leading-6 text-muted-foreground">{vm.selectedRun.statusDetail}</p>
                    <div className="mt-4">
                      <Button asChild size="sm" variant="outline">
                        <Link to={`/reporting/evidence?subjectKind=strategy-run&subjectId=${encodeURIComponent(vm.selectedRun.id)}`}>
                          <Network className="h-4 w-4" />
                          Open evidence packet
                        </Link>
                      </Button>
                    </div>
                    <dl className="mt-4 grid gap-2">
                      {vm.selectedRun.fields.map((field) => (
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
                    <div className="eyebrow-label">No run selected</div>
                    <p className="mt-2">{vm.runEmptyText}</p>
                  </div>
                )}
              </aside>
            </div>
          ) : (
            <div
              role="status"
              className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning"
            >
              {vm.runEmptyText}
            </div>
          )}
        </CardContent>
      </Card>

      {vm.cashFlowSummary ? (
        <Card className={cn("panel-surface border", cashFlowBorderClass[vm.cashFlowTone])}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <BriefcaseBusiness className="h-4 w-4 text-primary" />
                  Cash-flow posture
                </CardTitle>
                <CardDescription>{vm.cashFlowSummary}</CardDescription>
              </div>
              {vm.cashVarianceLabel ? <PortfolioChip label="Net variance" value={vm.cashVarianceLabel} /> : null}
            </div>
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

function PortfolioChip({ label, value }: { label: string; value: string }) {
  return (
    <span className="toolbar-chip">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </span>
  );
}

function workflowStatusVariant(statusTone: "default" | "success" | "warning" | "danger") {
  return statusTone === "default" ? "outline" : statusTone;
}
