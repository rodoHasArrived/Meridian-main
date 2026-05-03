import { Activity, ExternalLink, LoaderCircle, MonitorCheck, User } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import { buildSettingsScreenViewModel } from "@/screens/settings-screen.view-model";
import type {
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey
} from "@/types";

interface SettingsScreenProps {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  research?: ResearchWorkspaceResponse | null;
  trading?: TradingWorkspaceResponse | null;
  dataOperations?: DataOperationsWorkspaceResponse | null;
  governance?: GovernanceWorkspaceResponse | null;
  reporting?: GovernanceWorkspaceResponse | null;
  loading?: boolean;
  error?: string | null;
  workspaceErrors?: Partial<Record<WorkspaceKey, string>>;
}

const systemToneClass = {
  default: "border-border/70",
  success: "border-success/30",
  warning: "border-warning/30",
  danger: "border-danger/30"
} as const;

const eventToneClass = {
  default: "border-border/70 bg-secondary/25",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10"
} as const;

const itemToneClass = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
} as const;

const diagnosticToneClass = {
  default: "border-border/70 bg-secondary/30",
  success: "border-success/30 bg-success/10",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10"
} as const;

export function SettingsScreen({
  session,
  overview,
  research = null,
  trading = null,
  dataOperations = null,
  governance = null,
  reporting = null,
  loading = false,
  error = null,
  workspaceErrors = {}
}: SettingsScreenProps) {
  const vm = buildSettingsScreenViewModel({
    session,
    overview,
    research,
    trading,
    dataOperations,
    governance,
    reporting,
    loading,
    error,
    workspaceErrors
  });

  return (
    <div className="space-y-8">
      <section className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <div className="eyebrow-label">Settings Lane</div>
            <CardTitle className="flex items-center gap-2">
              <User className="h-5 w-5 text-primary" />
              {vm.sessionTitle}
            </CardTitle>
            <CardDescription>
              Active operator session context and environment configuration.
            </CardDescription>
          </CardHeader>
          <CardContent>
            {vm.hasSession ? (
              <dl className="grid gap-2">
                {vm.sessionItems.map((item) => (
                  <div
                    key={item.label}
                    className="grid grid-cols-[minmax(0,0.7fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2"
                  >
                    <dt className="text-xs text-muted-foreground">{item.label}</dt>
                    <dd className={cn("text-right font-mono text-xs", itemToneClass[item.tone])}>
                      {item.value}
                    </dd>
                  </div>
                ))}
              </dl>
            ) : (
              <p className="py-4 text-center text-sm text-muted-foreground">
                Session data is unavailable. Reconnect to the Meridian API.
              </p>
            )}
          </CardContent>
        </Card>

        <Card className={cn("border", systemToneClass[vm.systemTone])}>
          <CardHeader>
            <div className="eyebrow-label">System posture</div>
            <CardTitle className="flex items-center gap-2">
              <MonitorCheck className="h-5 w-5 text-primary" />
              {vm.systemTitle}
            </CardTitle>
            <CardDescription>{vm.systemSummary}</CardDescription>
          </CardHeader>
          <CardContent>
            {vm.hasOverview ? (
              <dl className="grid gap-2">
                {vm.systemItems.map((item) => (
                  <div
                    key={item.label}
                    className="grid grid-cols-[minmax(0,0.7fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2"
                  >
                    <dt className="text-xs text-muted-foreground">{item.label}</dt>
                    <dd className={cn("text-right font-mono text-xs", itemToneClass[item.tone])}>
                      {item.value}
                    </dd>
                  </div>
                ))}
              </dl>
            ) : (
              <p className="py-4 text-center text-sm text-muted-foreground">
                System overview is unavailable. Check the API connection.
              </p>
            )}
          </CardContent>
        </Card>
      </section>

      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <CardTitle className="flex items-center gap-2 text-base">
                <Activity className="h-4 w-4 text-primary" />
                {vm.recentEventsSection.title}
              </CardTitle>
              <CardDescription className="mt-2">{vm.recentEventsSection.description}</CardDescription>
            </div>
            <Badge
              variant={
                vm.recentEventsSection.state === "unavailable"
                  ? "danger"
                  : vm.recentEventsSection.state === "empty"
                    ? "outline"
                    : "default"
              }
              dot={vm.recentEventsSection.state === "ready"}
            >
              {vm.recentEventsSection.statusLabel}
            </Badge>
          </div>
        </CardHeader>
        <CardContent>
          {vm.recentEventsSection.rows.length > 0 ? (
            <div role="list" aria-label={vm.recentEventsSection.listLabel} className="space-y-2">
              {vm.recentEventsSection.rows.map((event) => (
                <div
                  key={event.id}
                  role="group"
                  aria-label={event.ariaLabel}
                  className={cn(
                    "grid gap-3 rounded-md border px-3 py-3 sm:grid-cols-[auto_minmax(0,1fr)_auto]",
                    eventToneClass[event.tone]
                  )}
                >
                  <Badge variant={event.badgeVariant} className="w-fit">
                    {event.statusCode}
                  </Badge>
                  <div className="min-w-0">
                    <p className="text-sm text-foreground">{event.message}</p>
                    <p className="mt-1 font-mono text-xs text-muted-foreground">{event.source}</p>
                  </div>
                  <span className="font-mono text-xs text-muted-foreground sm:text-right">{event.timestamp}</span>
                </div>
              ))}
            </div>
          ) : (
            <div
              role={vm.recentEventsSection.state === "unavailable" ? "alert" : "status"}
              className={cn(
                "rounded-md border px-4 py-4",
                vm.recentEventsSection.state === "unavailable"
                  ? "border-danger/35 bg-danger/10"
                  : "border-border/70 bg-secondary/25"
              )}
            >
              <div className="text-sm font-semibold text-foreground">{vm.recentEventsSection.statusLabel}</div>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.recentEventsSection.statusDetail}</p>
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <CardTitle className="flex items-center gap-2 text-base">
                <ExternalLink className="h-4 w-4 text-primary" />
                Diagnostic endpoints
              </CardTitle>
              <CardDescription className="mt-2">{vm.diagnosticSummary}</CardDescription>
            </div>
            <Badge variant={vm.diagnosticStatusVariant} dot={vm.diagnosticStatusVariant === "success"}>
              {vm.diagnosticStatusLabel}
            </Badge>
          </div>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2" role="list" aria-label={vm.diagnosticListLabel}>
          {vm.diagnosticLinks.map((link) => (
            <div key={link.href} role="listitem">
              <a
                href={link.href}
                target="_blank"
                rel="noreferrer"
                aria-label={link.ariaLabel}
                className={cn(
                  "group flex h-full flex-col gap-2 rounded-lg border px-4 py-3 transition-colors hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40",
                  diagnosticToneClass[link.tone]
                )}
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="font-semibold text-foreground group-hover:text-primary transition-colors">
                    {link.label}
                  </span>
                  <span className="inline-flex items-center gap-2">
                    <Badge variant={link.badgeVariant} className="shrink-0">
                      {link.statusLabel}
                    </Badge>
                    {link.isLoading ? (
                      <LoaderCircle className="h-3 w-3 shrink-0 animate-spin text-warning" aria-hidden="true" />
                    ) : (
                      <ExternalLink className="h-3 w-3 shrink-0 text-muted-foreground" aria-hidden="true" />
                    )}
                  </span>
                </div>
                <p className="text-xs leading-5 text-muted-foreground">{link.description}</p>
                <p className="text-xs leading-5 text-foreground/75">{link.statusDetail}</p>
                <span className="mt-1 font-mono text-[10px] text-muted-foreground">{link.href}</span>
              </a>
            </div>
          ))}
        </CardContent>
      </Card>
    </div>
  );
}
