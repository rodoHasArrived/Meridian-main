import { Network } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { SeverityBadge } from "@/components/operations";
import { redactReportingCredentialText, safeReportingHref } from "@/lib/reporting-link-safety";
import { evidenceWorkbenchPath, workstationRouteWithQuery } from "@/lib/workspace";
import { reportingRunReportWriterGridEndpoint } from "@/lib/workstation-endpoints";
import {
  ReportingScheduleField
} from "@/screens/reporting-screen.shared-components";
import type {
  ReportPackDeliveryAttempt,
  ReportPackDeliveryPackage
} from "@/types";

interface ReportingDeliveryHistoryPanelProps {
  deliveryAttempts: ReportPackDeliveryAttempt[];
}

export function ReportingDeliveryHistoryPanel({
  deliveryAttempts
}: ReportingDeliveryHistoryPanelProps) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <div className="eyebrow-label">Delivery history</div>
        <CardTitle>Distribution attempts</CardTitle>
        <CardDescription>Immutable, tenant-filtered report delivery evidence retained by recipient and channel.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-2">
        <p role="status" className="rounded-md border border-primary/25 bg-primary/10 px-3 py-2 text-xs leading-5 text-primary">
          Dispatch and provider failure receipts are server-owned. Use the governed run detail for release and distribution controls; this panel remains read-only evidence.
        </p>
        {deliveryAttempts.length > 0 ? (
          <div role="list" aria-label="Report-pack delivery attempts" className="space-y-2">
            {deliveryAttempts.slice(0, 6).map((attempt) => (
              <div
                key={attempt.attemptId}
                role="listitem"
                aria-label={`${attempt.recipient} delivery attempt ${attempt.state}`}
                className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
              >
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <span className="min-w-0">
                    <span className="block font-semibold text-foreground">{attempt.recipient}</span>
                    <span className="mt-1 block text-xs text-muted-foreground">{attempt.recipientRole} · {attempt.channel}</span>
                  </span>
                  <SeverityBadge status={attempt.state} label={attempt.state} />
                </div>
                <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">{redactReportingCredentialText(attempt.deliveryReference)}</p>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Attempt {attempt.attemptNumber} by {attempt.actor} at {attempt.attemptedAtUtc}
                </p>
                {attempt.package ? (
                  <div className="mt-2 space-y-1 text-xs leading-5 text-muted-foreground">
                    <p>
                      {attempt.package.deliveryMode} package · {attempt.package.formats.join(", ")}
                    </p>
                    {attempt.package.deliveryChannelSummary ? (
                      <p>{redactReportingCredentialText(attempt.package.deliveryChannelSummary)}</p>
                    ) : null}
                    {attempt.package.deliveryAccessSummary ? (
                      <p>{redactReportingCredentialText(attempt.package.deliveryAccessSummary)}</p>
                    ) : null}
                    {attempt.package.downloadSummary ? (
                      <p>{redactReportingCredentialText(attempt.package.downloadSummary)}</p>
                    ) : null}
                    {attempt.package.accessExpiresAtUtc ? (
                      <p className="break-all font-mono text-[11px]">
                        Access expires: {attempt.package.accessExpiresAtUtc}
                      </p>
                    ) : null}
                    {attempt.package.brandingTheme ? (
                      <p className="break-all text-[11px]">
                        Branding: {attempt.package.brandingTheme.name} · {attempt.package.brandingTheme.firmName} · {attempt.package.brandingTheme.themeId}
                      </p>
                    ) : null}
                    {attempt.package.accessLinks?.length ? (
                      <div className="flex flex-wrap gap-1.5" aria-label={`${attempt.attemptId} package access links`}>
                        {attempt.package.accessLinks.map((link, index) => {
                          const safeHref = safeReportingHref(link.href, {
                            requireOpaqueFragment: link.requiresToken
                          });
                          return safeHref ? (
                            <a
                              key={`${attempt.attemptId}-${link.kind}-${index}`}
                              className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                              href={safeHref}
                              aria-label={`${redactReportingCredentialText(link.label)} ${link.requiresToken ? "fragment-token gated" : "internal route"}`}
                            >
                              <span className="max-w-[12rem] truncate text-foreground">
                                {redactReportingCredentialText(link.label)}
                              </span>
                              <Badge variant="outline">{link.requiresToken ? "Fragment gated" : "Internal"}</Badge>
                              {link.expiresAtUtc ? <span>Expires {link.expiresAtUtc}</span> : null}
                            </a>
                          ) : (
                            <span
                              key={`${attempt.attemptId}-${link.kind}-${index}`}
                              className="inline-flex items-center gap-1.5 rounded-sm border border-warning/40 px-2 py-1 text-[11px] text-warning"
                            >
                              {redactReportingCredentialText(link.label)} <Badge variant="warning">Unsafe link suppressed</Badge>
                            </span>
                          );
                        })}
                      </div>
                    ) : attempt.package.secureLink ? (
                      <p className="text-[11px] text-muted-foreground">
                        Legacy package access reference retained as non-actionable evidence. Use the governed run for scoped recipient access.
                      </p>
                    ) : (
                      <p className="text-[11px] text-warning">Legacy or query-credential package link suppressed.</p>
                    )}
                    {attempt.package.notifications?.length ? (
                      <ul aria-label={`${attempt.recipient} delivery notifications`} className="grid gap-1.5">
                        {attempt.package.notifications.map((notification) => {
                          const safeNotificationHref = safeReportingHref(notification.href, {
                            requireOpaqueFragment: notification.requiresToken
                          });
                          return (
                            <li
                              key={`${attempt.attemptId}-${notification.notificationId}`}
                              className="rounded-md border border-border/70 bg-background/40 px-2 py-2"
                            >
                            <div className="flex flex-wrap items-start justify-between gap-2">
                              <span className="min-w-0">
                                <span className="block font-semibold text-foreground">
                                  {redactReportingCredentialText(notification.subject)}
                                </span>
                                <span className="mt-1 block break-all font-mono text-[11px] text-muted-foreground">
                                  {notification.notificationId}
                                </span>
                              </span>
                              <Badge variant="outline">{notification.status}</Badge>
                            </div>
                            <p className="mt-1 text-xs leading-5 text-muted-foreground">{redactReportingCredentialText(notification.body)}</p>
                            <dl className="mt-2 grid gap-1 text-[11px] sm:grid-cols-2">
                              <ReportingScheduleField label="Channel" value={`${notification.channel} · ${notification.deliveryMode}`} />
                              <ReportingScheduleField label="Recipient" value={`${notification.recipient} · ${notification.recipientRole}`} />
                              <ReportingScheduleField label="Created" value={notification.createdAtUtc} />
                              <ReportingScheduleField label="Expires" value={notification.expiresAtUtc ?? "No expiry"} />
                            </dl>
                              {safeNotificationHref ? (
                                <a
                                  className="mt-2 block font-mono text-[11px] text-primary underline-offset-2 hover:underline"
                                  href={safeNotificationHref}
                                  aria-label={`${redactReportingCredentialText(notification.subject)} ${notification.requiresToken ? "fragment-token gated" : "internal route"}`}
                                >
                                  Open retained notification access
                                </a>
                              ) : (
                                <p className="mt-2 text-[11px] text-warning">Unsafe notification link suppressed.</p>
                              )}
                            </li>
                          );
                        })}
                      </ul>
                    ) : null}
                    <p className="break-all font-mono text-[11px]">
                      {redactReportingCredentialText(attempt.package.retainedManifestPath)}
                    </p>
                    {attempt.package.reportingRunId ? (
                      <p className="break-all font-mono text-[11px]">
                        Reporting run: {attempt.package.reportingRunId}{" "}
                        <Link
                          className="font-sans font-medium text-primary underline-offset-2 hover:underline"
                          to={workstationRouteWithQuery("reportingRunDetail", { runId: attempt.package.reportingRunId })}
                        >
                          Open governed run
                        </Link>
                      </p>
                    ) : null}
                    {attempt.package.reportingTemplateId ? (
                      <p className="break-all font-mono text-[11px]">
                        Template: {attempt.package.reportingTemplateId}
                      </p>
                    ) : null}
                    {attempt.package.reportingScheduleId ? (
                      <p className="break-all font-mono text-[11px]">
                        Schedule: {attempt.package.reportingScheduleId}
                      </p>
                    ) : null}
                    {hasReportingRunMetadata(attempt.package) ? (
                      <p className="break-all font-mono text-[11px]">
                        Run metadata: {formatReportingRunPackageMetadata(attempt.package)}
                      </p>
                    ) : null}
                    {hasReportWriterDatasetMetadata(attempt.package) ? (
                      <p className="break-all font-mono text-[11px]">
                        Dataset source: {formatReportWriterDatasetMetadata(attempt.package)}
                      </p>
                    ) : null}
                    {attempt.package.sourceArtifacts?.length ? (
                      <p className="break-all font-mono text-[11px]">
                        Source artifacts: {redactReportingCredentialText(attempt.package.sourceArtifacts.join(", "))}
                      </p>
                    ) : null}
                    {attempt.package.generatedReportWriterGrids?.length ? (
                      <div className="break-all font-mono text-[11px]">
                        <p>
                          Generated grids: {attempt.package.generatedReportWriterGrids.map((grid) => {
                            const validationSummary = grid.validationSummary?.trim();
                            return `${grid.title || grid.gridId} (${grid.kind}, ${grid.dimensionCount}d/${grid.metricCount}m/${grid.formulaCount}f${validationSummary ? `, validation ${validationSummary}` : ""})`;
                          }).join(", ")}
                        </p>
                        {attempt.package.reportingRunId ? (
                          <div className="mt-1 flex flex-wrap gap-1.5" aria-label={`${attempt.attemptId} package report-writer grid exports`}>
                            {attempt.package.generatedReportWriterGrids.map((grid) => {
                              const title = grid.title || grid.gridId;
                              return (
                                <span
                                  key={`${attempt.attemptId}-${grid.gridId}`}
                                  className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground"
                                >
                                  <span className="max-w-[16rem] truncate text-foreground">{title}</span>
                                  <a
                                    className="text-primary underline-offset-2 hover:underline"
                                    href={reportingRunReportWriterGridEndpoint(attempt.package!.reportingRunId!, grid.gridId)}
                                    target="_blank"
                                    rel="noreferrer"
                                  >
                                    JSON
                                  </a>
                                  <a
                                    className="text-primary underline-offset-2 hover:underline"
                                    href={reportingRunReportWriterGridEndpoint(attempt.package!.reportingRunId!, grid.gridId, "csv")}
                                    target="_blank"
                                    rel="noreferrer"
                                  >
                                    CSV
                                  </a>
                                  <a
                                    className="text-primary underline-offset-2 hover:underline"
                                    href={reportingRunReportWriterGridEndpoint(attempt.package!.reportingRunId!, grid.gridId, "pdf")}
                                    target="_blank"
                                    rel="noreferrer"
                                  >
                                    PDF
                                  </a>
                                  <a
                                    className="text-primary underline-offset-2 hover:underline"
                                    href={reportingRunReportWriterGridEndpoint(attempt.package!.reportingRunId!, grid.gridId, "xls")}
                                    target="_blank"
                                    rel="noreferrer"
                                  >
                                    XLS
                                  </a>
                                  <a
                                    className="text-primary underline-offset-2 hover:underline"
                                    href={reportingRunReportWriterGridEndpoint(attempt.package!.reportingRunId!, grid.gridId, "xlsx")}
                                    target="_blank"
                                    rel="noreferrer"
                                  >
                                    XLSX
                                  </a>
                                </span>
                              );
                            })}
                          </div>
                        ) : null}
                      </div>
                    ) : null}
                    {attempt.package.renderedReportWriterGrids?.length ? (
                      <div className="rounded-md border border-border/70 bg-background/40 px-2 py-2 text-[11px]">
                        <p className="break-all font-mono">
                          Rendered grid rows: {attempt.package.renderedReportWriterGrids.map((grid) => `${grid.title || grid.gridId} (${grid.rows.length}r/${grid.columns.length}c)`).join(", ")}
                        </p>
                        <ul aria-label={`${attempt.recipient} rendered report-writer grid evidence`} className="mt-1 space-y-1">
                          {attempt.package.renderedReportWriterGrids.map((grid) => {
                            const title = grid.title || grid.gridId;
                            const dictionary = grid.dataDictionary ?? [];
                            const validationChecks = grid.validationChecks ?? [];
                            const generatedFieldCount = dictionary.filter((field) => field.isGenerated).length;
                            const passedCheckCount = validationChecks.filter((check) => check.status.toLowerCase() === "passed").length;
                            const issueCheckCount = validationChecks.length - passedCheckCount;
                            return (
                              <li key={`${attempt.attemptId}-${grid.gridId}-evidence`} className="break-all">
                                <p className="font-semibold text-foreground">
                                  {title} dictionary: {dictionary.length} field{dictionary.length === 1 ? "" : "s"}, {generatedFieldCount} generated
                                </p>
                                {dictionary.length ? (
                                  <p className="font-mono">
                                    {dictionary.slice(0, 4).map((field) => `${field.label || field.key}:${field.sourceField || "generated"}:${field.dataType}${field.isGenerated ? ":generated" : ""}`).join(" | ")}
                                  </p>
                                ) : null}
                                {validationChecks.length ? (
                                  <>
                                    <p className="mt-1 font-semibold text-foreground">
                                      {title} validation: {passedCheckCount} passed / {validationChecks.length} checks{issueCheckCount > 0 ? `, ${issueCheckCount} review` : ""}
                                    </p>
                                    <p className="font-mono">
                                      {validationChecks.slice(0, 4).map((check) => `${check.checkId}:${check.status}:${check.detail}`).join(" | ")}
                                    </p>
                                  </>
                                ) : null}
                              </li>
                            );
                          })}
                        </ul>
                      </div>
                    ) : null}
                    {attempt.package.lineProvenance?.length ? (
                      <div className="rounded-md border border-border/70 bg-background/40 px-2 py-2 text-[11px]">
                        <p className="font-semibold text-foreground">
                          Report-line provenance: {attempt.package.lineProvenance.length} line{attempt.package.lineProvenance.length === 1 ? "" : "s"}
                        </p>
                        <ul aria-label={`${attempt.recipient} package report-line provenance`} className="mt-1 space-y-1">
                          {attempt.package.lineProvenance.slice(0, 4).map((line) => (
                            <li key={`${attempt.attemptId}-${line.lineKey}-${line.evidenceId ?? line.sourceId}`} className="break-all">
                              <span className="font-mono text-foreground">{line.lineKey}</span>
                              <span> · {line.sourceKind} · {line.reportValue ?? "value pending"}</span>
                              {safeReportingHref(line.financialRecordHref) ? (
                                <a
                                  className="ml-2 text-primary underline-offset-2 hover:underline"
                                  href={safeReportingHref(line.financialRecordHref)!}
                                  aria-label={`Open Financial Record Explorer for ${line.lineKey}`}
                                >
                                  {formatFinancialRecordExplorerName(line.financialRecordExplorerId)}
                                </a>
                              ) : null}
                            </li>
                          ))}
                        </ul>
                      </div>
                    ) : null}
                    {attempt.package.deliveryEvidencePacket ? (
                      <div className="rounded-md border border-border/70 bg-background/40 px-2 py-2 text-[11px]">
                        <p className="font-semibold text-foreground">
                          Evidence packet: {attempt.package.deliveryEvidencePacket.packetKind} · {attempt.package.deliveryEvidencePacket.datasetVersion}
                        </p>
                        <p className="mt-1 break-all font-mono">
                          Template: {attempt.package.deliveryEvidencePacket.templateVersion} · Channel: {attempt.package.deliveryEvidencePacket.deliveryChannel}
                        </p>
                        <p className="mt-1">
                          Contents: {attempt.package.deliveryEvidencePacket.packageContents.length} · Support evidence: {attempt.package.deliveryEvidencePacket.supportEvidenceIds.length} · Delivery evidence: {attempt.package.deliveryEvidencePacket.deliveryEvidence.length}
                        </p>
                        {attempt.package.deliveryEvidencePacket.packageContents.length > 0 ? (
                          <p className="mt-1 break-all font-mono">
                            Package contents: {attempt.package.deliveryEvidencePacket.packageContents.join(" | ")}
                          </p>
                        ) : null}
                        {attempt.package.deliveryEvidencePacket.supportEvidenceIds.length > 0 ? (
                          <p className="mt-1 break-all font-mono">
                            Support evidence IDs: {attempt.package.deliveryEvidencePacket.supportEvidenceIds.join(" | ")}
                          </p>
                        ) : null}
                        {attempt.package.deliveryEvidencePacket.deliveryEvidence.length > 0 ? (
                          <ul aria-label={`${attempt.recipient} delivery packet evidence links`} className="mt-1 space-y-1">
                            {attempt.package.deliveryEvidencePacket.deliveryEvidence.map((evidence) => (
                              <li key={`${attempt.attemptId}-${evidence.evidenceId}`} className="break-all font-mono">
                                {safeReportingHref(evidence.route) ? (
                                  <a
                                    href={safeReportingHref(evidence.route)!}
                                    className="text-primary underline-offset-2 hover:underline"
                                    aria-label={`Open delivery evidence ${evidence.evidenceId}`}
                                  >
                                    {evidence.evidenceId}
                                  </a>
                                ) : (
                                  <span>{evidence.evidenceId}</span>
                                )}
                                <span> · {evidence.label} · {evidence.source}{evidence.capturedAtUtc ? ` · ${evidence.capturedAtUtc}` : ""}</span>
                              </li>
                            ))}
                          </ul>
                        ) : null}
                        <p className="mt-1 break-all font-mono">
                          Entitlement: {attempt.package.deliveryEvidencePacket.entitlementScope} · Fund: {attempt.package.deliveryEvidencePacket.fundProfileId} · Account: {attempt.package.deliveryEvidencePacket.fundAccountId} · Period: {attempt.package.deliveryEvidencePacket.period}
                        </p>
                        {attempt.package.deliveryEvidencePacket.recipientList.length > 0 ? (
                          <ul aria-label={`${attempt.recipient} delivery packet recipients`} className="mt-1 space-y-1">
                            {attempt.package.deliveryEvidencePacket.recipientList.map((recipient) => (
                              <li key={`${attempt.attemptId}-${recipient.distributionId}`} className="break-all font-mono">
                                {recipient.distributionId}: {recipient.recipient} · {recipient.recipientRole} · {recipient.channel}
                              </li>
                            ))}
                          </ul>
                        ) : null}
                        {attempt.package.deliveryEvidencePacket.approvalChain.length > 0 ? (
                          <ul aria-label={`${attempt.recipient} delivery packet approval chain`} className="mt-1 space-y-1">
                            {attempt.package.deliveryEvidencePacket.approvalChain.map((step) => (
                              <li key={`${attempt.attemptId}-${step.at}-${step.actor}-${step.action}`} className="break-all font-mono">
                                {step.at}: {step.actor} {step.action} {step.fromState} to {step.toState}{step.note ? ` - ${step.note}` : ""}
                              </li>
                            ))}
                          </ul>
                        ) : null}
                        {attempt.package.deliveryEvidencePacket.requestHistory.length > 0 ? (
                          <p className="mt-1 break-all font-mono">
                            Request history: {attempt.package.deliveryEvidencePacket.requestHistory.join(" | ")}
                          </p>
                        ) : null}
                        {attempt.package.deliveryEvidencePacket.amendmentReason ? (
                          <p className="mt-1 break-all font-mono">
                            Amendment: {attempt.package.deliveryEvidencePacket.amendmentReason}
                          </p>
                        ) : null}
                        {attempt.package.deliveryEvidencePacket.restatementLineage ? (
                          <p className="mt-1 break-all font-mono">
                            Restatement lineage: {attempt.package.deliveryEvidencePacket.restatementLineage}
                          </p>
                        ) : null}
                        {attempt.package.deliveryEvidencePacket.auditEventReferences?.length ? (
                          <p className="mt-1 break-all font-mono">
                            Audit references: {attempt.package.deliveryEvidencePacket.auditEventReferences.join(" | ")}
                          </p>
                        ) : null}
                        {attempt.package.deliveryEvidencePacket.blockedDownstreamOutputs?.length ? (
                          <p className="mt-1 break-all font-mono text-warning">
                            Blocked downstream outputs: {attempt.package.deliveryEvidencePacket.blockedDownstreamOutputs.join(" | ")}
                          </p>
                        ) : null}
                        <Link
                          className="mt-2 inline-flex items-center gap-1 text-[11px] font-medium text-primary underline-offset-2 hover:underline"
                          to={reportPackDeliveryEvidencePath(attempt.reportId, attempt.attemptId)}
                          aria-label={`Open delivery evidence graph for ${attempt.recipient} delivery attempt`}
                        >
                          <Network className="h-3 w-3" aria-hidden="true" />
                          Open delivery evidence graph
                        </Link>
                      </div>
                    ) : null}
                    {attempt.package.artifacts.length > 0 ? (
                      <ul aria-label={`${attempt.recipient} package artifact integrity`} className="grid gap-2 pt-1">
                        {attempt.package.artifacts.map((artifact) => (
                          <li
                            key={`${attempt.attemptId}-${artifact.artifactName}`}
                            className="rounded-md border border-border/70 bg-background/40 px-2 py-2"
                          >
                            <div className="flex flex-wrap items-start justify-between gap-2">
                              <span className="min-w-0">
                                {safeGovernedDistributionArtifactHref(artifact.downloadRoute) ? (
                                  <a
                                    className="block break-all font-mono text-[11px] text-primary underline-offset-2 hover:underline"
                                    href={safeGovernedDistributionArtifactHref(artifact.downloadRoute)!}
                                    aria-label={`Download ${artifact.artifactName}`}
                                  >
                                    {artifact.artifactName}
                                  </a>
                                ) : (
                                  <span className="block break-all font-mono text-[11px] text-foreground">
                                    {artifact.artifactName}
                                  </span>
                                )}
                                <span className="mt-1 block break-all font-mono text-[11px]">
                                  {redactReportingCredentialText(artifact.retainedPath)}
                                </span>
                              </span>
                              <Badge variant="outline">{artifact.format}</Badge>
                            </div>
                            <dl className="mt-2 grid gap-1 text-[11px] sm:grid-cols-2">
                              <ReportingScheduleField label="Size" value={`${artifact.byteSize.toLocaleString()} bytes`} />
                              <ReportingScheduleField label="Evidence" value={artifact.evidenceId} />
                              <ReportingScheduleField label="Checksum" value={artifact.checksumSha256 || "Checksum pending"} />
                              <ReportingScheduleField label="Version" value={artifact.versionStamp || "Version pending"} />
                            </dl>
                          </li>
                        ))}
                      </ul>
                    ) : null}
                  </div>
                ) : null}
                {attempt.failureReason ? (
                  <p className="mt-1 text-xs text-warning">
                    {redactReportingCredentialText(attempt.failureReason)}
                  </p>
                ) : null}
              </div>
            ))}
          </div>
        ) : (
          <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
            No report-pack delivery attempts have been retained yet.
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function formatFinancialRecordExplorerName(explorerId: string | null | undefined): string {
  if (explorerId === "portfolio") {
    return "Portfolio Explorer";
  }

  if (explorerId === "security-instrument") {
    return "Security & Instrument Explorer";
  }

  return "Ledger Explorer";
}

function hasReportingRunMetadata(reportPackage: ReportPackDeliveryPackage): boolean {
  return Boolean(
    reportPackage.reportingRunAsOfDate ||
    reportPackage.reportingRunStatus ||
    reportPackage.reportingRunTrigger ||
    reportPackage.reportingRunAttemptCount != null ||
    reportPackage.reportingRunSectionCount != null ||
    reportPackage.reportingRunLineageLinkedSections != null
  );
}

function formatReportingRunPackageMetadata(reportPackage: ReportPackDeliveryPackage): string {
  return [
    reportPackage.reportingRunAsOfDate ? `asOf=${reportPackage.reportingRunAsOfDate}` : null,
    reportPackage.reportingRunStatus ? `status=${reportPackage.reportingRunStatus}` : null,
    reportPackage.reportingRunTrigger ? `trigger=${reportPackage.reportingRunTrigger}` : null,
    reportPackage.reportingRunAttemptCount != null ? `attempt=${reportPackage.reportingRunAttemptCount.toLocaleString()}` : null,
    reportPackage.reportingRunSectionCount != null ? `sections=${reportPackage.reportingRunSectionCount.toLocaleString()}` : null,
    reportPackage.reportingRunLineageLinkedSections != null ? `lineage=${reportPackage.reportingRunLineageLinkedSections.toLocaleString()}` : null
  ].filter((part): part is string => Boolean(part)).join(" · ");
}

function hasReportWriterDatasetMetadata(reportPackage: ReportPackDeliveryPackage): boolean {
  return Boolean(
    reportPackage.reportWriterDatasetSourceLabel ||
    reportPackage.reportWriterDatasetSourceId ||
    reportPackage.reportWriterDatasetRowCount != null
  );
}

function formatReportWriterDatasetMetadata(reportPackage: ReportPackDeliveryPackage): string {
  const source = reportPackage.reportWriterDatasetSourceLabel?.trim() ||
    reportPackage.reportWriterDatasetSourceId?.trim() ||
    "Report-writer dataset";
  return reportPackage.reportWriterDatasetRowCount == null
    ? source
    : `${source} (${reportPackage.reportWriterDatasetRowCount.toLocaleString()} row${reportPackage.reportWriterDatasetRowCount === 1 ? "" : "s"})`;
}

function reportPackDeliveryEvidencePath(reportId: string, attemptId: string): string {
  return evidenceWorkbenchPath("report-pack-delivery", `${reportId}:${attemptId}`);
}

function safeGovernedDistributionArtifactHref(value: string | null | undefined): string | null {
  const href = safeReportingHref(value);
  return href?.startsWith("/api/fund-structure/reporting/distribution/packages/")
    ? href
    : null;
}
