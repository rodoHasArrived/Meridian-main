import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Select } from "@/components/ui/select";
import { describeApiError } from "@/lib/api-errors";
import { getManualJournalEntryWorkbench, runReportingNow } from "@/lib/api";
import { todayIsoDate } from "@/lib/reporting-periods";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import {
  ExportsReportRunner,
  type ExportsReportRunDraftField,
  type ExportsReportRunDraftState
} from "@/screens/reporting-screen.exports-runner";
import { ReportingCommandStatusView, type ReportingCommandStatus } from "@/screens/reporting-screen.shared-components";
import {
  buildExportsReportRunRequest,
  buildReportRunResultDetails,
  isExportsOnDemandRun
} from "@/screens/reporting-screen";
import { buildRunStatusRows, buildTemplateRows } from "@/screens/reporting-screen.view-model";
import { buildReportRunReadinessGateViewState } from "@/screens/report-run-parameters-screen.view-model";
import type { AccountingWorkspaceResponse, ManualJournalEntryDraft } from "@/types";

interface ReportRunParametersScreenProps {
  data: AccountingWorkspaceResponse | null;
  accounting: AccountingWorkspaceResponse | null;
}

export function ReportRunParametersScreen({ data, accounting }: ReportRunParametersScreenProps) {
  const [searchParams, setSearchParams] = useSearchParams();
  const templateId = searchParams.get("templateId") ?? "";
  const reporting = data?.reporting ?? null;

  const templates = useMemo(() => buildTemplateRows(reporting?.templates ?? []), [reporting?.templates]);
  const runStatusRows = useMemo(() => buildRunStatusRows(reporting?.recentRuns ?? []), [reporting?.recentRuns]);
  const exportsRunRows = useMemo(() => runStatusRows.filter(isExportsOnDemandRun), [runStatusRows]);
  const selectedTemplate = templates.find((template) => template.id === templateId) ?? null;

  const [draft, setDraft] = useState<ExportsReportRunDraftState>(() => ({
    templateRowId: templateId,
    asOfDate: todayIsoDate(),
    maxRetries: "0",
    requestedBy: "browser-user",
    datasetSourceId: ""
  }));
  const [status, setStatus] = useState<ReportingCommandStatus | null>(null);
  const [manualDrafts, setManualDrafts] = useState<ManualJournalEntryDraft[]>([]);

  useEffect(() => {
    let cancelled = false;

    getManualJournalEntryWorkbench({})
      .then((workbench) => {
        if (!cancelled) {
          setManualDrafts(workbench.drafts);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setManualDrafts([]);
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const readinessGate = useMemo(
    () => buildReportRunReadinessGateViewState({
      reconciliationQueue: accounting?.reconciliationQueue ?? [],
      manualDrafts
    }),
    [accounting?.reconciliationQueue, manualDrafts]
  );

  const runningTemplateRunId = status?.state === "running" ? status.id : null;

  const onDraftChange = (field: ExportsReportRunDraftField, value: string) => {
    setDraft((current) => ({ ...current, [field]: value }));
  };

  const onRun = async () => {
    if (!selectedTemplate || !selectedTemplate.canRunOnDemand || runningTemplateRunId) {
      return;
    }

    setStatus({
      id: selectedTemplate.id,
      label: "Report run",
      state: "running",
      message: `${selectedTemplate.name} is running.`,
      details: []
    });

    try {
      const result = await runReportingNow(buildExportsReportRunRequest(selectedTemplate, draft));
      setStatus({
        id: selectedTemplate.id,
        label: "Report run",
        state: "success",
        message: `${selectedTemplate.name} run created.`,
        details: buildReportRunResultDetails(result.run)
      });
    } catch (error) {
      const description = describeApiError(error, `${selectedTemplate.name} run failed.`);
      setStatus({
        id: selectedTemplate.id,
        label: "Report run",
        state: "error",
        message: description.summary,
        details: description.details
      });
    }
  };

  if (!data) {
    return (
      <Card
        className="panel-surface"
        role="status"
        aria-busy="true"
        aria-live="polite"
        aria-labelledby="report-run-loading-title"
      >
        <CardHeader>
          <CardTitle id="report-run-loading-title">Loading report parameters</CardTitle>
          <CardDescription>Waiting for reporting workspace data.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  if (!templateId) {
    return (
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Report Parameters</CardTitle>
          <CardDescription>Choose a report template to configure and run, or open one from the Report Library.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          {templates.length > 0 ? (
            <label className="block max-w-md space-y-1">
              <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Report template</span>
              <Select
                value=""
                onChange={(event) => {
                  const nextTemplateId = event.target.value;
                  if (nextTemplateId) {
                    setSearchParams({ templateId: nextTemplateId });
                  }
                }}
                aria-label="Choose a report template to run"
              >
                <option value="" disabled>Select a template</option>
                {templates.map((template) => (
                  <option key={template.id} value={template.id} disabled={!template.canRunOnDemand}>
                    {template.name} v{template.versionNumber} ({template.statusLabel})
                  </option>
                ))}
              </Select>
            </label>
          ) : (
            <p className="text-sm text-muted-foreground">No report templates are available yet.</p>
          )}
          <Link className="inline-block text-xs text-primary underline-offset-2 hover:underline" to={WORKSTATION_ROUTE_CATALOG.reportingLibrary}>
            Or browse the Report Library
          </Link>
        </CardContent>
      </Card>
    );
  }

  if (!selectedTemplate) {
    return (
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Template not found</CardTitle>
          <CardDescription>No report template matching "{templateId}" was found.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle>Readiness</CardTitle>
            <CardDescription>{readinessGate.disclaimer}</CardDescription>
          </div>
          <Badge variant={readinessGate.isClear ? "success" : "warning"}>
            {readinessGate.isClear ? "Clear to run" : "Warnings present"}
          </Badge>
        </CardHeader>
        <CardContent>
          <ul className="grid gap-2 sm:grid-cols-2" aria-label="Report run readiness checks">
            {readinessGate.items.map((item) => (
              <li key={item.id} className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
                <div className="flex items-center justify-between gap-2">
                  <span className="text-sm text-foreground">{item.label}</span>
                  <Badge variant={item.tone}>{item.count}</Badge>
                </div>
                {item.href && item.linkLabel ? (
                  <Link className="mt-1 inline-block text-xs text-primary underline-offset-2 hover:underline" to={item.href}>
                    {item.linkLabel}
                  </Link>
                ) : null}
              </li>
            ))}
          </ul>
        </CardContent>
      </Card>

      <ExportsReportRunner
        draft={draft}
        templates={templates}
        selectedTemplate={selectedTemplate}
        datasetSources={reporting?.reportWriterDatasetSources ?? []}
        recentRuns={exportsRunRows}
        status={status}
        runningTemplateRunId={runningTemplateRunId}
        defaultRequester="browser-user"
        onDraftChange={onDraftChange}
        onRun={() => void onRun()}
      />

      {status ? <ReportingCommandStatusView status={status} /> : null}

      <Link className="text-xs text-primary underline-offset-2 hover:underline" to={WORKSTATION_ROUTE_CATALOG.reportingLibrary}>
        Back to Report Library
      </Link>
    </div>
  );
}
