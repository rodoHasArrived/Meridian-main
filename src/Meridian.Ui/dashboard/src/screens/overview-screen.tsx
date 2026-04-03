import {
  Activity,
  AlertCircle,
  ArrowRight,
  BarChart3,
  CheckCircle2,
  Database,
  FlaskConical,
  Globe,
  LineChart,
  Radio,
  RefreshCcw,
  Shield,
  TrendingUp,
  XCircle
} from "lucide-react";
import { useState } from "react";
import { Link } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { getSystemStatus } from "@/lib/api";
import { cn } from "@/lib/utils";
import type { SessionInfo, SystemEventRecord, SystemOverviewResponse } from "@/types";

interface OverviewScreenProps {
  data: SystemOverviewResponse | null;
  session: SessionInfo | null;
}

const systemStatusConfig = {
  Healthy: {
    label: "All Systems Healthy",
    icon: CheckCircle2,
    className: "text-success",
    bannerClass: "border-success/30 bg-success/5"
  },
  Degraded: {
    label: "System Degraded",
    icon: AlertCircle,
    className: "text-warning",
    bannerClass: "border-warning/30 bg-warning/5"
  },
  Offline: {
    label: "System Offline",
    icon: XCircle,
    className: "text-danger",
    bannerClass: "border-danger/30 bg-danger/5"
  }
} as const;

const storageHealthConfig = {
  Healthy: { label: "Healthy", className: "text-success" },
  Warning: { label: "Warning", className: "text-warning" },
  Critical: { label: "Critical", className: "text-danger" }
} as const;

const eventTypeConfig = {
  info: { icon: Activity, className: "text-muted-foreground" },
  warning: { icon: AlertCircle, className: "text-warning" },
  error: { icon: XCircle, className: "text-danger" }
} as const;

const workspaceLinks = [
  {
    key: "research",
    label: "Research",
    description: "Backtests, run comparisons, and experiment tracking.",
    href: "/",
    icon: FlaskConical,
    accent: "text-blue-400"
  },
  {
    key: "trading",
    label: "Trading",
    description: "Paper operations cockpit, positions, and blotter.",
    href: "/trading",
    icon: TrendingUp,
    accent: "text-green-400"
  },
  {
    key: "data-operations",
    label: "Data Operations",
    description: "Providers, backfills, symbols, and quality monitoring.",
    href: "/data-operations",
    icon: Database,
    accent: "text-purple-400"
  },
  {
    key: "governance",
    label: "Governance",
    description: "Ledger, reconciliation, and security master.",
    href: "/governance",
    icon: Shield,
    accent: "text-orange-400"
  }
] as const;

export function OverviewScreen({ data, session }: OverviewScreenProps) {
  const [refreshing, setRefreshing] = useState(false);
  const [liveData, setLiveData] = useState<SystemOverviewResponse | null>(data);

  const current = liveData ?? data;
  const statusConfig = current ? systemStatusConfig[current.systemStatus] : null;
  const StatusIcon = statusConfig?.icon ?? Radio;

  async function handleRefresh() {
    setRefreshing(true);
    try {
      const fresh = await getSystemStatus();
      setLiveData(fresh);
    } catch {
      // silently ignore — stale data remains visible
    } finally {
      setRefreshing(false);
    }
  }

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
            {statusConfig?.label ?? "Connecting to system…"}
          </p>
          {current && (
            <p className="text-xs text-muted-foreground mt-0.5">
              {current.providersOnline} of {current.providersTotal} providers online
              {" · "}
              Storage: <span className={storageHealthConfig[current.storageHealth].className}>
                {storageHealthConfig[current.storageHealth].label}
              </span>
              {" · "}
              Last heartbeat: {new Date(current.lastHeartbeatUtc).toLocaleTimeString()}
            </p>
          )}
        </div>
        <Button
          variant="ghost"
          size="sm"
          onClick={() => { void handleRefresh(); }}
          disabled={refreshing}
          className="shrink-0"
        >
          <RefreshCcw className={cn("size-4 mr-1.5", refreshing && "animate-spin")} />
          {refreshing ? "Refreshing…" : "Refresh"}
        </Button>
      </div>

      {/* Metrics grid */}
      {current?.metrics && current.metrics.length > 0 ? (
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          {current.metrics.map((metric) => (
            <MetricCard key={metric.id} {...metric} />
          ))}
        </div>
      ) : (
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <StatCard
            icon={Globe}
            label="Providers Online"
            value={current ? `${current.providersOnline} / ${current.providersTotal}` : "—"}
            tone={
              !current ? "default"
              : current.providersOnline === current.providersTotal ? "success"
              : current.providersOnline === 0 ? "danger"
              : "warning"
            }
          />
          <StatCard
            icon={LineChart}
            label="Active Runs"
            value={current ? String(current.activeRuns) : "—"}
            tone={current && current.activeRuns > 0 ? "success" : "default"}
          />
          <StatCard
            icon={BarChart3}
            label="Monitored Symbols"
            value={current ? String(current.symbolsMonitored) : "—"}
            tone="default"
          />
          <StatCard
            icon={Activity}
            label="Active Backfills"
            value={current ? String(current.activeBackfills) : "—"}
            tone={current && current.activeBackfills > 0 ? "warning" : "default"}
          />
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
            {current?.recentEvents && current.recentEvents.length > 0 ? (
              <ul className="space-y-2">
                {current.recentEvents.map((event) => (
                  <EventRow key={event.id} event={event} />
                ))}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground py-4 text-center">
                {current ? "No recent events." : "Loading activity feed…"}
              </p>
            )}
          </CardContent>
        </Card>

        {/* Quick navigation */}
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Workspaces</CardTitle>
            <CardDescription>Navigate to any workspace.</CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="space-y-2">
              {workspaceLinks.map((ws) => {
                const Icon = ws.icon;
                return (
                  <li key={ws.key}>
                    <Link
                      to={ws.href}
                      className="flex items-center gap-3 rounded-md p-2.5 transition-colors hover:bg-muted/50 group"
                    >
                      <Icon className={cn("size-4 shrink-0", ws.accent)} />
                      <div className="flex-1 min-w-0">
                        <p className="text-sm font-medium leading-none">{ws.label}</p>
                        <p className="text-xs text-muted-foreground mt-0.5 truncate">{ws.description}</p>
                      </div>
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
  icon: React.ElementType;
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
