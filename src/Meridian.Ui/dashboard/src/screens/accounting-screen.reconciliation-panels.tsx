import { AlertCircle, BookCheck, CheckCircle2, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DenseDataTable, EntitySummary, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { cn } from "@/lib/utils";
import type {
  CalibrationProfileRowViewModel,
  CalibrationSummaryViewModel
} from "@/screens/accounting-screen.view-model";

const calibrationProfileColumns: DenseDataTableColumn<CalibrationProfileRowViewModel>[] = [
  {
    id: "profile",
    label: "Profile",
    render: (row) => <span className="font-mono text-foreground">{row.toleranceProfileId}</span>
  },
  {
    id: "route",
    label: "Route",
    render: (row) => <span className="text-muted-foreground">{row.exceptionRoute}</span>
  },
  {
    id: "severity",
    label: "Severity",
    render: (row) => <span className={cn("font-mono", calibrationSeverityClass(row.highestSeverity))}>{row.highestSeverity}</span>
  },
  {
    id: "open",
    label: "Open",
    align: "right",
    render: (row) => <span className={cn("font-mono tabular-nums", row.openBreakCount > 0 ? "text-warning" : "text-foreground")}>{row.openBreakCount}</span>
  },
  {
    id: "in-review",
    label: "Review",
    align: "right",
    render: (row) => <span className="font-mono tabular-nums text-foreground">{row.inReviewBreakCount}</span>
  },
  {
    id: "pending-signoff",
    label: "Sign-off",
    align: "right",
    render: (row) => (
      <span className={cn("font-mono tabular-nums", row.pendingSignoffCount > 0 ? "text-warning" : "text-foreground")}>
        {row.pendingSignoffCount}
      </span>
    )
  },
  {
    id: "tolerance",
    label: "Tolerance",
    align: "right",
    render: (row) => <span className="font-mono text-muted-foreground">{row.maxToleranceBandLabel}</span>
  },
  {
    id: "updated",
    label: "Updated",
    render: (row) => <span className="font-mono text-muted-foreground">{row.lastUpdatedLabel}</span>
  }
];

function calibrationSeverityClass(severity: string): string {
  const normalized = severity.trim().toLowerCase();
  if (normalized === "critical") {
    return "text-danger";
  }

  if (normalized === "warning" || normalized === "warn") {
    return "text-warning";
  }

  return "text-foreground";
}

export function CalibrationSummaryPanel({ view }: { view: CalibrationSummaryViewModel }) {
  const StatusIcon = view.statusIcon === "check" ? CheckCircle2 : AlertCircle;

  return (
    <Card id="accounting-history" className="panel-surface">
      <CardHeader className="gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <CardTitle className="flex items-center gap-2 text-base">
            <BookCheck className="h-4 w-4 text-primary" />
            Calibration summary
          </CardTitle>
          <CardDescription>Tolerance profile health, break trend, auto-match rate, and T+0 closure rate across active reconciliation routes.</CardDescription>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={view.refresh}
          disabled={view.refreshCommand.disabled}
          disabledReason={view.refreshCommand.disabledReason}
          aria-label={view.refreshCommand.ariaLabel}
          className="shrink-0"
        >
          <RefreshCcw className="mr-2 h-3.5 w-3.5" aria-hidden="true" />
          {view.refreshCommand.label}
        </Button>
      </CardHeader>
      <CardContent className="space-y-4">
        <span className="sr-only" aria-live="polite">{view.statusAnnouncement}</span>
        {view.loadingText && <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p>}
        {view.errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            <div>{view.errorText}</div>
            {view.errorDetails.length > 0 ? (
              <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                {view.errorDetails.map((detail) => (
                  <li key={detail}>{detail}</li>
                ))}
              </ul>
            ) : null}
          </div>
        )}
        {!view.loadingText && !view.errorText && (
          <>
            <div className={cn("flex items-center gap-3 rounded-lg border px-4 py-3", view.statusBannerClassName)}>
              <StatusIcon aria-hidden="true" className={cn("size-4 shrink-0", view.statusTextClassName)} />
              <div className="flex-1 min-w-0">
                <span className={cn("text-sm font-semibold", view.statusTextClassName)}>{view.statusLabel}</span>
                {view.summary && <p className="mt-0.5 text-xs text-muted-foreground">{view.summary}</p>}
              </div>
              <span className="shrink-0 font-mono text-xs text-muted-foreground">as of {view.asOfLabel}</span>
            </div>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-9">
              {view.metricRows.map((metric) => (
                <div
                  key={metric.id}
                  role="group"
                  aria-label={metric.ariaLabel}
                  className="rounded-md border border-border/60 bg-secondary/25 px-3 py-2 text-center"
                >
                  <div className="text-xs text-muted-foreground">{metric.label}</div>
                  <div className={cn("mt-1 font-mono text-lg font-semibold tabular-nums", metric.tone === "warning" ? "text-warning" : "text-foreground")}>
                    {metric.value}
                  </div>
                </div>
              ))}
            </div>
            <div>
              <div className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{view.profilesLabel}</div>
              {view.hasProfiles ? (
                <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,360px)]">
                  <DenseDataTable
                    columns={calibrationProfileColumns}
                    rows={view.profileRows}
                    getRowId={(row) => row.toleranceProfileId}
                    getRowAriaLabel={(row) => row.ariaLabel}
                    getRowSelectAriaLabel={(row) => row.selectAriaLabel}
                    getRowAriaControls={(row) => row.detailPanelId}
                    getRowAriaExpanded={(row) => row.isSelected}
                    selectedRowId={view.selectedProfileId}
                    onRowSelect={(row) => view.selectProfile(row.toleranceProfileId)}
                    emptyText={view.emptyText}
                    ariaLabel={view.tableAriaLabel}
                  />
                  <div id={view.detailPanelId} aria-live="polite">
                    {view.selectedProfile ? (
                      <EntitySummary
                        eyebrow="Tolerance profile"
                        title={view.selectedProfile.title}
                        subtitle={view.selectedProfile.subtitle}
                        description={view.selectedProfile.description}
                        status={<Badge variant={view.selectedProfile.statusTone} dot>{view.selectedProfile.statusLabel}</Badge>}
                        fields={view.selectedProfile.fields}
                        ariaLabel={view.selectedProfile.ariaLabel}
                      />
                    ) : (
                      <div role="status" className="rounded-lg border border-border/70 bg-secondary/25 px-4 py-3 text-sm text-muted-foreground">
                        Select a tolerance profile to inspect its calibration posture.
                      </div>
                    )}
                  </div>
                </div>
              ) : (
                <div role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning">
                  {view.emptyText}
                </div>
              )}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}
