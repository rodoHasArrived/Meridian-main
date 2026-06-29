import { ArrowRight } from "lucide-react";
import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import type { AppShellTrustStripState } from "@/app-shell.view-model";
import { NumberPassportPanel, type NumberPassportPanelItem } from "@/components/meridian/number-passport";
import { WorkstationTrustStrip } from "@/components/meridian/workstation-trust-strip";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import type { AppShellWorkflowContinuityViewModel } from "@/lib/app-shell-workflow-continuity";
import { buildDailyControlTowerModel, type DailyControlTowerBadgeVariant } from "@/lib/daily-control-tower";
import { cn } from "@/lib/utils";

export function DailyControlTowerScreen({
  viewModel,
  trustStrip
}: {
  viewModel: AppShellWorkflowContinuityViewModel;
  trustStrip: AppShellTrustStripState;
}) {
  const controlTower = buildDailyControlTowerModel(viewModel);
  const decision = controlTower.decision;

  return (
    <div className="space-y-5" aria-labelledby="daily-control-tower-heading">
      <section className="panel-surface-strong px-4 py-4" aria-labelledby="daily-control-tower-heading">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
          <div className="min-w-0">
            <div className="eyebrow-label">Daily control tower</div>
            <h2 id="daily-control-tower-heading" className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
              What needs an operator decision now
            </h2>
            <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">
              Ranked from the shell workflow continuity model so the first workstation screen shows blockers, owner, affected output, next action, and proof before opening a workspace.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={controlTower.decisionBadgeVariant} dot>
              {decision.statusLabel}
            </Badge>
            <Button asChild size="sm">
              <Link to={decision.actionHref} aria-label={decision.actionAriaLabel}>
                <span>{decision.actionLabel}</span>
                <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
              </Link>
            </Button>
          </div>
        </div>

        <div className="mt-4 grid gap-3 lg:grid-cols-[minmax(0,1fr)_20rem]">
          <article className={cn("workflow-continuity-decision", `workflow-continuity-decision-${decision.statusTone}`)} aria-labelledby="daily-control-tower-decision-title">
            <div className="workflow-continuity-decision-copy">
              <div className="workflow-continuity-decision-head">
                <span className="eyebrow-label">{decision.label}</span>
                <span className="workflow-continuity-decision-status">{decision.statusLabel}</span>
              </div>
              <h3 id="daily-control-tower-decision-title">{decision.title}</h3>
              <p>{decision.summary}</p>
              <div className="workflow-continuity-decision-reason">
                <span>{decision.reasonLabel}</span>
                <span>{decision.reason}</span>
              </div>
              {decision.evidenceLabel ? (
                <span className="workflow-continuity-decision-evidence">{decision.evidenceLabel}</span>
              ) : null}
              <dl className="mt-4 grid gap-2 sm:grid-cols-2 xl:grid-cols-5" aria-label="Daily control tower decision facts">
                {controlTower.decisionFacts.map((fact) => (
                  <div key={fact.id} className="rounded-md border border-border/70 bg-background/70 px-3 py-2">
                    <dt className="text-[11px] font-semibold uppercase text-muted-foreground">{fact.label}</dt>
                    <dd className="mt-1 min-w-0">
                      {fact.href ? (
                        <Link to={fact.href} className="font-medium text-primary hover:underline" aria-label={fact.ariaLabel ?? undefined}>
                          {fact.value}
                        </Link>
                      ) : fact.badgeVariant ? (
                        <Badge variant={fact.badgeVariant}>{fact.value}</Badge>
                      ) : (
                        <span className="font-medium text-foreground">{fact.value}</span>
                      )}
                    </dd>
                    <dd className="mt-1 text-xs leading-5 text-muted-foreground">{fact.detail}</dd>
                  </div>
                ))}
              </dl>
            </div>
          </article>

          <section className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3" aria-labelledby="daily-control-tower-trust-heading">
            <h3 id="daily-control-tower-trust-heading" className="text-sm font-semibold text-foreground">Trust and proof posture</h3>
            <WorkstationTrustStrip
              viewModel={trustStrip}
              variant="card-list"
              ariaLabel="Daily control tower trust strip"
              className="mt-3"
            />
          </section>
        </div>
      </section>

      <section className="panel-surface px-4 py-4" aria-labelledby="daily-control-tower-queue-heading">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h3 id="daily-control-tower-queue-heading" className="text-base font-semibold text-foreground">Blocked output queue</h3>
            <p className="mt-1 text-sm leading-6 text-muted-foreground">{controlTower.focusSummary}</p>
          </div>
          {controlTower.focusOverflowLabel ? <Badge variant="outline">{controlTower.focusOverflowLabel}</Badge> : null}
        </div>

        {controlTower.focusRows.length > 0 ? (
          <div className="mt-4 overflow-x-auto">
            <table className="w-full min-w-[1120px] text-left text-sm" aria-label="Daily control tower blocked output queue">
              <thead className="border-b border-border/70 text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
                <tr>
                  <th scope="col" className="px-3 py-2">Status</th>
                  <th scope="col" className="px-3 py-2">Blocked or review item</th>
                  <th scope="col" className="px-3 py-2">Owner</th>
                  <th scope="col" className="px-3 py-2">Affected output</th>
                  <th scope="col" className="px-3 py-2">Next action</th>
                  <th scope="col" className="px-3 py-2">Proof</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/60">
                {controlTower.focusRows.map((row) => {
                  const { item } = row;
                  return (
                    <tr key={item.id}>
                      <td className="px-3 py-3 align-top">
                        <Badge variant={row.badgeVariant}>{row.statusLabel}</Badge>
                      </td>
                      <td className="px-3 py-3 align-top">
                        <div className="font-semibold text-foreground">{item.label}</div>
                        <p className="mt-1 text-xs leading-5 text-muted-foreground">{item.detail}</p>
                      </td>
                      <td className="px-3 py-3 align-top text-foreground">{item.workspaceLabel}</td>
                      <td className="px-3 py-3 align-top text-muted-foreground">{row.outputLabel}</td>
                      <td className="px-3 py-3 align-top">
                        <Link to={item.route} className="font-medium text-primary hover:underline" aria-label={item.ariaLabel}>
                          {item.actionLabel}
                        </Link>
                      </td>
                      <td className="px-3 py-3 align-top">
                        <NumberPassportPanel
                          title="Proof Passport"
                          ariaLabel={`${item.label} Proof Passport`}
                          summary={row.proofPassportSummary}
                          statusLabel={row.proofPassportStatusLabel}
                          statusBadgeVariant={row.badgeVariant}
                          className="min-w-[28rem] border-border/70 bg-background/55"
                          items={row.proofPassportItems.map((passportItem) => ({
                            id: passportItem.id,
                            label: passportItem.label,
                            value: passportItem.value,
                            detail: passportItem.detail,
                            href: passportItem.href ?? undefined,
                            ariaLabel: passportItem.ariaLabel,
                            valueClassName: dailyControlTowerPassportTextClass(passportItem.badgeVariant)
                          }))}
                          renderLink={renderDailyControlTowerPassportLink}
                        />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="mt-4 rounded-md border border-border/70 bg-secondary/20 px-3 py-3 text-sm text-muted-foreground">
            {controlTower.focusEmptyText}
          </p>
        )}
      </section>

      <section className="grid gap-4 xl:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]" aria-label="Daily control tower supporting evidence">
        <div className="panel-surface px-4 py-4">
          <div className="flex flex-wrap items-start justify-between gap-2">
            <div className="min-w-0">
              <h3 className="text-base font-semibold text-foreground">{controlTower.linkedContextLabel}</h3>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">{controlTower.linkedContextSummary}</p>
            </div>
            <Badge variant={controlTower.linkedContextBadgeVariant}>{controlTower.linkedContextPostureLabel}</Badge>
          </div>
          <div className="mt-3 grid gap-2">
            {controlTower.linkedContextItems.length > 0 ? controlTower.linkedContextItems.map((item) => (
              <Link key={item.id} to={item.route} aria-label={item.ariaLabel} className={cn("workflow-continuity-linked-item", `workflow-continuity-linked-item-${item.tone}`)}>
                <span className="workflow-continuity-linked-copy">
                  <span>{item.workspaceLabel}</span>
                  <span>{item.label}</span>
                </span>
                <span className="workflow-continuity-linked-status">{item.statusLabel}</span>
              </Link>
            )) : (
              <p className="text-sm text-muted-foreground">{controlTower.linkedContextEmptyText}</p>
            )}
          </div>
        </div>

        <div className="panel-surface px-4 py-4">
          <div className="flex flex-wrap items-start justify-between gap-2">
            <div className="min-w-0">
              <h3 className="text-base font-semibold text-foreground">{controlTower.evidenceTimelineLabel}</h3>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">{controlTower.evidenceTimelineSummary}</p>
            </div>
            {controlTower.evidenceTimelineOverflowLabel ? <Badge variant="outline">{controlTower.evidenceTimelineOverflowLabel}</Badge> : null}
          </div>
          <div className="mt-3 grid gap-2">
            {controlTower.evidenceTimelineItems.length > 0 ? controlTower.evidenceTimelineItems.map((item) => (
              <Link key={item.id} to={item.route} aria-label={item.ariaLabel} className={cn("workflow-continuity-evidence-item", `workflow-continuity-evidence-item-${item.tone}`)}>
                <span className="workflow-continuity-evidence-copy">
                  <span>{item.workspaceLabel}</span>
                  <span>{item.label}</span>
                  <span>{item.detail}</span>
                </span>
                <time dateTime={item.timestampIso}>{item.timestampLabel}</time>
              </Link>
            )) : (
              <p className="text-sm text-muted-foreground">{controlTower.evidenceTimelineEmptyText}</p>
            )}
          </div>
        </div>
      </section>
    </div>
  );
}

function renderDailyControlTowerPassportLink(item: NumberPassportPanelItem, className: string, children: ReactNode) {
  return (
    <Link to={item.href ?? ""} className={cn(className, "hover:underline")} aria-label={item.ariaLabel ?? undefined}>
      {children}
    </Link>
  );
}

function dailyControlTowerPassportTextClass(variant: DailyControlTowerBadgeVariant): string {
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
