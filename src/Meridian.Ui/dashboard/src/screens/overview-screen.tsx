import {
  Activity,
  AlertCircle,
  ArrowRight,
  BarChart3,
  BriefcaseBusiness,
  CheckCircle2,
  Database,
  FileText,
  FlaskConical,
  Globe,
  LineChart,
  Radio,
  RefreshCcw,
  Settings,
  Shield,
  TrendingUp,
  XCircle
} from "lucide-react";
import type { ElementType } from "react";
import { Link } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import {
  useOverviewStatusViewModel,
  type OverviewActivityRow,
  type OverviewFallbackStatId
} from "@/screens/overview-screen.view-model";
import type { SessionInfo, SystemOverviewResponse, WorkspaceKey } from "@/types";

interface OverviewScreenProps {
  data: SystemOverviewResponse | null;
  session: SessionInfo | null;
}

const systemStatusConfig = {
  Healthy: {
    icon: CheckCircle2,
    className: "text-success",
    bannerClass: "border-success/30 bg-success/5"
  },
  Degraded: {
    icon: AlertCircle,
    className: "text-warning",
    bannerClass: "border-warning/30 bg-warning/5"
  },
  Offline: {
    icon: XCircle,
    className: "text-danger",
    bannerClass: "border-danger/30 bg-danger/5"
  }
} as const;

const storageHealthConfig = {
  Healthy: { className: "text-success" },
  Warning: { className: "text-warning" },
  Critical: { className: "text-danger" }
} as const;

const activityToneConfig = {
  default: {
    icon: Activity,
    iconClassName: "text-muted-foreground",
    rowClassName: "border-border/55 bg-secondary/20"
  },
  warning: {
    icon: AlertCircle,
    iconClassName: "text-warning",
    rowClassName: "border-warning/30 bg-warning/5"
  },
  danger: {
    icon: XCircle,
    iconClassName: "text-danger",
    rowClassName: "border-danger/30 bg-danger/5"
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

const fallbackStatIcons: Record<OverviewFallbackStatId, ElementType> = {
  providers: Globe,
  runs: LineChart,
  symbols: BarChart3,
  backfills: Activity
};

const priorityRouteCopy: Record<
  "trading" | "accounting" | "reporting",
  {
    eyebrow: string;
    title: string;
    detail: string;
    buttonLabel: string;
  }
> = {
  trading: {
    eyebrow: "Execution posture",
    title: "Keep the active session ready",
    detail: "Review paper-session evidence, promotion blockers, and live readiness before the next operator action.",
    buttonLabel: "Open trading cockpit"
  },
  accounting: {
    eyebrow: "Control evidence",
    title: "Clear ledger and trust-gate blockers",
    detail: "Resolve reconciliation, Security Master, and control follow-up before treating the day as sign-off ready.",
    buttonLabel: "Open accounting lane"
  },
  reporting: {
    eyebrow: "Governed outputs",
    title: "Prepare distribution-ready reporting",
    detail: "Check report-pack posture, approvals, and export readiness before circulating governed output.",
    buttonLabel: "Open reporting lane"
  }
};

export function OverviewScreen({ data, session }: OverviewScreenProps) {
  const vm = useOverviewStatusViewModel(data);
  const current = vm.current;
  const statusConfig = current ? systemStatusConfig[current.systemStatus] : null;
  const StatusIcon = statusConfig?.icon ?? Radio;
  const priorityRoutes = vm.workspaceLinks.filter(
    (workspace) => workspace.id === "trading" || workspace.id === "accounting" || workspace.id === "reporting"
  );
  const briefingItems = [
    {
      id: "session",
      label: "Session",
      value: session ? session.displayName : "Awaiting session",
      detail: session ? `${session.role} · ${session.commandCount} commands ready` : "Load a session to unlock command context",
      tone: "default" as const,
      badgeVariant: null
    },
    {
      id: "environment",
      label: "Operating mode",
      value: session ? session.environment : "Pending",
      detail: session ? `Current route ${session.activeWorkspace}` : "Environment is not loaded yet",
      tone: session?.environment === "live" ? "danger" as const : session?.environment === "paper" ? "success" as const : "default" as const,
      badgeVariant: session ? session.environment : null
    },
    {
      id: "providers",
      label: "Provider posture",
      value: vm.providerSummary ?? "Provider posture loading",
      detail: vm.storageLabel ? `Storage ${vm.storageLabel}` : "Storage posture loading",
      tone:
        current?.systemStatus === "Offline"
          ? "danger"
          : current?.systemStatus === "Degraded"
            ? "warning"
            : current
              ? "success"
              : "default",
      badgeVariant: null
    },
    {
      id: "heartbeat",
      label: "Heartbeat",
      value: vm.lastHeartbeatLabel ?? "Waiting for heartbeat",
      detail: vm.refreshErrorText ?? "Refresh the status banner to confirm the latest control-room posture",
      tone: vm.refreshErrorText ? "danger" as const : "default" as const,
      badgeVariant: null
    }
  ];

  return (
    <div className="space-y-6">
      {/* Status banner */}
      <div
        role={vm.statusBanner.role}
        aria-live={vm.statusBanner.ariaLive}
        aria-labelledby={vm.statusBanner.titleId}
        aria-describedby={vm.statusBanner.detailId ?? undefined}
        className={cn(
          "flex items-center gap-3 rounded-lg border px-4 py-3",
          statusConfig?.bannerClass ?? "border-border bg-muted/30"
        )}
      >
        <StatusIcon aria-hidden="true" className={cn("size-5 shrink-0", statusConfig?.className)} />
        <div className="flex-1">
          <p id={vm.statusBanner.titleId} className={cn("text-sm font-medium", statusConfig?.className)}>
            {vm.statusLabel}
          </p>
          {current && vm.providerSummary && vm.storageLabel && vm.lastHeartbeatLabel && (
            <p id={vm.statusBanner.detailId ?? undefined} className="text-xs text-muted-foreground mt-0.5">
              {vm.providerSummary}
              {" · "}
              Storage: <span className={storageHealthConfig[current.storageHealth].className}>
                {vm.storageLabel}
              </span>
              {" · "}
              Last heartbeat: {vm.lastHeartbeatLabel}
            </p>
          )}
          {!current && vm.statusBanner.detailText ? (
            <p id={vm.statusBanner.detailId ?? undefined} className="text-xs text-muted-foreground mt-0.5">
              {vm.statusBanner.detailText}
            </p>
          ) : null}
        </div>
        <Button
          variant="ghost"
          size="sm"
          onClick={() => { void vm.refresh(); }}
          disabled={vm.refreshing}
          aria-label={vm.refreshAriaLabel}
          className="shrink-0"
        >
          <RefreshCcw className={cn("size-4 mr-1.5", vm.refreshing && "animate-spin")} />
          {vm.refreshButtonLabel}
        </Button>
      </div>
      <span className="sr-only" aria-live="polite">{vm.refreshAnnouncement}</span>
      {vm.refreshErrorText && (
        <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
          {vm.refreshErrorText}
        </div>
      )}

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
            {briefingItems.map((item) => (
              <BriefingTile
                key={item.id}
                label={item.label}
                value={item.value}
                detail={item.detail}
                tone={item.tone}
                badgeVariant={item.badgeVariant}
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
            {priorityRoutes.map((workspace) => {
              const copy = priorityRouteCopy[workspace.id as keyof typeof priorityRouteCopy];

              return (
                <div key={workspace.id} className="rounded-lg border border-border/70 bg-secondary/25 p-4">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <div className="eyebrow-label">{copy.eyebrow}</div>
                      <h3 className="mt-2 text-sm font-semibold text-foreground">{copy.title}</h3>
                    </div>
                    <Badge variant={workspace.badgeVariant}>{workspace.status}</Badge>
                  </div>
                  <p className="mt-2 text-sm leading-6 text-muted-foreground">{copy.detail}</p>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{workspace.description}</p>
                  <Button asChild variant="outline" size="sm" className="mt-4">
                    <Link to={workspace.href} aria-label={workspace.ariaLabel}>
                      {copy.buttonLabel}
                      <ArrowRight className="size-3.5" aria-hidden="true" />
                    </Link>
                  </Button>
                </div>
              );
            })}
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
            <StatCard
              key={stat.id}
              icon={fallbackStatIcons[stat.id]}
              label={stat.label}
              value={stat.value}
              tone={stat.tone}
            />
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
              <ul aria-label={vm.activityListLabel} className="space-y-2">
                {vm.activityRows.map((event) => (
                  <EventRow key={event.id} event={event} />
                ))}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground py-4 text-center">
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

interface StatCardProps {
  icon: ElementType;
  label: string;
  value: string;
  tone: "default" | "success" | "warning" | "danger";
}

const toneClass: Record<StatCardProps["tone"], string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

function StatCard({ icon: Icon, label, value, tone }: StatCardProps) {
  return (
    <Card>
      <CardContent className="pt-5 pb-4">
        <div className="flex items-center gap-2 mb-2">
          <Icon className="size-4 text-muted-foreground" />
          <span className="text-xs text-muted-foreground">{label}</span>
        </div>
        <p className={cn("text-2xl font-semibold tabular-nums", toneClass[tone])}>{value}</p>
      </CardContent>
    </Card>
  );
}

interface BriefingTileProps {
  label: string;
  value: string;
  detail: string;
  tone: "default" | "success" | "warning" | "danger";
  badgeVariant: "paper" | "live" | "research" | null;
}

function BriefingTile({ label, value, detail, tone, badgeVariant }: BriefingTileProps) {
  return (
    <div className="rounded-lg border border-border/70 bg-background/70 p-4">
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

function EventRow({ event }: { event: OverviewActivityRow }) {
  const config = activityToneConfig[event.tone];
  const Icon = config.icon;

  return (
    <li>
      <div
        role="group"
        aria-label={event.ariaLabel}
        className={cn("flex items-start gap-3 rounded-md border px-3 py-2", config.rowClassName)}
      >
        <Icon aria-hidden="true" className={cn("mt-0.5 size-3.5 shrink-0", config.iconClassName)} />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={event.badgeVariant} dot>{event.statusCode}</Badge>
            <span className="font-mono text-[11px] text-muted-foreground">{event.source}</span>
            <span aria-hidden="true" className="text-muted-foreground/45">·</span>
            <span className="font-mono text-[11px] text-muted-foreground">{event.timestampLabel}</span>
          </div>
          <p className="mt-1 text-sm leading-snug">{event.message}</p>
        </div>
      </div>
    </li>
  );
}
