import type { KeyboardEvent } from "react";
import { useEffect, useRef, useState } from "react";
import { BriefcaseBusiness, FileCheck2, LineChart, Network, Settings, ShieldCheck, Wallet } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FinancialRecordExplorerShell } from "@/components/meridian/financial-record-explorer";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { MetricCard } from "@/components/meridian/metric-card";
import { getRunAttribution, getRunCashFlows, getRunEquityCurve, getRunFills } from "@/lib/api";
import { cn } from "@/lib/utils";
import {
  resolveBrokerageAccountFilterKeyCommand,
  type PortfolioBrokerageAccountRow,
  type PortfolioBrokeragePositionRow,
  type PortfolioPositionRow,
  type PortfolioRunDrillInData,
  type PortfolioWorkflowTaskAction,
  type PortfolioRunRow,
  usePortfolioScreenViewModel
} from "@/screens/portfolio-screen.view-model";
import type {
  BrokerageConnectionStatus,
  BrokerageHouseholdPortfolio,
  AccountingWorkspaceResponse,
  MultiAssetCoverageSummary,
  PortfolioWorkspaceResponse,
  StrategyWorkspaceResponse,
  TradingWorkspaceResponse
} from "@/types";

interface PortfolioScreenProps {
  portfolio?: PortfolioWorkspaceResponse | null;
  trading: TradingWorkspaceResponse | null;
  strategy: StrategyWorkspaceResponse | null;
  accounting: AccountingWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
  brokeragePortfolio?: BrokerageHouseholdPortfolio | null;
  multiAssetCoverage?: MultiAssetCoverageSummary | null;
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

const positionColumns: DenseDataTableColumn<PortfolioPositionRow>[] = [
  {
    id: "symbol",
    label: "Symbol",
    render: (row) => <span className="font-mono font-semibold text-foreground">{row.symbol}</span>
  },
  {
    id: "side",
    label: "Side",
    render: (row) => <span className="font-mono text-foreground">{row.side}</span>
  },
  {
    id: "quantity",
    label: "Qty",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.quantity}</span>
  },
  {
    id: "average",
    label: "Avg",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.avgPrice}</span>
  },
  {
    id: "mark",
    label: "Mark",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.markPrice}</span>
  },
  {
    id: "unrealized-pnl",
    label: "Unrealized P&L",
    align: "right",
    render: (row) => <span className={cn("font-mono font-semibold", pnlToneClass[row.pnlTone])}>{row.unrealizedPnl}</span>
  },
  {
    id: "exposure",
    label: "Exposure",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.exposure}</span>
  }
];

const runColumns: DenseDataTableColumn<PortfolioRunRow>[] = [
  {
    id: "strategy",
    label: "Strategy",
    render: (row) => <span className="font-semibold text-foreground">{row.strategyName}</span>
  },
  {
    id: "mode",
    label: "Mode",
    render: (row) => <Badge variant={row.modeBadgeVariant}>{row.mode}</Badge>
  },
  {
    id: "status",
    label: "Status",
    render: (row) => <span className="text-foreground">{row.status}</span>
  },
  {
    id: "pnl",
    label: "P&L",
    align: "right",
    render: (row) => <span className={cn("font-mono font-semibold", pnlToneClass[row.pnlTone])}>{row.pnl}</span>
  },
  {
    id: "sharpe",
    label: "Sharpe",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.sharpe}</span>
  },
  {
    id: "promotion",
    label: "Promotion",
    render: (row) => <span className="text-muted-foreground">{row.promotionState ?? "-"}</span>
  }
];

const brokerageAccountColumns: DenseDataTableColumn<PortfolioBrokerageAccountRow>[] = [
  {
    id: "account",
    label: "Account",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.kind}</span>
        <span className="block truncate text-xs text-muted-foreground">{row.label}</span>
      </span>
    )
  },
  {
    id: "health",
    label: "Health",
    render: (row) => <Badge variant={row.healthBadgeVariant}>{row.health}</Badge>
  },
  {
    id: "equity",
    label: "Equity",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.equity}</span>
  },
  {
    id: "cash",
    label: "Cash",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.cash}</span>
  },
  {
    id: "buying-power",
    label: "Buying power",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.buyingPower}</span>
  },
  {
    id: "synced",
    label: "Synced",
    render: (row) => <span className="font-mono text-xs text-muted-foreground">{row.syncedAt}</span>
  },
  {
    id: "warnings",
    label: "Warnings",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono text-xs", row.hasWarning ? "text-warning" : "text-success")}>
        {row.warningCount}
      </span>
    )
  }
];

const brokeragePositionColumns: DenseDataTableColumn<PortfolioBrokeragePositionRow>[] = [
  {
    id: "account",
    label: "Account",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.accountKind}</span>
        <span className="block truncate text-xs text-muted-foreground">{row.accountLabel}</span>
      </span>
    )
  },
  {
    id: "symbol",
    label: "Symbol",
    render: (row) => <span className="font-mono font-semibold text-foreground">{row.symbol}</span>
  },
  {
    id: "quantity",
    label: "Qty",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.quantity}</span>
  },
  {
    id: "average",
    label: "Avg",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.averagePrice}</span>
  },
  {
    id: "mark",
    label: "Mark",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.markPrice}</span>
  },
  {
    id: "market-value",
    label: "Market value",
    align: "right",
    render: (row) => <span className="font-mono text-foreground">{row.marketValue}</span>
  },
  {
    id: "unrealized-pnl",
    label: "Unrealized P&L",
    align: "right",
    render: (row) => <span className={cn("font-mono font-semibold", pnlToneClass[row.pnlTone])}>{row.unrealizedPnl}</span>
  },
  {
    id: "coverage",
    label: "Coverage",
    render: (row) => (
      <Badge variant={row.securityCoverage === "Covered" ? "success" : "warning"}>
        {row.securityCoverage}
      </Badge>
    )
  }
];

export function PortfolioScreen({
  portfolio,
  trading,
  strategy,
  accounting,
  brokerageConnection,
  brokeragePortfolio,
  multiAssetCoverage
}: PortfolioScreenProps) {
  const location = useLocation();
  const brokerageAccountButtonRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const shouldFocusBrokerageAccount = useRef(false);
  const drillInRequestId = useRef(0);
  const [selectedRunDrillIn, setSelectedRunDrillIn] = useState<PortfolioRunDrillInData | null>(null);
  const vm = usePortfolioScreenViewModel({
    portfolio,
    trading,
    strategy,
    accounting,
    brokerageConnection,
    brokeragePortfolio,
    multiAssetCoverage,
    selectedRunDrillIn,
    pathname: location.pathname
  });

  useEffect(() => {
    if (!shouldFocusBrokerageAccount.current) {
      return;
    }

    shouldFocusBrokerageAccount.current = false;
    brokerageAccountButtonRefs.current[vm.selectedBrokerageAccountKey]?.focus();
  }, [vm.selectedBrokerageAccountKey]);

  useEffect(() => {
    if (selectedRunDrillIn && selectedRunDrillIn.runId !== vm.selectedRun?.id) {
      setSelectedRunDrillIn(null);
    }
  }, [selectedRunDrillIn, vm.selectedRun?.id]);

  async function loadSelectedRunDrillIn() {
    const runId = vm.selectedRun?.id;
    if (!runId) {
      return;
    }

    const requestId = ++drillInRequestId.current;
    setSelectedRunDrillIn({
      runId,
      attribution: null,
      drawdownProfile: null,
      cashFlow: null,
      trades: null,
      isLoading: true,
      error: null
    });

    const [attribution, drawdownProfile, cashFlow, trades] = await Promise.allSettled([
      getRunAttribution(runId),
      getRunEquityCurve(runId),
      getRunCashFlows(runId),
      getRunFills(runId)
    ]);

    if (drillInRequestId.current !== requestId) {
      return;
    }

    const failures = [attribution, drawdownProfile, cashFlow, trades]
      .filter((result): result is PromiseRejectedResult => result.status === "rejected");
    setSelectedRunDrillIn({
      runId,
      attribution: attribution.status === "fulfilled" ? attribution.value : null,
      drawdownProfile: drawdownProfile.status === "fulfilled" ? drawdownProfile.value : null,
      cashFlow: cashFlow.status === "fulfilled" ? cashFlow.value : null,
      trades: trades.status === "fulfilled" ? trades.value : null,
      isLoading: false,
      error: failures.length > 0 ? `${failures.length} drill-in request${failures.length === 1 ? "" : "s"} failed.` : null
    });
  }

  function handleBrokerageAccountFilterKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const command = resolveBrokerageAccountFilterKeyCommand(event.key);
    if (!command) {
      return;
    }

    event.preventDefault();
    shouldFocusBrokerageAccount.current = true;
    vm.selectAdjacentBrokerageAccount(command);
  }

  const financialRecordExplorerScopeItems = [
    { id: "portfolio-source", label: "Source", value: vm.positionSourceLabel },
    { id: "portfolio-positions", label: "Positions", value: vm.positionCountLabel },
    { id: "portfolio-runs", label: "Runs", value: vm.runCountLabel },
    { id: "portfolio-brokerage", label: "Brokerage", value: vm.brokerageConnectionLabel }
  ];

  const financialRecordExplorerSavedViews = [
    {
      id: "portfolio-open-positions-run-evidence",
      label: "Open positions + run evidence",
      detail: `${vm.positionCountLabel}; ${vm.runCountLabel}; ${vm.runEvidenceChip.value}`,
      active: true
    },
    {
      id: "portfolio-selected-holding",
      label: "Selected holding",
      detail: `${vm.selectedPositionChip.label}: ${vm.selectedPositionChip.value}`
    },
    {
      id: "portfolio-selected-run",
      label: "Selected run proof",
      detail: `${vm.selectedRunChip.label}: ${vm.selectedRunChip.value}`
    }
  ];

  const financialRecordExplorerSummaryItems = [
    {
      id: "portfolio-open-position-count",
      label: "Open positions",
      value: vm.positionCountLabel,
      tone: vm.hasPositions ? "success" : "warning"
    },
    {
      id: "portfolio-selected-position",
      label: "Selected holding",
      value: vm.selectedPositionChip.value,
      tone: vm.selectedPosition?.statusTone ?? "warning"
    },
    {
      id: "portfolio-run-count",
      label: "Run evidence",
      value: vm.runEvidenceChip.value,
      tone: vm.hasRuns ? "success" : "warning"
    },
    {
      id: "portfolio-selected-run",
      label: "Selected run",
      value: vm.selectedRunChip.value,
      tone: vm.selectedRun?.statusTone ?? "warning"
    }
  ];

  const financialRecordExplorerAppliedFilters = [
    { id: "portfolio-filter-source", label: "Position source", value: vm.positionSourceLabel },
    { id: "portfolio-filter-holding", label: vm.selectedPositionChip.label, value: vm.selectedPositionChip.value },
    { id: "portfolio-filter-run", label: vm.selectedRunChip.label, value: vm.selectedRunChip.value },
    { id: "portfolio-filter-brokerage-account", label: "Brokerage account", value: vm.selectedBrokerageAccount.title }
  ];

  const financialRecordExplorerActions = [
    vm.selectedPosition
      ? {
          id: "portfolio-position-proof",
          label: `Holding proof: ${vm.selectedPosition.title}`,
          ariaLabel: vm.selectedPosition.ariaLabel
        }
      : {
          id: "portfolio-position-proof-empty",
          label: vm.positionDetailEmptyTitle
        },
    vm.selectedRun
      ? {
          id: "portfolio-run-evidence",
          label: vm.selectedRun.evidenceAction.label,
          href: vm.selectedRun.evidenceAction.href,
          ariaLabel: vm.selectedRun.evidenceAction.ariaLabel
        }
      : {
          id: "portfolio-run-proof-empty",
          label: vm.runDetailEmptyTitle
        },
    vm.multiAssetCoveragePanel
      ? {
          id: "portfolio-coverage-evidence",
          label: "Coverage proof",
          href: vm.multiAssetCoveragePanel.evidenceRoute,
          ariaLabel: `Open coverage endpoint ${vm.multiAssetCoveragePanel.evidenceRouteLabel}`
        }
      : {
          id: "portfolio-coverage-evidence-empty",
          label: "Coverage proof unavailable"
        }
  ];

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
              <div className="mt-4 flex flex-wrap gap-2" aria-label={vm.workflowTaskPanel.actionListLabel}>
                {vm.workflowTaskPanel.actions.map((action) => (
                  <Button key={action.id} asChild variant={action.variant} size="sm">
                    <Link to={action.href} aria-label={action.ariaLabel} aria-describedby={action.detailId}>
                      <PortfolioWorkflowTaskActionIcon actionId={action.id} />
                      {action.label}
                      <span id={action.detailId} className="sr-only">{action.detail}</span>
                    </Link>
                  </Button>
                ))}
              </div>
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

      {vm.multiAssetCoveragePanel ? (
        <Card className={cn("panel-surface border", cashFlowBorderClass[vm.multiAssetCoveragePanel.statusTone])}>
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="eyebrow-label">Coverage</div>
                <CardTitle className="mt-2 flex items-center gap-2 text-base">
                  <Network className="h-4 w-4 text-primary" />
                  {vm.multiAssetCoveragePanel.title}
                </CardTitle>
                <CardDescription>{vm.multiAssetCoveragePanel.description}</CardDescription>
              </div>
              <Badge variant={workflowStatusVariant(vm.multiAssetCoveragePanel.statusTone)}>
                {vm.multiAssetCoveragePanel.statusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              {vm.multiAssetCoveragePanel.chips.map((chip) => (
                <PortfolioChip key={chip.label} label={chip.label} value={chip.value} />
              ))}
            </div>
            <div className="grid gap-3">
              {vm.multiAssetCoveragePanel.groups.map((group) => (
                <section
                  key={group.id}
                  role="group"
                  aria-label={`${group.label} multi-asset coverage`}
                  className={cn("rounded-lg border bg-background/20 p-3", cashFlowBorderClass[group.statusTone])}
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="min-w-0">
                      <div className="text-sm font-semibold text-foreground">{group.label}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{group.summary}</div>
                    </div>
                    <Badge variant={workflowStatusVariant(group.statusTone)}>{group.label}</Badge>
                  </div>
                  <div className="mt-3 grid gap-3">
                    {group.rows.map((row) => (
                      <div key={row.id} className="rounded-md border border-border/60 bg-secondary/20 px-3 py-3">
                        <div className="flex flex-wrap items-start justify-between gap-3">
                          <div className="min-w-0">
                            <div className="font-semibold text-foreground">{row.displayName}</div>
                            <div className="mt-1 text-xs leading-5 text-muted-foreground">{row.summary}</div>
                          </div>
                          <Badge variant={workflowStatusVariant(row.statusTone)}>{row.statusLabel}</Badge>
                        </div>
                        <div className="mt-3 grid gap-2 md:grid-cols-3">
                          <div className="rounded-md border border-border/60 bg-background/30 px-3 py-2">
                            <div className="text-[11px] uppercase text-muted-foreground">Readiness</div>
                            <div className="mt-1 text-xs text-foreground">{row.readinessDetail}</div>
                          </div>
                          <div className="rounded-md border border-border/60 bg-background/30 px-3 py-2">
                            <div className="text-[11px] uppercase text-muted-foreground">Ledger</div>
                            <div className="mt-1 text-xs text-foreground">{row.ledgerLabel}</div>
                          </div>
                          <div className="rounded-md border border-border/60 bg-background/30 px-3 py-2">
                            <div className="text-[11px] uppercase text-muted-foreground">Reconciliation</div>
                            <div className="mt-1 text-xs text-foreground">{row.reconciliationLabel}</div>
                          </div>
                        </div>
                        <div className="mt-3 grid gap-3 xl:grid-cols-[1fr_0.9fr]">
                          <div>
                            <div className="text-[11px] uppercase text-muted-foreground">Evidence targets</div>
                            <div className="mt-2 flex flex-wrap gap-2">
                              {row.evidenceTargets.map((target) => (
                                <a
                                  key={target.id}
                                  href={target.href}
                                  aria-label={target.ariaLabel}
                                  className="inline-flex items-center gap-2 rounded-md border border-border/60 bg-background/40 px-2 py-1 text-xs hover:border-primary/50 hover:bg-primary/10"
                                >
                                  <Badge variant={workflowStatusVariant(target.statusTone)}>{target.statusLabel}</Badge>
                                  <span className="font-medium text-foreground">{target.label}</span>
                                  <span className="text-muted-foreground">{target.category}</span>
                                  <span className="font-mono text-muted-foreground">{target.requiredLabel}</span>
                                </a>
                              ))}
                            </div>
                          </div>
                          <div>
                            <div className="text-[11px] uppercase text-muted-foreground">Review targets</div>
                            <div className="mt-2 grid gap-2">
                              {row.blockerTargets.length > 0 ? row.blockerTargets.map((target) => (
                                target.href ? (
                                  <a
                                    key={target.id}
                                    href={target.href}
                                    aria-label={target.ariaLabel}
                                    className="rounded-md border border-warning/30 bg-warning/10 px-2 py-1 text-xs text-warning hover:border-warning/60"
                                  >
                                    <span className="font-semibold">{target.label}</span>
                                    <span className="ml-2">{target.source}</span>
                                    <span className="ml-2">{target.detail}</span>
                                  </a>
                                ) : (
                                  <div key={target.id} aria-label={target.ariaLabel} className="rounded-md border border-warning/30 bg-warning/10 px-2 py-1 text-xs text-warning">
                                    <span className="font-semibold">{target.label}</span>
                                    <span className="ml-2">{target.source}</span>
                                    <span className="ml-2">{target.detail}</span>
                                  </div>
                                )
                              )) : (
                                <div className="rounded-md border border-border/60 bg-background/30 px-2 py-1 text-xs text-muted-foreground">
                                  {row.blockerLabel}
                                </div>
                              )}
                            </div>
                          </div>
                        </div>
                        {row.drillThroughTargets.length > 0 ? (
                          <div className="mt-3">
                            <div className="text-[11px] uppercase text-muted-foreground">Drill-through targets</div>
                            <div className="mt-2 flex flex-wrap gap-2">
                              {row.drillThroughTargets.map((target) => (
                                <a
                                  key={target.id}
                                  href={target.href}
                                  aria-label={target.ariaLabel}
                                  className="inline-flex items-center gap-2 rounded-md border border-border/60 bg-background/40 px-2 py-1 text-xs hover:border-primary/50 hover:bg-primary/10"
                                >
                                  <Badge variant={workflowStatusVariant(target.statusTone)}>{target.statusLabel}</Badge>
                                  <span className="font-medium text-foreground">{target.label}</span>
                                  <span className="text-muted-foreground">{target.type}</span>
                                  <span className="font-mono text-muted-foreground">{target.source}</span>
                                </a>
                              ))}
                            </div>
                          </div>
                        ) : null}
                      </div>
                    ))}
                  </div>
                </section>
              ))}
            </div>
            {vm.multiAssetCoveragePanel.blockerMessages.length > 0 ? (
              <div className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                {vm.multiAssetCoveragePanel.blockerMessages.map((message) => (
                  <div key={message}>{message}</div>
                ))}
              </div>
            ) : null}
            <a href={vm.multiAssetCoveragePanel.evidenceRoute} className="text-xs font-medium text-primary hover:underline">
              Open coverage endpoint: {vm.multiAssetCoveragePanel.evidenceRouteLabel}
            </a>
          </CardContent>
        </Card>
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
          <section
            role="region"
            aria-label={vm.brokerageTrustSnapshot.regionLabel}
            className={cn("rounded-lg border bg-secondary/15 px-4 py-3", cashFlowBorderClass[vm.brokerageTrustSnapshot.statusTone])}
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <div className="eyebrow-label">Sync trust</div>
                <div className="mt-2 flex flex-wrap items-center gap-2">
                  <h3 className="text-base font-semibold text-foreground">{vm.brokerageTrustSnapshot.title}</h3>
                  <Badge variant={workflowStatusVariant(vm.brokerageTrustSnapshot.statusTone)}>
                    {vm.brokerageTrustSnapshot.statusLabel}
                  </Badge>
                </div>
                <p className="mt-2 max-w-4xl text-sm leading-6 text-muted-foreground">
                  {vm.brokerageTrustSnapshot.summary}
                </p>
              </div>
              <div className="flex flex-wrap items-center gap-2 lg:justify-end">
                {vm.brokerageTrustSnapshot.chips.map((chip) => (
                  <PortfolioChip key={chip.label} label={chip.label} value={chip.value} />
                ))}
              </div>
            </div>
            <dl className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
              {vm.brokerageTrustSnapshot.fields.map((field) => (
                <div
                  key={field.label}
                  className="grid grid-cols-[minmax(0,0.7fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/20 px-3 py-2"
                >
                  <dt className="text-xs text-muted-foreground">{field.label}</dt>
                  <dd className={cn("text-right font-mono text-xs", detailFieldToneClass[field.tone])}>
                    {field.value}
                  </dd>
                </div>
              ))}
            </dl>
          </section>

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

          <div className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
            <DenseDataTable
              columns={brokerageAccountColumns}
              rows={vm.brokerageAccountRows}
              getRowId={(row) => row.id}
              getRowAriaLabel={(row) => row.ariaLabel}
              getRowSelectAriaLabel={(row) => row.selectAriaLabel}
              getRowAriaControls={(row) => row.detailPanelId}
              getRowAriaExpanded={(row) => row.expanded}
              getRowClassName={(row) => row.rowClassName}
              onRowSelect={(row) => vm.selectBrokerageAccount(row.id)}
              selectedRowId={vm.selectedBrokerageAccount.id === "all" ? null : vm.selectedBrokerageAccount.id}
              emptyText={vm.brokerageAccountEmptyText}
              ariaLabel={vm.brokerageAccountsTableLabel}
              caption="Select a brokerage account row to filter live positions and update the account inspector."
            />
            <aside
              id={vm.brokerageAccountDetailId}
              role="complementary"
              aria-live="polite"
              aria-label={vm.selectedBrokerageAccount.ariaLabel}
              className={cn(
                "row-detail-panel h-fit min-w-0",
                cashFlowBorderClass[vm.selectedBrokerageAccount.statusTone]
              )}
            >
              <div className="head flex items-center justify-between gap-3">
                <span>{vm.selectedBrokerageAccount.statusTitle}</span>
                <Badge variant={vm.selectedBrokerageAccount.statusBadgeVariant}>
                  {vm.selectedBrokerageAccount.statusBadgeLabel}
                </Badge>
              </div>
              <div className="body">
                <div className="min-w-0">
                  <h3 className="text-sm font-semibold text-foreground">{vm.selectedBrokerageAccount.title}</h3>
                  <p className="mt-1 break-words font-mono text-xs text-muted-foreground">
                    {vm.selectedBrokerageAccount.subtitle}
                  </p>
                </div>
                <p className="mt-3 text-sm leading-6 text-muted-foreground">
                  {vm.selectedBrokerageAccount.statusDetail}
                </p>
                <dl className="mt-4 grid gap-2">
                  {vm.selectedBrokerageAccount.fields.map((field) => (
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
              </div>
            </aside>
          </div>

          {vm.hasBrokeragePositions ? (
            <div className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
              <DenseDataTable
                columns={brokeragePositionColumns}
                rows={vm.brokeragePositionRows}
                getRowId={(row) => row.id}
                getRowAriaLabel={(row) => row.ariaLabel}
                getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                getRowAriaControls={(row) => row.detailPanelId}
                getRowAriaExpanded={(row) => row.expanded}
                getRowClassName={(row) => row.rowClassName}
                onRowSelect={(row) => vm.selectBrokeragePosition(row.id)}
                selectedRowId={vm.selectedBrokeragePositionId}
                emptyText={vm.brokerageEmptyText}
                ariaLabel={vm.brokeragePositionsTableLabel}
                caption="Select a brokerage position row to update the live brokerage position inspector."
              />
              <aside
                id={vm.brokeragePositionDetailId}
                role="complementary"
                aria-live="polite"
                aria-label={vm.selectedBrokeragePosition?.ariaLabel ?? "Brokerage position detail"}
                className={cn(
                  "panel-surface h-fit min-w-0 overflow-hidden p-4",
                  vm.selectedBrokeragePosition
                    ? cashFlowBorderClass[vm.selectedBrokeragePosition.statusTone]
                    : "border-border/70"
                )}
              >
                {vm.selectedBrokeragePosition ? (
                  <>
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <div className="eyebrow-label">{vm.selectedBrokeragePosition.statusTitle}</div>
                        <h3 className="mt-2 text-base font-semibold text-foreground">{vm.selectedBrokeragePosition.title}</h3>
                        <p className="mt-1 break-words font-mono text-xs text-muted-foreground">
                          {vm.selectedBrokeragePosition.subtitle}
                        </p>
                      </div>
                      <Badge variant={vm.selectedBrokeragePosition.statusBadgeVariant}>
                        {vm.selectedBrokeragePosition.statusBadgeLabel}
                      </Badge>
                    </div>
                    <p className="mt-3 text-sm leading-6 text-muted-foreground">
                      {vm.selectedBrokeragePosition.statusDetail}
                    </p>
                    <dl className="mt-4 grid gap-2">
                      {vm.selectedBrokeragePosition.fields.map((field) => (
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
                    <div className="eyebrow-label">No brokerage position selected</div>
                    <p className="mt-2">{vm.brokerageEmptyText}</p>
                  </div>
                )}
              </aside>
            </div>
          ) : (
            <div className="flex flex-col gap-3 rounded-lg border border-border/70 bg-secondary/20 px-4 py-3 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
              <span role="status">{vm.brokerageEmptyText}</span>
              {vm.brokerageSetupAction ? (
                <Button asChild variant="outline" size="sm" className="w-fit bg-background/40">
                  <Link to={vm.brokerageSetupAction.href} aria-label={vm.brokerageSetupAction.ariaLabel}>
                    <Settings className="h-3.5 w-3.5" aria-hidden="true" />
                    <span>{vm.brokerageSetupAction.label}</span>
                  </Link>
                </Button>
              ) : null}
              {vm.brokerageSetupAction ? (
                <span className="sr-only">{vm.brokerageSetupAction.detail}</span>
              ) : null}
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

      <FinancialRecordExplorerShell
        explorerLabel="Financial Record Explorer"
        title="Portfolio proof workbench"
        titleId="portfolio-financial-record-explorer-title"
        description="Open holdings, run evidence, brokerage posture, and retained proof links stay anchored to the existing portfolio read model."
        scopeItems={financialRecordExplorerScopeItems}
        savedViews={financialRecordExplorerSavedViews}
        summaryItems={financialRecordExplorerSummaryItems}
        appliedFilters={financialRecordExplorerAppliedFilters}
        actions={financialRecordExplorerActions}
      >
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
                <PortfolioChip label={vm.selectedPositionChip.label} value={vm.selectedPositionChip.value} />
                <PortfolioChip label="Execution source" value={vm.positionSourceLabel} />
                <PortfolioChip label={vm.runEvidenceChip.label} value={vm.runEvidenceChip.value} />
              </div>
              {vm.hasPositions ? (
                <DenseDataTable
                  columns={positionColumns}
                  rows={vm.positionRows}
                  getRowId={(row) => row.id}
                  getRowAriaLabel={(row) => row.ariaLabel}
                  getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                  getRowAriaControls={(row) => row.detailPanelId}
                  getRowAriaExpanded={(row) => row.expanded}
                  onRowSelect={(row) => vm.selectPosition(row.id)}
                  selectedRowId={vm.selectedPosition?.id ?? null}
                  emptyText={vm.positionEmptyText}
                  ariaLabel={vm.positionListLabel}
                  caption="Select a position row to update the holding detail panel."
                />
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
                <div className="eyebrow-label">{vm.positionDetailEmptyTitle}</div>
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
                <PortfolioChip label={vm.selectedRunChip.label} value={vm.selectedRunChip.value} />
              </div>
            </div>
          </CardHeader>
          <CardContent>
            {vm.hasRuns ? (
              <div className="space-y-4">
                <section
                  aria-label={vm.runComparisonSummary.ariaLabel}
                  className={cn("rounded-lg border bg-secondary/15 p-4", cashFlowBorderClass[vm.runComparisonSummary.statusTone])}
                >
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="eyebrow-label">{vm.runComparisonSummary.title}</div>
                      <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.runComparisonSummary.description}</p>
                    </div>
                    <Badge variant={vm.runComparisonSummary.statusTone === "default" ? "outline" : vm.runComparisonSummary.statusTone}>
                      {vm.runComparisonSummary.cards.length} signals
                    </Badge>
                  </div>
                  <div className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
                    {vm.runComparisonSummary.cards.map((card) => (
                      <div key={card.id} className="rounded-md border border-border/60 bg-background/45 px-3 py-3">
                        <div className="eyebrow-label">{card.label}</div>
                        <div className={cn("mt-2 font-mono text-sm font-semibold", detailFieldToneClass[card.tone])}>{card.value}</div>
                        <p className="mt-2 text-xs leading-5 text-muted-foreground">{card.detail}</p>
                      </div>
                    ))}
                  </div>
                </section>
                <section
                  aria-label={vm.runDrillInSummary.ariaLabel}
                  className={cn("rounded-lg border bg-secondary/15 p-4", cashFlowBorderClass[vm.runDrillInSummary.statusTone])}
                >
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="eyebrow-label">{vm.runDrillInSummary.title}</div>
                      <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.runDrillInSummary.description}</p>
                    </div>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => void loadSelectedRunDrillIn()}
                      disabled={!vm.selectedRun || vm.runDrillInSummary.actionLabel === "Loading drill-ins..."}
                      disabledReason={!vm.selectedRun ? "Select a run before loading drill-in evidence." : undefined}
                      busy={vm.runDrillInSummary.actionLabel === "Loading drill-ins..."}
                      busyLabel={vm.runDrillInSummary.actionLabel}
                      aria-label={vm.runDrillInSummary.actionAriaLabel}
                    >
                      {vm.runDrillInSummary.actionLabel}
                    </Button>
                  </div>
                  <div className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                    {vm.runDrillInSummary.cards.map((card) => (
                      <div key={card.id} className="rounded-md border border-border/60 bg-background/45 px-3 py-3">
                        <div className="eyebrow-label">{card.label}</div>
                        <div className={cn("mt-2 font-mono text-sm font-semibold", detailFieldToneClass[card.tone])}>{card.value}</div>
                        <p className="mt-2 text-xs leading-5 text-muted-foreground">{card.detail}</p>
                      </div>
                    ))}
                  </div>
                  {vm.runDrillInSummary.bridgeRows.length > 0 ? (
                    <div className="mt-3 grid gap-2 lg:grid-cols-2" aria-label="Selected run realized unrealized bridge">
                      {vm.runDrillInSummary.bridgeRows.map((row) => (
                        <div
                          key={row.id}
                          className="grid gap-2 rounded-md border border-border/60 bg-background/45 px-3 py-2 sm:grid-cols-[minmax(0,0.45fr)_minmax(0,0.25fr)_minmax(0,1fr)]"
                        >
                          <div className="eyebrow-label">{row.label}</div>
                          <div className={cn("font-mono text-xs font-semibold sm:text-right", detailFieldToneClass[row.tone])}>
                            {row.value}
                          </div>
                          <p className="text-xs leading-5 text-muted-foreground">{row.detail}</p>
                        </div>
                      ))}
                    </div>
                  ) : null}
                  {vm.runDrillInSummary.tradeEvidenceRows.length > 0 ? (
                    <div className="mt-3 overflow-hidden rounded-md border border-border/60 bg-background/45">
                      <div className="border-b border-border/60 px-3 py-2">
                        <div className="eyebrow-label">Recent trade evidence</div>
                      </div>
                      <div className="grid divide-y divide-border/60">
                        {vm.runDrillInSummary.tradeEvidenceRows.map((row) => (
                          <div
                            key={row.id}
                            aria-label={row.ariaLabel}
                            className="grid gap-2 px-3 py-2 text-xs sm:grid-cols-[minmax(0,0.5fr)_minmax(0,0.45fr)_minmax(0,0.45fr)_minmax(0,0.45fr)_minmax(0,0.7fr)_minmax(0,0.7fr)]"
                          >
                            <span className="font-semibold text-foreground">{row.symbol}</span>
                            <span className="font-mono text-muted-foreground">{row.quantity}</span>
                            <span className="font-mono text-muted-foreground">{row.price}</span>
                            <span className="font-mono text-muted-foreground">{row.commission}</span>
                            <span className="font-mono text-muted-foreground">{row.filledAt}</span>
                            <span className="break-words font-mono text-muted-foreground">{row.accountId}</span>
                          </div>
                        ))}
                      </div>
                    </div>
                  ) : null}
                </section>
                <div className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
                <DenseDataTable
                  columns={runColumns}
                  rows={vm.runRows}
                  getRowId={(row) => row.id}
                  getRowAriaLabel={(row) => row.ariaLabel}
                  getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                  getRowAriaControls={(row) => row.detailPanelId}
                  getRowAriaExpanded={(row) => row.expanded}
                  onRowSelect={(row) => vm.selectRun(row.id)}
                  selectedRowId={vm.selectedRun?.id ?? null}
                  emptyText={vm.runEmptyText}
                  ariaLabel={vm.runListLabel}
                  caption="Select a run row to update the run evidence detail panel."
                />
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
                          <Link to={vm.selectedRun.evidenceAction.href} aria-label={vm.selectedRun.evidenceAction.ariaLabel}>
                            <Network className="h-4 w-4" />
                            {vm.selectedRun.evidenceAction.label}
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
                      <div className="eyebrow-label">{vm.runDetailEmptyTitle}</div>
                      <p className="mt-2">{vm.runEmptyText}</p>
                    </div>
                  )}
                </aside>
                </div>
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
      </FinancialRecordExplorerShell>

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

function PortfolioWorkflowTaskActionIcon({ actionId }: { actionId: PortfolioWorkflowTaskAction["id"] }) {
  const Icon =
    actionId === "provider-setup"
      ? Settings
      : actionId === "brokerage-sync"
        ? BriefcaseBusiness
      : actionId === "trading-readiness"
        ? ShieldCheck
        : actionId === "evidence"
          ? FileCheck2
          : Wallet;

  return <Icon className="h-4 w-4" aria-hidden="true" />;
}

function workflowStatusVariant(statusTone: "default" | "success" | "warning" | "danger") {
  return statusTone === "default" ? "outline" : statusTone;
}
