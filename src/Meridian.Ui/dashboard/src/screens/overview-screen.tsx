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

type OverviewTab = "overview" | "providers" | "risk" | "data-health" | "activity";

const OVERVIEW_TABS: { key: OverviewTab; label: string }[] = [
  { key: "overview", label: "Overview" },
  { key: "providers", label: "Providers" },
  { key: "risk", label: "Risk" },
  { key: "data-health", label: "Data Health" },
  { key: "activity", label: "Activity" }
];

const systemStatusConfig = {
  Healthy: {
    label: "All Systems Healthy",
    icon: CheckCircle2,
    className: "text-success",
    bannerClass: "border-success/25 bg-success/5"
  },
  Degraded: {
    label: "System Degraded",
    icon: AlertCircle,
    className: "text-warning",
    bannerClass: "border-warning/25 bg-warning/5"
  },
  Offline: {
    label: "System Offline",
    icon: XCircle,
    className: "text-danger",
    bannerClass: "border-danger/25 bg-danger/5"
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
  const [activeTab, setActiveTab] = useState<OverviewTab>("overview");
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
    <div className="space-y-0">
      {/* ── Marquee-style compact status bar ── */}
      <div
        className={cn(
          "mb-0 flex items-center gap-3 rounded-lg border px-4 py-2.5",
          statusConfig?.bannerClass ?? "border-border/40 bg-muted/20"
        )}
      >
        <StatusIcon className={cn("size-4 shrink-0", statusConfig?.className)} />
        <div className="flex flex-1 items-center gap-4 min-w-0">
          <p className={cn("text-[12px] font-semibold", statusConfig?.className)}>
            {statusConfig?.label ?? "Connecting to system…"}
          </p>
          {current && (
            <div className="flex items-center gap-3 text-[11px] text-muted-foreground">
              <span className="font-mono">
                <span className="text-muted-foreground/60">Providers</span>{" "}
                <span className={cn(
                  current.providersOnline === current.providersTotal ? "text-success" :
                  current.providersOnline === 0 ? "text-danger" : "text-warning"
                )}>
                  {current.providersOnline}/{current.providersTotal}
                </span>
              </span>
              <span className="text-border/60">·</span>
              <span>
                Storage:{" "}
                <span className={storageHealthConfig[current.storageHealth].className}>
                  {storageHealthConfig[current.storageHealth].label}
                </span>
              </span>
              <span className="text-border/60">·</span>
              <span className="font-mono">{new Date(current.lastHeartbeatUtc).toLocaleTimeString()}</span>
            </div>
          )}
        </div>
        <Button
          variant="ghost"
          size="sm"
          onClick={() => { void handleRefresh(); }}
          disabled={refreshing}
          className="h-7 shrink-0 gap-1 px-2 text-xs"
        >
          <RefreshCcw className={cn("size-3", refreshing && "animate-spin")} />
          {refreshing ? "…" : "Refresh"}
        </Button>
      </div>

      {/* ── Marquee-style horizontal analysis tabs ── */}
      <div className="mq-tab-nav my-4 rounded-lg">
        {OVERVIEW_TABS.map((tab) => (
          <button
            key={tab.key}
            className="mq-tab"
            data-active={activeTab === tab.key}
            onClick={() => setActiveTab(tab.key)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* ── Tab content ── */}
      {activeTab === "overview" && (
        <OverviewTabContent current={current} session={session} />
      )}
      {activeTab === "providers" && (
        <ProvidersTabContent current={current} />
      )}
      {activeTab === "risk" && (
        <RiskTabContent current={current} />
      )}
      {activeTab === "data-health" && (
        <DataHealthTabContent current={current} />
      )}
      {activeTab === "activity" && (
        <ActivityTabContent current={current} />
      )}
    </div>
  );
}

// ─── Tab content panels ────────────────────────────────────────────────────

function OverviewTabContent({
  current,
  session
}: {
  current: SystemOverviewResponse | null;
  session: SessionInfo | null;
}) {
  return (
    <div className="space-y-4">
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

      {/* ── Marquee-style split panel: Recent Activity + Workspace Nav ── */}
      <div className="mq-split-2 rounded-lg overflow-hidden">
        {/* Left panel: recent events */}
        <div className="mq-panel">
          <div className="mq-panel-header">
            <span className="mq-panel-title">Recent Activity</span>
            <span className="text-[10px] text-muted-foreground/50">
              {current?.recentEvents?.length ?? 0} events
            </span>
          </div>
          <div>
            {current?.recentEvents && current.recentEvents.length > 0 ? (
              current.recentEvents.map((event) => (
                <EventRow key={event.id} event={event} />
              ))
            ) : (
              <p className="py-6 text-center text-xs text-muted-foreground">
                {current ? "No recent events." : "Loading activity feed…"}
              </p>
            )}
          </div>
        </div>

        {/* Right panel: workspace quick-nav */}
        <div className="mq-panel">
          <div className="mq-panel-header">
            <span className="mq-panel-title">Workspaces</span>
          </div>
          <div>
            {workspaceLinks.map((ws) => {
              const Icon = ws.icon;
              return (
                <Link
                  key={ws.key}
                  to={ws.href}
                  className="mq-data-row group gap-3"
                >
                  <Icon className={cn("size-3.5 shrink-0", ws.accent)} />
                  <div className="flex flex-1 flex-col min-w-0">
                    <span className="text-[12px] font-semibold text-foreground/90">{ws.label}</span>
                    <span className="text-[10px] text-muted-foreground/65 truncate">{ws.description}</span>
                  </div>
                  <ArrowRight className="size-3 shrink-0 text-muted-foreground/30 transition-colors group-hover:text-muted-foreground" />
                </Link>
              );
            })}
          </div>

          {/* Session context at bottom of panel */}
          {session && (
            <div className="border-t border-border/30 px-4 py-2.5">
              <div className="flex flex-wrap items-center gap-3 text-[11px]">
                <span className="text-muted-foreground/60">
                  Signed in as{" "}
                  <span className="font-medium text-foreground/80">{session.displayName}</span>
                </span>
                <span className="text-border/50">·</span>
                <Badge
                  variant="outline"
                  className={cn(
                    "h-[16px] px-1.5 text-[9px]",
                    session.environment === "live" && "border-danger/40 text-danger",
                    session.environment === "paper" && "border-warning/40 text-warning",
                    session.environment === "research" && "border-blue-400/40 text-blue-400"
                  )}
                >
                  {session.environment}
                </Badge>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function ProvidersTabContent({ current }: { current: SystemOverviewResponse | null }) {
  if (!current) {
    return <EmptyTabState message="Loading provider data…" />;
  }
  return (
    <div className="mq-split-2 rounded-lg overflow-hidden">
      <div className="mq-panel">
        <div className="mq-panel-header">
          <span className="mq-panel-title">Provider Status</span>
        </div>
        <div className="mq-data-row">
          <span className="mq-data-row-label">Online</span>
          <span className={cn(
            "mq-data-row-value",
            current.providersOnline === current.providersTotal ? "text-success" : "text-warning"
          )}>
            {current.providersOnline} / {current.providersTotal}
          </span>
        </div>
        <div className="mq-data-row">
          <span className="mq-data-row-label">Active Backfills</span>
          <span className={cn(
            "mq-data-row-value",
            current.activeBackfills > 0 ? "text-warning" : "text-foreground/80"
          )}>
            {current.activeBackfills}
          </span>
        </div>
        <div className="mq-data-row">
          <span className="mq-data-row-label">Monitored Symbols</span>
          <span className="mq-data-row-value">{current.symbolsMonitored}</span>
        </div>
        <div className="mq-data-row">
          <span className="mq-data-row-label">Active Runs</span>
          <span className={cn(
            "mq-data-row-value",
            current.activeRuns > 0 ? "text-success" : "text-foreground/80"
          )}>
            {current.activeRuns}
          </span>
        </div>
      </div>
      <div className="mq-panel">
        <div className="mq-panel-header">
          <span className="mq-panel-title">Storage Health</span>
        </div>
        <div className="mq-data-row">
          <span className="mq-data-row-label">Overall Status</span>
          <span className={cn("mq-data-row-value", storageHealthConfig[current.storageHealth].className)}>
            {storageHealthConfig[current.storageHealth].label}
          </span>
        </div>
        <div className="mq-data-row">
          <span className="mq-data-row-label">Last Heartbeat</span>
          <span className="mq-data-row-value font-mono text-[11px]">
            {new Date(current.lastHeartbeatUtc).toLocaleTimeString()}
          </span>
        </div>
      </div>
    </div>
  );
}

function RiskTabContent({ current }: { current: SystemOverviewResponse | null }) {
  if (!current) {
    return <EmptyTabState message="Loading risk data…" />;
  }
  return (
    <div className="mq-panel rounded-lg overflow-hidden">
      <div className="mq-panel-header">
        <span className="mq-panel-title">Risk Summary</span>
      </div>
      <div className="mq-data-row">
        <span className="mq-data-row-label">System Status</span>
        <span className={cn(
          "mq-data-row-value",
          systemStatusConfig[current.systemStatus].className
        )}>
          {systemStatusConfig[current.systemStatus].label}
        </span>
      </div>
      <div className="mq-data-row">
        <span className="mq-data-row-label">Active Runs</span>
        <span className="mq-data-row-value">{current.activeRuns}</span>
      </div>
      <div className="mq-data-row">
        <span className="mq-data-row-label">Storage Health</span>
        <span className={cn("mq-data-row-value", storageHealthConfig[current.storageHealth].className)}>
          {storageHealthConfig[current.storageHealth].label}
        </span>
      </div>
      <p className="px-4 py-3 text-[11px] text-muted-foreground/50">
        Navigate to the Trading workspace for live risk metrics, position limits, and circuit breaker status.
      </p>
    </div>
  );
}

function DataHealthTabContent({ current }: { current: SystemOverviewResponse | null }) {
  if (!current) {
    return <EmptyTabState message="Loading data health…" />;
  }
  return (
    <div className="mq-split-2 rounded-lg overflow-hidden">
      <div className="mq-panel">
        <div className="mq-panel-header">
          <span className="mq-panel-title">Ingestion Health</span>
        </div>
        <div className="mq-data-row">
          <span className="mq-data-row-label">Providers Online</span>
          <span className={cn(
            "mq-data-row-value",
            current.providersOnline === current.providersTotal ? "text-success" :
            current.providersOnline === 0 ? "text-danger" : "text-warning"
          )}>
            {current.providersOnline} / {current.providersTotal}
          </span>
        </div>
        <div className="mq-data-row">
          <span className="mq-data-row-label">Active Backfills</span>
          <span className="mq-data-row-value">{current.activeBackfills}</span>
        </div>
        <div className="mq-data-row">
          <span className="mq-data-row-label">Symbols Monitored</span>
          <span className="mq-data-row-value">{current.symbolsMonitored}</span>
        </div>
      </div>
      <div className="mq-panel">
        <div className="mq-panel-header">
          <span className="mq-panel-title">Storage</span>
        </div>
        <div className="mq-data-row">
          <span className="mq-data-row-label">Status</span>
          <span className={cn("mq-data-row-value", storageHealthConfig[current.storageHealth].className)}>
            {storageHealthConfig[current.storageHealth].label}
          </span>
        </div>
        <div className="mq-data-row">
          <span className="mq-data-row-label">Last Heartbeat</span>
          <span className="mq-data-row-value">{new Date(current.lastHeartbeatUtc).toLocaleTimeString()}</span>
        </div>
        <p className="px-4 py-3 text-[11px] text-muted-foreground/50">
          Full data quality monitoring is available in the Data Operations workspace.
        </p>
      </div>
    </div>
  );
}

function ActivityTabContent({ current }: { current: SystemOverviewResponse | null }) {
  return (
    <div className="mq-panel rounded-lg overflow-hidden">
      <div className="mq-panel-header">
        <span className="mq-panel-title">System Activity</span>
        <span className="text-[10px] text-muted-foreground/50">
          {current?.recentEvents?.length ?? 0} events
        </span>
      </div>
      {current?.recentEvents && current.recentEvents.length > 0 ? (
        current.recentEvents.map((event) => (
          <EventRow key={event.id} event={event} />
        ))
      ) : (
        <p className="py-8 text-center text-xs text-muted-foreground">
          {current ? "No recent events." : "Loading activity feed…"}
        </p>
      )}
    </div>
  );
}

// ─── Shared sub-components ─────────────────────────────────────────────────

function EmptyTabState({ message }: { message: string }) {
  return (
    <div className="flex h-32 items-center justify-center text-sm text-muted-foreground">
      {message}
    </div>
  );
}

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
      <CardContent className="pb-4 pt-5">
        <div className="mb-2 flex items-center gap-2">
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
    <div className="mq-data-row gap-2.5">
      <Icon className={cn("size-3 mt-0.5 shrink-0", config.className)} />
      <div className="flex flex-1 flex-col min-w-0">
        <p className="text-[12px] leading-snug text-foreground/85">{event.message}</p>
        <p className="text-[10px] text-muted-foreground/60 mt-0.5">
          {event.source} · {new Date(event.timestamp).toLocaleTimeString()}
        </p>
      </div>
    </div>
  );
}
