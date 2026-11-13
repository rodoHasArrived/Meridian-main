// Diagnostics section (endpoints, recent events, service status) of the Settings screen.
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { SeverityBadge } from "@/components/operations";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { eyebrowClassName, usesTrackedCaps } from "@/design-system/label-case";
import { cn } from "@/lib/utils";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { type SettingsRecentEventDetail, type SettingsRecentEventTableRow } from "@/screens/settings-screen.view-model";
import type { SystemOverviewResponse } from "@/types";
import { Activity, ArrowRight, ExternalLink, LoaderCircle } from "lucide-react";
import { Link } from "react-router-dom";
import { diagnosticToneClass, itemToneClass } from "./settings-provider-integration";
import type { SettingsDiagnosticLink, SettingsRecentEventsSelectionViewModel, SettingsScreenViewModel } from "@/screens/settings-screen.view-model";
import { SettingsChip } from "@/screens/settings-screen.section-primitives";
import { toSeverity } from "@/design-system/status-styles";

interface SettingsDiagnosticsSectionProps {
  vm: SettingsScreenViewModel;
  overview: SystemOverviewResponse | null;
  recentEventsVm: SettingsRecentEventsSelectionViewModel;
  showAdvancedDiagnosticsSection: boolean;
  visibleDiagnosticLinks: SettingsDiagnosticLink[];
}

/** Diagnostic endpoints, recent events, and service status. */
export function SettingsDiagnosticsSection({ vm, overview, recentEventsVm, showAdvancedDiagnosticsSection, visibleDiagnosticLinks }: SettingsDiagnosticsSectionProps) {
  return (
    <section className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
      <Card id="diagnostic-endpoints" className="panel-surface scroll-mt-6">
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <div className="eyebrow-label">Event posture</div>
              <CardTitle className="flex items-center gap-2 text-base">
                <Activity className="h-4 w-4 text-primary" />
                {vm.recentEventsSection.title}
              </CardTitle>
              <CardDescription className="mt-2">{vm.recentEventsSection.description}</CardDescription>
            </div>
            <SeverityBadge
              status={recentEventsSeverity(vm.recentEventsSection.state)}
              label={vm.recentEventsSection.statusLabel}
            />
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap gap-2">
            <SettingsChip label="Count" value={vm.recentEventsSection.countLabel} />
            <SettingsChip label="Heartbeat" value={overview ? vm.systemFreshness.label : "—"} />
            <SettingsChip label="Stream" value={vm.recentEventsSection.state} />
          </div>
          {overview?.lastHeartbeatUtc ? (
            <TechnicalDetails
              label="Heartbeat details"
              description="The source timestamp is retained for diagnostic comparison."
            >
              <code className="break-all text-xs text-muted-foreground">{overview.lastHeartbeatUtc}</code>
            </TechnicalDetails>
          ) : null}
          {recentEventsVm.rows.length > 0 ? (
            <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(260px,0.48fr)]">
              <DenseDataTable
                columns={recentEventColumns}
                rows={recentEventsVm.rows}
                getRowId={(event) => event.id}
                getRowAriaLabel={(event) => event.ariaLabel}
                getRowSelectAriaLabel={(event) => event.selectAriaLabel}
                getRowAriaControls={(event) => event.detailPanelId}
                getRowAriaExpanded={(event) => event.expanded}
                getRowClassName={(event) => eventToneClass[event.tone]}
                onRowSelect={(event) => recentEventsVm.selectRow(event.id)}
                selectedRowId={recentEventsVm.selectedRowId}
                emptyText={vm.recentEventsSection.statusDetail}
                ariaLabel={recentEventsVm.tableLabel}
                caption={recentEventsVm.tableCaption}
              />
              <RecentEventDetailPanel
                id={recentEventsVm.detailPanelId}
                title={recentEventsVm.detailPanelTitle}
                description={recentEventsVm.detailPanelDescription}
                emptyText={recentEventsVm.detailPanelEmptyText}
                ariaLabel={recentEventsVm.detailPanelAriaLabel}
                detail={recentEventsVm.selectedDetail}
              />
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

      <Card className="panel-surface">
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <div className="eyebrow-label">Service status</div>
              <CardTitle className="flex items-center gap-2 text-base">
                <ExternalLink className="h-4 w-4 text-primary" />
                {showAdvancedDiagnosticsSection ? "Diagnostic service inventory" : "Exceptions requiring attention"}
              </CardTitle>
              <CardDescription className="mt-2">{vm.diagnosticSummary}</CardDescription>
            </div>
            <SeverityBadge
              status={toSeverity(vm.diagnosticStatusVariant)}
              label={vm.diagnosticStatusLabel}
            />
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap gap-2">
            <SettingsChip label="Loaded" value={vm.diagnosticCounts.loadedLabel} />
            <SettingsChip label="Failed" value={vm.diagnosticCounts.failedLabel} />
            <SettingsChip label="Checking" value={vm.diagnosticCounts.checkingLabel} />
          </div>
          {visibleDiagnosticLinks.length > 0 ? (
          <div
            className="grid gap-3 md:grid-cols-2"
            role="list"
            aria-label={showAdvancedDiagnosticsSection ? vm.diagnosticListLabel : "Diagnostic exceptions requiring attention"}
          >
            {visibleDiagnosticLinks.map((link) => (
              <div key={link.href} role="listitem">
                <a
                  href={link.href}
                  target="_blank"
                  rel="noreferrer"
                  aria-label={link.ariaLabel}
                  className={cn(
                    "group flex h-full flex-col gap-2 rounded-lg border px-4 py-3 transition-colors hover:bg-secondary/45 focus:outline-hidden focus:ring-2 focus:ring-primary/40",
                    diagnosticToneClass[link.tone]
                  )}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-semibold text-foreground transition-colors group-hover:text-primary">
                      {link.label}
                    </span>
                    <span className="inline-flex items-center gap-2">
                      <SeverityBadge
                        status={toSeverity(link.badgeVariant)}
                        label={link.statusLabel}
                        className="shrink-0"
                      />
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
          </div>
          ) : (
            <div className="rounded-md border border-success/35 bg-success/10 px-4 py-4" role="status">
              <div className="text-sm font-semibold text-foreground">No diagnostic exceptions need attention</div>
              <p className="mt-2 text-xs leading-5 text-muted-foreground">
                {vm.diagnosticHealthyLinks.length} diagnostic services are loaded. Open the advanced inventory for endpoint-level evidence.
              </p>
            </div>
          )}
          {!showAdvancedDiagnosticsSection ? (
            <Button asChild variant="outline" size="sm">
              <Link to={WORKSTATION_ROUTE_CATALOG.settingsDiagnosticsAdvanced} aria-label="Open advanced diagnostic service inventory">
                Open advanced diagnostics
                <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
              </Link>
            </Button>
          ) : null}
        </CardContent>
      </Card>
    </section>
  );
}

// Event-stream readiness → Concrete severity (SeverityBadge): a live stream is Ready,
// an empty stream is Info, and an unavailable stream is Blocked.
function recentEventsSeverity(state: "ready" | "empty" | "unavailable"): string {
  if (state === "unavailable") return "blocked";
  if (state === "empty") return "info";
  return "ready";
}

const recentEventColumns: DenseDataTableColumn<SettingsRecentEventTableRow>[] = [
  {
    id: "status",
    label: "Status",
    render: (event) => <Badge variant={event.badgeVariant}>{event.statusCode}</Badge>
  },
  {
    id: "message",
    label: "Message",
    className: "min-w-[14rem]",
    render: (event) => <span className="text-foreground">{event.message}</span>
  },
  {
    id: "source",
    label: "Source",
    className: "font-mono text-muted-foreground",
    render: (event) => event.source
  },
  {
    id: "timestamp",
    label: "Timestamp",
    className: "font-mono text-muted-foreground",
    render: (event) => event.timestamp
  }
];

const eventToneClass = {
  default: "border-border/70 bg-secondary/25",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10"
} as const;

function RecentEventDetailPanel({
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
  detail: SettingsRecentEventDetail | null;
}) {
  return (
    <aside
      id={id}
      role="complementary"
      aria-label={ariaLabel}
      aria-live="polite"
      className="row-detail-panel h-fit min-w-0"
    >
      <div className="head">{title}</div>
      <div className="body">
        {detail ? (
          <div role="region" aria-label={detail.ariaLabel} className="space-y-3">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className={eyebrowClassName(detail.eyebrow)}>{detail.eyebrow}</div>
                <h3 className="mt-2 break-words text-sm font-semibold text-foreground">{detail.title}</h3>
                <p className="mt-1 break-words font-mono text-xs text-muted-foreground">{detail.subtitle}</p>
              </div>
              <SeverityBadge
                status={toSeverity(detail.statusVariant)}
                label={detail.statusLabel}
                className="shrink-0"
              />
            </div>
            <p className="text-sm leading-6 text-muted-foreground">{detail.description}</p>
            <dl className="grid gap-2 sm:grid-cols-2 xl:grid-cols-1 2xl:grid-cols-2">
              {detail.fields.map((field) => (
                <div key={field.label} className="rounded-sm border border-border/60 bg-background/35 px-2.5 py-2">
                  <dt className={cn("text-[10px] text-muted-foreground", usesTrackedCaps(field.label) && "uppercase tracking-[0.14em]")}>{field.label}</dt>
                  <dd className={cn("mt-1 break-words font-mono text-xs", itemToneClass[field.tone])}>
                    {field.value}
                  </dd>
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
