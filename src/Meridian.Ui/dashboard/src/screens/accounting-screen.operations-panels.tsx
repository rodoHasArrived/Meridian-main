import { AlertCircle, BookCheck, TrendingUp } from "lucide-react";
import { Link } from "react-router-dom";
import { MetricSnapshotCard } from "@/components/meridian/metric-card";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { cn } from "@/lib/utils";
import type {
  OperationalExceptionWorkbenchViewState,
  ReconciliationQueuePanelViewState,
  ReconciliationQueueRunRowViewModel,
  ReconciliationQueueRunTone,
  TradingParametersViewState,
} from "./accounting-screen.view-model";

const reconciliationQueueToneClass: Record<ReconciliationQueueRunTone, string> = {
  muted: "text-muted-foreground",
  warning: "text-warning",
  success: "text-success",
  primary: "text-primary"
};

export const reconciliationQueueColumns: DenseDataTableColumn<ReconciliationQueueRunRowViewModel>[] = [
  {
    id: "run",
    label: "Run",
    render: (row) => (
      <span className="block min-w-0">
        <span className="block font-semibold text-foreground">{row.strategyName}</span>
      </span>
    )
  },
  { id: "mode", label: "Mode", render: (row) => <span className="font-mono uppercase text-muted-foreground">{row.modeLabel}</span> },
  { id: "status", label: "Status", render: (row) => row.runStatusLabel },
  { id: "breaks", label: "Breaks", align: "right", render: (row) => <span className="font-mono tabular-nums">{row.breakCountLabel}</span> },
  { id: "open", label: "Open", align: "right", render: (row) => <span className="font-mono tabular-nums">{row.openBreakLabel}</span> },
  {
    id: "reconciliation",
    label: "Reconciliation",
    render: (row) => (
      <span className={cn("font-mono text-xs uppercase tracking-[0.14em]", reconciliationQueueToneClass[row.reconciliationTone])}>
        {row.reconciliationStatusLabel}
      </span>
    )
  },
  { id: "updated", label: "Updated", render: (row) => <span className="font-mono text-muted-foreground">{row.lastUpdatedLabel}</span> }
];

export function TradingParametersPanel({ view }: { view: TradingParametersViewState }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <TrendingUp className="h-4 w-4 text-primary" />
          Trading parameters
        </CardTitle>
        <CardDescription>
          Lot size, tick size, margin, and circuit-breaker constraints
          {view.securityId ? <> for <span className="font-mono">{view.securityId}</span></> : null}
          {view.asOfLabel !== "—" ? <> as of {view.asOfLabel}</> : null}.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-2">
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
        {!view.loadingText && !view.errorText && view.fields.length === 0 && (
          <p className="text-sm text-muted-foreground">No trading parameters available for this security.</p>
        )}
        {view.fields.length > 0 && (
          <dl className="grid gap-2">
            {view.fields.map((field) => (
              <div key={field.label} className="grid min-w-0 grid-cols-[minmax(0,0.6fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2">
                <dt className="min-w-0 text-xs text-muted-foreground">{field.label}</dt>
                <dd className={cn(
                  "min-w-0 break-words text-right font-mono text-xs",
                  field.tone === "warning" ? "text-warning" : "text-foreground"
                )}>
                  {field.value}
                </dd>
              </div>
            ))}
          </dl>
        )}
      </CardContent>
    </Card>
  );
}

export function ReconciliationQueueSummaryCard({ view }: { view: ReconciliationQueuePanelViewState }) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2 text-base">
              <BookCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              {view.overviewTitle}
            </CardTitle>
            <CardDescription className="mt-2">{view.overviewDescription}</CardDescription>
          </div>
          <Button asChild variant="outline" size="sm" className="w-fit shrink-0">
            <Link to={view.overviewActionHref} aria-label={view.overviewActionAriaLabel}>
              {view.overviewActionLabel}
            </Link>
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <DenseDataTable
          columns={reconciliationQueueColumns}
          rows={view.rows}
          getRowId={(row) => row.runId}
          getRowAriaLabel={(row) => row.ariaLabel}
          emptyText={view.emptyText}
          ariaLabel={view.listLabel}
          caption={view.overviewCaption}
        />
      </CardContent>
    </Card>
  );
}


export function OperationalExceptionWorkbenchPanel({ view }: { view: OperationalExceptionWorkbenchViewState }) {
  return (
    <section id="accounting-exceptions" className="workspace-section-band" aria-labelledby="accounting-exceptions-heading">
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Exceptions</p>
          <h3 id="accounting-exceptions-heading" className="workspace-section-title">{view.title}</h3>
          <p className="workspace-section-summary">{view.description}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button asChild size="sm" variant="outline">
            <Link to={view.reconciliationHref}>Reconciliation queue</Link>
          </Button>
          <Button asChild size="sm" variant="outline">
            <Link to={view.approvalsHref}>Approval gate</Link>
          </Button>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {view.metricRows.map((metric) => (
          <MetricSnapshotCard
            key={metric.id}
            id={metric.id}
            label={metric.label}
            value={metric.value}
            delta={metric.detail}
            tone={metric.tone}
          />
        ))}
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
        <Card className="panel-surface" role="region" aria-label="Unified operational exception queue">
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <AlertCircle className="h-4 w-4 text-primary" aria-hidden="true" />
              Case queue
            </CardTitle>
            <CardDescription>Reconciliation exceptions with owner, SLA, comments, and audit evidence counts.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {view.cases.length > 0 ? (
              <div role="list" className="space-y-2" aria-label="Operational exception cases">
                {view.cases.map((item) => (
                  <div key={item.id} role="listitem" aria-label={item.ariaLabel} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="min-w-0">
                        <div className="font-semibold text-foreground">{item.title}</div>
                        <div className="mt-1 break-words text-xs leading-5 text-muted-foreground">{item.subtitle}</div>
                      </div>
                      <Badge variant={item.statusTone}>{item.statusLabel}</Badge>
                    </div>
                    <div className="mt-3 grid gap-2 text-xs text-muted-foreground sm:grid-cols-2 xl:grid-cols-4">
                      <span>Owner: {item.ownerLabel}</span>
                      <span>Urgency: {item.slaLabel}</span>
                      <span>{item.commentLabel}</span>
                      <span>{item.auditLabel}</span>
                    </div>
                    <Button asChild size="sm" variant="ghost" className="mt-3">
                      <Link to={item.routeHref}>{item.routeLabel}</Link>
                    </Button>
                    <TechnicalDetails label="Audit details" className="mt-3 bg-background/45">
                      <dl className="grid gap-2 text-xs text-muted-foreground">
                        <OperationalDetailValue label="Case ID" value={item.id} />
                        <OperationalDetailValue label="Raw category" value={item.rawCategoryLabel} />
                      </dl>
                    </TechnicalDetails>
                  </div>
                ))}
              </div>
            ) : (
              <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
                {view.emptyText}
              </p>
            )}
          </CardContent>
        </Card>

        <Card className="panel-surface" role="region" aria-label="Exception workflow handoffs">
          <CardHeader>
            <CardTitle className="text-base">Workflow handoffs</CardTitle>
            <CardDescription>Resolution work stays connected to approval, audit, and retained evidence paths.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2">
            <Button asChild variant="secondary" className="w-full justify-start">
              <Link to={view.reconciliationHref}>Open break queue</Link>
            </Button>
            <Button asChild variant="outline" className="w-full justify-start">
              <Link to={view.approvalsHref}>Review approval blockers</Link>
            </Button>
            <Button asChild variant="outline" className="w-full justify-start">
              <Link to={view.evidenceHref}>Open exception evidence packet</Link>
            </Button>
            <Button asChild variant="outline" className="w-full justify-start">
              <Link to={view.auditHref}>Open audit timeline</Link>
            </Button>
          </CardContent>
        </Card>
      </div>
    </section>
  );
}


function OperationalDetailValue({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start justify-between gap-3">
      <dt>{label}</dt>
      <dd className="min-w-0 break-words text-right font-mono text-foreground">{value}</dd>
    </div>
  );
}
