import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
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

const reportRunSavedViews = [
  {
    id: "close-pack",
    label: "Close pack",
    title: "Monthly close package",
    detail: "Investor statement, trial balance, evidence appendix",
    cadence: "Month end"
  },
  {
    id: "board-pack",
    label: "Board",
    title: "Board finance packet",
    detail: "NAV bridge, exposure, exceptions, report-pack status",
    cadence: "Weekly"
  },
  {
    id: "operations",
    label: "Ops",
    title: "Operations evidence packet",
    detail: "Break queue, provider health, retained support files",
    cadence: "Daily"
  }
];

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
  const [standardDraft, setStandardDraft] = useState({
    entityScope: "All entities",
    period: todayIsoDate().slice(0, 7),
    ledgerBook: "Primary GL",
    accountingBasis: "GAAP",
    currency: "USD",
    consolidationLevel: "Fund",
    outputFormat: "PDF",
    finality: "Draft",
    includeSchedules: true,
    includeEvidence: true
  });

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
    const runnableTemplates = templates.filter((template) => template.canRunOnDemand);
    const recentRuns = runStatusRows.slice(0, 3);
    const primaryTemplate = runnableTemplates[0] ?? templates[0] ?? null;
    const runSetupActions = [
      {
        id: "breaks",
        label: "Review breaks",
        detail: `${readinessGate.items.find((item) => item.id === "reconciliation-breaks")?.count ?? 0} open reconciliation break(s)`,
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        tone: readinessGate.isClear ? ("success" as const) : ("warning" as const)
      },
      {
        id: "library",
        label: "Open library",
        detail: `${templates.length} governed template(s) available`,
        href: WORKSTATION_ROUTE_CATALOG.reportingLibrary,
        tone: "outline" as const
      },
      {
        id: "status",
        label: "Check run status",
        detail: `${runStatusRows.length} recent run(s) loaded`,
        href: WORKSTATION_ROUTE_CATALOG.reportingRunStatus,
        tone: runStatusRows.length > 0 ? ("success" as const) : ("outline" as const)
      }
    ];

    return (
      <Card className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle>Report Parameters</CardTitle>
            <CardDescription>Choose a governed template, review readiness, then run or open one from the Report Library.</CardDescription>
          </div>
          <Badge variant={readinessGate.isClear ? "success" : "warning"}>
            {readinessGate.isClear ? "Readiness clear" : "Review readiness"}
          </Badge>
        </CardHeader>
        <CardContent className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
          <div className="space-y-4">
            <section className="grid gap-2 md:grid-cols-3" aria-label="Report run setup scan band">
              <div className="border border-border bg-secondary/20 px-3 py-2">
                <div className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">Templates</div>
                <div className="mt-1 text-lg font-semibold text-foreground">{templates.length}</div>
                <p className="text-xs text-muted-foreground">{runnableTemplates.length} ready for on-demand runs</p>
              </div>
              <div className="border border-border bg-secondary/20 px-3 py-2">
                <div className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">Readiness</div>
                <div className="mt-1 text-lg font-semibold text-foreground">{readinessGate.isClear ? "Clear" : "Review"}</div>
                <p className="text-xs text-muted-foreground">{readinessGate.disclaimer}</p>
              </div>
              <div className="border border-border bg-secondary/20 px-3 py-2">
                <div className="font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">Recent runs</div>
                <div className="mt-1 text-lg font-semibold text-foreground">{runStatusRows.length}</div>
                <p className="text-xs text-muted-foreground">Run history and exceptions stay visible before configuration.</p>
              </div>
            </section>

            {templates.length > 0 ? (
              <label className="block max-w-xl space-y-1">
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

            <section aria-label="Recommended report templates" className="space-y-2">
              <div className="flex items-center justify-between gap-3">
                <h3 className="text-sm font-semibold text-foreground">Recommended templates</h3>
                <Link className="text-xs text-primary underline-offset-2 hover:underline" to={WORKSTATION_ROUTE_CATALOG.reportingLibrary}>
                  Or browse the Report Library
                </Link>
              </div>
              {runnableTemplates.length > 0 ? (
                <ul className="grid gap-2" aria-label="Runnable report templates">
                  {runnableTemplates.slice(0, 4).map((template) => (
                    <li key={template.id} className="flex items-center justify-between gap-3 border border-border bg-background/50 px-3 py-2">
                      <div className="min-w-0">
                        <div className="truncate text-sm font-medium text-foreground">{template.name}</div>
                        <div className="text-xs text-muted-foreground">v{template.versionNumber} · {template.statusLabel}</div>
                      </div>
                      <Button
                        type="button"
                        variant="secondary"
                        size="sm"
                        onClick={() => setSearchParams({ templateId: template.id })}
                      >
                        Configure
                      </Button>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="border border-border bg-background/50 p-3 text-sm text-muted-foreground">
                  No runnable templates are available. Open the library to review approval posture.
                </p>
              )}
            </section>

            <section aria-label="Saved report run views" className="space-y-2">
              <h3 className="text-sm font-semibold text-foreground">Saved run views</h3>
              <div className="grid gap-2 lg:grid-cols-3">
                {reportRunSavedViews.map((view) => (
                  <div key={view.id} className="border border-border bg-background/50 px-3 py-2">
                    <div className="flex items-center justify-between gap-2">
                      <div className="text-sm font-semibold text-foreground">{view.title}</div>
                      <Badge variant="outline">{view.label}</Badge>
                    </div>
                    <p className="mt-1 text-xs leading-5 text-muted-foreground">{view.detail}</p>
                    <div className="mt-2 flex items-center justify-between gap-2">
                      <span className="font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{view.cadence}</span>
                      {primaryTemplate ? (
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          onClick={() => setSearchParams({ templateId: primaryTemplate.id })}
                        >
                          Open
                        </Button>
                      ) : (
                        <Button asChild variant="ghost" size="sm">
                          <Link to={WORKSTATION_ROUTE_CATALOG.reportingLibrary}>Open</Link>
                        </Button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </section>
          </div>

          <aside className="space-y-4" aria-label="Report run setup context">
            <section className="space-y-2">
              <h3 className="text-sm font-semibold text-foreground">Next actions</h3>
              <ul className="grid gap-2" aria-label="Report run next actions">
                {runSetupActions.map((action) => (
                  <li key={action.id} className="border border-border bg-background/50 px-3 py-2">
                    <div className="flex items-center justify-between gap-2">
                      <Link className="text-xs font-semibold text-primary underline-offset-2 hover:underline" to={action.href}>
                        {action.label}
                      </Link>
                      <Badge variant={action.tone}>{action.id}</Badge>
                    </div>
                    <p className="mt-1 text-[11px] leading-5 text-muted-foreground">{action.detail}</p>
                  </li>
                ))}
              </ul>
            </section>
            <section className="space-y-2">
              <h3 className="text-sm font-semibold text-foreground">Readiness cues</h3>
              <ul className="grid gap-2" aria-label="Report run readiness cues">
                {readinessGate.items.map((item) => (
                  <li key={item.id} className="flex items-center justify-between gap-3 border border-border bg-background/50 px-3 py-2">
                    <span className="text-xs text-muted-foreground">{item.label}</span>
                    <Badge variant={item.tone === "success" ? "success" : "warning"}>{item.count}</Badge>
                  </li>
                ))}
              </ul>
            </section>
            <section className="space-y-2">
              <h3 className="text-sm font-semibold text-foreground">Recent runs</h3>
              {recentRuns.length > 0 ? (
                <ul className="grid gap-2" aria-label="Recent report runs">
                  {recentRuns.map((run) => (
                    <li key={run.id} className="border border-border bg-background/50 px-3 py-2">
                      <div className="text-xs font-medium text-foreground">{run.templateLabel}</div>
                      <div className="text-[11px] text-muted-foreground">{run.status} · {run.asOfDateLabel}</div>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="border border-border bg-background/50 p-3 text-xs leading-5 text-muted-foreground">
                  No recent report runs are loaded. Select a template to prepare the first run.
                </p>
              )}
            </section>
          </aside>
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
      <section className="grid gap-4 xl:grid-cols-[minmax(0,1.25fr)_minmax(280px,0.75fr)]">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Report Parameters</CardTitle>
            <CardDescription>Set the business scope before preview, validation, run, and distribution.</CardDescription>
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
                value={standardDraft.accountingBasis}
                onChange={(event) => onStandardDraftChange("accountingBasis", event.target.value)}
              >
                <option>GAAP</option>
                <option>Tax</option>
                <option>Management</option>
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
            <div className="flex flex-wrap gap-4 md:col-span-2">
              <Checkbox
                label="Include supporting schedules"
                checked={standardDraft.includeSchedules}
                onCheckedChange={(checked) => onStandardDraftChange("includeSchedules", checked)}
              />
              <Checkbox
                label="Include evidence appendix"
                checked={standardDraft.includeEvidence}
                onCheckedChange={(checked) => onStandardDraftChange("includeEvidence", checked)}
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
