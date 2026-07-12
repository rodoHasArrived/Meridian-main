import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { SeverityBadge } from "@/components/operations";
import { formatCurrency as formatCurrencyAmount } from "@/lib/format";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type {
  AccountingWorkspaceResponse,
  PrivateCapitalActivityProjection,
  PrivateCapitalCapitalAccountSubledger,
  PrivateCapitalEvidenceCategory,
  PrivateCapitalFundEventLedgerRecord,
  PrivateCapitalLedgerImpact,
  PrivateCapitalReportOutput
} from "@/types";

export function ReportingPrivateCapitalReadinessPanel({ data }: { data: AccountingWorkspaceResponse | null }) {
  const activity = data?.manualJournalWorkbench?.privateCapitalActivity ?? null;
  const fundEventRecords = activity?.fundEventRecords ?? [];
  const subledgers = activity?.capitalAccountSubledgers ?? [];
  const ledgerImpacts = activity?.ledgerImpacts ?? [];
  const reportOutputs = activity?.reportOutputs ?? [];

  return (
    <section role="region" aria-label="Private-capital report readiness">
      <Card className="panel-surface">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="eyebrow-label">Private capital</div>
              <CardTitle>Fund event ledger and capital account subledger</CardTitle>
              <CardDescription>
                Read-only report readiness from Accounting private-capital activity data.
              </CardDescription>
            </div>
            <SeverityBadge
              status={activity ? (fundEventRecords.length > 0 ? "Ready" : "Info") : "ReviewRequired"}
              label={activity ? "Source data" : "Not loaded"}
            />
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {activity ? (
            <>
              <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-5">
                <ReportingPrivateCapitalMetric
                  label="Fund events"
                  value={activity.fundEventCount.toLocaleString()}
                  detail={`${activity.postedFundEventCount.toLocaleString()} posted / ${activity.submittedFundEventCount.toLocaleString()} submitted`}
                />
                <ReportingPrivateCapitalMetric
                  label="Capital accounts"
                  value={activity.capitalAccountCount.toLocaleString()}
                  detail={`${activity.approvalQueueCount.toLocaleString()} approval queue`}
                />
                <ReportingPrivateCapitalMetric
                  label="Ledger impacts"
                  value={ledgerImpacts.length.toLocaleString()}
                  detail={`${ledgerImpacts.filter((impact) => impact.isPostingReady).length.toLocaleString()} posting-ready`}
                />
                <ReportingPrivateCapitalMetric
                  label="Report outputs"
                  value={reportOutputs.length.toLocaleString()}
                  detail={`${reportOutputs.filter((output) => output.isReportReady).length.toLocaleString()} report-ready / ${activity.publishedReportOutputCount.toLocaleString()} published`}
                />
                <ReportingPrivateCapitalMetric
                  label="Net activity"
                  value={formatReportingMoney(activity.netCapitalActivity, activity.currency)}
                  detail={`Projected ${activity.projectedAtUtc}`}
                />
              </div>

              {activity.validationIssues.length > 0 ? (
                <div role="status" aria-label="Private-capital data warnings" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                  <p>{activity.validationIssues.length.toLocaleString()} data issue{activity.validationIssues.length === 1 ? "" : "s"} retained with the shared activity data.</p>
                  <ul className="mt-2 space-y-1 text-xs">
                    {activity.validationIssues.slice(0, 3).map((issue, index) => (
                      <li key={`${issue.code}-${index}`}>{issue.code}: {issue.message}</li>
                    ))}
                  </ul>
                </div>
              ) : null}

              {fundEventRecords.length > 0 ? (
                <div className="overflow-x-auto rounded-md border border-border/70">
                  <table className="w-full min-w-[1120px] text-sm" aria-label="Private-capital report-ready fund event records">
                    <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                      <tr>
                        <th className="px-3 py-2 text-left">Fund event</th>
                        <th className="px-3 py-2 text-left">Approval and readiness</th>
                        <th className="px-3 py-2 text-left">Report state</th>
                        <th className="px-3 py-2 text-left">Evidence categories</th>
                        <th className="px-3 py-2 text-left">Ledger impact</th>
                        <th className="px-3 py-2 text-left">Capital account</th>
                        <th className="px-3 py-2 text-left">Routes</th>
                      </tr>
                    </thead>
                    <tbody>
                      {fundEventRecords.map((record) => (
                        <ReportingPrivateCapitalFundEventRecordRow key={record.fundEventRecordId} activity={activity} record={record} />
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : (
                <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                  No private-capital fund event ledger records were included in the shared workbench data.
                </p>
              )}

              <div className="grid gap-4 xl:grid-cols-[1fr_0.9fr]">
                <div className="overflow-x-auto rounded-md border border-border/70">
                  <table className="w-full min-w-[760px] text-sm" aria-label="Private-capital capital account subledger references">
                    <thead className="bg-secondary/40 text-xs uppercase text-muted-foreground">
                      <tr>
                        <th className="px-3 py-2 text-left">Subledger</th>
                        <th className="px-3 py-2 text-left">Roll-forward</th>
                        <th className="px-3 py-2 text-left">Event counts</th>
                        <th className="px-3 py-2 text-left">Evidence</th>
                      </tr>
                    </thead>
                    <tbody>
                      {subledgers.length > 0 ? subledgers.map((subledger) => (
                        <ReportingPrivateCapitalSubledgerRow key={subledger.subledgerId} subledger={subledger} />
                      )) : (
                        <tr>
                          <td colSpan={4} className="px-3 py-3 text-sm text-muted-foreground">
                            No account-level capital-account subledger references were included.
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>

                <div className="space-y-3">
                  <ReportingPrivateCapitalLedgerImpactList impacts={ledgerImpacts} />
                  <ReportingPrivateCapitalReportOutputList outputs={reportOutputs} />
                </div>
              </div>
            </>
          ) : (
            <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
              The loaded Reporting workspace data does not include private-capital activity, so private-capital report readiness is not shown from backup data.
            </p>
          )}
        </CardContent>
      </Card>
    </section>
  );
}

function ReportingPrivateCapitalFundEventRecordRow({
  activity,
  record
}: {
  activity: PrivateCapitalActivityProjection;
  record: PrivateCapitalFundEventLedgerRecord;
}) {
  const commandCenterRoute = buildReportingFundEventCommandCenterRoute(activity, record);

  return (
    <tr className="border-t border-border/60 bg-background/30 align-top">
      <td className="px-3 py-2">
        <div className="font-semibold text-foreground">{record.fundEventType || record.fundEventId}</div>
        <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{record.fundEventRecordId}</div>
        <div className="mt-1 text-xs text-muted-foreground">{record.memo || record.journalEntryId}</div>
        <div className="mt-1 font-mono text-[11px] text-muted-foreground">{record.effectiveDate}</div>
      </td>
      <td className="px-3 py-2">
        <div className="flex flex-wrap gap-1.5">
          <SeverityBadge status={record.approvalState} label={record.approvalState} />
          <SeverityBadge status={record.readiness ?? "Info"} label={record.readinessLabel || record.readiness} />
          {record.isPosted ? (
            <SeverityBadge status="Ready" label="Posted" />
          ) : (
            <SeverityBadge status="Info" label="Unposted" />
          )}
        </div>
        <p className="mt-2 text-xs leading-5 text-muted-foreground">{record.readinessReason || "No readiness reason retained."}</p>
        <p className="mt-1 text-xs text-muted-foreground">{record.nextAction || "No next action retained."}</p>
      </td>
      <td className="px-3 py-2">
        <div className="flex flex-wrap gap-1.5">
          <SeverityBadge
            status={record.isReportReady ? "Ready" : "ReviewRequired"}
            label={record.isReportReady ? "Report-ready" : "Not report-ready"}
          />
          <SeverityBadge
            status={record.isPublished ? "Ready" : "Info"}
            label={record.isPublished ? "Published" : "Not published"}
          />
        </div>
        <p className="mt-2 text-xs text-muted-foreground">
          {record.reportOutputCount.toLocaleString()} output{record.reportOutputCount === 1 ? "" : "s"} / {record.reportLineProvenanceCount.toLocaleString()} provenance line{record.reportLineProvenanceCount === 1 ? "" : "s"}
        </p>
        <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">
          {record.primaryReportOutputType ?? record.reportWorkflowState ?? record.publicationManifestId ?? "No primary report output"}
        </p>
      </td>
      <td className="px-3 py-2">
        <div className="text-xs text-muted-foreground">{record.evidenceLinkCount.toLocaleString()} retained evidence link{record.evidenceLinkCount === 1 ? "" : "s"}</div>
        <ReportingPrivateCapitalEvidenceCategories categories={record.evidenceCategories ?? []} />
      </td>
      <td className="px-3 py-2 text-xs">
        <div>{record.ledgerImpactCount.toLocaleString()} ledger impact{record.ledgerImpactCount === 1 ? "" : "s"}</div>
        <div className="mt-1">{record.isPostingReady ? "Posting-ready" : "Posting review"}</div>
        <div className="mt-1 font-mono text-muted-foreground">{formatReportingMoney(record.grossAmount, record.currency)} gross</div>
      </td>
      <td className="px-3 py-2 text-xs">
        <div className="break-all font-mono text-foreground">{record.capitalAccountId}</div>
        <div className="mt-1 break-all text-muted-foreground">{record.investorId ?? "Investor not assigned"}</div>
        <div className="mt-1 font-mono text-muted-foreground">
          {formatReportingMoney(record.capitalAccountOpeningNetActivity, record.currency)} to {formatReportingMoney(record.capitalAccountEndingNetActivity, record.currency)}
        </div>
        <div className="mt-1">{record.capitalAccountSubledgerEntryCount.toLocaleString()} subledger movement{record.capitalAccountSubledgerEntryCount === 1 ? "" : "s"}</div>
      </td>
      <td className="px-3 py-2 text-[11px]">
        <ReportingPrivateCapitalRouteLink label="Command center" href={commandCenterRoute} />
        <ReportingPrivateCapitalRouteLink label="Activity" href={record.activityRoute} />
        <ReportingPrivateCapitalRouteLink label="Evidence" href={record.evidenceRoute} />
        <ReportingPrivateCapitalRouteLink label="Approval" href={record.approvalRoute ?? null} />
        <ReportingPrivateCapitalRouteLink label="Report" href={record.primaryReportRoute ?? record.retainedManifestPath ?? null} />
      </td>
    </tr>
  );
}

function buildReportingFundEventCommandCenterRoute(
  activity: PrivateCapitalActivityProjection,
  record: PrivateCapitalFundEventLedgerRecord
): string {
  const params = new URLSearchParams();
  if (activity.fundProfileId?.trim()) {
    params.set("fundProfileId", activity.fundProfileId.trim());
  }

  if (activity.ledgerBookId?.trim() && isGuid(activity.ledgerBookId.trim())) {
    params.set("ledgerBookId", activity.ledgerBookId.trim());
  }

  params.set("fundEventId", record.fundEventId);
  return `${WORKSTATION_API_ENDPOINTS.privateCapitalFundEventCommandCenter}?${params.toString()}`;
}

function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

function ReportingPrivateCapitalSubledgerRow({ subledger }: { subledger: PrivateCapitalCapitalAccountSubledger }) {
  return (
    <tr className="border-t border-border/60 bg-background/30 align-top">
      <td className="px-3 py-2">
        <div className="break-all font-mono text-xs text-foreground">{subledger.capitalAccountId}</div>
        <div className="mt-1 break-all text-[11px] text-muted-foreground">{subledger.investorId ?? "Investor not assigned"}</div>
        <ReportingPrivateCapitalRouteLink label="Subledger" href={subledger.activityRoute} />
      </td>
      <td className="px-3 py-2 font-mono text-xs">
        <div>{formatReportingMoney(subledger.openingNetActivity, subledger.currency)} opening</div>
        <div className="mt-1">{formatReportingMoney(subledger.netCapitalActivity, subledger.currency)} net</div>
        <div className="mt-1">{formatReportingMoney(subledger.endingNetActivity, subledger.currency)} ending</div>
      </td>
      <td className="px-3 py-2 text-xs">
        <div>{subledger.fundEventCount.toLocaleString()} fund event{subledger.fundEventCount === 1 ? "" : "s"}</div>
        <div className="mt-1">{subledger.postedFundEventCount.toLocaleString()} posted</div>
        <div className="mt-1">{subledger.approvalQueueCount.toLocaleString()} approval queue</div>
        <div className="mt-1">{subledger.publishedReportOutputCount.toLocaleString()} published report output{subledger.publishedReportOutputCount === 1 ? "" : "s"}</div>
      </td>
      <td className="px-3 py-2 text-xs">
        <SeverityBadge
          status={subledger.readiness ?? "EvidenceMissing"}
          label={subledger.readinessLabel || subledger.readiness || "Evidence missing"}
        />
        <div className="mt-1 text-[11px] text-muted-foreground">{subledger.readinessReason || "No subledger readiness reason"}</div>
        <ReportingPrivateCapitalRouteLink label={subledger.nextAction || "Next action"} href={subledger.nextActionRoute ?? subledger.activityRoute} />
        <div>{subledger.evidenceLinkCount.toLocaleString()} retained evidence link{subledger.evidenceLinkCount === 1 ? "" : "s"}</div>
        <div className="mt-1">{subledger.validationIssueCount.toLocaleString()} validation issue{subledger.validationIssueCount === 1 ? "" : "s"}</div>
        <ReportingPrivateCapitalEvidenceCategories categories={subledger.evidenceCategories ?? []} />
      </td>
    </tr>
  );
}

function ReportingPrivateCapitalLedgerImpactList({ impacts }: { impacts: PrivateCapitalLedgerImpact[] }) {
  return (
    <div className="rounded-md border border-border/70 bg-background/30 px-3 py-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <h4 className="text-sm font-semibold text-foreground">Ledger impacts</h4>
        <Badge variant="outline">{impacts.length.toLocaleString()}</Badge>
      </div>
      {impacts.length > 0 ? (
        <div role="list" aria-label="Private-capital ledger impacts" className="mt-3 space-y-2">
          {impacts.slice(0, 4).map((impact) => (
            <div key={impact.ledgerImpactId} role="listitem" className="rounded border border-border/60 bg-secondary/20 px-2.5 py-2 text-xs">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <span className="min-w-0">
                  <span className="block font-semibold text-foreground">{impact.fundEventType || impact.fundEventId}</span>
                  <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{impact.journalEntryId}</span>
                </span>
                <SeverityBadge
                  status={impact.isPostingReady ? "Ready" : impact.isBalanced ? "ReviewRequired" : "Blocked"}
                  label={impact.isPostingReady ? "Posting-ready" : impact.isBalanced ? "Balanced review" : "Unbalanced"}
                />
              </div>
              <div className="mt-2 grid grid-cols-2 gap-2 font-mono text-[11px] text-muted-foreground">
                <span>Debits {formatReportingMoney(impact.totalDebits, impact.currency)}</span>
                <span>Credits {formatReportingMoney(impact.totalCredits, impact.currency)}</span>
                <span>Imbalance {formatReportingMoney(impact.imbalance, impact.currency)}</span>
                <span>{impact.lineCount.toLocaleString()} GL lines</span>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <p role="status" className="mt-3 text-sm text-muted-foreground">No retained ledger-impact rows were included.</p>
      )}
    </div>
  );
}

function ReportingPrivateCapitalReportOutputList({ outputs }: { outputs: PrivateCapitalReportOutput[] }) {
  return (
    <div className="rounded-md border border-border/70 bg-background/30 px-3 py-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <h4 className="text-sm font-semibold text-foreground">Report outputs</h4>
        <Badge variant="outline">{outputs.length.toLocaleString()}</Badge>
      </div>
      {outputs.length > 0 ? (
        <div role="list" aria-label="Private-capital report outputs" className="mt-3 space-y-2">
          {outputs.slice(0, 4).map((output) => (
            <div key={output.reportOutputId} role="listitem" className="rounded border border-border/60 bg-secondary/20 px-2.5 py-2 text-xs">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <span className="min-w-0">
                  <span className="block font-semibold text-foreground">{output.displayName || output.reportOutputType}</span>
                  <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">{output.reportOutputId}</span>
                </span>
                <span className="flex flex-wrap justify-end gap-1.5">
                  <SeverityBadge
                    status={output.isReportReady ? "Ready" : "ReviewRequired"}
                    label={output.readinessLabel || (output.isReportReady ? "Report-ready" : "Review")}
                  />
                  <SeverityBadge
                    status={output.isPublished ? "Ready" : "Info"}
                    label={output.isPublished ? "Published" : "Unpublished"}
                  />
                </span>
              </div>
              <p className="mt-2 text-[11px] text-muted-foreground">{output.readinessReason || "No report-output readiness reason"}</p>
              <p className="mt-2 font-mono text-[11px] text-muted-foreground">
                {formatReportingMoney(output.netCapitalActivity, output.currency)} / {output.approvalState} / {output.reportWorkflowState ?? "No workflow state"}
              </p>
              <p className="mt-1 text-[11px] text-muted-foreground">
                {output.evidenceLinkCount.toLocaleString()} evidence link{output.evidenceLinkCount === 1 ? "" : "s"} / {(output.reportLineProvenanceCount ?? 0).toLocaleString()} provenance line{output.reportLineProvenanceCount === 1 ? "" : "s"}
              </p>
              <ReportingPrivateCapitalRouteLink label={output.nextAction || "Output"} href={output.nextActionRoute ?? output.reportOutputRoute ?? output.reportRoute} />
            </div>
          ))}
        </div>
      ) : (
        <p role="status" className="mt-3 text-sm text-muted-foreground">No report-output rows were included.</p>
      )}
    </div>
  );
}

function ReportingPrivateCapitalEvidenceCategories({ categories }: { categories: PrivateCapitalEvidenceCategory[] }) {
  if (categories.length === 0) {
    return <div className="mt-2 text-[11px] text-muted-foreground">No evidence categories retained.</div>;
  }

  return (
    <div className="mt-2 space-y-1" aria-label="Private-capital retained evidence categories">
      {categories.map((category) => (
        <div key={category.categoryId} className="rounded-sm border border-border/60 bg-secondary/20 px-2 py-1">
          <div className="flex flex-wrap items-center gap-1.5">
            <SeverityBadge
              status={category.isReady ? "Ready" : "ReviewRequired"}
              label={category.label || category.categoryId}
            />
            <span className="font-mono text-[11px] text-muted-foreground">{category.evidenceLinkCount.toLocaleString()} evidence</span>
          </div>
          <p className="mt-1 text-[11px] leading-5 text-muted-foreground">{category.summary || "No evidence summary retained."}</p>
          {(category.requiredEvidence ?? []).length > 0 ? (
            <p className="mt-1 text-[11px] leading-5 text-muted-foreground">
              Required: {(category.requiredEvidence ?? []).join(", ")}
            </p>
          ) : null}
        </div>
      ))}
    </div>
  );
}

function ReportingPrivateCapitalMetric({ label, value, detail }: { label: string; value: string; detail: string }) {
  return (
    <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
      <div className="text-[11px] font-semibold uppercase text-muted-foreground">{label}</div>
      <div className="mt-1 font-mono text-base text-foreground">{value}</div>
      <div className="mt-1 text-xs leading-5 text-muted-foreground">{detail}</div>
    </div>
  );
}

function ReportingPrivateCapitalRouteLink({ label, href }: { label: string; href: string | null | undefined }) {
  if (!href) {
    return <div className="mt-1 text-[11px] text-muted-foreground">{label}: none retained</div>;
  }

  return (
    <a className="mt-1 block break-all font-mono text-primary underline-offset-2 hover:underline" href={href}>
      {label}: {href}
    </a>
  );
}

function formatReportingMoney(value: number, currency: string): string {
  return formatCurrencyAmount(value, { currency, maximumFractionDigits: Math.abs(value) >= 1000 ? 0 : 2 });
}
