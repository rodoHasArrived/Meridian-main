import { AlertTriangle, ArrowUpRight, FileText } from "lucide-react";
import type { ReactNode } from "react";
import { NumberPassportPanel, type NumberPassportPanelItem } from "@/components/meridian/number-passport";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import type { ReportingHubBadgeVariant, ReportingHubModel, ReportingHubProofPassportItem, ReportingHubTone } from "@/lib/reporting-hub";

export interface ReportingDailyCockpitProps {
  model: ReportingHubModel;
}

const toneClasses: Record<ReportingHubTone, string> = {
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
};

export function ReportingDailyCockpit({ model }: ReportingDailyCockpitProps) {
  return (
    <>
      {model.decisionQueues.length > 0 ? (
        <div className="space-y-3" aria-label="Daily reporting decision queue model">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-sm font-semibold text-foreground">Decision queues</h3>
            <Badge variant="outline">{model.decisionQueueSummaryLabel}</Badge>
          </div>
          <ul className="grid gap-3 lg:grid-cols-2" aria-label="Daily reporting decision queues">
            {model.decisionQueues.map((queue) => (
              <li
                key={queue.id}
                aria-label={queue.ariaLabel}
                className="flex min-h-40 flex-col gap-3 rounded-md border border-border/70 bg-secondary/20 px-3 py-3"
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
                      <AlertTriangle className={cn("h-3.5 w-3.5", toneClasses[queue.tone])} aria-hidden="true" />
                      Decision queue
                    </div>
                    <div className="mt-1 break-words text-sm font-semibold text-foreground">{queue.title}</div>
                  </div>
                  <span className="flex shrink-0 flex-wrap justify-end gap-1.5">
                    <Badge variant={queue.badgeVariant}>{queue.statusLabel}</Badge>
                    <Badge variant="outline">{queue.countLabel}</Badge>
                  </span>
                </div>
                <p className="text-xs leading-5 text-muted-foreground">{queue.detail}</p>
                <dl className="grid gap-2 text-xs sm:grid-cols-2" aria-label={`${queue.title} control summary`}>
                  <div className="rounded-md border border-border/60 bg-background/30 px-2.5 py-2">
                    <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">Blocked</dt>
                    <dd className="mt-1 font-semibold text-foreground">{queue.isBlocked ? "Blocked" : queue.tone === "warning" ? "Needs review" : "Ready"}</dd>
                  </div>
                  <div className="rounded-md border border-border/60 bg-background/30 px-2.5 py-2">
                    <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">Owner</dt>
                    <dd className="mt-1 break-words font-mono text-[11px] text-foreground">{queue.ownerLabel}</dd>
                  </div>
                  <div className="rounded-md border border-border/60 bg-background/30 px-2.5 py-2">
                    <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">Affected output</dt>
                    <dd className="mt-1 break-words font-semibold text-foreground">{queue.affectedOutputLabel}</dd>
                  </div>
                  <div className="rounded-md border border-border/60 bg-background/30 px-2.5 py-2">
                    <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">Next action</dt>
                    <dd className="mt-1 break-words text-foreground">{queue.nextActionLabel}</dd>
                  </div>
                  <div className="rounded-md border border-border/60 bg-background/30 px-2.5 py-2 sm:col-span-2">
                    <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">Proof</dt>
                    <dd className="mt-2">
                      <ReportingNumberPassport
                        ariaLabel={`${queue.title} Number Passport`}
                        summary={queue.proofPassportSummary}
                        statusLabel={queue.proofPassportStatusLabel}
                        statusBadgeVariant={queue.badgeVariant}
                        items={queue.proofPassportItems}
                      />
                    </dd>
                  </div>
                </dl>
                <div className="mt-auto flex flex-wrap gap-2 pt-1">
                  {queue.primaryActionHref ? (
                    <Button asChild variant="outline" size="sm">
                      <a href={queue.primaryActionHref} aria-label={`${queue.primaryActionLabel}: ${queue.title}`}>
                        <FileText className="h-4 w-4" aria-hidden="true" />
                        {queue.primaryActionLabel}
                        <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
                      </a>
                    </Button>
                  ) : (
                    <Badge variant="outline">{queue.primaryActionLabel}</Badge>
                  )}
                  {queue.secondaryActionHref && queue.secondaryActionLabel ? (
                    <Button asChild variant="ghost" size="sm">
                      <a href={queue.secondaryActionHref} aria-label={`${queue.secondaryActionLabel}: ${queue.title}`}>
                        {queue.secondaryActionLabel}
                        <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
                      </a>
                    </Button>
                  ) : null}
                </div>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

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
              <dl className="grid gap-2 text-xs sm:grid-cols-2" aria-label={`${item.title} control summary`}>
                <div className="rounded-md border border-border/60 bg-background/30 px-2.5 py-2">
                  <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">Blocked</dt>
                  <dd className="mt-1 font-semibold text-foreground">{item.blockedLabel}</dd>
                </div>
                <div className="rounded-md border border-border/60 bg-background/30 px-2.5 py-2">
                  <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">Owner</dt>
                  <dd className="mt-1 break-words font-mono text-[11px] text-foreground">{item.owner}</dd>
                </div>
                <div className="rounded-md border border-border/60 bg-background/30 px-2.5 py-2">
                  <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">Affected output</dt>
                  <dd className="mt-1 break-words font-semibold text-foreground">{item.affectedOutputLabel}</dd>
                </div>
                <div className="rounded-md border border-border/60 bg-background/30 px-2.5 py-2">
                  <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">Next action</dt>
                  <dd className="mt-1 break-words text-foreground">{item.nextActionLabel}</dd>
                </div>
                <div className="rounded-md border border-border/60 bg-background/30 px-2.5 py-2 sm:col-span-2">
                  <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">Proof</dt>
                  <dd className="mt-2">
                    <ReportingNumberPassport
                      ariaLabel={`${item.title} Number Passport`}
                      summary={item.proofPassportSummary}
                      statusLabel={item.proofPassportStatusLabel}
                      statusBadgeVariant={item.badgeVariant}
                      items={item.proofPassportItems}
                    />
                  </dd>
                </div>
              </dl>
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
    </>
  );
}

function ReportingNumberPassport({
  ariaLabel,
  summary,
  statusLabel,
  statusBadgeVariant,
  items
}: {
  ariaLabel: string;
  summary: string;
  statusLabel: string;
  statusBadgeVariant: ReportingHubBadgeVariant;
  items: ReportingHubProofPassportItem[];
}) {
  return (
    <NumberPassportPanel
      title="Number Passport"
      ariaLabel={ariaLabel}
      summary={summary}
      statusLabel={statusLabel}
      statusBadgeVariant={statusBadgeVariant}
      className="border-border/70 bg-background/55"
      items={items.map((item) => ({
        id: item.id,
        label: item.label,
        value: item.value,
        detail: item.detail,
        href: item.href ?? undefined,
        ariaLabel: item.ariaLabel,
        valueClassName: reportingPassportTextClass(item.badgeVariant)
      }))}
      renderLink={renderReportingPassportLink}
    />
  );
}

function renderReportingPassportLink(item: NumberPassportPanelItem, className: string, children: ReactNode) {
  return (
    <a href={item.href ?? ""} className={cn(className, "hover:underline")} aria-label={item.ariaLabel ?? undefined}>
      {children}
    </a>
  );
}

function reportingPassportTextClass(variant: ReportingHubBadgeVariant): string {
  switch (variant) {
    case "success":
      return "text-success";
    case "warning":
      return "text-warning";
    case "danger":
      return "text-danger";
    default:
      return "text-foreground";
  }
}
