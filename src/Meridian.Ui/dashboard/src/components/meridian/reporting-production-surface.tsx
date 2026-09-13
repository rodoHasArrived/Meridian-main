import { ArrowUpRight, CircleAlert, Clock } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { DesignSystemStatus } from "@/design-system/status";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { cn } from "@/lib/utils";
import type { ReportingProductionModel, ReportingProductionRow } from "@/lib/reporting-production";
import type { ReportingPeriodModel } from "@/lib/reporting-period-object";

export interface ReportingProductionSurfaceProps {
  model: ReportingProductionModel;
  /** Rendered as the period governance strip when the period is known. */
  period?: ReportingPeriodModel | null;
  className?: string;
}

/**
 * Reporting production control surface.
 *
 * Answers "what needs attention for this reporting period?" before anything else.
 * The production register is the dominant plane; the lane index, period strip and
 * attention rail are supporting context rather than competing containers.
 */
export function ReportingProductionSurface({ model, period, className }: ReportingProductionSurfaceProps) {
  // The owner column is only worth its width once at least one report has a known
  // owner; otherwise it renders a column of "Unassigned" and crowds out the state.
  const hasKnownOwner = model.register.some((row) => row.owner !== "Unassigned");

  const columns: DenseDataTableColumn<ReportingProductionRow>[] = [
    {
      id: "report",
      label: "Report",
      render: (row) => (
        <div className="min-w-0">
          <a
            href={row.href}
            className="break-words text-sm font-semibold text-foreground underline-offset-4 hover:underline"
          >
            {row.reportName}
          </a>
          <div className="text-xs text-muted-foreground">
            {row.reportClass} · {row.asOfLabel}
          </div>
        </div>
      )
    },
    ...(hasKnownOwner
      ? [{
          id: "owner",
          label: "Owner",
          render: (row: ReportingProductionRow) => <span className="text-xs text-muted-foreground">{row.owner}</span>
        } satisfies DenseDataTableColumn<ReportingProductionRow>]
      : []),
    {
      id: "state",
      label: "State",
      render: (row) => (
        <div className="min-w-0">
          <DesignSystemStatus status={row.severity}>{row.stateLabel}</DesignSystemStatus>
          {row.blockingReasons.length > 0 ? (
            <div className="mt-1 break-words text-xs text-muted-foreground">{row.blockingReasons[0]}</div>
          ) : null}
        </div>
      )
    },
    {
      id: "due",
      label: "Due",
      align: "right",
      render: (row) => (
        <span className={cn("text-xs tabular-nums", row.isOverdue ? "font-semibold text-danger" : "text-muted-foreground")}>
          {row.dueLabel}
          {row.isOverdue ? <span className="sr-only"> (past due)</span> : null}
        </span>
      )
    }
  ];

  return (
    <section
      role="region"
      aria-label="Reporting production control surface"
      className={cn("workspace-section-band", className)}
    >
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <div className="eyebrow-label">Reporting production</div>
          <h2 className="workspace-section-title">{model.periodLabel}</h2>
          <p className="workspace-section-summary">
            Plan, prepare, review, approve, publish and preserve the reports this period owes. Blocked and past-due
            reports are ranked first.
          </p>
        </div>
        <div className="flex flex-wrap items-start gap-2">
          <Badge variant={model.blockedCount > 0 ? "danger" : model.reviewCount > 0 ? "warning" : "success"}>
            {model.headlineLabel}
          </Badge>
          <Badge variant="outline">As of {model.asOfLabel}</Badge>
        </div>
      </div>

      {period ? <ReportingPeriodStrip period={period} /> : null}

      <nav aria-label="Reporting pipeline lanes" className="mt-4">
        <ol className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
          {model.lanes.map((lane) => (
            <li key={lane.key}>
              <a
                href={lane.href}
                className="flex h-full items-start gap-2 rounded-md border border-border/70 bg-background/75 px-3 py-2 transition-colors hover:border-primary/50 hover:bg-primary/5"
                aria-label={`${lane.label}: ${lane.purpose}`}
              >
                <span className="font-mono text-xs text-muted-foreground" aria-hidden="true">
                  {lane.ordinalLabel}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="flex items-center justify-between gap-2">
                    <span className="text-sm font-semibold text-foreground">{lane.label}</span>
                    <span className="font-mono text-xs tabular-nums text-muted-foreground">{lane.countLabel}</span>
                  </span>
                  <span className="mt-0.5 block text-xs leading-4 text-muted-foreground">{lane.purpose}</span>
                </span>
              </a>
            </li>
          ))}
        </ol>
      </nav>

      <div className="mt-4 grid gap-4 xl:grid-cols-[minmax(0,1fr)_300px]">
        <section aria-label="Production register" className="min-w-0 rounded-md border border-border/70 bg-background/75">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border/70 px-3 py-2">
            <h3 className="text-sm font-semibold text-foreground">Production status</h3>
            <Badge variant="outline">{model.totalCount} in period</Badge>
          </div>
          {model.isEmpty ? (
            <p className="px-3 py-6 text-sm text-muted-foreground">
              No reports are in production for this period.
            </p>
          ) : (
            <DenseDataTable
              columns={columns}
              rows={model.register}
              getRowId={(row) => row.runId}
              getRowAriaLabel={(row) => row.ariaLabel}
              ariaLabel="Reports in production this period"
              emptyText="No reports are in production for this period."
            />
          )}
        </section>

        <div className="min-w-0 space-y-4">
          <section aria-label="Reporting attention" className="rounded-md border border-border/70 bg-background/75">
            <div className="flex items-center gap-2 border-b border-border/70 px-3 py-2">
              <CircleAlert className="h-4 w-4 text-warning" aria-hidden="true" />
              <h3 className="text-sm font-semibold text-foreground">Attention</h3>
            </div>
            {model.attention.length === 0 ? (
              <p className="px-3 py-3 text-xs text-muted-foreground">Nothing needs attention in this period.</p>
            ) : (
              <ul className="divide-y divide-border/70">
                {model.attention.map((item) => (
                  <li key={item.key} className="flex items-center justify-between gap-2 px-3 py-2">
                    <DesignSystemStatus status={item.severity}>{item.label}</DesignSystemStatus>
                    {item.href ? (
                      <a
                        href={item.href}
                        className="inline-flex items-center gap-1 text-xs text-primary underline-offset-4 hover:underline"
                        aria-label={`Open ${item.label}`}
                      >
                        Open
                        <ArrowUpRight className="h-3 w-3" aria-hidden="true" />
                      </a>
                    ) : null}
                  </li>
                ))}
              </ul>
            )}
          </section>

          {model.recentlyPublished.length > 0 ? (
            <section aria-label="Recently published" className="rounded-md border border-border/70 bg-background/75">
              <div className="flex items-center gap-2 border-b border-border/70 px-3 py-2">
                <Clock className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
                <h3 className="text-sm font-semibold text-foreground">Recently published</h3>
              </div>
              <ul className="divide-y divide-border/70">
                {model.recentlyPublished.map((row) => (
                  <li key={row.runId} className="px-3 py-2">
                    <a href={row.href} className="block text-xs font-semibold text-foreground underline-offset-4 hover:underline">
                      {row.reportName}
                    </a>
                    <div className="text-xs text-muted-foreground">{row.asOfLabel}</div>
                  </li>
                ))}
              </ul>
            </section>
          ) : null}
        </div>
      </div>
    </section>
  );
}

function ReportingPeriodStrip({ period }: { period: ReportingPeriodModel }) {
  return (
    <section
      aria-label="Reporting period"
      className="mt-4 rounded-md border border-border/70 bg-background/75 px-3 py-2"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm font-semibold text-foreground">{period.label}</span>
          <DesignSystemStatus status={period.statusSeverity}>{period.statusLabel}</DesignSystemStatus>
          {period.isReproducible ? null : (
            <Badge variant="warning">{period.missingSnapshotLabels.length} snapshots unbound</Badge>
          )}
        </div>
        <span className="text-xs text-muted-foreground">Accounting close: {period.closeStatusLabel}</span>
      </div>

      <dl className="mt-2 grid gap-2 text-xs sm:grid-cols-3 xl:grid-cols-5" aria-label="Reporting period milestones">
        {period.milestones.map((milestone) => (
          <div key={milestone.key} className="min-w-0">
            <dt className="text-muted-foreground">{milestone.label}</dt>
            <dd
              className={cn(
                "font-mono tabular-nums",
                milestone.isReached ? "text-muted-foreground" : "font-semibold text-foreground"
              )}
            >
              {milestone.dateLabel}
            </dd>
          </div>
        ))}
      </dl>

      {period.requiresRefreezeReview && period.refreezeReason ? (
        <p role="status" className="mt-2 text-xs font-semibold text-danger">
          {period.refreezeReason} Affected reports must be re-frozen before publication.
        </p>
      ) : null}
    </section>
  );
}
