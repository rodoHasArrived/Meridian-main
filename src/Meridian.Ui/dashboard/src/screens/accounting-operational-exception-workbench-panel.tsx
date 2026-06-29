import { AlertCircle } from "lucide-react";
import { Link } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { OperationalExceptionWorkbenchViewState } from "@/screens/accounting-screen.view-model";

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
          <MetricCard
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
                        <div className="mt-1 break-words font-mono text-[11px] text-muted-foreground">{item.subtitle}</div>
                      </div>
                      <Badge variant={item.statusTone}>{item.statusLabel}</Badge>
                    </div>
                    <div className="mt-3 grid gap-2 text-xs text-muted-foreground sm:grid-cols-2 xl:grid-cols-4">
                      <span>Owner: {item.ownerLabel}</span>
                      <span>SLA: {item.slaLabel}</span>
                      <span>{item.commentLabel}</span>
                      <span>{item.auditLabel}</span>
                    </div>
                    <Button asChild size="sm" variant="ghost" className="mt-3">
                      <Link to={item.routeHref}>{item.routeLabel}</Link>
                    </Button>
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
