import {
  Activity,
  AlertCircle,
  ArrowRight,
  BriefcaseBusiness,
  CheckCircle2,
  Database,
  FileText,
  FlaskConical,
  LineChart,
  Radio,
  RefreshCcw,
  Settings,
  Shield,
  Sparkles,
  TrendingUp,
  XCircle
} from "lucide-react";
import type { ElementType } from "react";
import { Link } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import {
  buildOverviewPortfolioPanel,
  useOverviewActivitySelectionViewModel,
  useOverviewStatusViewModel,
  type OverviewActivityDetail,
  type OverviewActivityRow,
  type OverviewBriefingBadgeVariant,
  type OverviewBriefingTone,
  type OverviewPortfolioPanel,
  type OverviewStatusBannerIcon,
  type OverviewValueBlocker,
  type PortfolioPanelTone
} from "@/screens/overview-screen.view-model";
import {
  buildTodayPanelViewModel,
  type TodayFillRow,
  type TodayMetric,
  type TodayMoverRow,
  type TodayOrderRow,
  type TodayPanelViewModel,
  type TodayQuickAction,
  type TodayTone
} from "@/screens/today-panel.view-model";
import type {
  PortfolioWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey
} from "@/types";

interface OverviewScreenProps {
  data: SystemOverviewResponse | null;
  session: SessionInfo | null;
  trading?: TradingWorkspaceResponse | null;
  portfolio?: PortfolioWorkspaceResponse | null;
}

const statusBannerIconConfig: Record<OverviewStatusBannerIcon, ElementType> = {
  healthy: CheckCircle2,
  warning: AlertCircle,
  offline: XCircle,
  pending: Radio
};

const blockerToneConfig = {
  default: {
    icon: Activity,
    rowClassName: "border-border/70 bg-secondary/20",
    iconClassName: "text-muted-foreground"
  },
  warning: {
    icon: AlertCircle,
    rowClassName: "border-warning/30 bg-warning/5",
    iconClassName: "text-warning"
  },
  danger: {
    icon: XCircle,
    rowClassName: "border-danger/30 bg-danger/5",
    iconClassName: "text-danger"
  }
} as const;

const workspaceIconConfig: Record<WorkspaceKey, { icon: ElementType; accent: string }> = {
  trading: { icon: TrendingUp, accent: "text-success" },
  portfolio: { icon: BriefcaseBusiness, accent: "text-paper" },
  accounting: { icon: Shield, accent: "text-warning" },
  reporting: { icon: FileText, accent: "text-primary" },
  strategy: { icon: FlaskConical, accent: "text-primary" },
  data: { icon: Database, accent: "text-live" },
  settings: { icon: Settings, accent: "text-muted-foreground" }
};

const activityColumns: DenseDataTableColumn<OverviewActivityRow>[] = [
  {
    id: "status",
    label: "Status",
    render: (event) => <Badge variant={event.badgeVariant} dot>{event.statusCode}</Badge>
  },
  {
    id: "source",
    label: "Source",
    render: (event) => <span className="font-mono text-xs text-muted-foreground">{event.source}</span>
  },
  {
    id: "time",
    label: "Time",
    render: (event) => <span className="font-mono text-xs text-muted-foreground">{event.timestampLabel}</span>
  },
  {
    id: "message",
    label: "Message",
    render: (event) => <span className="text-sm text-foreground">{event.message}</span>
  }
];

const todayMoverColumns: DenseDataTableColumn<TodayMoverRow>[] = [
  {
    id: "symbol",
    label: "Symbol",
    className: "font-mono font-semibold text-foreground",
    render: (row) => row.symbol
  },
  {
    id: "side",
    label: "Side",
    render: (row) => (
      <Badge variant={row.side === "Long" ? "outline" : "warning"} className="text-[10px]">
        {row.side}
      </Badge>
    )
  },
  {
    id: "quantity",
    label: "Qty",
    align: "right",
    className: "font-mono text-muted-foreground",
    render: (row) => row.quantity
  },
  {
    id: "mark",
    label: "Mark",
    align: "right",
    className: "font-mono text-muted-foreground",
    render: (row) => row.markPrice
  },
  {
    id: "day-pnl",
    label: "Day P&L",
    align: "right",
    className: "font-mono",
    render: (row) => <span className={todayMoverPnlToneClass[row.dayPnlTone]}>{row.dayPnl}</span>
  }
];

const todayOrderColumns: DenseDataTableColumn<TodayOrderRow>[] = [
  {
    id: "symbol",
    label: "Symbol",
    className: "font-mono font-semibold text-foreground",
    render: (row) => row.symbol
  },
  {
    id: "side",
    label: "Side",
    render: (row) => (
      <Badge variant={row.side === "Buy" ? "success" : "warning"} className="text-[10px]">
        {row.side}
      </Badge>
    )
  },
  {
    id: "quantity",
    label: "Qty",
    align: "right",
    className: "font-mono text-muted-foreground",
    render: (row) => row.quantity
  },
  {
    id: "price",
    label: "Price",
    align: "right",
    className: "font-mono text-muted-foreground",
    render: (row) => row.priceLabel
  },
  {
    id: "status",
    label: "Status",
    className: "font-mono text-muted-foreground",
    render: (row) => row.status
  },
  {
    id: "submitted",
    label: "Submitted",
    className: "font-mono text-muted-foreground",
    render: (row) => row.submittedLabel
  }
];

const todayFillColumns: DenseDataTableColumn<TodayFillRow>[] = [
  {
    id: "symbol",
    label: "Symbol",
    className: "font-mono font-semibold text-foreground",
    render: (row) => row.symbol
  },
  {
    id: "side",
    label: "Side",
    render: (row) => (
      <Badge variant={row.side === "Buy" ? "success" : "warning"} className="text-[10px]">
        {row.side}
      </Badge>
    )
  },
  {
    id: "quantity",
    label: "Qty",
    align: "right",
    className: "font-mono text-muted-foreground",
    render: (row) => row.quantity
  },
  {
    id: "price",
    label: "Price",
    align: "right",
    className: "font-mono text-muted-foreground",
    render: (row) => row.price
  },
  {
    id: "venue",
    label: "Venue",
    className: "font-mono text-muted-foreground",
    render: (row) => row.venue
  },
  {
    id: "time",
    label: "Time",
    className: "font-mono text-muted-foreground",
    render: (row) => row.timestampLabel
  }
];

export function OverviewScreen({ data, session, trading = null, portfolio = null }: OverviewScreenProps) {
  const vm = useOverviewStatusViewModel(data, session);
  const StatusIcon = statusBannerIconConfig[vm.statusBanner.icon];
  const portfolioPanel = buildOverviewPortfolioPanel(trading, portfolio);
  const todayPanel = buildTodayPanelViewModel(trading, portfolio);
  const activityVm = useOverviewActivitySelectionViewModel(vm.activityRows);

  return (
    <div className="space-y-6">
      {/* Status banner */}
      <div
        role={vm.statusBanner.role}
        aria-live={vm.statusBanner.ariaLive}
        aria-labelledby={vm.statusBanner.titleId}
        aria-describedby={vm.statusBanner.detailId ?? undefined}
        className={cn("flex items-center gap-3 rounded-lg border px-4 py-3", vm.statusBanner.containerClassName)}
      >
        <StatusIcon aria-hidden="true" className={cn("size-5 shrink-0", vm.statusBanner.iconClassName)} />
        <div className="flex-1">
          <p id={vm.statusBanner.titleId} className={cn("text-sm font-medium", vm.statusBanner.titleClassName)}>
            {vm.statusLabel}
          </p>
          {vm.statusBanner.detailParts ? (
            <p id={vm.statusBanner.detailId ?? undefined} className="text-xs text-muted-foreground mt-0.5">
              {vm.statusBanner.detailParts.providerSummary}
              {" · "}
              Storage: <span className={vm.statusBanner.detailParts.storageClassName}>
                {vm.statusBanner.detailParts.storageLabel}
              </span>
              {" · "}
              Last heartbeat: {vm.statusBanner.detailParts.lastHeartbeatLabel}
            </p>
          ) : vm.statusBanner.detailText ? (
            <p id={vm.statusBanner.detailId ?? undefined} className="text-xs text-muted-foreground mt-0.5">
              {vm.statusBanner.detailText}
            </p>
          ) : null}
        </div>
        <Button
          variant="ghost"
          size="sm"
          onClick={() => { void vm.refresh(); }}
          busy={vm.refreshCommand.busy}
          busyLabel={vm.refreshCommand.busyLabel}
          disabled={vm.refreshCommand.disabled}
          disabledReason={vm.refreshCommand.disabledReason}
          aria-label={vm.refreshCommand.ariaLabel}
          className="shrink-0"
        >
          <RefreshCcw className="size-4 mr-1.5" aria-hidden="true" />
          {vm.refreshCommand.label}
        </Button>
      </div>
      <span className="sr-only" aria-live="polite">{vm.refreshAnnouncement}</span>
      {vm.refreshErrorText && (
        <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
          {vm.refreshErrorText}
        </div>
      )}

      <TodayPanel panel={todayPanel} />

      <PortfolioPanel panel={portfolioPanel} />

      <div className="grid gap-6 xl:grid-cols-[1.05fr_0.95fr]">
        <Card className="border-border/70 bg-panel-strong">
          <CardHeader className="pb-3">
            <div className="eyebrow-label">Operator briefing</div>
            <CardTitle className="text-xl">Meridian workstation control tower</CardTitle>
            <CardDescription>
              System posture, session context, and operator follow-up aligned in one surface before you step into a workspace.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-3 md:grid-cols-2">
            {vm.briefingItems.map((item) => (
              <BriefingTile
                key={item.id}
                label={item.label}
                value={item.value}
                detail={item.detail}
                tone={item.tone}
                badgeVariant={item.badgeVariant}
                ariaLabel={item.ariaLabel}
              />
            ))}
          </CardContent>
        </Card>

        <Card className="border-border/70">
          <CardHeader className="pb-3">
            <div className="eyebrow-label">Priority routes</div>
            <CardTitle className="text-base">Move from posture to action</CardTitle>
            <CardDescription>
              Start with these lanes when triaging readiness, control evidence, or governed output for the current operating window.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <section
              aria-label={vm.valueBlockerRegionLabel}
              className="rounded-lg border border-border/70 bg-background/70 p-4"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <div className="eyebrow-label">Readiness blockers</div>
                  <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.valueBlockerSummary}</p>
                </div>
                <Badge variant={vm.hasValueBlockers ? "warning" : "success"}>
                  {vm.hasValueBlockers ? `${vm.valueBlockers.length} open` : "Clear"}
                </Badge>
              </div>
              {vm.hasValueBlockers ? (
                <ul className="mt-3 space-y-2">
                  {vm.valueBlockers.map((blocker) => (
                    <ValueBlockerRow key={blocker.id} blocker={blocker} />
                  ))}
                </ul>
              ) : null}
            </section>
            {vm.priorityRoutes.map((route) => (
              <div key={route.id} className="rounded-lg border border-border/70 bg-secondary/25 p-4">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <div className="eyebrow-label">{route.eyebrow}</div>
                    <h3 className="mt-2 text-sm font-semibold text-foreground">{route.title}</h3>
                  </div>
                  <Badge variant={route.badgeVariant}>{route.status}</Badge>
                </div>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{route.detail}</p>
                <p className="mt-2 text-xs leading-5 text-muted-foreground">{route.description}</p>
                <Button asChild variant="outline" size="sm" className="mt-4">
                  <Link to={route.href} aria-label={route.ariaLabel}>
                    {route.buttonLabel}
                    <ArrowRight className="size-3.5" aria-hidden="true" />
                  </Link>
                </Button>
              </div>
            ))}
          </CardContent>
        </Card>
      </div>

      {/* Metrics grid */}
      {vm.hasMetrics ? (
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          {vm.metrics.map((metric) => (
            <MetricCard key={metric.id} {...metric} />
          ))}
        </div>
      ) : (
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          {vm.fallbackStats.map((stat) => (
            <MetricCard key={stat.id} {...stat} />
          ))}
        </div>
      )}

      {/* Main content: recent events + workspace nav */}
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        {/* Recent activity */}
        <Card className="lg:col-span-2">
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Recent activity</CardTitle>
            <CardDescription>Latest system events across all workspaces.</CardDescription>
          </CardHeader>
          <CardContent>
            {vm.hasEvents ? (
              <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,0.44fr)]">
                <DenseDataTable
                  columns={activityColumns}
                  rows={activityVm.rows}
                  getRowId={(event) => event.id}
                  getRowAriaLabel={(event) => event.ariaLabel}
                  getRowSelectAriaLabel={(event) => event.selectAriaLabel}
                  getRowAriaControls={(event) => event.detailPanelId}
                  getRowAriaExpanded={(event) => event.expanded}
                  onRowSelect={(event) => activityVm.selectActivity(event.id)}
                  selectedRowId={activityVm.selectedRowId}
                  emptyText={vm.activityEmptyText}
                  ariaLabel={activityVm.tableLabel}
                  caption={activityVm.tableCaption}
                />
                <ActivityDetailPanel
                  id={activityVm.detailPanelId}
                  title={activityVm.detailPanelTitle}
                  description={activityVm.detailPanelDescription}
                  emptyText={activityVm.detailPanelEmptyText}
                  ariaLabel={activityVm.detailPanelAriaLabel}
                  detail={activityVm.selectedDetail}
                />
              </div>
            ) : (
              <p role="status" className="text-sm text-muted-foreground py-4 text-center">
                {vm.activityEmptyText}
              </p>
            )}
          </CardContent>
        </Card>

        {/* Quick navigation */}
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Workspaces</CardTitle>
            <CardDescription>{vm.workspaceSummary}</CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="space-y-2">
              {vm.workspaceLinks.map((ws) => {
                const iconConfig = workspaceIconConfig[ws.id];
                const Icon = iconConfig.icon;
                return (
                  <li key={ws.id}>
                    <Link
                      to={ws.href}
                      aria-label={ws.ariaLabel}
                      className="group flex items-center gap-3 rounded-md border border-transparent p-2.5 transition-colors hover:border-border/70 hover:bg-muted/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                    >
                      <Icon className={cn("size-4 shrink-0", iconConfig.accent)} />
                      <div className="flex-1 min-w-0">
                        <p className="text-sm font-medium leading-none">{ws.label}</p>
                        <p className="text-xs text-muted-foreground mt-0.5 truncate">{ws.description}</p>
                      </div>
                      <Badge variant={ws.badgeVariant} className="hidden shrink-0 md:inline-flex">{ws.status}</Badge>
                      <ArrowRight className="size-3.5 text-muted-foreground/50 shrink-0 group-hover:text-muted-foreground transition-colors" />
                    </Link>
                  </li>
                );
              })}
            </ul>
          </CardContent>
        </Card>
      </div>

    </div>
  );
}

// --- Sub-components ---

const toneClass: Record<OverviewBriefingTone, string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

interface BriefingTileProps {
  label: string;
  value: string;
  detail: string;
  tone: OverviewBriefingTone;
  badgeVariant: OverviewBriefingBadgeVariant | null;
  ariaLabel: string;
}

function BriefingTile({ label, value, detail, tone, badgeVariant, ariaLabel }: BriefingTileProps) {
  return (
    <div role="group" aria-label={ariaLabel} className="rounded-lg border border-border/70 bg-background/70 p-4">
      <div className="flex items-center justify-between gap-3">
        <div className="eyebrow-label">{label}</div>
        {badgeVariant ? <Badge variant={badgeVariant}>{value}</Badge> : null}
      </div>
      {!badgeVariant ? (
        <p className={cn("mt-2 text-sm font-semibold text-foreground", toneClass[tone])}>{value}</p>
      ) : null}
      <p className="mt-2 text-xs leading-5 text-muted-foreground">{detail}</p>
    </div>
  );
}

function ValueBlockerRow({ blocker }: { blocker: OverviewValueBlocker }) {
  const config = blockerToneConfig[blocker.tone];
  const Icon = config.icon;

  return (
    <li>
      <div
        role="group"
        aria-label={blocker.ariaLabel}
        className={cn("rounded-md border px-3 py-2", config.rowClassName)}
      >
        <div className="flex items-start gap-3">
          <Icon aria-hidden="true" className={cn("mt-0.5 size-4 shrink-0", config.iconClassName)} />
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={blocker.badgeVariant}>{blocker.badgeLabel}</Badge>
              <p className="text-sm font-medium text-foreground">{blocker.title}</p>
            </div>
            <p className="mt-1 text-xs leading-5 text-muted-foreground">{blocker.detail}</p>
          </div>
        </div>
        <Link
          to={blocker.href}
          className="mt-2 inline-flex items-center gap-1.5 text-xs font-medium text-primary underline-offset-4 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          aria-label={blocker.ariaLabel}
        >
          {blocker.actionLabel}
          <ArrowRight className="size-3" aria-hidden="true" />
        </Link>
      </div>
    </li>
  );
}

const portfolioPanelToneClass: Record<PortfolioPanelTone, string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
} as const;

const riskBadgeVariant: Record<PortfolioPanelTone, "outline" | "success" | "warning" | "danger"> = {
  default: "outline",
  success: "success",
  warning: "warning",
  danger: "danger"
} as const;

function PortfolioPanel({ panel }: { panel: OverviewPortfolioPanel }) {
  return (
    <Card className="border-border/70">
      <CardHeader className="pb-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <div className="eyebrow-label">Portfolio cockpit</div>
            <CardTitle className="mt-1 flex items-center gap-2 text-base">
              <TrendingUp className="size-4 text-success" aria-hidden="true" />
              Portfolio at a glance
            </CardTitle>
          </div>
          <div className="flex items-center gap-2">
            {panel.hasData && (
              <Badge variant={riskBadgeVariant[panel.riskTone]} dot={panel.riskTone === "success"}>
                {panel.riskState}
              </Badge>
            )}
            <Button asChild variant="outline" size="sm">
              <Link to="/trading">
                <ArrowRight className="size-3.5" aria-hidden="true" />
                Trading cockpit
              </Link>
            </Button>
          </div>
        </div>
        {panel.hasData && panel.brokerageLabel !== "—" && (
          <p className="mt-1 font-mono text-[11px] text-muted-foreground">{panel.brokerageLabel}</p>
        )}
      </CardHeader>
      <CardContent>
        {panel.hasData ? (
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              {panel.metrics.map((metric) => (
                <MetricCard key={metric.id} {...metric} />
              ))}
            </div>
            {panel.positions.length > 0 ? (
              <div>
                <p className="mb-2 font-mono text-[10px] font-medium uppercase tracking-[0.14em] text-muted-foreground">
                  Open positions
                </p>
                <ul className="space-y-1.5" aria-label="Open positions overview">
                  {panel.positions.map((pos) => (
                    <li
                      key={pos.key}
                      role="group"
                      aria-label={pos.ariaLabel}
                      className="flex flex-wrap items-center gap-x-4 gap-y-1 rounded-md border border-border/60 bg-secondary/20 px-3 py-2 text-xs"
                    >
                      <span className="min-w-[3.5rem] font-mono font-semibold text-foreground">{pos.symbol}</span>
                      <Badge variant={pos.side === "Long" ? "outline" : "warning"} className="shrink-0 text-[10px]">
                        {pos.side}
                      </Badge>
                      <span className="font-mono text-muted-foreground">{pos.quantity} shares</span>
                      <span className="font-mono text-muted-foreground">mark {pos.markPrice}</span>
                      <span className={cn("ml-auto font-mono font-medium", portfolioPanelToneClass[pos.pnlTone])}>
                        {pos.unrealizedPnl}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            ) : (
              <div className="rounded-md border border-border/60 bg-secondary/20 px-4 py-3 text-center text-xs text-muted-foreground">
                <p>{panel.emptyMessage}</p>
                <Button asChild variant="outline" size="sm" className="mt-3">
                  <Link to={panel.emptyAction.href} aria-label={panel.emptyAction.ariaLabel}>
                    {panel.emptyAction.label}
                    <ArrowRight className="size-3.5" aria-hidden="true" />
                  </Link>
                </Button>
              </div>
            )}
            {panel.riskSummary ? (
              <p className={cn("text-xs leading-5", portfolioPanelToneClass[panel.riskTone])}>
                {panel.riskSummary}
              </p>
            ) : null}
          </div>
        ) : (
          <div className="rounded-md border border-border/60 bg-secondary/20 px-4 py-6 text-center text-sm text-muted-foreground">
            <p>{panel.emptyMessage}</p>
            <Button asChild variant="outline" size="sm" className="mt-3">
              <Link to={panel.emptyAction.href} aria-label={panel.emptyAction.ariaLabel}>
                {panel.emptyAction.label}
                <ArrowRight className="size-3.5" aria-hidden="true" />
              </Link>
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function ActivityDetailPanel({
  id,
  title,
  description,
  emptyText,
  ariaLabel,
  detail
}: {
  id: string;
  title: string;
  description: string;
  emptyText: string;
  ariaLabel: string;
  detail: OverviewActivityDetail | null;
}) {
  return (
    <aside
      id={id}
      role="complementary"
      aria-label={ariaLabel}
      aria-live="polite"
      className="row-detail-panel h-fit min-w-0"
    >
      <div className="head flex items-center justify-between gap-3">
        <span>{title}</span>
        {detail ? <Badge variant={detail.badgeVariant}>{detail.badgeLabel}</Badge> : null}
      </div>
      <div className="body">
        {detail ? (
          <div role="region" aria-label={detail.ariaLabel} className="space-y-3">
            <div>
              <div className="eyebrow-label">{detail.eyebrow}</div>
              <h3 className="mt-2 text-sm font-semibold text-foreground">{detail.title}</h3>
              <p className="mt-1 font-mono text-xs text-muted-foreground">{detail.subtitle}</p>
            </div>
            <p className="text-sm leading-6 text-muted-foreground">{detail.description}</p>
            <dl className="grid gap-2 sm:grid-cols-2 xl:grid-cols-1">
              {detail.fields.map((field) => (
                <div key={field.label} className="rounded-sm border border-border/60 bg-background/35 px-2.5 py-2">
                  <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{field.label}</dt>
                  <dd className="mt-1 break-words font-mono text-xs text-foreground">{field.value}</dd>
                </div>
              ))}
            </dl>
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

const todayToneTextClass: Record<TodayTone, string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

const todayMoverPnlToneClass: Record<TodayTone, string> = {
  default: "text-muted-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

function TodayPanel({ panel }: { panel: TodayPanelViewModel }) {
  return (
    <Card className="border-border/70 bg-panel-strong">
      <CardHeader className="pb-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <div className="eyebrow-label">{panel.headline}</div>
            <CardTitle className="mt-1 flex items-center gap-2 text-lg">
              <Sparkles className="size-4 text-primary" aria-hidden="true" />
              Your day at a glance
            </CardTitle>
            <CardDescription className="mt-1">{panel.subheadline}</CardDescription>
          </div>
          {panel.brokerageLabel ? (
            <p className="font-mono text-[11px] text-muted-foreground">{panel.brokerageLabel}</p>
          ) : null}
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        {panel.hasData ? (
          <>
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              {panel.metrics.map((metric) => (
                <TodayMetricTile key={metric.id} metric={metric} />
              ))}
            </div>

            <div className="grid gap-4 xl:grid-cols-3">
              <TodayMoversCard panel={panel} />
              <TodayOrdersCard panel={panel} />
              <TodayFillsCard panel={panel} />
            </div>

            <TodayQuickActions
              label={panel.quickActionsLabel}
              eyebrow={panel.quickActionsEyebrow}
              actions={panel.quickActions}
            />
          </>
        ) : (
          <div className="rounded-md border border-border/60 bg-secondary/20 px-4 py-6 text-center text-sm text-muted-foreground">
            <p>{panel.emptyMessage}</p>
            <Button asChild variant="outline" size="sm" className="mt-3">
              <Link to={panel.emptyActionHref} aria-label={panel.emptyActionAriaLabel}>
                <Settings className="size-3.5" aria-hidden="true" />
                <span className="ml-1.5">{panel.emptyActionLabel}</span>
              </Link>
            </Button>
            <TodayQuickActions
              label={panel.quickActionsLabel}
              eyebrow={panel.quickActionsEyebrow}
              actions={panel.quickActions}
            />
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function TodayMetricTile({ metric }: { metric: TodayMetric }) {
  return (
    <div
      role="group"
      aria-label={metric.ariaLabel}
      className="rounded-lg border border-border/70 bg-background/70 p-4"
    >
      <div className="eyebrow-label">{metric.label}</div>
      <p className={cn("mt-2 text-xl font-semibold tabular-nums", todayToneTextClass[metric.tone])}>
        {metric.value}
      </p>
      <p className="mt-1 text-xs leading-5 text-muted-foreground">{metric.detail}</p>
    </div>
  );
}

function TodayMoversCard({ panel }: { panel: TodayPanelViewModel }) {
  return (
    <Card className="border-border/60 bg-background/60">
      <CardHeader className="pb-2">
        <div className="flex items-center justify-between gap-3">
          <CardTitle className="text-sm font-semibold">Top movers</CardTitle>
          <Button asChild variant="ghost" size="sm" className="h-7 px-2 text-xs">
            <Link to="/portfolio" aria-label="Open portfolio for all positions">
              Portfolio
              <ArrowRight className="size-3" aria-hidden="true" />
            </Link>
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-2 pt-0">
        {panel.hasMovers ? (
          <>
            <DenseDataTable
              columns={todayMoverColumns}
              rows={panel.movers}
              getRowId={(row) => row.key}
              getRowAriaLabel={(row) => row.ariaLabel}
              getRowClassName={(row) => row.rowClassName}
              emptyText={panel.moversEmptyMessage}
              ariaLabel={panel.moversTableLabel}
              caption={panel.moversTableCaption}
            />
            {panel.moversMoreLabel ? (
              <p role="status" className="pt-1 text-[11px] text-muted-foreground">{panel.moversMoreLabel}</p>
            ) : null}
          </>
        ) : (
          <p className="rounded-md border border-dashed border-border/60 px-3 py-3 text-xs text-muted-foreground">
            {panel.moversEmptyMessage}
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function TodayOrdersCard({ panel }: { panel: TodayPanelViewModel }) {
  return (
    <Card className="border-border/60 bg-background/60">
      <CardHeader className="pb-2">
        <div className="flex items-center justify-between gap-3">
          <CardTitle className="text-sm font-semibold">
            Open orders
            {panel.ordersTotal > 0 ? (
              <span className="ml-1.5 font-mono text-[11px] font-normal text-muted-foreground">
                ({panel.ordersTotal})
              </span>
            ) : null}
          </CardTitle>
          <Button asChild variant="ghost" size="sm" className="h-7 px-2 text-xs">
            <Link to="/trading" aria-label="Open trading cockpit for all orders">
              Trading
              <ArrowRight className="size-3" aria-hidden="true" />
            </Link>
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-2 pt-0">
        {panel.hasOrders ? (
          <>
            <DenseDataTable
              columns={todayOrderColumns}
              rows={panel.orders}
              getRowId={(row) => row.key}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={panel.ordersEmptyMessage}
              ariaLabel={panel.ordersTableLabel}
              caption={panel.ordersTableCaption}
            />
            {panel.ordersMoreLabel ? (
              <p role="status" className="pt-1 text-[11px] text-muted-foreground">{panel.ordersMoreLabel}</p>
            ) : null}
          </>
        ) : (
          <p className="rounded-md border border-dashed border-border/60 px-3 py-3 text-xs text-muted-foreground">
            {panel.ordersEmptyMessage}
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function TodayFillsCard({ panel }: { panel: TodayPanelViewModel }) {
  return (
    <Card className="border-border/60 bg-background/60">
      <CardHeader className="pb-2">
        <div className="flex items-center justify-between gap-3">
          <CardTitle className="text-sm font-semibold">
            Recent fills
            {panel.fillsTotal > 0 ? (
              <span className="ml-1.5 font-mono text-[11px] font-normal text-muted-foreground">
                ({panel.fillsTotal})
              </span>
            ) : null}
          </CardTitle>
          <Button asChild variant="ghost" size="sm" className="h-7 px-2 text-xs">
            <Link to="/trading" aria-label="Open trading cockpit for all fills">
              Trading
              <ArrowRight className="size-3" aria-hidden="true" />
            </Link>
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-2 pt-0">
        {panel.hasFills ? (
          <>
            <DenseDataTable
              columns={todayFillColumns}
              rows={panel.fills}
              getRowId={(row) => row.key}
              getRowAriaLabel={(row) => row.ariaLabel}
              emptyText={panel.fillsEmptyMessage}
              ariaLabel={panel.fillsTableLabel}
              caption={panel.fillsTableCaption}
            />
            {panel.fillsMoreLabel ? (
              <p role="status" className="pt-1 text-[11px] text-muted-foreground">{panel.fillsMoreLabel}</p>
            ) : null}
          </>
        ) : (
          <p className="rounded-md border border-dashed border-border/60 px-3 py-3 text-xs text-muted-foreground">
            {panel.fillsEmptyMessage}
          </p>
        )}
      </CardContent>
    </Card>
  );
}

const todayQuickActionIcon: Record<TodayQuickAction["id"], ElementType> = {
  "place-order": TrendingUp,
  "add-symbol": Database,
  "live-quote": LineChart,
  reconcile: Shield
};

function TodayQuickActions({ label, eyebrow, actions }: { label: string; eyebrow: string; actions: TodayQuickAction[] }) {
  return (
    <div
      aria-label={label}
      className="flex flex-wrap items-center gap-2"
      role="group"
    >
      <span className="text-xs font-semibold uppercase tracking-normal text-muted-foreground">
        {eyebrow}
      </span>
      {actions.map((action) => {
        const Icon = todayQuickActionIcon[action.id];
        return (
          <Button asChild variant="outline" size="sm" key={action.id}>
            <Link to={action.href} aria-label={action.ariaLabel}>
              <Icon className="size-3.5" aria-hidden="true" />
              <span className="ml-1.5">{action.label}</span>
            </Link>
          </Button>
        );
      })}
    </div>
  );
}
