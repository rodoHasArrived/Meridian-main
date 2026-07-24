import { Send } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { TechnicalDetails } from "@/components/ui/technical-details";
import {
  latestReleasedRunsPerSeries,
  presentReportingAsOfDate,
  presentReportingIdentifier,
  presentReportingRunStatusLabel
} from "@/screens/reporting-screen.view-model";
import type { ReportingRunStatusRow, ReportingTemplateRow } from "@/screens/reporting-screen.view-model";
import {
  ReportingCommandStatusView,
  ReportingScheduleField,
  type ReportingCommandStatus
} from "@/screens/reporting-screen.shared-components";
import type { ReportWriterDatasetSource } from "@/types";

export type ExportsReportRunDraftField =
  | "templateRowId"
  | "asOfDate"
  | "maxRetries"
  | "requestedBy"
  | "datasetSourceId"
  | "retryReason";

export interface ExportsReportRunDraftState {
  templateRowId: string;
  asOfDate: string;
  maxRetries: string;
  requestedBy: string;
  datasetSourceId: string;
  retryReason: string;
  // Identity of the released run being superseded. An empty runId means "run a new report".
  restatementTargetRunId: string;
  restatementTemplateId: string;
  restatementJobId: string;
  restatementAsOfDate: string;
  restatementDatasetSourceId: string;
}

/** Identity carried when the operator authorizes restating a specific released run. */
export interface RestatementTargetSelection {
  runId: string;
  templateId: string;
  jobId: string;
  asOfDate: string;
  datasetSourceId: string;
}

interface ExportsReportRunnerProps {
  context?: "exports" | "run";
  draft: ExportsReportRunDraftState;
  templates: ReportingTemplateRow[];
  selectedTemplate: ReportingTemplateRow | null;
  datasetSources: ReportWriterDatasetSource[];
  recentRuns: ReportingRunStatusRow[];
  status: ReportingCommandStatus | null;
  runningTemplateRunId: string | null;
  runBlockedReason?: string | null;
  defaultRequester: string;
  onDraftChange: (field: ExportsReportRunDraftField, value: string) => void;
  onRestatementTargetChange: (target: RestatementTargetSelection | null) => void;
  onRun: () => void;
}

export function ExportsReportRunner({
  context = "exports",
  draft,
  templates,
  selectedTemplate,
  datasetSources,
  recentRuns,
  status,
  runningTemplateRunId,
  runBlockedReason = null,
  defaultRequester,
  onDraftChange,
  onRestatementTargetChange,
  onRun
}: ExportsReportRunnerProps) {
  const isRunWorkspace = context === "run";
  const contextLabel = isRunWorkspace ? "Report run" : "Exports report";
  const workspaceLabel = isRunWorkspace ? "Report run" : "Exports";
  const selectedDataset = datasetSources.find((source) => source.sourceId === draft.datasetSourceId) ?? null;
  // Only the latest released run per series can be restated: the backend supersedes the series'
  // released head, so offering an older released version would mismatch the run the operator picked.
  const releasedRuns = latestReleasedRunsPerSeries(recentRuns);
  const restatementTarget = draft.restatementTargetRunId
    ? releasedRuns.find((run) => run.id === draft.restatementTargetRunId) ?? null
    : null;
  const isRestating = Boolean(restatementTarget);
  const restatementReasonMissing = isRestating && draft.retryReason.trim().length === 0;
  const templateRunDisabledReason = isRestating
    ? (restatementReasonMissing
      ? "Enter a restatement reason to supersede the selected released report."
      : null)
    : resolveExportsRunDisabledReason(selectedTemplate);
  const runDisabledReason = runBlockedReason ?? templateRunDisabledReason;
  const activeRunId = isRestating ? draft.restatementTargetRunId : selectedTemplate?.id;
  const isRunningSelected = Boolean(activeRunId && runningTemplateRunId === activeRunId);

  const handleRestatementTargetChange = (runId: string) => {
    const target = runId ? releasedRuns.find((run) => run.id === runId) ?? null : null;
    onRestatementTargetChange(
      target
        ? {
            runId: target.id,
            templateId: target.templateId,
            jobId: target.restatementJobId,
            asOfDate: target.restatementAsOfDate,
            datasetSourceId: target.restatementDatasetSourceId
          }
        : null
    );
  };

  return (
    <section role="region" aria-label={isRunWorkspace ? "Report run form" : "Exports report runner"}>
      <Card className="panel-surface">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="eyebrow-label">{workspaceLabel}</div>
              <CardTitle>Run a governed report now</CardTitle>
              <CardDescription>
                Submit approved Reporting templates through Meridian reporting services with explicit date, requester, retry, and dataset context.
              </CardDescription>
            </div>
            <span className="flex flex-wrap items-center gap-1.5">
              <Badge variant={selectedTemplate?.canRunOnDemand ? "success" : "warning"}>
                {selectedTemplate?.canRunOnDemand ? "Runnable" : "Gated"}
              </Badge>
              <Badge variant="outline">Reporting service ready</Badge>
              <Badge variant="outline">{isRunWorkspace ? "Run workspace" : "Exports workspace"}</Badge>
            </span>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-3 lg:grid-cols-[1.3fr_0.7fr_0.7fr]">
            <label className="space-y-1">
              <span className="text-xs font-medium text-muted-foreground">Template</span>
              <Select
                value={selectedTemplate?.id ?? draft.templateRowId}
                onChange={(event) => onDraftChange("templateRowId", event.target.value)}
                disabled={isRestating}
                aria-label={`${contextLabel} template`}
              >
                {templates.length > 0 ? templates.map((template) => (
                  <option key={template.id} value={template.id} disabled={!template.canRunOnDemand}>
                    {template.name} v{template.versionNumber} ({template.statusLabel})
                  </option>
                )) : (
                  <option value="">No report templates available</option>
                )}
              </Select>
            </label>
            <label className="space-y-1">
              <span className="text-xs font-medium text-muted-foreground">As of</span>
              <Input
                type="date"
                value={draft.asOfDate}
                onChange={(event) => onDraftChange("asOfDate", event.target.value)}
                disabled={isRestating}
                aria-label={`${contextLabel} as-of date`}
              />
            </label>
            <label className="space-y-1">
              <span className="text-xs font-medium text-muted-foreground">Retries</span>
              <Input
                type="number"
                min="0"
                step="1"
                value={draft.maxRetries}
                onChange={(event) => onDraftChange("maxRetries", event.target.value)}
                aria-label={`${contextLabel} max retries`}
              />
            </label>
          </div>

          <div className="grid gap-3 lg:grid-cols-[1fr_1fr_auto]">
            <label className="space-y-1">
              <span className="text-xs font-medium text-muted-foreground">Dataset source</span>
              <Select
                value={draft.datasetSourceId}
                onChange={(event) => onDraftChange("datasetSourceId", event.target.value)}
                disabled={isRestating || !selectedTemplate?.hasWriterGrids || datasetSources.length === 0}
                aria-label={`${contextLabel} dataset source`}
              >
                <option value="">Default retained dataset</option>
                {datasetSources.map((source) => (
                  <option key={source.sourceId} value={source.sourceId}>
                    {source.label} ({source.rowCount})
                  </option>
                ))}
              </Select>
            </label>
            <label className="space-y-1">
              <span className="text-xs font-medium text-muted-foreground">Requested by</span>
              <Input
                value={draft.requestedBy}
                onChange={(event) => onDraftChange("requestedBy", event.target.value)}
                aria-label={`${contextLabel} requested by`}
              />
            </label>
            <div className="flex items-end">
              <Button
                className="w-full lg:w-auto"
                disabled={Boolean(runDisabledReason) || Boolean(runningTemplateRunId)}
                disabledReason={runDisabledReason}
                busy={isRunningSelected}
                busyLabel="Running"
                onClick={onRun}
                aria-label={
                  isRestating
                    ? `Restate released report ${restatementTarget?.id ?? ""}`.trim()
                    : selectedTemplate
                      ? `Run ${selectedTemplate.name}${isRunWorkspace ? "" : " from Exports"}`
                      : "Run report"
                }
              >
                <Send className="h-4 w-4" aria-hidden="true" />
                {isRestating ? "Restate report" : "Run report"}
              </Button>
            </div>
          </div>

          <details className="rounded-md border border-warning/40 bg-warning/5" open={isRestating || undefined}>
            <summary className="cursor-pointer px-3 py-3 text-sm font-semibold text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">
              Advanced: restate a released report
            </summary>
          <div className="space-y-3 border-t border-warning/30 px-3 py-3" aria-label="Restatement authorization">
            <div>
              <h4 className="text-sm font-semibold text-foreground">Restate a released report</h4>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">
                Regenerating a released report is a governed action that supersedes the released run. Select the
                released run to restate — its template and as-of date are reused so the new run versions into the same
                series — and record why. The reason is retained in the run&apos;s audit trail.
              </p>
            </div>
            <label className="block space-y-1">
              <span className="text-xs font-medium text-muted-foreground">Released run to supersede</span>
              <Select
                value={draft.restatementTargetRunId}
                onChange={(event) => handleRestatementTargetChange(event.target.value)}
                disabled={releasedRuns.length === 0}
                aria-label="Released run to restate"
              >
                <option value="">Run a new report (no restatement)</option>
                {releasedRuns.map((run) => (
                  <option key={run.id} value={run.id}>
                    {run.id} · {run.templateLabel} · as of {run.asOfDateLabel}
                  </option>
                ))}
              </Select>
              {releasedRuns.length === 0 ? (
                <span className="text-xs text-muted-foreground">No released reports are available to restate.</span>
              ) : null}
            </label>
            {isRestating ? (
              <label className="block space-y-1">
                <span className="text-xs font-medium text-muted-foreground">Restatement reason</span>
                <Input
                  value={draft.retryReason}
                  onChange={(event) => onDraftChange("retryReason", event.target.value)}
                  placeholder="Reason for restating the released report"
                  aria-label="Restatement reason"
                  aria-invalid={restatementReasonMissing ? true : undefined}
                />
                {restatementReasonMissing ? (
                  <span className="text-xs text-warning">A restatement reason is required to supersede a released report.</span>
                ) : null}
              </label>
            ) : null}
          </div>
          </details>

          <div className="grid gap-3 xl:grid-cols-[1fr_1fr]">
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3" aria-label={`${contextLabel} selected template readiness`}>
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <h4 className="text-sm font-semibold text-foreground">{selectedTemplate?.name ?? "No template selected"}</h4>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">
                    {selectedTemplate ? selectedTemplate.approvalSummary : "Load approved report templates before running an export report."}
                  </p>
                </div>
                <span className="flex flex-wrap justify-end gap-1.5">
                  {selectedTemplate ? (
                    <>
                      <Badge variant={selectedTemplate.statusVariant}>{selectedTemplate.statusLabel}</Badge>
                      <Badge variant={selectedTemplate.accessGovernance.postureVariant}>{selectedTemplate.accessGovernance.postureLabel}</Badge>
                      <Badge variant="outline">{presentReportingIdentifier(selectedTemplate.family, "Report")}</Badge>
                    </>
                  ) : <Badge variant="outline">Unavailable</Badge>}
                </span>
              </div>
              <dl className="mt-3 grid gap-2 text-xs sm:grid-cols-2" aria-label={`${contextLabel} request summary`}>
                <ReportingScheduleField label="Template" value={selectedTemplate?.name ?? "No template"} />
                <ReportingScheduleField label="Version" value={selectedTemplate?.version ?? "Unavailable"} />
                <ReportingScheduleField label="Sections" value={selectedTemplate?.sectionSummary ?? "Unavailable"} />
                <ReportingScheduleField label="Access" value={selectedTemplate?.accessSummary ?? "Unavailable"} />
                <ReportingScheduleField label="Dataset" value={selectedDataset ? `${selectedDataset.label} (${selectedDataset.rowCount})` : "Default retained dataset"} />
                <ReportingScheduleField label="Requester" value={draft.requestedBy.trim() || defaultRequester} />
              </dl>
              {runDisabledReason ? <p className="mt-3 text-xs leading-5 text-warning">{runDisabledReason}</p> : null}
            </div>

            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h4 className="text-sm font-semibold text-foreground">{isRunWorkspace ? "Recent report runs" : "Recent export runs"}</h4>
                <Badge variant="outline">{recentRuns.length}</Badge>
              </div>
              {recentRuns.length > 0 ? (
                <div role="list" aria-label={isRunWorkspace ? "Recent report runs" : "Recent Exports report runs"} className="mt-3 space-y-2">
                  {recentRuns.slice(0, 3).map((run) => (
                    <div key={run.id} role="listitem" className="rounded-sm border border-border/60 bg-background/30 px-2.5 py-2 text-xs">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="font-medium text-foreground">{run.templateLabel}</span>
                        <Badge variant={run.status === "Failed" || presentReportingRunStatusLabel(run.status, run.asOfDateLabel) === "Period confirmation required" ? "warning" : "outline"}>
                          {presentReportingRunStatusLabel(run.status, run.asOfDateLabel)}
                        </Badge>
                      </div>
                      <p className="mt-1 text-muted-foreground">
                        {presentReportingAsOfDate(run.asOfDateLabel)} / {run.datasetSourceLabel}
                      </p>
                      <TechnicalDetails label="Run identifier" className="mt-2">
                        <p className="break-all font-mono text-xs text-muted-foreground">{run.id}</p>
                      </TechnicalDetails>
                    </div>
                  ))}
                </div>
              ) : (
                <p role="status" className="mt-3 text-sm text-muted-foreground">No {isRunWorkspace ? "report" : "export"} runs have been created yet.</p>
              )}
            </div>
          </div>

          {status?.label === "Exports report run" ? <ReportingCommandStatusView status={status} /> : null}
        </CardContent>
      </Card>
    </section>
  );
}

function resolveExportsRunDisabledReason(template: ReportingTemplateRow | null): string | null {
  if (!template) {
    return "No report template is available to run.";
  }

  return template.canRunOnDemand ? null : template.runDisabledReason ?? `${template.name} is not approved for export runs.`;
}
