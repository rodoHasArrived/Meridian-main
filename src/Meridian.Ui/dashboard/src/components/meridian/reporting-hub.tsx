import { AlertTriangle, ArrowUpRight, FileText, PanelRight, TableProperties } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { SeverityBadge } from "@/components/operations";
import { cn } from "@/lib/utils";
import { badgeVariantToSeverityStatus, semanticToneToTextClass } from "@/lib/shared-tone-mappings";
import type { ReportingHubModel, ReportingHubTone } from "@/lib/reporting-hub";

export interface ReportingHubProps {
  model: ReportingHubModel;
  className?: string;
}

/**
 * Reporting launch surface with daily work first and report-family launch cards below.
 */
export function ReportingHub({ model, className }: ReportingHubProps) {
  if (model.isEmpty) {
    return null;
  }

  const selectedWork = model.dailyWork[0] ?? null;

  return (
    <section role="region" aria-label="Daily reporting cockpit" className={cn("workspace-section-band", className)}>
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <div className="eyebrow-label">Reporting workbench</div>
          <h2 className="workspace-section-title">Daily reporting cockpit</h2>
          <p className="workspace-section-summary">
            Triage queued reporting work, inspect the selected blocker, then open the report family or evidence route that owns the next action.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Badge variant={model.dailyWork.length > 0 || model.attentionCount > 0 ? "warning" : "success"}>
            {model.dailyWorkSummaryLabel}
          </Badge>
          {model.cards.length > 0 ? (
            <Badge variant={model.attentionCount > 0 ? "warning" : "success"}>{model.summaryLabel}</Badge>
          ) : null}
        </div>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="space-y-4">
          <section className="rounded-md border border-border/70 bg-background/75" aria-label="Daily reporting triage queue">
            <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border/70 px-3 py-2">
              <div className="flex items-center gap-2">
                <AlertTriangle className="h-4 w-4 text-warning" aria-hidden="true" />
                <h3 className="text-sm font-semibold text-foreground">Triage queue</h3>
              </div>
              <Badge variant="outline">{model.dailyWorkSummaryLabel}</Badge>
            </div>
            {model.dailyWork.length > 0 ? (
              <ul className="divide-y divide-border/70" aria-label="Daily reporting work">
                {model.dailyWork.map((item, index) => (
                  <li key={item.workItemId} aria-label={item.ariaLabel} className={cn("grid gap-3 px-3 py-3 lg:grid-cols-[minmax(0,1fr)_auto]", index === 0 && "bg-primary/5")}>
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="text-xs font-semibold text-muted-foreground">{item.kindLabel}</span>
                        {index === 0 ? <Badge variant="outline">Selected</Badge> : null}
                        {item.dueLabel ? <Badge variant="outline">{item.dueLabel}</Badge> : null}
                      </div>
                      <div className="mt-1 break-words text-sm font-semibold text-foreground">{item.title}</div>
                      <p className="mt-1 text-xs leading-5 text-muted-foreground">{item.detail}</p>
                      <dl className="mt-3 grid gap-2 text-xs sm:grid-cols-2 xl:grid-cols-3" aria-label={`${item.title} decision facts`}>
                        <ReportingHubFact label="Blocked" value={item.blockedLabel} tone={item.tone} />
                        <ReportingHubFact label="Owner" value={item.owner} />
                        <ReportingHubFact label="Output" value={item.affectedOutputLabel} />
                        <ReportingHubFact label="Next action" value={item.nextActionLabel} />
                        <ReportingHubFact label="Proof" value={item.proofLabel} tone={item.evidenceGaps.length > 0 ? "warning" : "success"} />
                      </dl>
                    </div>
                    <div className="flex flex-wrap items-start gap-2 lg:justify-end">
                      <SeverityBadge status={badgeVariantToSeverityStatus(item.badgeVariant)} label={item.statusLabel} />
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
                    </div>
                  </li>
                ))}
              </ul>
            ) : (
              <div className="px-3 py-4 text-sm text-muted-foreground">
                {model.attentionCount > 0
                  ? `No dedicated daily work items are loaded. ${model.attentionCount} report ${model.attentionCount === 1 ? "family still needs" : "families still need"} review in the organizer below.`
                  : "No due packages, approvals, delivery failures, restatements, or evidence gaps are queued."}
              </div>
            )}
          </section>

          {model.cards.length > 0 ? (
            <section className="rounded-md border border-border/70 bg-background/75" aria-label="Report family organizer">
              <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border/70 px-3 py-2">
                <div className="flex items-center gap-2">
                  <TableProperties className="h-4 w-4 text-primary" aria-hidden="true" />
                  <h3 className="text-sm font-semibold text-foreground">Report family organizer</h3>
                </div>
                <Badge variant="outline">{model.summaryLabel}</Badge>
              </div>
              <div className="overflow-x-auto">
                <table className="min-w-full text-sm">
                  <thead className="bg-secondary/30 text-xs uppercase text-muted-foreground">
                    <tr>
                      <th className="px-3 py-2 text-left">Family</th>
                      <th className="px-3 py-2 text-left">Readiness</th>
                      <th className="px-3 py-2 text-left">Latest approved</th>
                      <th className="px-3 py-2 text-left">Activity</th>
                      <th className="px-3 py-2 text-left">Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {model.cards.map((card) => (
                      <tr key={card.familyKey} aria-label={card.ariaLabel} className="border-t border-border/70">
                        <td className="px-3 py-2 font-semibold text-foreground">{card.family}</td>
                        <td className="px-3 py-2">
                          <SeverityBadge status={badgeVariantToSeverityStatus(card.badgeVariant)} label={card.statusLabel} />
                        </td>
                        <td className={cn("px-3 py-2 text-xs", semanticToneToTextClass(card.statusTone))}>{card.approvedAsOfLabel}</td>
                        <td className="px-3 py-2 text-xs text-muted-foreground">
                          {card.detail}
                          {card.latestRunId ? (
                            <>
                              {" · latest "}
                              <span className="font-mono">{card.latestAsOfLabel}</span>
                            </>
                          ) : null}
                        </td>
                        <td className="px-3 py-2">
                          <Button asChild variant={card.needsAttention ? "default" : "outline"} size="sm">
                            <a href={card.nextActionHref} aria-label={`${card.nextActionLabel} for ${card.family}`}>
                              <FileText className="h-4 w-4" aria-hidden="true" />
                              {card.nextActionLabel}
                              <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
                            </a>
                          </Button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          ) : null}
        </div>

        <aside className="rounded-md border border-border/70 bg-background/75 p-3" aria-label="Selected reporting work detail">
          <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
            <PanelRight className="h-4 w-4 text-primary" aria-hidden="true" />
            Selected work
          </div>
          {selectedWork ? (
            <div className="mt-3 space-y-3">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <SeverityBadge status={badgeVariantToSeverityStatus(selectedWork.badgeVariant)} label={selectedWork.statusLabel} />
                  <Badge variant="outline">{selectedWork.kindLabel}</Badge>
                </div>
                <h3 className="mt-3 text-base font-semibold leading-snug text-foreground">{selectedWork.title}</h3>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{selectedWork.detail}</p>
              </div>
              <dl className="grid gap-2 text-xs" aria-label={`${selectedWork.title} selected detail`}>
                <ReportingHubFact label="Owner" value={selectedWork.owner} />
                <ReportingHubFact label="Affected output" value={selectedWork.affectedOutputLabel} />
                <ReportingHubFact label="Next action" value={selectedWork.nextActionLabel} />
                <ReportingHubFact label="Proof posture" value={selectedWork.proofLabel} tone={selectedWork.evidenceGaps.length > 0 ? "warning" : "success"} />
              </dl>
              <div className="flex flex-wrap gap-1.5">
                {selectedWork.context.slice(0, 4).map((context) => (
                  <Badge key={context} variant="outline">{context}</Badge>
                ))}
              </div>
              {selectedWork.evidenceGaps.length > 0 ? (
                <ul className="grid gap-1 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning" aria-label={`${selectedWork.title} evidence gaps`}>
                  {selectedWork.evidenceGaps.slice(0, 3).map((gap) => (
                    <li key={gap}>{gap}</li>
                  ))}
                </ul>
              ) : null}
              <div className="flex flex-wrap gap-2 pt-1">
                {selectedWork.primaryActionHref ? (
                  <Button asChild size="sm">
                    <a href={selectedWork.primaryActionHref} aria-label={`${selectedWork.primaryActionLabel}: ${selectedWork.title}`}>
                      <FileText className="h-4 w-4" aria-hidden="true" />
                      {selectedWork.primaryActionLabel}
                    </a>
                  </Button>
                ) : null}
                {selectedWork.secondaryActionHref && selectedWork.secondaryActionLabel ? (
                  <Button asChild variant="outline" size="sm">
                    <a href={selectedWork.secondaryActionHref} aria-label={`${selectedWork.secondaryActionLabel}: ${selectedWork.title}`}>
                      {selectedWork.secondaryActionLabel}
                      <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
                    </a>
                  </Button>
                ) : null}
              </div>
            </div>
          ) : (
            <p className="mt-3 text-sm leading-6 text-muted-foreground">
              No urgent reporting work is queued. Use the report family organizer to run, review, or set up the next output.
            </p>
          )}
        </aside>
      </div>
    </section>
  );
}

function ReportingHubFact({
  label,
  value,
  tone = "muted"
}: {
  label: string;
  value: string;
  tone?: ReportingHubTone;
}) {
  return (
    <div className="rounded-md border border-border/60 bg-background/50 px-2.5 py-2">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className={cn("mt-1 break-words text-xs leading-5", semanticToneToTextClass(tone))}>{value}</dd>
    </div>
  );
}
