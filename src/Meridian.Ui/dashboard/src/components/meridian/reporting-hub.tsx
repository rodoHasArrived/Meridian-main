import { AlertTriangle, ArrowUpRight, FileText } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import type { ReportingHubModel, ReportingHubTone } from "@/lib/reporting-hub";

export interface ReportingHubProps {
  model: ReportingHubModel;
  className?: string;
}

const toneClasses: Record<ReportingHubTone, string> = {
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
};

/**
 * Reporting launch surface with daily work first and report-family launch cards below.
 */
export function ReportingHub({ model, className }: ReportingHubProps) {
  if (model.isEmpty) {
    return null;
  }

  return (
    <section role="region" aria-label="Daily reporting cockpit" className={className}>
      <Card className="panel-surface">
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="min-w-0">
              <div className="eyebrow-label">Reporting hub</div>
              <CardTitle>Daily reporting cockpit</CardTitle>
              <CardDescription>
                Due packages, approvals, delivery failures, restatements, and evidence gaps.
              </CardDescription>
            </div>
            <div className="flex flex-wrap gap-2">
              <Badge variant={model.dailyWork.length > 0 ? "warning" : "success"}>{model.dailyWorkSummaryLabel}</Badge>
              {model.cards.length > 0 ? (
                <Badge variant={model.attentionCount > 0 ? "warning" : "success"}>{model.summaryLabel}</Badge>
              ) : null}
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-5">
          {model.dailyWork.length > 0 ? (
            <ul className="grid gap-3 lg:grid-cols-2" aria-label="Daily reporting work">
              {model.dailyWork.map((item) => (
                <li
                  key={item.workItemId}
                  aria-label={item.ariaLabel}
                  className="flex min-h-40 flex-col gap-3 rounded-md border border-border/70 bg-secondary/20 px-3 py-3"
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
                        <AlertTriangle className={cn("h-3.5 w-3.5", toneClasses[item.tone])} aria-hidden="true" />
                        {item.kindLabel}
                      </div>
                      <div className="mt-1 break-words text-sm font-semibold text-foreground">{item.title}</div>
                    </div>
                    <Badge variant={item.badgeVariant}>{item.statusLabel}</Badge>
                  </div>
                  <p className="text-xs leading-5 text-muted-foreground">{item.detail}</p>
                  <div className="flex flex-wrap gap-1.5">
                    {item.dueLabel ? <Badge variant="outline">{item.dueLabel}</Badge> : null}
                    <Badge variant="outline">{item.owner}</Badge>
                    {item.context.slice(0, 3).map((context) => (
                      <Badge key={context} variant="outline">{context}</Badge>
                    ))}
                  </div>
                  {item.evidenceGaps.length > 0 ? (
                    <ul className="grid gap-1 text-xs leading-5 text-warning" aria-label={`${item.title} evidence gaps`}>
                      {item.evidenceGaps.slice(0, 2).map((gap) => (
                        <li key={gap}>{gap}</li>
                      ))}
                    </ul>
                  ) : null}
                  <div className="mt-auto flex flex-wrap gap-2 pt-1">
                    {item.primaryActionHref ? (
                      <Button asChild variant="outline" size="sm">
                        <a href={item.primaryActionHref} aria-label={`${item.primaryActionLabel}: ${item.title}`}>
                          <FileText className="h-4 w-4" aria-hidden="true" />
                          {item.primaryActionLabel}
                          <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
                        </a>
                      </Button>
                    ) : (
                      <Badge variant="outline">{item.primaryActionLabel}</Badge>
                    )}
                    {item.secondaryActionHref && item.secondaryActionLabel ? (
                      <Button asChild variant="ghost" size="sm">
                        <a href={item.secondaryActionHref} aria-label={`${item.secondaryActionLabel}: ${item.title}`}>
                          {item.secondaryActionLabel}
                          <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
                        </a>
                      </Button>
                    ) : null}
                  </div>
                </li>
              ))}
            </ul>
          ) : (
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3 text-sm text-muted-foreground">
              No due packages, approvals, delivery failures, restatements, or evidence gaps are queued.
            </div>
          )}

          {model.cards.length > 0 ? (
            <div className="space-y-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h3 className="text-sm font-semibold text-foreground">Reports by family</h3>
                <Badge variant="outline">{model.summaryLabel}</Badge>
              </div>
              <ul className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                {model.cards.map((card) => (
                  <li
                    key={card.family}
                    aria-label={card.ariaLabel}
                    className="flex h-full flex-col gap-2 rounded-md border border-border/70 bg-secondary/20 px-3 py-3"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <span className="min-w-0 break-words font-semibold text-foreground">{card.family}</span>
                      <Badge variant={card.badgeVariant}>{card.statusLabel}</Badge>
                    </div>
                    <p className={cn("text-xs leading-5", toneClasses[card.statusTone])}>{card.approvedAsOfLabel}</p>
                    <p className="text-[11px] leading-4 text-muted-foreground">
                      {card.detail}
                      {card.latestRunId ? (
                        <>
                          {" · latest "}
                          <span className="font-mono">{card.latestAsOfLabel}</span>
                        </>
                      ) : null}
                    </p>
                    <div className="mt-auto pt-1">
                      {card.openHref ? (
                        <Button asChild variant="outline" size="sm">
                          <a href={card.openHref} aria-label={`${card.openLabel} for ${card.family}`}>
                            <FileText className="h-4 w-4" aria-hidden="true" />
                            {card.openLabel}
                            <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
                          </a>
                        </Button>
                      ) : (
                        <span className="text-[11px] italic text-muted-foreground">No output to open yet</span>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </CardContent>
      </Card>
    </section>
  );
}
