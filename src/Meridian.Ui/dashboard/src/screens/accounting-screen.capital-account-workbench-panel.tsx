import { RefreshCcw } from "lucide-react";
import { Link } from "react-router-dom";
import { MetricSnapshotCard } from "@/components/meridian/metric-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { cn } from "@/lib/utils";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { accountingToolingBadgeVariant, accountingToolingBorderClass } from "@/screens/accounting-screen.styles";
import { AccountingChip } from "@/screens/accounting-screen.workbench-context";
import type { CapitalAccountWorkbenchViewModel } from "@/screens/accounting-screen.view-model";

export function CapitalAccountWorkbenchPanel({ view }: { view: CapitalAccountWorkbenchViewModel }) {
  if (!view.available) {
    return (
      <Card className="panel-surface" role="region" aria-labelledby="capital-account-workbench-heading">
        <CardHeader>
          <CardTitle id="capital-account-workbench-heading" className="text-base">{view.title}</CardTitle>
          <CardDescription>{view.description}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <StatusBanner
            role={view.errorText ? "alert" : "status"}
            tone={view.errorText ? "danger" : "info"}
            title={view.errorText ? "Capital account data unavailable" : "Loading capital account data"}
            detail={view.errorText ?? "Loading investor accounts, allocation evidence, statement lineage, and audit links from the shared workbench."}
          />
          <Button size="sm" variant="outline" disabled={view.loading} busy={view.loading} onClick={() => void view.refresh()}>
            <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
            {view.loading ? "Loading" : "Retry"}
          </Button>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card className="panel-surface" role="region" aria-labelledby="capital-account-workbench-heading">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="eyebrow-label">Private capital</p>
            <CardTitle id="capital-account-workbench-heading" className="text-base">{view.title}</CardTitle>
            <CardDescription>{view.description}</CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={accountingToolingBadgeVariant(view.statusTone)} dot>{view.statusLabel}</Badge>
            <Button size="sm" variant="outline" disabled={view.loading} busy={view.loading} onClick={() => void view.refresh()}>
              <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
              Refresh
            </Button>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        {view.errorText ? (
          <div role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
            {view.errorText}
          </div>
        ) : null}

        <div className="flex flex-wrap gap-2">
          <AccountingChip label="Projected" value={view.projectedAtLabel} />
        </div>
        <TechnicalDetails label="Workbench route" className="max-w-3xl">
          <div className="break-all font-mono text-xs text-foreground">{view.workbenchRouteLabel}</div>
        </TechnicalDetails>
        <p className="text-sm leading-6 text-muted-foreground">{view.statusReason}</p>

        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          {view.summaryCards.map((metric) => (
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

        {view.investorAccounts.length === 0 && !view.loading ? (
          <div role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-3 text-sm text-foreground">
            <p>{view.emptyText}</p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Button asChild size="sm" variant="outline">
                <Link to={WORKSTATION_ROUTE_CATALOG.accountingJournalEntries}>Open journal entries</Link>
              </Button>
              <Button asChild size="sm" variant="outline">
                <Link to={WORKSTATION_ROUTE_CATALOG.accountingConfigure}>Review accounting scope</Link>
              </Button>
            </div>
          </div>
        ) : null}

        {view.fundEventCommandRows.length > 0 ? (
          <section className="space-y-2" aria-labelledby="capital-account-fund-event-command-heading">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h4 id="capital-account-fund-event-command-heading" className="text-sm font-semibold text-foreground">Fund-event command centers</h4>
              <Badge variant="outline">{view.fundEventCommandRows.length.toLocaleString()} events</Badge>
            </div>
            <div className="overflow-x-auto rounded-md border border-border/70">
              <table className="w-full min-w-[940px] text-sm" aria-label="Capital-account fund-event command centers">
                <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2 text-left">Fund event</th>
                    <th className="px-3 py-2 text-left">Readiness</th>
                    <th className="px-3 py-2 text-left">Evidence</th>
                    <th className="px-3 py-2 text-left">Routes</th>
                  </tr>
                </thead>
                <tbody>
                  {view.fundEventCommandRows.map((row) => (
                    <tr key={row.id} className="border-t border-border/60 align-top">
                      <td className="px-3 py-2">
                        <div className="font-semibold text-foreground">{row.title}</div>
                        <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{row.subtitle}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{row.memoLabel}</div>
                        <div className="mt-1 font-mono text-xs text-muted-foreground">{row.netActivityLabel}</div>
                      </td>
                      <td className="px-3 py-2">
                        <Badge variant={row.readinessTone} dot>{row.readinessLabel}</Badge>
                        <div className="mt-1 text-xs text-muted-foreground">{row.readinessReasonLabel}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{row.nextActionLabel}</div>
                      </td>
                      <td className="px-3 py-2 text-xs text-muted-foreground">
                        <div>{row.evidenceLabel}</div>
                        <div className="mt-1">{row.subledgerLabel}</div>
                        <div className="mt-1">{row.ledgerImpactLabel}</div>
                        <div className="mt-1">{row.reportOutputLabel}</div>
                      </td>
                      <td className="px-3 py-2">
                        <a
                          className="block break-all font-mono text-[11px] text-primary hover:underline"
                          href={row.commandCenterRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open capital-account fund-event command center for ${row.title}`}
                        >
                          {row.commandCenterRouteLabel}
                        </a>
                        <a
                          className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                          href={row.activityRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open capital-account fund-event activity for ${row.title}`}
                        >
                          {row.activityRouteLabel}
                        </a>
                        <a
                          className="mt-1 block break-all font-mono text-[11px] text-primary hover:underline"
                          href={row.evidenceRouteLabel}
                          target="_blank"
                          rel="noreferrer"
                          aria-label={`Open capital-account fund-event evidence for ${row.title}`}
                        >
                          {row.evidenceRouteLabel}
                        </a>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        ) : null}

        <div className="grid min-w-0 gap-4 2xl:grid-cols-[minmax(0,1.15fr)_minmax(0,0.85fr)]">
          <section className="min-w-0 space-y-2" aria-labelledby="capital-account-investor-heading">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h4 id="capital-account-investor-heading" className="text-sm font-semibold text-foreground">Investor capital accounts</h4>
              <Badge variant="outline">{view.investorAccounts.length.toLocaleString()} rows</Badge>
            </div>
            <div className="overflow-x-auto rounded-md border border-border/70">
              <table className="w-full min-w-[880px] text-sm">
                <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2 text-left">Account</th>
                    <th className="px-3 py-2 text-left">Readiness</th>
                    <th className="px-3 py-2 text-right">Net</th>
                    <th className="px-3 py-2 text-left">Evidence</th>
                    <th className="px-3 py-2 text-left">Cash support</th>
                    <th className="px-3 py-2 text-left">Route</th>
                  </tr>
                </thead>
                <tbody>
                  {view.investorAccounts.map((row) => (
                    <tr key={row.id} className="border-t border-border/60">
                      <td className="px-3 py-2">
                        <div className="font-semibold text-foreground">{row.title}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{row.subtitle}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{row.eventLabel}</div>
                      </td>
                      <td className="px-3 py-2">
                        <Badge variant={accountingToolingBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
                      </td>
                      <td className="px-3 py-2 text-right font-mono tabular-nums">
                        <div className="text-foreground">{row.netActivityLabel}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{row.rollForwardLabel}</div>
                        <div className="mt-1 text-xs text-muted-foreground">{row.activityMixLabel}</div>
                      </td>
                      <td className="px-3 py-2 text-xs text-muted-foreground">{row.evidenceLabel}</td>
                      <td className="px-3 py-2 text-xs">
                        <Badge variant={row.paymentEvidenceTone}>{row.paymentEvidenceLabel}</Badge>
                        <div className="mt-1 text-muted-foreground">{row.paymentEvidenceSummaryLabel}</div>
                        <div className="mt-1 text-muted-foreground">{row.paymentEvidenceRequiredLabel}</div>
                      </td>
                      <td className="px-3 py-2">
                        <a href={row.routeLabel} className="inline-flex whitespace-nowrap text-xs font-medium text-primary hover:underline">
                          Open capital activity
                        </a>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="min-w-0 space-y-2" aria-labelledby="capital-account-allocation-heading">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h4 id="capital-account-allocation-heading" className="text-sm font-semibold text-foreground">Allocation rules</h4>
              <Badge variant="outline">{view.allocationRules.length.toLocaleString()} checks</Badge>
            </div>
            <div className="grid gap-2">
              {view.allocationRules.map((row) => (
                <div key={row.id} className={cn("rounded-md border px-3 py-2 text-sm", accountingToolingBorderClass(row.statusTone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{row.label}</div>
                      <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{row.accountLabel}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{row.reason}</p>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{row.basis}</p>
                  <div className="mt-2 flex flex-wrap gap-2 text-xs">
                    <AccountingChip label="Evidence" value={row.evidenceLabel} />
                    <AccountingChip label="Required" value={row.requiredLabel} />
                    <AccountingChip label="Policy" value={row.policyLabel} />
                    <AccountingChip label="Effective" value={row.effectiveWindowLabel} />
                    <AccountingChip label="Approval" value={row.approvalLabel} />
                    <AccountingChip label="Inputs" value={row.inputSummaryLabel} />
                    <AccountingChip label="Fund events" value={row.relatedFundEventLabel} />
                  </div>
                  <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">{row.formulaLabel}</p>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{row.traceLabel}</p>
                  {row.routeLabel !== "No route" ? (
                    <a href={row.routeLabel} className="mt-2 block text-xs font-medium text-primary hover:underline">
                      Open allocation evidence
                    </a>
                  ) : null}
                </div>
              ))}
            </div>
          </section>
        </div>

        <div className="grid gap-4 xl:grid-cols-2">
          <section className="space-y-2" aria-labelledby="capital-account-statement-heading">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h4 id="capital-account-statement-heading" className="text-sm font-semibold text-foreground">Statement lineage</h4>
              <Badge variant="outline">{view.statementLineage.length.toLocaleString()} statements</Badge>
            </div>
            <div className="grid gap-2">
              {view.statementLineage.map((row) => (
                <div key={row.id} className={cn("rounded-md border px-3 py-2 text-sm", accountingToolingBorderClass(row.statusTone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{row.title}</div>
                      <div className="mt-1 text-xs text-muted-foreground">{row.subtitle}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
                  </div>
                  <div className="mt-2 grid gap-1 text-xs text-muted-foreground">
                    <span>{row.publicationLabel}</span>
                    <span>{row.provenanceLabel}</span>
                    <span>{row.restatementLabel}</span>
                  </div>
                  {row.changedLineRows.length > 0 ? (
                    <ul className="mt-2 grid gap-1 text-xs text-muted-foreground" aria-label={`${row.title} restatement changed lines`}>
                      {row.changedLineRows.map((line) => (
                        <li key={line.id} className="rounded-sm border border-border/60 px-2 py-1">
                          <span className="break-all font-mono text-[11px]">{line.lineKey}</span>
                          <span className="mx-2">{line.valueLabel}</span>
                          <span>{line.evidenceLabel}</span>
                        </li>
                      ))}
                    </ul>
                  ) : null}
                  <TechnicalDetails label="Statement technical details" className="mt-2">
                    <div className="break-all font-mono text-[11px] text-muted-foreground">{row.manifestLabel}</div>
                  </TechnicalDetails>
                  <a href={row.routeLabel} className="mt-2 block text-xs font-medium text-primary hover:underline">Open statement</a>
                </div>
              ))}
            </div>
          </section>

          <section className="space-y-2" aria-labelledby="capital-account-audit-heading">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h4 id="capital-account-audit-heading" className="text-sm font-semibold text-foreground">Audit drill-through</h4>
              <Badge variant="outline">{view.auditDrillThroughs.length.toLocaleString()} targets</Badge>
            </div>
            <div className="grid gap-2">
              {view.auditDrillThroughs.map((row) => (
                <div key={row.id} className={cn("rounded-md border px-3 py-2 text-sm", accountingToolingBorderClass(row.statusTone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{row.title}</div>
                      <div className="mt-1 text-xs uppercase text-muted-foreground">{row.kind}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(row.statusTone)}>{row.statusLabel}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{row.summary}</p>
                  <div className="mt-2 flex flex-wrap gap-2 text-xs">
                    <AccountingChip label="Evidence" value={row.evidenceLabel} />
                    <AccountingChip label="Related" value={row.relatedLabel} />
                  </div>
                  {row.routeLabel !== "No route" ? (
                    <a href={row.routeLabel} className="mt-2 block text-xs font-medium text-primary hover:underline">
                      Open audit evidence
                    </a>
                  ) : null}
                </div>
              ))}
            </div>
          </section>
        </div>

        <div className="grid gap-3 md:grid-cols-2">
          <div className="rounded-md border border-success/30 bg-success/10 px-3 py-3">
            <div className="text-xs font-semibold uppercase text-success">Live in v0.18 slice</div>
            <ul className="mt-2 grid gap-1 text-sm text-foreground">
              {view.liveCapabilities.map((item) => <li key={item}>{item}</li>)}
            </ul>
          </div>
          <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
            <div className="text-xs font-semibold uppercase text-muted-foreground">Still planned</div>
            <ul className="mt-2 grid gap-1 text-sm text-muted-foreground">
              {view.plannedCapabilities.map((item) => <li key={item}>{item}</li>)}
            </ul>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
