import { TableWorkbench, type TableWorkbenchColumn } from "@/components/meridian/table-workbench";
import { formatNumber } from "@/lib/format";
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
import { cn } from "@/lib/utils";
import { usesTrackedCaps } from "@/design-system/label-case";

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
                  value={formatNumber(activity.fundEventCount, { maximumFractionDigits: 0 })}
                  detail={`${formatNumber(activity.postedFundEventCount, { maximumFractionDigits: 0 })} posted / ${formatNumber(activity.submittedFundEventCount, { maximumFractionDigits: 0 })} submitted`}
                />
                <ReportingPrivateCapitalMetric
                  label="Capital accounts"
                  value={formatNumber(activity.capitalAccountCount, { maximumFractionDigits: 0 })}
                  detail={`${formatNumber(activity.approvalQueueCount, { maximumFractionDigits: 0 })} approval queue`}
                />
                <ReportingPrivateCapitalMetric
                  label="Ledger impacts"
                  value={formatNumber(ledgerImpacts.length, { maximumFractionDigits: 0 })}
                  detail={`${formatNumber(ledgerImpacts.filter((impact) => impact.isPostingReady).length, { maximumFractionDigits: 0 })} posting-ready`}
                />
                <ReportingPrivateCapitalMetric
                  label="Report outputs"
                  value={formatNumber(reportOutputs.length, { maximumFractionDigits: 0 })}
                  detail={`${formatNumber(reportOutputs.filter((output) => output.isReportReady).length, { maximumFractionDigits: 0 })} report-ready / ${formatNumber(activity.publishedReportOutputCount, { maximumFractionDigits: 0 })} published`}
                />
                <ReportingPrivateCapitalMetric
                  label="Net activity"
                  value={formatReportingMoney(activity.netCapitalActivity, activity.currency)}
                  detail={`Projected ${activity.projectedAtUtc}`}
                />
              </div>

              {activity.validationIssues.length > 0 ? (
                <div role="status" aria-label="Private-capital data warnings" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                  <p>{formatNumber(activity.validationIssues.length, { maximumFractionDigits: 0 })} data issue{activity.validationIssues.length === 1 ? "" : "s"} retained with the shared activity data.</p>
                  <ul className="mt-2 space-y-1 text-xs">
                    {activity.validationIssues.slice(0, 3).map((issue, index) => (
                      <li key={`${issue.code}-${index}`}>{issue.code}: {issue.message}</li>
                    ))}
                  </ul>
                </div>
              ) : null}

              {fundEventRecords.length > 0 ? (
                <div className="overflow-x-auto rounded-md border border-border/70">
                  <TableWorkbench workbenchId="reporting-private-capital-events" sourceKey={activity.projectedAtUtc}
                    rows={fundEventRecords} columns={fundEventColumns(activity)} getRowId={record => record.fundEventRecordId}
                    getSearchText={record => [record.fundEventType, record.fundEventId, record.capitalAccountId, record.memo].join(" ")}
                    completePopulation={activity.fundEventCount === fundEventRecords.length} sourceDescription="Private-capital activity projection, as of the displayed projection time; amounts retain their source currency."
                    ariaLabel="Private-capital report-ready fund event records" emptyText="No private-capital fund event records returned." />
                </div>
              ) : (
                <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                  No private-capital fund event ledger records were included in the shared workbench data.
                </p>
              )}

              <div className="grid gap-4 xl:grid-cols-[1fr_0.9fr]">
                <div className="overflow-x-auto rounded-md border border-border/70">
                  <TableWorkbench workbenchId="reporting-capital-subledgers" sourceKey={activity.projectedAtUtc}
                    rows={subledgers} columns={subledgerColumns()} getRowId={subledger => subledger.subledgerId}
                    getSearchText={subledger => JSON.stringify(subledger)} completePopulation={activity.capitalAccountCount === subledgers.length}
                    sourceDescription="Capital-account references from the authorized private-capital projection."
                    ariaLabel="Private-capital capital account subledger references" emptyText="No account-level capital-account subledger references were included." />
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

function fundEventColumns(activity: PrivateCapitalActivityProjection): TableWorkbenchColumn<PrivateCapitalFundEventLedgerRecord>[] { return [
  { id: "field-0", label: "Fund event", exportValue: (record) => record.fundEventType || record.fundEventId, render: (record) => { return <>
        <div className="font-semibold text-foreground">{record.fundEventType || record.fundEventId}</div>
        <div className="mt-1 break-all font-mono text-[11px] text-muted-foreground">{record.fundEventRecordId}</div>
        <div className="mt-1 text-xs text-muted-foreground">{record.memo || record.journalEntryId}</div>
        <div className="mt-1 font-mono text-[11px] text-muted-foreground">{record.effectiveDate}</div>
      </>; } },
  { id: "field-1", label: "Approval and readiness", exportValue: (record) => `${record.approvalState}; ${record.readiness ?? ""}`, render: (record) => { return <>
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
      </>; } },
  { id: "field-2", label: "Report state", exportValue: (record) => JSON.stringify({state:record.reportWorkflowState,ready:record.isReportReady,published:record.isPublished}), render: (record) => { return <>
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
          {formatNumber(record.reportOutputCount, { maximumFractionDigits: 0 })} output{record.reportOutputCount === 1 ? "" : "s"} / {formatNumber(record.reportLineProvenanceCount, { maximumFractionDigits: 0 })} provenance line{record.reportLineProvenanceCount === 1 ? "" : "s"}
        </p>
        <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">
          {record.primaryReportOutputType ?? record.reportWorkflowState ?? record.publicationManifestId ?? "No primary report output"}
        </p>
      </>; } },
  { id: "field-3", label: "Evidence categories", exportValue: (record) => JSON.stringify(record.evidenceCategories ?? []), render: (record) => { return <>
        <div className="text-xs text-muted-foreground">{formatNumber(record.evidenceLinkCount, { maximumFractionDigits: 0 })} retained evidence link{record.evidenceLinkCount === 1 ? "" : "s"}</div>
        <ReportingPrivateCapitalEvidenceCategories categories={record.evidenceCategories ?? []} />
      </>; } },
  { id: "field-4", label: "Ledger impact", exportValue: (record) => JSON.stringify({count:record.ledgerImpactCount,grossAmount:record.grossAmount,currency:record.currency}), render: (record) => { return <>
        <div>{formatNumber(record.ledgerImpactCount, { maximumFractionDigits: 0 })} ledger impact{record.ledgerImpactCount === 1 ? "" : "s"}</div>
        <div className="mt-1">{record.isPostingReady ? "Posting-ready" : "Posting review"}</div>
        <div className="mt-1 font-mono text-muted-foreground">{formatReportingMoney(record.grossAmount, record.currency)} gross</div>
      </>; } },
  { id: "field-5", label: "Capital account", exportValue: (record) => record.capitalAccountId, render: (record) => { return <>
        <div className="break-all font-mono text-foreground">{record.capitalAccountId}</div>
        <div className="mt-1 break-all text-muted-foreground">{record.investorId ?? "Investor not assigned"}</div>
        <div className="mt-1 font-mono text-muted-foreground">
          {formatReportingMoney(record.capitalAccountOpeningNetActivity, record.currency)} to {formatReportingMoney(record.capitalAccountEndingNetActivity, record.currency)}
        </div>
        <div className="mt-1">{formatNumber(record.capitalAccountSubledgerEntryCount, { maximumFractionDigits: 0 })} subledger movement{record.capitalAccountSubledgerEntryCount === 1 ? "" : "s"}</div>
      </>; } },
  { id: "field-6", label: "Routes", exportValue: (record) => record.activityRoute, render: (record) => {const commandCenterRoute = buildReportingFundEventCommandCenterRoute(activity, record); return <>
        <ReportingPrivateCapitalRouteLink label="Command center" href={commandCenterRoute} />
        <ReportingPrivateCapitalRouteLink label="Activity" href={record.activityRoute} />
        <ReportingPrivateCapitalRouteLink label="Evidence" href={record.evidenceRoute} />
        <ReportingPrivateCapitalRouteLink label="Approval" href={record.approvalRoute ?? null} />
        <ReportingPrivateCapitalRouteLink label="Report" href={record.primaryReportRoute ?? record.retainedManifestPath ?? null} />
      </>; } }
]; }

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

function subledgerColumns(): TableWorkbenchColumn<PrivateCapitalCapitalAccountSubledger>[] { return [
  { id: "field-0", label: "Subledger", exportValue: (subledger) => subledger.subledgerId, render: (subledger) => { return <>
        <div className="break-all font-mono text-xs text-foreground">{subledger.capitalAccountId}</div>
        <div className="mt-1 break-all text-[11px] text-muted-foreground">{subledger.investorId ?? "Investor not assigned"}</div>
        <ReportingPrivateCapitalRouteLink label="Subledger" href={subledger.activityRoute} />
      </>; } },
  { id: "field-1", label: "Roll-forward", exportValue: (subledger) => JSON.stringify(subledger), render: (subledger) => { return <>
        <div>{formatReportingMoney(subledger.openingNetActivity, subledger.currency)} opening</div>
        <div className="mt-1">{formatReportingMoney(subledger.netCapitalActivity, subledger.currency)} net</div>
        <div className="mt-1">{formatReportingMoney(subledger.endingNetActivity, subledger.currency)} ending</div>
      </>; } },
  { id: "field-2", label: "Event counts", exportValue: (subledger) => subledger.fundEventCount, render: (subledger) => { return <>
        <div>{formatNumber(subledger.fundEventCount, { maximumFractionDigits: 0 })} fund event{subledger.fundEventCount === 1 ? "" : "s"}</div>
        <div className="mt-1">{formatNumber(subledger.postedFundEventCount, { maximumFractionDigits: 0 })} posted</div>
        <div className="mt-1">{formatNumber(subledger.approvalQueueCount, { maximumFractionDigits: 0 })} approval queue</div>
        <div className="mt-1">{formatNumber(subledger.publishedReportOutputCount, { maximumFractionDigits: 0 })} published report output{subledger.publishedReportOutputCount === 1 ? "" : "s"}</div>
      </>; } },
  { id: "field-3", label: "Evidence", exportValue: (subledger) => subledger.evidenceLinkCount, render: (subledger) => { return <>
        <SeverityBadge
          status={subledger.readiness ?? "EvidenceMissing"}
          label={subledger.readinessLabel || subledger.readiness || "Evidence missing"}
        />
        <div className="mt-1 text-[11px] text-muted-foreground">{subledger.readinessReason || "No subledger readiness reason"}</div>
        <ReportingPrivateCapitalRouteLink label={subledger.nextAction || "Next action"} href={subledger.nextActionRoute ?? subledger.activityRoute} />
        <div>{formatNumber(subledger.evidenceLinkCount, { maximumFractionDigits: 0 })} retained evidence link{subledger.evidenceLinkCount === 1 ? "" : "s"}</div>
        <div className="mt-1">{formatNumber(subledger.validationIssueCount, { maximumFractionDigits: 0 })} validation issue{subledger.validationIssueCount === 1 ? "" : "s"}</div>
        <ReportingPrivateCapitalEvidenceCategories categories={subledger.evidenceCategories ?? []} />
      </>; } }
]; }

function ReportingPrivateCapitalLedgerImpactList({ impacts }: { impacts: PrivateCapitalLedgerImpact[] }) {
  return (
    <div className="rounded-md border border-border/70 bg-background/30 px-3 py-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <h4 className="text-sm font-semibold text-foreground">Ledger impacts</h4>
        <Badge variant="outline">{formatNumber(impacts.length, { maximumFractionDigits: 0 })}</Badge>
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
                <span>{formatNumber(impact.lineCount, { maximumFractionDigits: 0 })} GL lines</span>
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
        <Badge variant="outline">{formatNumber(outputs.length, { maximumFractionDigits: 0 })}</Badge>
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
                {formatNumber(output.evidenceLinkCount, { maximumFractionDigits: 0 })} evidence link{output.evidenceLinkCount === 1 ? "" : "s"} / {formatNumber((output.reportLineProvenanceCount ?? 0), { maximumFractionDigits: 0 })} provenance line{output.reportLineProvenanceCount === 1 ? "" : "s"}
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
            <span className="font-mono text-[11px] text-muted-foreground">{formatNumber(category.evidenceLinkCount, { maximumFractionDigits: 0 })} evidence</span>
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
      <div className={cn("text-[11px] font-semibold text-muted-foreground", usesTrackedCaps(label) && "uppercase")}>{label}</div>
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
