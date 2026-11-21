import { JournalLifecycleControls } from "./journal-lifecycle-controls";
import { AssetAccountingWorkflowPanel } from "./accounting-screen.asset-workflow-panel";
import { Copy, Paperclip, RefreshCcw, X } from "lucide-react";
import { useEffect, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { RecurringJournalQueue } from "@/components/accounting/RecurringJournalQueue";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { cn } from "@/lib/utils";
import { accountingToolingBadgeVariant, accountingToolingBorderClass } from "@/screens/accounting-screen.styles";
import { focusManualJournalCell, handleManualJournalCellKeyDown, ManualJournalDimensionsInspector, ManualJournalHealthBar, ManualJournalValidationNavigator } from "@/screens/accounting-screen.journal-entry-enhancements";
import type { ManualJournalEntryWorkbenchViewModel } from "@/screens/accounting-screen.view-model";

function ManualJournalPrivateCapitalActivityPanel({ activity }: { activity: ManualJournalEntryWorkbenchViewModel["privateCapitalActivity"] }) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div className="min-w-0">
            <CardTitle className="text-base">{activity.title}</CardTitle>
            <CardDescription>{activity.statusLabel} / {activity.projectedAtLabel}</CardDescription>
          </div>
          <Badge variant={activity.validationIssues.length > 0 ? "warning" : activity.fundEvents.length > 0 ? "success" : "outline"} dot>
            {activity.validationIssues.length > 0 ? "Review" : activity.fundEvents.length > 0 ? "Projected" : "Empty"}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-2 sm:grid-cols-2">
          {activity.summaryCards.map((metric) => (
            <div
              key={metric.id}
              className={cn(
                "rounded-md border px-3 py-2 text-sm",
                metric.tone === "success" ? "border-success/30 bg-success/10 text-success" :
                  metric.tone === "warning" ? "border-warning/30 bg-warning/10 text-warning" :
                    metric.tone === "danger" ? "border-danger/30 bg-danger/10 text-danger" :
                      "border-border/70 bg-secondary/20 text-muted-foreground"
              )}
            >
              <div className="text-[11px] font-semibold uppercase">{metric.label}</div>
              <div className="mt-1 font-mono text-base text-foreground">{metric.value}</div>
              <div className="mt-1 text-xs">{metric.detail}</div>
            </div>
          ))}
        </div>

        {activity.validationIssues.length > 0 ? (
          <div className="space-y-2">
            {activity.validationIssues.map((issue) => (
              <div key={issue.id} className="rounded border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                <div className="font-semibold">{issue.label}</div>
                <div className="mt-1">{issue.message}</div>
                <div className="mt-1 text-xs">{issue.detail}</div>
              </div>
            ))}
          </div>
        ) : null}

        {activity.paymentIntents.length > 0 ? (
          <>
            <div className="overflow-x-auto rounded-md border border-border/70">
              <table className="w-full min-w-[1040px] text-sm" aria-label="Payment intent and cash evidence workflows">
                <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2 text-left">Payment intent</th>
                    <th className="px-3 py-2 text-left">Status</th>
                    <th className="px-3 py-2 text-left">Expected cash</th>
                    <th className="px-3 py-2 text-left">Approvals</th>
                    <th className="px-3 py-2 text-left">Cash evidence</th>
                    <th className="px-3 py-2 text-left">Reconciliation</th>
                    <th className="px-3 py-2 text-left">Audit</th>
                  </tr>
                </thead>
                <tbody>
                  {activity.paymentIntents.map((intent) => (
                    <tr key={intent.id} className="border-t border-border/60 bg-background/30 align-top">
                      <td className="px-3 py-2">
                        <div className="break-all font-mono text-xs text-foreground">{intent.title}</div>
                        <div className="mt-1 break-all text-[11px] text-muted-foreground">{intent.subtitle}</div>
                        <div className="mt-1 text-[11px] text-muted-foreground">{intent.requestedLabel}</div>
                        {intent.workbenchRouteLabel !== "No workbench route" ? (
                          <a
                            className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                            href={intent.workbenchRouteLabel}
                            target="_blank"
                            rel="noreferrer"
                            aria-label={`Open payment intent workbench route for ${intent.title}`}
                          >
                            {intent.workbenchRouteLabel}
                          </a>
                        ) : null}
                      </td>
                      <td className="px-3 py-2">
                        <Badge variant={intent.statusTone} dot>{intent.statusLabel}</Badge>
                        <div className="mt-1 text-xs text-muted-foreground">{intent.readinessReasonLabel}</div>
                        <div className="mt-1 text-[11px] text-muted-foreground">{intent.executionDeferredLabel}</div>
                      </td>
                      <td className="px-3 py-2 text-xs">
                        <div className="font-mono">{intent.expectedCashLabel}</div>
                        <div className="mt-1 text-[11px] text-muted-foreground">{intent.requestMetadataLabel}</div>
                        <div className="mt-1 text-[11px] text-muted-foreground">{intent.sourceEvidenceLabel}</div>
                      </td>
                      <td className="px-3 py-2 text-xs">{intent.approvalLabel}</td>
                      <td className="px-3 py-2 text-xs">
                        <div>{intent.bankEvidenceLabel}</div>
                        {intent.evidenceRouteLabel !== "No evidence route" ? (
                          <a
                            className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                            href={intent.evidenceRouteLabel}
                            target="_blank"
                            rel="noreferrer"
                            aria-label={`Open payment intent evidence packet for ${intent.title}`}
                          >
                            {intent.evidenceRouteLabel}
                          </a>
                        ) : null}
                      </td>
                      <td className="px-3 py-2 text-xs">{intent.reconciliationLabel}</td>
                      <td className="px-3 py-2 text-xs">{intent.auditLabel}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <section className="space-y-3" aria-label="Payment intent cash evidence drilldowns">
              {activity.paymentIntents.map((intent) => (
                <section
                  key={`${intent.id}-cash-evidence-drilldown`}
                  className="rounded-md border border-border/70 bg-background/30 px-3 py-3"
                  aria-label={`Cash evidence drilldown for ${intent.title}`}
                >
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <div className="break-all font-mono text-xs text-foreground">{intent.title}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{intent.expectedCashLabel}</div>
                      <div className="mt-1 text-[11px] text-muted-foreground">{intent.requestMetadataLabel}</div>
                      <div className="mt-1 text-[11px] text-muted-foreground">{intent.sourceEvidenceLabel}</div>
                    </div>
                    <Badge variant={intent.statusTone} dot>{intent.statusLabel}</Badge>
                  </div>
                  <div className="mt-3 grid gap-3 lg:grid-cols-4">
                    <div>
                      <div className="text-[11px] font-semibold uppercase text-muted-foreground">Approval chain</div>
                      {intent.approvalSteps.length > 0 ? (
                        <ol className="mt-2 space-y-2">
                          {intent.approvalSteps.map((step) => (
                            <li key={step.id} className="text-xs">
                              <div className="flex flex-wrap items-center gap-2">
                                <span className="font-semibold text-foreground">{step.sequenceLabel}</span>
                                <Badge variant="outline">{step.statusLabel}</Badge>
                              </div>
                              <div className="mt-1 text-muted-foreground">{step.roleLabel} / {step.actorLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{step.decidedLabel}</div>
                              {step.evidenceRouteLabel !== "No approval evidence route" ? (
                                <a
                                  className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                                  href={step.evidenceRouteLabel}
                                  target="_blank"
                                  rel="noreferrer"
                                  aria-label={`Open approval evidence for ${intent.title} ${step.sequenceLabel}`}
                                >
                                  {step.evidenceRouteLabel}
                                </a>
                              ) : null}
                            </li>
                          ))}
                        </ol>
                      ) : (
                        <div className="mt-2 text-xs text-muted-foreground">No approval chain</div>
                      )}
                    </div>

                    <div>
                      <div className="text-[11px] font-semibold uppercase text-muted-foreground">Bank evidence</div>
                      {intent.bankEvidence.length > 0 ? (
                        <div className="mt-2 space-y-2">
                          {intent.bankEvidence.map((item) => (
                            <div key={item.id} className="text-xs">
                              <div className="flex flex-wrap items-center gap-2">
                                <span className="font-semibold text-foreground">{item.title}</span>
                                <Badge variant="outline">{item.statusLabel}</Badge>
                              </div>
                              <div className="mt-1 text-muted-foreground">{item.summaryLabel}</div>
                              <div className="mt-1 font-mono text-[11px] text-muted-foreground">{item.amountLabel} / {item.effectiveDateLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{item.referenceLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{item.recordedLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{item.recorderLabel}</div>
                              {item.evidenceRouteLabel !== "No bank evidence route" ? (
                                <a
                                  className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                                  href={item.evidenceRouteLabel}
                                  target="_blank"
                                  rel="noreferrer"
                                  aria-label={`Open bank evidence for ${intent.title} ${item.title}`}
                                >
                                  {item.evidenceRouteLabel}
                                </a>
                              ) : null}
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="mt-2 text-xs text-muted-foreground">No bank evidence</div>
                      )}
                    </div>

                    <div>
                      <div className="text-[11px] font-semibold uppercase text-muted-foreground">Reconciliation</div>
                      {intent.reconciliationLinks.length > 0 ? (
                        <div className="mt-2 space-y-2">
                          {intent.reconciliationLinks.map((link) => (
                            <div key={link.id} className="text-xs">
                              <div className="flex flex-wrap items-center gap-2">
                                <span className="break-all font-mono text-[11px] text-foreground">{link.id}</span>
                                <Badge variant="outline">{link.statusLabel}</Badge>
                              </div>
                              <div className="mt-1 text-muted-foreground">{link.summaryLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{link.caseLabel}</div>
                              {link.routeLabel !== "No reconciliation evidence route" ? (
                                <a
                                  className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                                  href={link.routeLabel}
                                  target="_blank"
                                  rel="noreferrer"
                                  aria-label={`Open reconciliation evidence for ${intent.title} ${link.id}`}
                                >
                                  {link.routeLabel}
                                </a>
                              ) : null}
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="mt-2 text-xs text-muted-foreground">No reconciliation link</div>
                      )}
                    </div>

                    <div>
                      <div className="text-[11px] font-semibold uppercase text-muted-foreground">Audit trail</div>
                      {intent.auditEvents.length > 0 ? (
                        <div className="mt-2 space-y-2">
                          {intent.auditEvents.map((event) => (
                            <div key={event.id} className="text-xs">
                              <div className="font-semibold text-foreground">{event.actionLabel}</div>
                              <div className="mt-1 text-muted-foreground">{event.summaryLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{event.actorLabel} / {event.recordedLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{event.evidenceLabel}</div>
                              {event.evidenceRouteLabels.map((route, index) => (
                                <a
                                  key={`${event.id}-${route}`}
                                  className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                                  href={route}
                                  target="_blank"
                                  rel="noreferrer"
                                  aria-label={`Open audit evidence ${index + 1} for ${intent.title} ${event.actionLabel}`}
                                >
                                  {route}
                                </a>
                              ))}
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="mt-2 text-xs text-muted-foreground">No audit trail</div>
                      )}
                    </div>
                  </div>
                </section>
              ))}
            </section>
          </>
        ) : null}

        {activity.fundEventLedgerRecords.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="w-full min-w-[980px] text-sm" aria-label="Private-capital fund event ledger records">
              <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left">Fund event</th>
                  <th className="px-3 py-2 text-left">Approval</th>
                  <th className="px-3 py-2 text-left">Effective</th>
                  <th className="px-3 py-2 text-left">Net</th>
                  <th className="px-3 py-2 text-left">Subledger</th>
                  <th className="px-3 py-2 text-left">GL impact</th>
                  <th className="px-3 py-2 text-left">Report output</th>
                  <th className="px-3 py-2 text-left">Evidence</th>
                </tr>
              </thead>
              <tbody>
                {activity.fundEventLedgerRecords.map((record) => (
                  <tr key={record.id} className="border-t border-border/60 bg-background/30 align-top">
                    <td className="px-3 py-2">
                      <div className="font-semibold text-foreground">{record.title}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{record.subtitle}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{record.memoLabel}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{record.referenceLabel}</div>
                      <div className="mt-2 rounded border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground">
                        <div className="flex flex-wrap items-center gap-1">
                          <Badge variant={record.paymentEvidenceTone} dot>{record.paymentEvidenceLabel}</Badge>
                        </div>
                        <div className="mt-1">{record.paymentEvidenceSummaryLabel}</div>
                        <div className="mt-1">{record.paymentEvidenceRequiredLabel}</div>
                      </div>
                      <a
                        className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                        href={record.activityRouteLabel}
                        target="_blank"
                        rel="noreferrer"
                        aria-label={`Open private-capital activity record for ${record.title}`}
                      >
                        {record.activityRouteLabel}
                      </a>
                      <a
                        className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                        href={record.commandCenterRouteLabel}
                        target="_blank"
                        rel="noreferrer"
                        aria-label={`Open fund event command center for ${record.title}`}
                      >
                        {record.commandCenterRouteLabel}
                      </a>
                    </td>
                    <td className="px-3 py-2">
                      <Badge variant={record.statusTone} dot>{record.statusLabel}</Badge>
                      <div className="mt-2">
                        <Badge variant={record.readinessTone} dot>{record.readinessLabel}</Badge>
                      </div>
                      <div className="mt-1 text-xs text-muted-foreground">{record.readinessReasonLabel}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{record.issueLabel}</div>
                      {record.nextActionRouteLabel !== "No next-action route" ? (
                        <a
                          className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                          href={record.nextActionRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open next action for ${record.title}`}
                        >
                          {record.nextActionLabel}
                        </a>
                      ) : (
                        <div className="mt-1 text-[11px] text-muted-foreground">{record.nextActionLabel}</div>
                      )}
                      {record.approvalRouteLabel !== "No approval route" ? (
                        <a
                          className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                          href={record.approvalRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open approval route for ${record.title}`}
                        >
                          {record.approvalRouteLabel}
                        </a>
                      ) : null}
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">{record.effectiveDateLabel}</td>
                    <td className="px-3 py-2 font-mono text-xs">
                      <div>{record.netActivityLabel}</div>
                      <div className="mt-1 text-[11px] text-muted-foreground">{record.grossActivityLabel} gross</div>
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>{record.subledgerLabel}</div>
                      <div className="mt-1 text-muted-foreground">{record.capitalAccountRollForwardLabel}</div>
                    </td>
                    <td className="px-3 py-2 text-xs">{record.ledgerImpactLabel}</td>
                    <td className="px-3 py-2 text-xs">
                      <div>{record.reportOutputLabel}</div>
                      <div className="mt-1 text-muted-foreground">{record.reportOutputDetailLabel}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{record.reportOutputRouteLabel}</div>
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>{record.evidenceLabel}</div>
                      {record.evidenceRouteLabel !== "No evidence route" ? (
                        <a
                          className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                          href={record.evidenceRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open evidence packet for ${record.title}`}
                        >
                          {record.evidenceRouteLabel}
                        </a>
                      ) : null}
                      {record.evidenceCategories.length > 0 ? (
                        <div className="mt-2 space-y-1" aria-label={`Evidence readiness categories for ${record.title}`}>
                          <div className="text-[11px] font-semibold uppercase text-muted-foreground">
                            {record.evidenceCategorySummaryLabel}
                          </div>
                          {record.evidenceCategories.map((category) => (
                            <div key={category.id} className="rounded border border-border/60 bg-secondary/20 px-2 py-1">
                              <div className="flex flex-wrap items-center gap-1">
                                <Badge variant={category.tone} dot>{category.label}</Badge>
                                <span className="text-[11px] text-muted-foreground">{category.statusLabel}</span>
                                <span className="font-mono text-[11px] text-muted-foreground">{category.evidenceLabel}</span>
                              </div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{category.summaryLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{category.requiredEvidenceLabel}</div>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="mt-2 text-[11px] text-muted-foreground">{record.evidenceCategorySummaryLabel}</div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {activity.capitalAccounts.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="w-full min-w-[760px] text-sm" aria-label="Private-capital capital account activity">
              <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left">Capital account</th>
                  <th className="px-3 py-2 text-left">Net</th>
                  <th className="px-3 py-2 text-left">Calls</th>
                  <th className="px-3 py-2 text-left">Distributions</th>
                  <th className="px-3 py-2 text-left">Other</th>
                  <th className="px-3 py-2 text-left">Last event</th>
                </tr>
              </thead>
              <tbody>
                {activity.capitalAccounts.map((account) => (
                  <tr key={account.id} className="border-t border-border/60 bg-background/30 align-top">
                    <td className="px-3 py-2">
                      <div className="break-all font-mono text-xs text-foreground">{account.title}</div>
                      <div className="mt-1 break-all text-[11px] text-muted-foreground">{account.subtitle}</div>
                    </td>
                    <td className="px-3 py-2 font-mono">{account.netActivityLabel}</td>
                    <td className="px-3 py-2 font-mono">{account.contributionLabel}</td>
                    <td className="px-3 py-2 font-mono">{account.distributionLabel}</td>
                    <td className="px-3 py-2 text-xs">
                      <div>Subscriptions {account.subscriptionLabel}</div>
                      <div>Redemptions {account.redemptionLabel}</div>
                      <div>Fees {account.managementFeeLabel}</div>
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>{account.eventCountLabel}</div>
                      <div className="mt-1 text-muted-foreground">{account.lastEventLabel}</div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {activity.capitalAccountSubledgers.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="w-full min-w-[980px] text-sm" aria-label="Private-capital capital account subledgers">
              <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left">Capital account</th>
                  <th className="px-3 py-2 text-left">Status</th>
                  <th className="px-3 py-2 text-left">Roll-forward</th>
                  <th className="px-3 py-2 text-left">Activity</th>
                  <th className="px-3 py-2 text-left">Counts</th>
                  <th className="px-3 py-2 text-left">Evidence</th>
                </tr>
              </thead>
              <tbody>
                {activity.capitalAccountSubledgers.map((subledger) => (
                  <tr key={subledger.id} className="border-t border-border/60 bg-background/30 align-top">
                    <td className="px-3 py-2">
                      <div className="break-all font-mono text-xs text-foreground">{subledger.title}</div>
                      <div className="mt-1 break-all text-[11px] text-muted-foreground">{subledger.subtitle}</div>
                      {subledger.activityRouteLabel !== "No subledger route" ? (
                        <a
                          className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                          href={subledger.activityRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open capital-account subledger for ${subledger.title}`}
                        >
                          {subledger.activityRouteLabel}
                        </a>
                      ) : null}
                    </td>
                    <td className="px-3 py-2">
                      <Badge variant={subledger.statusTone} dot>{subledger.statusLabel}</Badge>
                      <div className="mt-1 text-xs text-muted-foreground">{subledger.issueLabel}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{subledger.dateRangeLabel}</div>
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">
                      <div>{subledger.openingLabel} opening</div>
                      <div className="mt-1">{subledger.netActivityLabel} net</div>
                      <div className="mt-1">{subledger.endingLabel} ending</div>
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>Calls {subledger.contributionLabel}</div>
                      <div>Distributions {subledger.distributionLabel}</div>
                      <div className="mt-1 text-muted-foreground">{subledger.otherActivityLabel}</div>
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>{subledger.eventCountLabel}</div>
                      <div className="mt-1">{subledger.approvalQueueLabel}</div>
                      <div className="mt-1">{subledger.postedEventLabel}</div>
                      <div className="mt-1">{subledger.publishedReportLabel}</div>
                    </td>
                    <td className="px-3 py-2 text-xs">
                      <div>{subledger.evidenceLabel}</div>
                      <div className="mt-2 rounded border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground">
                        <div className="flex flex-wrap items-center gap-1">
                          <Badge variant={subledger.paymentEvidenceTone} dot>{subledger.paymentEvidenceLabel}</Badge>
                        </div>
                        <div className="mt-1">{subledger.paymentEvidenceSummaryLabel}</div>
                        <div className="mt-1">{subledger.paymentEvidenceRequiredLabel}</div>
                      </div>
                      {subledger.evidenceCategories.length > 0 ? (
                        <div className="mt-2 space-y-1" aria-label={`Subledger evidence readiness categories for ${subledger.title}`}>
                          <div className="text-[11px] font-semibold uppercase text-muted-foreground">
                            {subledger.evidenceCategorySummaryLabel}
                          </div>
                          {subledger.evidenceCategories.map((category) => (
                            <div key={category.id} className="rounded border border-border/60 bg-secondary/20 px-2 py-1">
                              <div className="flex flex-wrap items-center gap-1">
                                <Badge variant={category.tone} dot>{category.label}</Badge>
                                <span className="text-[11px] text-muted-foreground">{category.statusLabel}</span>
                                <span className="font-mono text-[11px] text-muted-foreground">{category.evidenceLabel}</span>
                              </div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{category.summaryLabel}</div>
                              <div className="mt-1 text-[11px] text-muted-foreground">{category.requiredEvidenceLabel}</div>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="mt-2 text-[11px] text-muted-foreground">{subledger.evidenceCategorySummaryLabel}</div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {activity.capitalAccountSubledgerEntries.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="w-full min-w-[820px] text-sm" aria-label="Private-capital capital account subledger">
              <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left">Event</th>
                  <th className="px-3 py-2 text-left">Status</th>
                  <th className="px-3 py-2 text-left">Effective</th>
                  <th className="px-3 py-2 text-left">Net</th>
                  <th className="px-3 py-2 text-left">Running</th>
                  <th className="px-3 py-2 text-left">Gross</th>
                  <th className="px-3 py-2 text-left">Evidence</th>
                </tr>
              </thead>
              <tbody>
                {activity.capitalAccountSubledgerEntries.map((entry) => (
                  <tr key={entry.id} className="border-t border-border/60 bg-background/30 align-top">
                    <td className="px-3 py-2">
                      <div className="font-semibold text-foreground">{entry.title}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{entry.subtitle}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{entry.memoLabel}</div>
                    </td>
                    <td className="px-3 py-2">
                      <Badge variant={entry.statusTone} dot>{entry.statusLabel}</Badge>
                      <div className="mt-1 text-xs text-muted-foreground">{entry.issueLabel}</div>
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">{entry.effectiveDateLabel}</td>
                    <td className="px-3 py-2 font-mono tabular-nums text-xs">{entry.netActivityLabel}</td>
                    <td className="px-3 py-2 font-mono tabular-nums text-xs">{entry.runningBalanceLabel}</td>
                    <td className="px-3 py-2 font-mono tabular-nums text-xs">{entry.grossAmountLabel}</td>
                    <td className="px-3 py-2 text-xs">{entry.evidenceLabel}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {activity.ledgerImpacts.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="w-full min-w-[820px] text-sm" aria-label="Private-capital ledger impacts">
              <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left">Impact</th>
                  <th className="px-3 py-2 text-left">Readiness</th>
                  <th className="px-3 py-2 text-left">Effective</th>
                  <th className="px-3 py-2 text-left">Debits</th>
                  <th className="px-3 py-2 text-left">Credits</th>
                  <th className="px-3 py-2 text-left">Imbalance</th>
                  <th className="px-3 py-2 text-left">Evidence</th>
                </tr>
              </thead>
              <tbody>
                {activity.ledgerImpacts.map((impact) => (
                  <tr key={impact.id} className="border-t border-border/60 bg-background/30 align-top">
                    <td className="px-3 py-2">
                      <div className="font-semibold text-foreground">{impact.title}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{impact.subtitle}</div>
                    </td>
                    <td className="px-3 py-2">
                      <Badge variant={impact.readinessTone} dot>{impact.readinessLabel}</Badge>
                      <div className="mt-1 text-xs text-muted-foreground">{impact.issueLabel}</div>
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">{impact.effectiveDateLabel}</td>
                    <td className="px-3 py-2 font-mono tabular-nums text-xs">{impact.debitLabel}</td>
                    <td className="px-3 py-2 font-mono tabular-nums text-xs">{impact.creditLabel}</td>
                    <td className="px-3 py-2 font-mono tabular-nums text-xs">{impact.imbalanceLabel}</td>
                    <td className="px-3 py-2 text-xs">
                      <div>{impact.evidenceLabel}</div>
                      <div className="mt-1 text-muted-foreground">{impact.lineLabel}</div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {activity.reportOutputs.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="w-full min-w-[960px] text-sm" aria-label="Private-capital report outputs">
              <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left">Output</th>
                  <th className="px-3 py-2 text-left">Readiness</th>
                  <th className="px-3 py-2 text-left">Effective</th>
                  <th className="px-3 py-2 text-left">Amount</th>
                  <th className="px-3 py-2 text-left">Evidence</th>
                  <th className="px-3 py-2 text-left">Workflow</th>
                  <th className="px-3 py-2 text-left">Route</th>
                </tr>
              </thead>
              <tbody>
                {activity.reportOutputs.map((output) => (
                  <tr key={output.id} className="border-t border-border/60 bg-background/30 align-top">
                    <td className="px-3 py-2">
                      <div className="font-semibold text-foreground">{output.title}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{output.subtitle}</div>
                    </td>
                    <td className="px-3 py-2">
                      <Badge variant={output.readinessTone} dot>{output.readinessLabel}</Badge>
                      <div className="mt-1 text-xs text-muted-foreground">{output.issueLabel}</div>
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">{output.effectiveDateLabel}</td>
                    <td className="px-3 py-2 font-mono tabular-nums text-xs">{output.amountLabel}</td>
                    <td className="px-3 py-2 text-xs">{output.evidenceLabel}</td>
                    <td className="px-3 py-2 text-xs">
                      <div className="break-all font-mono text-[11px] text-foreground">{output.workflowLabel}</div>
                      <div className="mt-1 text-muted-foreground">{output.publicationLabel}</div>
                      <div className="mt-1 text-muted-foreground">{output.provenanceLabel}</div>
                    </td>
                    <td className="px-3 py-2 break-all font-mono text-[11px] text-muted-foreground">{output.routeLabel}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        {activity.fundEvents.length > 0 ? (
          <div className="space-y-2">
            {activity.fundEvents.map((event) => (
              <div key={event.id} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="font-semibold text-foreground">{event.title}</div>
                    <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{event.subtitle}</div>
                  </div>
                  <Badge variant={event.statusTone} dot>{event.statusLabel}</Badge>
                </div>
                <dl className="mt-2 grid gap-2 text-xs sm:grid-cols-2">
                  <div><dt className="text-muted-foreground">Effective</dt><dd className="font-mono">{event.effectiveDateLabel}</dd></div>
                  <div><dt className="text-muted-foreground">Net</dt><dd className="font-mono tabular-nums">{event.amountLabel}</dd></div>
                  <div><dt className="text-muted-foreground">Gross</dt><dd className="font-mono tabular-nums">{event.grossAmountLabel}</dd></div>
                  <div><dt className="text-muted-foreground">Evidence</dt><dd>{event.evidenceLabel}</dd></div>
                  <div><dt className="text-muted-foreground">Payment</dt><dd className="break-all">{event.paymentLabel}</dd></div>
                  <div><dt className="text-muted-foreground">Validation</dt><dd>{event.validationLabel}</dd></div>
                </dl>
                <div className="mt-2 text-xs text-muted-foreground">{event.memoLabel}</div>
              </div>
            ))}
          </div>
        ) : (
          <p role="status" className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm text-muted-foreground">
            {activity.emptyText}
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function ManualJournalLineBadges({ badges }: { badges: ReturnType<ManualJournalEntryWorkbenchViewModel["getLineBadges"]> }) {
  return (
    <div className="mt-2 flex flex-wrap gap-1" aria-label="Line validation badges">
      {badges.map((badge) => (
        <span
          key={badge.id}
          title={badge.message}
          className={cn(
            "inline-flex max-w-full items-center rounded border px-1.5 py-0.5 text-[10px] font-semibold uppercase leading-tight",
            badge.tone === "danger"
              ? "border-danger/30 bg-danger/10 text-danger"
              : badge.tone === "warning"
                ? "border-warning/30 bg-warning/10 text-warning"
                : badge.tone === "success"
                  ? "border-success/30 bg-success/10 text-success"
                  : "border-border/70 bg-secondary/40 text-muted-foreground"
          )}
        >
          {badge.label}
        </span>
      ))}
    </div>
  );
}

export function ManualJournalEntryWorkbenchPanel({ view, search }: { view: ManualJournalEntryWorkbenchViewModel; search: string }) {
  const [recurringEntitySelection, setRecurringEntitySelection] = useState<{ scopeKey: string; entityId: string } | null>(null);
  const scopeQuery = new URLSearchParams(search);
  const recurringFund = scopeQuery.get("fundProfileId") || view.draft.fundProfileId;
  const recurringBook = scopeQuery.get("ledgerBookId") || view.draft.ledgerBookId;
  const recurringScopeKey = JSON.stringify([recurringFund, recurringBook, search]);
  const recurringEntity = recurringEntitySelection?.scopeKey === recurringScopeKey
    ? recurringEntitySelection.entityId : scopeQuery.get("entityId") || view.draft.entityId || "";
  const recurringQueue = <div className="space-y-2">
    <label className="block text-xs font-semibold">Recurring journal entity scope
      <Input value={recurringEntity} placeholder="Select an entity ID" className="mt-1"
        onChange={(event) => setRecurringEntitySelection({ scopeKey: recurringScopeKey, entityId: event.target.value })} />
    </label>
    <RecurringJournalQueue
      scope={recurringFund && recurringBook && recurringEntity.trim()
        ? { fundProfileId: recurringFund, ledgerBookId: recurringBook, entityId: recurringEntity.trim() }
        : null}
      availableDraftIds={view.drafts.map((draft) => draft.journalEntryId)}
      onSelectDraft={view.selectDraft}
    />
  </div>;
  // While an amount input holds unparseable text, its raw string is kept here (keyed
  // `${lineId}:${side}`) and mirrored back as the controlled value. Mirroring what the DOM
  // reports means React never rewrites the node, so the user's in-progress text survives while
  // the inline error shows - and the unparseable text is never coerced into the draft as zero.
  // A key's presence is also the invalid marker; a successful parse clears the line's keys.
  const [amountDrafts, setAmountDrafts] = useState<Readonly<Record<string, string>>>({});
  // Pending raw text belongs to one revision of one draft. Switching drafts, refreshing, or
  // applying a server result replaces the rows, so stale mirrors must not survive to mask the
  // committed amount of a freshly mounted line. Local line edits leave these fields untouched.
  const draftIdentity = `${view.draft?.journalEntryId ?? ""}:${view.draft?.version ?? 0}:${view.draft?.updatedAtUtc ?? ""}`;
  useEffect(() => {
    setAmountDrafts({});
  }, [draftIdentity]);
  const hasInvalidAmountEdits = Object.keys(amountDrafts).length > 0;
  const invalidAmountReason = "Correct the flagged amount entries before continuing.";
  const handleAmountChange = (lineId: string, side: "Debit" | "Credit", input: HTMLInputElement) => {
    const amount = parseJournalAmount(input.value, input.validity.badInput);
    setAmountDrafts((current) => {
      if (amount === null) {
        return { ...current, [`${lineId}:${side}`]: input.value };
      }
      const debitKey = `${lineId}:Debit`;
      const creditKey = `${lineId}:Credit`;
      if (!(debitKey in current) && !(creditKey in current)) {
        return current;
      }
      const next = { ...current };
      delete next[debitKey];
      delete next[creditKey];
      return next;
    });
    if (amount === null) {
      return;
    }
    view.updateLine(lineId, { side, amount });
  };
  const handleAmountBlur = (lineId: string, side: "Debit" | "Credit", input: HTMLInputElement) => {
    // React swallows the badInput -> genuinely-empty transition (the node reports "" in both
    // states, so no change event fires when the user clears bad text); re-evaluate on blur so a
    // cleared field commits as zero and drops its error instead of staying flagged forever. The
    // guard keeps ordinary blurs from re-running updateLine and voiding a fresh validation pass.
    if (`${lineId}:${side}` in amountDrafts) {
      handleAmountChange(lineId, side, input);
    }
  };
  if (!view.available) {
    return (
      <section className="workspace-section-band" aria-labelledby="manual-je-heading">
        <div className="workspace-section-subheader">
          <div className="min-w-0">
            <p className="eyebrow-label">Manual journal entries</p>
            <h3 id="manual-je-heading" className="workspace-section-title">{view.title}</h3>
            <p className="workspace-section-summary">{view.description}</p>
          </div>
        </div>
        <StatusBanner
          role={view.errorText ? "alert" : "status"}
          tone={view.errorText ? "danger" : "info"}
          title={view.errorText ? "Journal workbench unavailable" : "Loading journal workbench"}
          detail={view.errorText ?? "Loading the chart of accounts, governed draft state, validation rules, and retained evidence before editing is enabled."}
        />
        <div>
          <Button size="sm" variant="outline" disabled={view.loading} busy={view.loading} onClick={() => void view.refresh()}>
            <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
            {view.loading ? "Loading" : "Retry"}
          </Button>
        </div>
        {recurringQueue}
      </section>
    );
  }

  const nextLineSide = view.draft.totalDebits <= view.draft.totalCredits ? "Debit" : "Credit";
  const evidenceAttachments = view.draft.evidenceAttachments ?? [];
  const attachmentUris = new Set(evidenceAttachments.map((attachment) => attachment.uri));
  const retainedEvidenceLinks = Array.from(new Set(view.draft.evidenceLinks ?? []))
    .filter((link) => link.trim().length > 0 && !attachmentUris.has(link));
  const evidenceCount = new Set([
    ...evidenceAttachments.map((attachment) => attachment.uri),
    ...retainedEvidenceLinks
  ]).size;
  return (
    <section className="workspace-section-band" aria-labelledby="manual-je-heading">
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Manual journal entries</p>
          <h3 id="manual-je-heading" className="workspace-section-title">{view.title}</h3>
          <p className="workspace-section-summary">{view.description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={view.draft.status === "Submitted" ? "success" : view.draft.status === "NeedsFix" ? "warning" : "outline"} dot>
            {view.statusLabel}
          </Badge>
          <Button size="sm" variant="outline" disabled={view.loading} onClick={() => void view.refresh()}>
            <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
            Refresh
          </Button>
        </div>
      </div>

      {view.errorText ? (
        <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
          {view.errorText}
        </div>
      ) : null}

      {view.draft.assetAccountingDraft ? <p role="status" className="rounded border border-warning/30 bg-warning/10 p-3 text-sm">{view.assetWorkflow?.capability ? "This asset accounting draft is immutable. Available approval and posting decisions use the authenticated governed asset workflow below. Corrections remain unavailable here." : "This is an immutable asset accounting review draft. Validate and submit it for review here. Approval, posting and corrections require retained approval evidence through the governed asset workflow and remain unavailable until supported workflow capabilities are loaded."}</p> : null}
      <ManualJournalHealthBar view={view} hasInvalidAmountEdits={hasInvalidAmountEdits} invalidAmountReason={invalidAmountReason} />

      <div className="grid gap-4 xl:grid-cols-[minmax(14rem,0.28fr)_minmax(0,1fr)]">
        <div className="space-y-4">
          <section className="accounting-draft-rail" data-appearance="light" aria-label="Manual journal draft queue">
            <div className="accounting-reference-heading">
              <div>
                <p className="accounting-reference-kicker">Draft queue</p>
                <p className="accounting-reference-subtitle">Saved drafts and approval records</p>
              </div>
            </div>
            <div className="space-y-2">
              {view.drafts.length > 0 ? view.drafts.map((draft) => (
                <button
                  key={draft.journalEntryId}
                  type="button"
                  className={cn(
                    "accounting-draft-button",
                    draft.journalEntryId === view.draft.journalEntryId && "is-active"
                  )}
                  onClick={() => view.selectDraft(draft.journalEntryId)}
                >
                  <span className="block font-semibold text-foreground">{draft.memo || "Untitled journal entry"}</span>
                  <span className="mt-1 block text-[11px] text-muted-foreground">{draft.entryType}</span>
                  <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{draft.status} / v{draft.version}</span>
                  <span className="mt-1 block font-mono text-[11px] text-muted-foreground">{draft.accountingDate} / {draft.currency}</span>
                </button>
              )) : (
                <p role="status" className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm text-muted-foreground">
                  No saved drafts yet. Save the current entry to add it to the queue.
                </p>
              )}
            </div>
          </section>

          {recurringQueue}
          <ManualJournalPrivateCapitalActivityPanel activity={view.privateCapitalActivity} />
        </div>

        <div className="space-y-4">
          <section className="accounting-reference-panel accounting-journal-panel" data-appearance="light" aria-label="Manual journal entry - balanced double-entry">
            <fieldset disabled={Boolean(view.draft.assetAccountingDraft)} className="min-w-0" aria-label="Journal source fields">
            <div className="accounting-reference-heading">
              <div className="min-w-0">
                <p className="accounting-reference-kicker">Manual journal entry - balanced double-entry</p>
                <p className="accounting-reference-subtitle">{view.draft.entryType} / {view.treasuryContextLabel}</p>
              </div>
              <div className={cn("accounting-reference-balance-badge", view.balanceStatusTone === "success" ? "is-balanced" : "is-out")}>
                <span aria-hidden="true" />
                {view.balanceStatusLabel}
              </div>
            </div>

            <div className="accounting-journal-header-grid">
              <label>
                <span>Date</span>
                <input
                  id="manual-je-header-accountingDate"
                  type="date"
                  value={view.draft.accountingDate}
                  onChange={(event) => view.updateHeader("accountingDate", event.target.value)}
                />
              </label>
              <label>
                <span>Prepared by</span>
                <input
                  id="manual-je-header-preparedBy"
                  readOnly
                  value={view.draft.preparedBy || "Current operator"}
                />
              </label>
              <label className="md:col-span-2 xl:col-span-1">
                <span>Memo</span>
                <input
                  id="manual-je-header-memo"
                  value={view.draft.memo}
                  onChange={(event) => view.updateHeader("memo", event.target.value)}
                />
              </label>
              <label>
                <span>Fund profile</span>
                <input id="manual-je-header-fundProfileId" className="font-mono" value={view.draft.fundProfileId} onChange={(event) => view.updateHeader("fundProfileId", event.target.value)} />
              </label>
              <label>
                <span>Period</span>
                <input id="manual-je-header-periodId" className="font-mono" value={view.draft.periodId ?? ""} onChange={(event) => view.updateHeader("periodId", event.target.value)} />
              </label>
              <label>
                <span>Currency</span>
                <input id="manual-je-header-currency" className="font-mono" value={view.draft.currency} onChange={(event) => view.updateHeader("currency", event.target.value.toUpperCase())} />
              </label>
            </div>

            <TechnicalDetails
              label="Journal system details"
              className="mt-3"
              description="Identifiers and ledger scope used for audit and support follow-up."
            >
              <dl className="grid gap-3 text-xs sm:grid-cols-2 xl:grid-cols-3">
                <div><dt className="text-muted-foreground">Journal reference</dt><dd className="mt-1 break-all font-mono text-foreground">{view.draft.journalEntryId}</dd></div>
                <div><dt className="text-muted-foreground">Ledger book</dt><dd className="mt-1 break-all font-mono text-foreground">{view.draft.ledgerBookId ?? "Default ledger book"}</dd></div>
                <div><dt className="text-muted-foreground">Record version</dt><dd className="mt-1 font-mono text-foreground">{view.draft.version}</dd></div>
              </dl>
            </TechnicalDetails>

            <p className="accounting-journal-keyboard-hint" id="manual-je-keyboard-hint">
              Keyboard: Arrow Up or Down and Enter move within a column; Ctrl+Enter inserts a line beneath the current row.
            </p>

            </fieldset>
            <div className="accounting-journal-table-wrap" tabIndex={view.draft.assetAccountingDraft ? 0 : undefined} role="region" aria-label="Journal source lines">
              <table className="accounting-journal-table">
                <thead>
                  <tr>
                    <th>Account</th>
                    <th>Debit</th>
                    <th>Credit</th>
                    <th>Support</th>
                    <th><span className="sr-only">Line actions</span></th>
                  </tr>
                </thead>
                <tbody>
                  {view.draft.lines.map((line) => {
                    const badges = view.getLineBadges(line.lineId);
                    const isDebit = line.side === "Debit";
                    return (
                      <tr
                        key={line.lineId}
                        id={`manual-je-line-${line.lineId}`}
                        className={cn(line.lineId === view.selectedLineId && "is-selected")}
                        tabIndex={-1}
                      >
                        <td>
                          <select disabled={Boolean(view.draft.assetAccountingDraft)}
                            id={`manual-je-account-${line.lineId}`}
                            value={line.accountPath}
                            onChange={(event) => view.updateLine(line.lineId, { accountPath: event.target.value })}
                            onFocus={() => view.selectLine(line.lineId)}
                            onKeyDown={(event) => {
                              if (event.ctrlKey && event.key === "Enter") {
                                handleManualJournalCellKeyDown(event, view, line.lineId, "account", line.side);
                              }
                            }}
                            aria-label={`GL account for line ${line.lineId}`}
                            aria-describedby="manual-je-keyboard-hint"
                          >
                            <option value="">Select GL account</option>
                            {view.accountOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                          </select>
                          {badges.length > 0 ? <ManualJournalLineBadges badges={badges} /> : null}
                        </td>
                        <td>
                          <input disabled={Boolean(view.draft.assetAccountingDraft)}
                            id={`manual-je-debit-${line.lineId}`}
                            aria-label={`Debit amount for line ${line.lineId}`}
                            aria-describedby="manual-je-keyboard-hint"
                            className="text-right font-mono"
                            type="number"
                            value={amountDrafts[`${line.lineId}:Debit`] ?? (isDebit ? line.amount : 0)}
                            aria-invalid={`${line.lineId}:Debit` in amountDrafts || undefined}
                            onChange={(event) => handleAmountChange(line.lineId, "Debit", event.target)}
                            onBlur={(event) => handleAmountBlur(line.lineId, "Debit", event.target)}
                            onFocus={() => view.selectLine(line.lineId)}
                            onKeyDown={(event) => handleManualJournalCellKeyDown(event, view, line.lineId, "debit", "Debit")}
                          />
                          {`${line.lineId}:Debit` in amountDrafts ? (
                            <p id={`manual-je-debit-error-${line.lineId}`} className="mt-1 text-right text-[11px] text-danger" role="alert">Enter a valid amount.</p>
                          ) : null}
                        </td>
                        <td>
                          <input disabled={Boolean(view.draft.assetAccountingDraft)}
                            id={`manual-je-credit-${line.lineId}`}
                            aria-label={`Credit amount for line ${line.lineId}`}
                            aria-describedby={`${line.lineId}:Credit` in amountDrafts ? `manual-je-keyboard-hint manual-je-credit-error-${line.lineId}` : "manual-je-keyboard-hint"}
                            className="text-right font-mono"
                            type="number"
                            value={amountDrafts[`${line.lineId}:Credit`] ?? (isDebit ? 0 : line.amount)}
                            aria-invalid={`${line.lineId}:Credit` in amountDrafts || undefined}
                            onChange={(event) => handleAmountChange(line.lineId, "Credit", event.target)}
                            onBlur={(event) => handleAmountBlur(line.lineId, "Credit", event.target)}
                            onFocus={() => view.selectLine(line.lineId)}
                            onKeyDown={(event) => handleManualJournalCellKeyDown(event, view, line.lineId, "credit", "Credit")}
                          />
                          {`${line.lineId}:Credit` in amountDrafts ? (
                            <p id={`manual-je-credit-error-${line.lineId}`} className="mt-1 text-right text-[11px] text-danger" role="alert">Enter a valid amount.</p>
                          ) : null}
                        </td>
                        <td>
                          <button
                            type="button"
                            className="accounting-journal-support-button"
                            onClick={() => view.selectLine(line.lineId)}
                          >
                            <span className="block truncate font-semibold text-foreground">{line.securityDisplayName || (view.draft.assetAccountingDraft ? "Inspect line attribution" : "Choose security")}</span>
                            <span className="block truncate font-mono text-[11px] text-muted-foreground">{line.securityId || "No Security Master reference"}</span>
                          </button>
                          <input disabled={Boolean(view.draft.assetAccountingDraft)}
                            id={`manual-je-description-${line.lineId}`}
                            className="mt-2"
                            aria-label={`Description for line ${line.lineId}`}
                            aria-describedby="manual-je-keyboard-hint"
                            value={line.description ?? ""}
                            onChange={(event) => view.updateLine(line.lineId, { description: event.target.value })}
                            onFocus={() => view.selectLine(line.lineId)}
                            onKeyDown={(event) => handleManualJournalCellKeyDown(event, view, line.lineId, "description", line.side)}
                          />
                        </td>
                        <td>
                          <div className="flex items-center gap-1">
                            <Button
                              size="icon"
                              variant="ghost"
                              disabled={Boolean(view.draft.assetAccountingDraft)} aria-label={`Duplicate journal line ${line.lineId}`}
                              onClick={() => {
                                const duplicateId = view.duplicateLine(line.lineId);
                                if (duplicateId) {
                                  focusManualJournalCell(duplicateId, "account");
                                }
                              }}
                            >
                              <Copy className="h-3.5 w-3.5" aria-hidden="true" />
                            </Button>
                            <Button
                              size="icon"
                              variant="ghost"
                              aria-label={`Remove journal line ${line.lineId}`}
                              disabled={Boolean(view.draft.assetAccountingDraft) || view.draft.lines.length <= 2}
                              disabledReason={view.draft.lines.length <= 2 ? "A balanced journal entry needs at least two lines." : null}
                              onClick={() => view.removeLine(line.lineId)}
                            >
                              <X className="h-3.5 w-3.5" aria-hidden="true" />
                            </Button>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                  <tr className="accounting-journal-total-row">
                    <td>Totals</td>
                    <td>{view.totalDebitsLabel}</td>
                    <td>{view.totalCreditsLabel}</td>
                    <td colSpan={2}>{view.imbalanceLabel}</td>
                  </tr>
                </tbody>
              </table>
            </div>

            <div className="accounting-journal-footer">
              <Button size="sm" variant="outline" disabled={Boolean(view.draft.assetAccountingDraft)} onClick={() => view.addLine(nextLineSide)}>
                + Add line
              </Button>
              <div className={cn("accounting-reference-balance-badge", view.balanceStatusTone === "success" ? "is-balanced" : "is-out")}>
                <span aria-hidden="true" />
                {view.balanceStatusLabel}
              </div>
            </div>

          </section>

          <section className="rounded-md border border-border/70 bg-secondary/20 p-3" aria-labelledby="manual-je-balance-impact-heading">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div>
                <h4 id="manual-je-balance-impact-heading" className="text-sm font-semibold text-foreground">Balance impact preview</h4>
                <p className="text-xs text-muted-foreground">
                  See how the draft will move each ledger account before saving, validating, or submitting approval.
                </p>
              </div>
              <Badge variant={view.balanceImpactRows.length > 0 ? "success" : "warning"} dot>
                {view.balanceImpactRows.length} account{view.balanceImpactRows.length === 1 ? "" : "s"}
              </Badge>
            </div>
            <div className="mt-3 overflow-x-auto" tabIndex={view.draft.assetAccountingDraft ? 0 : undefined} role="region" aria-label="Journal balance impact">
              {view.balanceImpactRows.length > 0 ? (
                <table className="accounting-journal-table">
                  <thead>
                    <tr>
                      <th>Ledger account</th>
                      <th>Debit entry</th>
                      <th>Credit entry</th>
                      <th>Balance effect</th>
                      <th>Agency cue</th>
                    </tr>
                  </thead>
                  <tbody>
                    {view.balanceImpactRows.map((row) => (
                      <tr key={row.id}>
                        <td>
                          <div className="font-semibold text-foreground">{row.accountName}</div>
                          <div className="font-mono text-[11px] text-muted-foreground">{row.accountPath} / {row.accountType}</div>
                        </td>
                        <td className="font-mono tabular-nums text-right">{row.debitLabel}</td>
                        <td className="font-mono tabular-nums text-right">{row.creditLabel}</td>
                        <td>
                          <Badge variant={row.tone === "warning" ? "warning" : row.tone === "success" ? "success" : "outline"}>
                            {row.netEffectLabel}
                          </Badge>
                        </td>
                        <td className="text-xs text-muted-foreground">
                          <span className="block">{row.balanceDirectionLabel}</span>
                          <span className="mt-1 block font-mono">{row.lineCountLabel}</span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              ) : (
                <p role="status" className="rounded border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                  Select at least one GL account and amount to preview account-level balance impact.
                </p>
              )}
            </div>
          </section>

          <div className="rounded-md border border-border/70 bg-secondary/20 p-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <h4 className="text-sm font-semibold text-foreground">Source evidence</h4>
                  <p className="text-xs text-muted-foreground">Attach source support before approval submission.</p>
                </div>
                <Badge variant={evidenceCount > 0 ? "success" : "warning"} dot>
                  {evidenceCount} linked
                </Badge>
              </div>
              <div className="mt-3 grid gap-3 lg:grid-cols-[minmax(0,0.22fr)_minmax(0,0.34fr)_minmax(0,0.18fr)_minmax(0,0.16fr)_auto]">
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Label</span>
                  <input className="min-h-9 w-full rounded border border-border bg-background px-2" value={view.attachmentDraft.displayName} onChange={(event) => view.updateAttachmentDraft({ displayName: event.target.value })} />
                </label>
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Route or path</span>
                  <input className="min-h-9 w-full rounded border border-border bg-background px-2 font-mono" value={view.attachmentDraft.uri} onChange={(event) => view.updateAttachmentDraft({ uri: event.target.value })} />
                </label>
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Kind</span>
                  <select className="min-h-9 w-full rounded border border-border bg-background px-2" value={view.attachmentDraft.evidenceKind} onChange={(event) => view.updateAttachmentDraft({ evidenceKind: event.target.value })}>
                    <option value="SourceDocument">Source document</option>
                    <option value="ApprovalSupport">Approval support</option>
                    <option value="ReconciliationEvidence">Reconciliation</option>
                    <option value="ValuationSupport">Valuation support</option>
                  </select>
                </label>
                <label className="space-y-1 text-sm">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">Scope</span>
                  <select className="min-h-9 w-full rounded border border-border bg-background px-2" value={view.attachmentDraft.lineId ?? ""} onChange={(event) => view.updateAttachmentDraft({ lineId: event.target.value || null })}>
                    <option value="">Header</option>
                    {view.draft.lines.map((line) => <option key={line.lineId} value={line.lineId}>{line.side} {line.accountPath || line.lineId}</option>)}
                  </select>
                </label>
                <div className="flex items-end">
                  <Button
                    size="sm"
                    variant="outline"
                    busy={view.attachEvidenceBusy}
                    disabled={!!view.assetWorkflow?.pendingAction || view.assetWorkflow?.hasPendingPost}
                    busyLabel="Attaching evidence"
                    onClick={() => void view.addAttachment()}
                  >
                    <Paperclip className="h-3.5 w-3.5" aria-hidden="true" />
                    Attach
                  </Button>
                </div>
              </div>
              {view.attachEvidenceStatusText ? (
                <p role="status" className="mt-3 rounded border border-primary/30 bg-primary/10 px-3 py-2 text-sm text-primary">
                  {view.attachEvidenceStatusText}
                </p>
              ) : null}
              <div className="mt-3 grid gap-2 md:grid-cols-2">
                {evidenceAttachments.map((attachment) => (
                  <div key={attachment.attachmentId} className="flex items-start justify-between gap-3 rounded border border-border/70 bg-background/50 px-3 py-2 text-sm">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{attachment.displayName}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{attachment.lineId ? "Supports one journal line" : "Supports the full journal entry"}</div>
                      <TechnicalDetails label="Evidence details" className="mt-2" contentClassName="space-y-2 text-xs">
                        <div><span className="text-muted-foreground">Route or path</span><div className="mt-1 break-all font-mono text-foreground">{attachment.uri}</div></div>
                        <div><span className="text-muted-foreground">Evidence kind</span><div className="mt-1 break-all font-mono text-foreground">{attachment.evidenceKind}</div></div>
                        <div><span className="text-muted-foreground">Source system</span><div className="mt-1 break-all font-mono text-foreground">{attachment.sourceSystem}</div></div>
                      </TechnicalDetails>
                    </div>
                    <Button size="icon" variant="ghost" aria-label={`Remove evidence ${attachment.displayName}`} disabled={Boolean(view.draft.assetAccountingDraft)} disabledReason={view.draft.assetAccountingDraft ? "Evidence retained on an asset source draft cannot be removed here." : null} onClick={() => view.removeAttachment(attachment.attachmentId)}>
                      <X className="h-3.5 w-3.5" aria-hidden="true" />
                    </Button>
                  </div>
                ))}
                {retainedEvidenceLinks.map((link, index) => (
                  <div key={link} className="rounded border border-border/70 bg-background/50 px-3 py-2 text-sm">
                    <div className="font-semibold text-foreground">Retained evidence link {index + 1}</div>
                    <div className="mt-1 text-xs text-muted-foreground">Available to validation and approval controls.</div>
                    <TechnicalDetails label="Evidence route" className="mt-2">
                      <div className="break-all font-mono text-xs text-foreground">{link}</div>
                    </TechnicalDetails>
                  </div>
                ))}
                {evidenceCount === 0 ? (
                  <p className="rounded border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                    No source evidence is attached yet. Approval submission remains blocked until at least one evidence route or source document is linked.
                  </p>
                ) : null}
              </div>
          </div>

          <div className="rounded-md border border-border/70 bg-secondary/20 p-3">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div>
                <h4 className="text-sm font-semibold text-foreground">Lifecycle controls</h4>
                <p className="text-xs text-muted-foreground">Approve, post, reverse, rebook, and close-lock actions follow the journal-entry lifecycle controls.</p>
              </div>
              <Badge variant={view.draft.status === "Posted" || view.draft.status === "CloseLocked" ? "success" : view.draft.status === "Rejected" || view.draft.status === "NeedsFix" ? "danger" : view.draft.status === "Submitted" || view.draft.status === "Approved" ? "warning" : "outline"} dot>
                {view.statusLabel}
              </Badge>
            </div>
            {view.draft.assetAccountingDraft && view.assetWorkflow ? <AssetAccountingWorkflowPanel view={view.assetWorkflow} /> : null}
            <JournalLifecycleControls view={view} />
            <div className="mt-3 rounded-md border border-border/70 bg-background/50 px-3 py-3" aria-label="Manual journal lifecycle checklist">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Lifecycle checklist</div>
              <div role="list" className="mt-3 grid gap-2 md:grid-cols-2 xl:grid-cols-5">
                {view.lifecycleChecklist.map((item) => (
                  <div key={item.id} role="listitem" className={cn("rounded border bg-secondary/20 px-2.5 py-2 text-xs", accountingToolingBorderClass(item.tone))}>
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <span className="font-semibold text-foreground">{item.label}</span>
                      <Badge variant={accountingToolingBadgeVariant(item.tone)}>{item.value}</Badge>
                    </div>
                    <p className="mt-2 leading-5 text-muted-foreground">{item.detail}</p>
                  </div>
                ))}
              </div>
            </div>
            {view.lifecycleStatusText ? (
              <p role="status" className="mt-3 rounded border border-primary/30 bg-primary/10 px-3 py-2 text-sm text-primary">
                {view.lifecycleStatusText}
              </p>
            ) : null}
            {view.lifecycleCorrectionRows.length > 0 ? (
              <div className="mt-3">
                <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Generated correction drafts</div>
                <div className="mt-2 grid gap-2 md:grid-cols-2">
                  {view.lifecycleCorrectionRows.map((row) => (
                    <div key={row.id} className="rounded border border-border/70 bg-background/50 px-3 py-2 text-sm">
                      <div className="font-semibold text-foreground">{row.title}</div>
                      <div className="mt-1 font-mono text-xs text-muted-foreground">{row.subtitle}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{row.balanceLabel}</div>
                      <div className="mt-1 break-all text-xs text-muted-foreground">{row.sourceLabel}</div>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}
            <div className="mt-3">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Transition audit</div>
              <div className="mt-2 space-y-2">
                {view.lifecycleTransitions.length > 0 ? view.lifecycleTransitions.map((transition) => (
                  <div key={transition.id} className="rounded border border-border/70 bg-background/50 px-3 py-2 text-sm">
                    <div className="font-semibold text-foreground">{transition.title}</div>
                    <div className="mt-1 text-xs text-muted-foreground">{transition.detail}</div>
                    <div className="mt-2 flex flex-wrap gap-2">
                      <Badge variant="outline" className="font-mono text-[11px]">{transition.auditLabel}</Badge>
                      <Badge variant="outline" className="font-mono text-[11px]">{transition.correlationLabel}</Badge>
                      <Badge variant={transition.evidenceTone}>{transition.evidenceLabel}</Badge>
                    </div>
                    <div className="mt-2 space-y-1">
                      {transition.evidenceRows.map((evidence) => (
                        <div key={`${transition.id}-${evidence}`} className="break-all rounded border border-border/60 bg-secondary/30 px-2 py-1 font-mono text-[11px] text-muted-foreground">
                          {evidence}
                        </div>
                      ))}
                    </div>
                  </div>
                )) : (
                  <p className="rounded border border-border/70 bg-background/50 px-3 py-2 text-sm text-muted-foreground">
                    No lifecycle transition audit events are retained on this journal entry yet.
                  </p>
                )}
              </div>
            </div>
          </div>

          <div className="grid gap-4 lg:grid-cols-[minmax(0,0.55fr)_minmax(0,0.45fr)]">
            <ManualJournalValidationNavigator view={view} />
            <fieldset disabled={Boolean(view.draft.assetAccountingDraft)} className="min-w-0" aria-label="Journal attribution"><ManualJournalDimensionsInspector view={view} /></fieldset>
          </div>
        </div>
      </div>
    </section>
  );
}

/**
 * Parses a journal amount from a number input. Returns null (never a silent zero) when the field
 * holds unparseable text - the DOM reports that via validity.badInput with an empty value - so a
 * pasted "1,234.00" can never post a line as zero. A genuinely cleared field still parses as 0.
 */
function parseJournalAmount(value: string, badInput: boolean): number | null {
  if (badInput) {
    return null;
  }
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return 0;
  }
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}
