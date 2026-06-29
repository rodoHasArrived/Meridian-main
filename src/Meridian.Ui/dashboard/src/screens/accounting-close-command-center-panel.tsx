import { AlertCircle, CheckCircle2, RefreshCcw } from "lucide-react";
import { Link } from "react-router-dom";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { accountingToolingBadgeVariant, accountingToolingBorderClass } from "@/screens/accounting-screen.styles";
import type { CloseCommandCenterViewState } from "@/screens/accounting-screen.view-model";
import { cn } from "@/lib/utils";

interface CloseCommandCenterPanelProps {
  view: CloseCommandCenterViewState;
  onRefresh: () => void;
}

export function CloseCommandCenterPanel({ view, onRefresh }: CloseCommandCenterPanelProps) {
  return (
    <section id="close-command-center" className="workspace-section-band" aria-labelledby="close-command-center-heading">
      <span className="sr-only" aria-live="polite">{view.liveRegionText}</span>
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Controller close</p>
          <h3 id="close-command-center-heading" className="workspace-section-title">{view.title}</h3>
          <p className="workspace-section-summary">{view.description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={accountingToolingBadgeVariant(view.statusTone)} dot>{view.statusLabel}</Badge>
          <Button type="button" size="sm" variant="outline" disabled={view.status === "loading"} busy={view.status === "loading"} busyLabel="Refreshing close command center" onClick={onRefresh}>
            <RefreshCcw className={cn("h-3.5 w-3.5", view.status === "loading" && "animate-spin")} aria-hidden="true" />
            Refresh
          </Button>
        </div>
      </div>

      <Card className={cn("panel-surface", accountingToolingBorderClass(view.statusTone))} role="region" aria-label={view.ariaLabel}>
        <CardHeader>
          <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.35fr)]">
            <div className="min-w-0">
              <CardTitle className="flex items-center gap-2 text-base">
                {view.statusTone === "success" ? (
                  <CheckCircle2 className="h-4 w-4 text-success" aria-hidden="true" />
                ) : (
                  <AlertCircle className={cn("h-4 w-4", view.statusTone === "danger" ? "text-danger" : "text-warning")} aria-hidden="true" />
                )}
                {view.periodLabel}
              </CardTitle>
              <CardDescription className="mt-2">{view.summary}</CardDescription>
            </div>
            <div className="grid gap-2 text-sm">
              <AccountingCloseValue label="Fund account" value={view.fundAccountLabel} />
              <AccountingCloseValue label="Updated" value={view.updatedLabel} />
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {view.loadingText ? <p role="status" className="text-sm text-muted-foreground">{view.loadingText}</p> : null}
          {view.errorText ? (
            <div role="alert" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
              {view.errorText}
            </div>
          ) : null}

          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {view.metricRows.map((metric) => {
              const body = (
                <div className={cn("h-full rounded-md border bg-secondary/20 px-3 py-3", accountingToolingBorderClass(metric.tone))}>
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0 text-xs font-semibold uppercase text-muted-foreground">{metric.label}</div>
                    <Badge variant={accountingToolingBadgeVariant(metric.tone)}>{metric.value}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{metric.detail}</p>
                </div>
              );

              return metric.href ? (
                <Link key={metric.id} to={metric.href} className="block focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40" aria-label={`Open ${metric.label} detail`}>
                  {body}
                </Link>
              ) : (
                <div key={metric.id}>{body}</div>
              );
            })}
          </div>

          <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_20rem]">
            <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Blocking and at-risk items</div>
              {view.blockerRows.length > 0 ? (
                <div role="list" className="mt-3 space-y-2" aria-label="Close command center blockers">
                  {view.blockerRows.map((item) => (
                    <div key={item.id} role="listitem" className={cn("rounded-md border bg-background/45 px-3 py-2", accountingToolingBorderClass(item.tone))}>
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <span className="font-semibold text-foreground">{item.label}</span>
                        <Badge variant={accountingToolingBadgeVariant(item.tone)}>{item.tone === "danger" ? "Blocker" : "Review"}</Badge>
                      </div>
                      <p className="mt-1 text-sm leading-6 text-muted-foreground">{item.detail}</p>
                      <div className="mt-3 grid gap-2 text-xs sm:grid-cols-2">
                        <AccountingCloseValue label="Status" value={item.statusLabel} />
                        <AccountingCloseValue label="Impact" value={item.impactLabel} />
                        {item.ownerLabel ? <AccountingCloseValue label="Owner" value={item.ownerLabel} /> : null}
                        {item.dueLabel ? <AccountingCloseValue label="Due" value={item.dueLabel} /> : null}
                        <AccountingCloseValue label="Evidence" value={item.evidenceLabel} />
                        <div className="data-grid-surface px-3 py-2 sm:col-span-2">
                          <span className="text-muted-foreground">Action</span>
                          <p className="mt-1 break-words text-foreground">{item.actionLabel}</p>
                        </div>
                      </div>
                      {item.href ? <Link to={item.href} className="mt-2 inline-block text-xs font-medium text-primary hover:underline">Open evidence</Link> : null}
                    </div>
                  ))}
                </div>
              ) : (
                <p className="mt-3 text-sm text-muted-foreground">No blocking or at-risk close items are surfaced.</p>
              )}
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Close actions</div>
              <div className="mt-3 grid gap-2">
                {view.actionRows.map((action) => (
                  <Button key={action.id} asChild variant={action.tone === "success" ? "outline" : "default"} size="sm">
                    <Link to={action.href} aria-label={action.ariaLabel}>{action.label}</Link>
                  </Button>
                ))}
              </div>
            </div>
          </div>
        </CardContent>
      </Card>
    </section>
  );
}

function AccountingCloseValue({ label, value }: { label: string; value: string }) {
  return (
    <div className="data-grid-surface px-3 py-2">
      <span className="text-muted-foreground">{label}</span>
      <p className="mt-1 break-words font-mono text-xs text-foreground">{value}</p>
    </div>
  );
}
