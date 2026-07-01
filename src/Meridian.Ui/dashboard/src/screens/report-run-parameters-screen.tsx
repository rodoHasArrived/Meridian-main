import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
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
  const [standardDraft, setStandardDraft] = useState(() => ({
    entityScope: "All entities",
    period: todayIsoDate().slice(0, 7),
    ledgerBook: "Primary GL",
    basis: "GAAP",
    currency: "USD",
    consolidationLevel: "Fund",
    outputFormat: "PDF",
    finality: "Draft",
    includeSchedules: true,
    includeEvidence: true
  }));

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

  const onStandardDraftChange = (field: keyof typeof standardDraft, value: string | boolean) => {
    setStandardDraft((current) => ({ ...current, [field]: value }));
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
      <section className="grid gap-4 xl:grid-cols-[minmax(0,1.35fr)_minmax(280px,0.65fr)]">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Report Parameters</CardTitle>
            <CardDescription>{selectedTemplate.name} setup for Finance review, preview, and retained output.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-3 md:grid-cols-2">
            <FormRow label="Entity / fund / portfolio" labelFor="report-entity-scope">
              <Input
                id="report-entity-scope"
                value={standardDraft.entityScope}
                onChange={(event) => onStandardDraftChange("entityScope", event.target.value)}
              />
            </FormRow>
            <FormRow label="Period or as-of date" labelFor="report-period">
              <Input
                id="report-period"
                value={standardDraft.period}
                onChange={(event) => onStandardDraftChange("period", event.target.value)}
              />
            </FormRow>
            <FormRow label="Ledger book" labelFor="report-ledger-book">
              <Input
                id="report-ledger-book"
                value={standardDraft.ledgerBook}
                onChange={(event) => onStandardDraftChange("ledgerBook", event.target.value)}
              />
            </FormRow>
            <FormRow label="Accounting basis" labelFor="report-accounting-basis">
              <Select
                id="report-accounting-basis"
                value={standardDraft.basis}
                onChange={(event) => onStandardDraftChange("basis", event.target.value)}
              >
                <option>GAAP</option>
                <option>Tax</option>
                <option>Management</option>
                <option>Statutory</option>
              </Select>
            </FormRow>
            <FormRow label="Currency" labelFor="report-currency">
              <Select
                id="report-currency"
                value={standardDraft.currency}
                onChange={(event) => onStandardDraftChange("currency", event.target.value)}
              >
                <option>USD</option>
                <option>EUR</option>
                <option>GBP</option>
                <option>JPY</option>
              </Select>
            </FormRow>
            <FormRow label="Consolidation level" labelFor="report-consolidation-level">
              <Select
                id="report-consolidation-level"
                value={standardDraft.consolidationLevel}
                onChange={(event) => onStandardDraftChange("consolidationLevel", event.target.value)}
              >
                <option>Fund</option>
                <option>Entity</option>
                <option>Portfolio</option>
                <option>Investor</option>
              </Select>
            </FormRow>
            <FormRow label="Output format" labelFor="report-output-format">
              <Select
                id="report-output-format"
                value={standardDraft.outputFormat}
                onChange={(event) => onStandardDraftChange("outputFormat", event.target.value)}
              >
                <option>PDF</option>
                <option>XLSX</option>
                <option>CSV</option>
                <option>Evidence Vault</option>
              </Select>
            </FormRow>
            <FormRow label="Draft vs final" labelFor="report-finality">
              <Select
                id="report-finality"
                value={standardDraft.finality}
                onChange={(event) => onStandardDraftChange("finality", event.target.value)}
              >
                <option>Draft</option>
                <option>Final</option>
              </Select>
            </FormRow>
            <div className="space-y-3 md:col-span-2">
              <Checkbox
                checked={standardDraft.includeSchedules}
                onCheckedChange={(checked) => onStandardDraftChange("includeSchedules", checked)}
                label="Include supporting schedules"
              />
              <Checkbox
                checked={standardDraft.includeEvidence}
                onCheckedChange={(checked) => onStandardDraftChange("includeEvidence", checked)}
                label="Include evidence appendix"
              />
            </div>
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <CardTitle>Can this report run?</CardTitle>
              <CardDescription>{readinessGate.disclaimer}</CardDescription>
            </div>
            <Badge variant={readinessGate.isClear ? "success" : "warning"}>
              {readinessGate.isClear ? "Clear to run" : "Warnings present"}
            </Badge>
          </CardHeader>
          <CardContent>
            <ul className="grid gap-2" aria-label="Report run readiness checks">
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
              <li className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2 text-sm text-foreground">
                Also checks missing prices, unlocked evidence, and period-close state before final output.
              </li>
            </ul>
          </CardContent>
        </Card>
      </section>

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
