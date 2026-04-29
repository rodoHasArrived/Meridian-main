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
import { useOverviewStatusViewModel, type OverviewFallbackStatId } from "@/screens/overview-screen.view-model";
import type { SessionInfo, SystemEventRecord, SystemOverviewResponse, WorkspaceKey } from "@/types";

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

const eventTypeConfig = {
  info: { icon: Activity, className: "text-muted-foreground" },
  warning: { icon: AlertCircle, className: "text-warning" },
  error: { icon: XCircle, className: "text-danger" }
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

export function OverviewScreen({ data, session }: OverviewScreenProps) {
  const vm = useOverviewStatusViewModel(data);
  const current = vm.current;
  const statusConfig = current ? systemStatusConfig[current.systemStatus] : null;
  const StatusIcon = statusConfig?.icon ?? Radio;

  return (
    <div className="space-y-6">
      {/* Status banner */}
      <div
        className={cn(
          "flex items-center gap-3 rounded-lg border px-4 py-3",
          statusConfig?.bannerClass ?? "border-border bg-muted/30"
        )}
      >
        <StatusIcon className={cn("size-5 shrink-0", statusConfig?.className)} />
        <div className="flex-1">
          <p className={cn("text-sm font-medium", statusConfig?.className)}>
            {vm.statusLabel}
          </p>
          {current && vm.providerSummary && vm.storageLabel && vm.lastHeartbeatLabel && (
            <p className="text-xs text-muted-foreground mt-0.5">
              {vm.providerSummary}
              {" · "}
              Storage: <span className={storageHealthConfig[current.storageHealth].className}>
                {vm.storageLabel}
              </span>
              {" · "}
              Last heartbeat: {vm.lastHeartbeatLabel}
            </p>
          )}
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
            <CardTitle className="text-base">Recent Activity</CardTitle>
            <CardDescription>Latest system events across all workspaces.</CardDescription>
          </CardHeader>
          <CardContent>
            {vm.hasEvents ? (
              <ul className="space-y-2">
                {vm.events.map((event) => (
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

      {/* Session context */}
      {session && (
        <Card className="border-border/50">
          <CardContent className="pt-4 pb-4">
            <div className="flex items-center gap-4 text-sm flex-wrap">
              <span className="text-muted-foreground">
                Signed in as <span className="text-foreground font-medium">{session.displayName}</span>
              </span>
              <span className="text-muted-foreground/50">·</span>
              <span className="text-muted-foreground">
                Role: <span className="text-foreground">{session.role}</span>
              </span>
              <span className="text-muted-foreground/50">·</span>
              <span className="text-muted-foreground">
                Environment:{" "}
                <Badge
                  variant="outline"
                  className={cn(
                    "text-xs capitalize",
                    session.environment === "live" && "border-danger/40 text-danger",
                    session.environment === "paper" && "border-warning/40 text-warning",
                    session.environment === "research" && "border-blue-400/40 text-blue-400"
                  )}
                >
                  {session.environment}
                </Badge>
              </span>
            </div>
          </CardContent>
        </Card>
      )}
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

function EventRow({ event }: { event: SystemEventRecord }) {
  const config = eventTypeConfig[event.type];
  const Icon = config.icon;

  return (
    <li className="flex items-start gap-2.5 py-1.5 border-b border-border/40 last:border-0">
      <Icon className={cn("size-3.5 mt-0.5 shrink-0", config.className)} />
      <div className="flex-1 min-w-0">
        <p className="text-sm leading-snug">{event.message}</p>
        <p className="text-xs text-muted-foreground mt-0.5">
          {event.source} · {new Date(event.timestamp).toLocaleTimeString()}
        </p>
      </div>
    </li>
  );
}
