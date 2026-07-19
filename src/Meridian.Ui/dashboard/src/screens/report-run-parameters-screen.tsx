import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { describeApiError } from "@/lib/api-errors";
import { assessReportingRunReadiness, getManualJournalEntryWorkbench, runReportingNow } from "@/lib/api";
import { todayIsoDate } from "@/lib/reporting-periods";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import {
  ExportsReportRunner,
  type ExportsReportRunDraftField,
  type ExportsReportRunDraftState,
  type RestatementTargetSelection
} from "@/screens/reporting-screen.exports-runner";
import { ReportingCommandStatusView, type ReportingCommandStatus } from "@/screens/reporting-screen.shared-components";
import {
  buildExportsReportRunRequest,
  buildReportRunResultDetails
} from "@/screens/reporting-screen";
import {
  buildRunStatusRows,
  buildTemplateRows,
  presentReportingAsOfDate,
  presentReportingIdentifier,
  presentReportingStatusLabel
} from "@/screens/reporting-screen.view-model";
import {
  buildAuthoritativeReadinessGateViewState,
  buildDefaultReportRunParameterDraft,
  buildReportRunReadinessGateViewState,
  validateAndBuildReportingRunParameters,
  type ReportRunParameterDraftField,
  type ReportRunParameterDraftState
} from "@/screens/report-run-parameters-screen.view-model";
import type {
  AccountingWorkspaceResponse,
  ManualJournalEntryDraft,
  ReportingRunReadiness,
  ReportingRunRequest
} from "@/types";

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

type ReportingReadinessPreflightState =
  | { phase: "idle"; requestKey: null; readiness: null; error: null }
  | { phase: "checking"; requestKey: string; readiness: null; error: null }
  | { phase: "complete"; requestKey: string; readiness: ReportingRunReadiness; error: null }
  | { phase: "error"; requestKey: string; readiness: null; error: string };

const idleReadinessPreflight: ReportingReadinessPreflightState = {
  phase: "idle",
  requestKey: null,
  readiness: null,
  error: null
};

export function ReportRunParametersScreen({ data, accounting }: ReportRunParametersScreenProps) {
  const [searchParams, setSearchParams] = useSearchParams();
  const reporting = data?.reporting ?? null;

  const templates = useMemo(() => buildTemplateRows(reporting?.templates ?? []), [reporting?.templates]);
  const runStatusRows = useMemo(
    () => buildRunStatusRows(reporting?.recentRuns ?? [], reporting?.templates ?? []),
    [reporting?.recentRuns, reporting?.templates]
  );
  const cloneRunId = searchParams.get("cloneRunId") ?? "";
  const clonedRun = cloneRunId ? runStatusRows.find((run) => run.id === cloneRunId) ?? null : null;
  const clonedTemplate = clonedRun
    ? templates.find((template) => template.templateName === clonedRun.templateId || template.id === clonedRun.templateId) ?? null
    : null;
  const requestedTemplateId = searchParams.get("templateId") ?? clonedTemplate?.id ?? "";

  const [draft, setDraft] = useState<ExportsReportRunDraftState>(() => ({
    templateRowId: requestedTemplateId,
    asOfDate: clonedRun?.restatementAsOfDate || todayIsoDate(),
    maxRetries: "0",
    requestedBy: "browser-user",
    datasetSourceId: clonedRun?.restatementDatasetSourceId ?? "",
    retryReason: "",
    restatementTargetRunId: "",
    restatementTemplateId: "",
    restatementJobId: "",
    restatementAsOfDate: "",
    restatementDatasetSourceId: ""
  }));
  const hydratedRouteContextRef = useRef<string | null>(null);
  const selectedTemplate = templates.find((template) => template.id === draft.templateRowId)
    ?? templates.find((template) => template.id === requestedTemplateId)
    ?? null;
  const [status, setStatus] = useState<ReportingCommandStatus | null>(null);
  const [manualDrafts, setManualDrafts] = useState<ManualJournalEntryDraft[]>([]);
  const [standardDraft, setStandardDraft] = useState(() => buildDefaultReportRunParameterDraft({
    fundProfileId: reporting?.selectedFundProfileId ?? reporting?.fundProfileId,
    asOfDate: clonedRun?.restatementAsOfDate || todayIsoDate(),
    parameters: reporting?.recentRuns?.find((run) => run.runId === cloneRunId)?.resolvedParameters
  }));
  const [readinessPreflight, setReadinessPreflight] = useState<ReportingReadinessPreflightState>(idleReadinessPreflight);

  useEffect(() => {
    const routeContextKey = cloneRunId
      ? `clone:${cloneRunId}`
      : requestedTemplateId
        ? `template:${requestedTemplateId}`
        : null;
    if (!routeContextKey || hydratedRouteContextRef.current === routeContextKey) {
      return;
    }

    if (cloneRunId) {
      if (!clonedRun || !clonedTemplate) {
        return;
      }

      setDraft((current) => ({
        ...current,
        templateRowId: clonedTemplate.id,
        asOfDate: clonedRun.restatementAsOfDate || todayIsoDate(),
        datasetSourceId: clonedRun.restatementDatasetSourceId ?? ""
      }));
    } else {
      if (!templates.some((template) => template.id === requestedTemplateId)) {
        return;
      }

      setDraft((current) => ({ ...current, templateRowId: requestedTemplateId }));
    }

    hydratedRouteContextRef.current = routeContextKey;
  }, [cloneRunId, clonedRun, clonedTemplate, requestedTemplateId, templates]);

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

  useEffect(() => {
    const fundProfileId = reporting?.selectedFundProfileId?.trim() || reporting?.fundProfileId?.trim();
    if (!fundProfileId) {
      return;
    }

    setStandardDraft((current) => current.fundProfileId
      ? current
      : { ...current, fundProfileId });
  }, [reporting?.fundProfileId, reporting?.selectedFundProfileId]);

  const advisoryReadinessGate = useMemo(
    () => buildReportRunReadinessGateViewState({
      reconciliationQueue: accounting?.reconciliationQueue ?? [],
      manualDrafts
    }),
    [accounting?.reconciliationQueue, manualDrafts]
  );

  const parameterValidation = useMemo(
    () => validateAndBuildReportingRunParameters(standardDraft, draft.asOfDate),
    [draft.asOfDate, standardDraft]
  );
  const readinessRequest = useMemo<ReportingRunRequest | null>(() => {
    if (
      !selectedTemplate
      || !selectedTemplate.canRunOnDemand
      || draft.restatementTargetRunId
      || !parameterValidation.parameters
    ) {
      return null;
    }

    return buildExportsReportRunRequest(selectedTemplate, draft, parameterValidation.parameters);
  }, [draft, parameterValidation.parameters, selectedTemplate]);
  const readinessRequestJson = useMemo(
    () => readinessRequest ? JSON.stringify(readinessRequest) : null,
    [readinessRequest]
  );

  useEffect(() => {
    if (!readinessRequestJson) {
      setReadinessPreflight(idleReadinessPreflight);
      return;
    }

    const controller = new AbortController();
    setReadinessPreflight({
      phase: "checking",
      requestKey: readinessRequestJson,
      readiness: null,
      error: null
    });

    assessReportingRunReadiness(JSON.parse(readinessRequestJson) as ReportingRunRequest, { signal: controller.signal })
      .then((readiness) => {
        if (!controller.signal.aborted) {
          setReadinessPreflight({
            phase: "complete",
            requestKey: readinessRequestJson,
            readiness,
            error: null
          });
        }
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        const description = describeApiError(error, "Server readiness could not be verified.");
        setReadinessPreflight({
          phase: "error",
          requestKey: readinessRequestJson,
          readiness: null,
          error: [description.summary, ...description.details].join(" ")
        });
      });

    return () => controller.abort();
  }, [readinessRequestJson]);

  const authoritativeReadinessGate = readinessPreflight.phase === "complete"
    ? buildAuthoritativeReadinessGateViewState(readinessPreflight.readiness, standardDraft.finality)
    : null;
  const runBlockedReason = draft.restatementTargetRunId
    ? "Restatements must use the governed restatement-request workflow."
    : parameterValidation.issues[0]
      ?? (readinessPreflight.phase === "checking"
        ? "Wait for the server readiness preflight to finish."
        : readinessPreflight.phase === "error"
          ? readinessPreflight.error
          : readinessPreflight.phase !== "complete"
            ? "Server readiness must be verified before this report can run."
            : authoritativeReadinessGate?.canRun
              ? null
              : authoritativeReadinessGate?.blockingReasons[0] ?? "The server blocked this report run.");

  const runningTemplateRunId = status?.state === "running" ? status.id : null;

  const onDraftChange = (field: ExportsReportRunDraftField, value: string) => {
    setDraft((current) => ({ ...current, [field]: value }));
    if (field === "templateRowId") {
      setSearchParams((current) => {
        const next = new URLSearchParams(current);
        next.delete("cloneRunId");
        if (value) {
          next.set("templateId", value);
        } else {
          next.delete("templateId");
        }
        return next;
      }, { replace: true });
    }
  };

  const onRestatementTargetChange = (target: RestatementTargetSelection | null) => {
    setDraft((current) => ({
      ...current,
      restatementTargetRunId: target?.runId ?? "",
      restatementTemplateId: target?.templateId ?? "",
      restatementJobId: target?.jobId ?? "",
      restatementAsOfDate: target?.asOfDate ?? "",
      restatementDatasetSourceId: target?.datasetSourceId ?? "",
      retryReason: target ? current.retryReason : ""
    }));
  };

  const onStandardDraftChange = (field: ReportRunParameterDraftField, value: string | boolean) => {
    setStandardDraft((current) => ({ ...current, [field]: value } as ReportRunParameterDraftState));
  };

  const onRun = async () => {
    if (runningTemplateRunId || runBlockedReason || !readinessRequest) {
      return;
    }

    // Restatement runs from the selected released run's identity, independent of the current
    // template selection; an ordinary run still needs a runnable template.
    const isRestating = draft.restatementTargetRunId.trim().length > 0;
    const identity = isRestating
      ? { id: draft.restatementTargetRunId, name: `Restatement of ${draft.restatementTargetRunId}` }
      : selectedTemplate && selectedTemplate.canRunOnDemand
        ? { id: selectedTemplate.id, name: selectedTemplate.name }
        : null;
    if (!identity) {
      return;
    }

    setStatus({
      id: identity.id,
      label: "Report run",
      state: "running",
      message: `${identity.name} is running.`,
      details: []
    });

    try {
      const result = await runReportingNow(readinessRequest);
      setStatus({
        id: identity.id,
        label: "Report run",
        state: "success",
        message: `${identity.name} run created.`,
        details: buildReportRunResultDetails(result.run),
        technicalDetails: {
          label: "Run reference",
          description: "Use this retained identifier when reviewing the run audit trail or requesting support.",
          items: [result.run.runId]
        }
      });
    } catch (error) {
      const description = describeApiError(error, `${identity.name} run failed.`);
      setStatus({
        id: identity.id,
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

  if (!requestedTemplateId) {
    const runnableTemplates = templates.filter((template) => template.canRunOnDemand);
    const recentRuns = runStatusRows.slice(0, 3);
    const primaryTemplate = runnableTemplates[0] ?? templates[0] ?? null;
    const runSetupActions = [
      {
        id: "breaks",
        label: "Review breaks",
        detail: `${advisoryReadinessGate.items.find((item) => item.id === "open-breaks")?.count ?? 0} open reconciliation break(s)`,
        href: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        tone: advisoryReadinessGate.isClear ? ("success" as const) : ("warning" as const)
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
          <Badge variant={advisoryReadinessGate.isClear ? "success" : "warning"}>
            {advisoryReadinessGate.isClear ? "Readiness clear" : "Review readiness"}
          </Badge>
        </CardHeader>
        <CardContent className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
          <div className="space-y-4">
            <section className="grid gap-2 md:grid-cols-3" aria-label="Report run setup scan band">
              <div className="border border-border bg-secondary/20 px-3 py-2">
                <div className="font-mono text-xs font-semibold text-muted-foreground">Templates</div>
                <div className="mt-1 text-lg font-semibold text-foreground">{templates.length}</div>
                <p className="text-xs text-muted-foreground">{runnableTemplates.length} ready for on-demand runs</p>
              </div>
              <div className="border border-border bg-secondary/20 px-3 py-2">
                <div className="font-mono text-xs font-semibold text-muted-foreground">Readiness</div>
                <div className="mt-1 text-lg font-semibold text-foreground">{advisoryReadinessGate.isClear ? "Clear" : "Review"}</div>
                <p className="text-xs text-muted-foreground">{advisoryReadinessGate.disclaimer}</p>
              </div>
              <div className="border border-border bg-secondary/20 px-3 py-2">
                <div className="font-mono text-xs font-semibold text-muted-foreground">Recent runs</div>
                <div className="mt-1 text-lg font-semibold text-foreground">{runStatusRows.length}</div>
                <p className="text-xs text-muted-foreground">Run history and exceptions stay visible before configuration.</p>
              </div>
            </section>

            {templates.length > 0 ? (
              <label className="block max-w-xl space-y-1">
                <span className="text-xs font-medium text-muted-foreground">Report template</span>
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
                      <span className="font-mono text-xs text-muted-foreground">{view.cadence}</span>
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
                    <p className="mt-1 text-xs leading-5 text-muted-foreground">{action.detail}</p>
                  </li>
                ))}
              </ul>
            </section>
            <section className="space-y-2">
              <h3 className="text-sm font-semibold text-foreground">Readiness cues</h3>
              <ul className="grid gap-2" aria-label="Report run readiness cues">
                {advisoryReadinessGate.items.map((item) => (
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
                      <div className="text-xs text-muted-foreground">
                        {presentReportingStatusLabel(run.status)} · {presentReportingAsOfDate(run.asOfDateLabel)}
                      </div>
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
          <CardDescription>
            No report template matching "{presentReportingIdentifier(requestedTemplateId.split(":", 1)[0], "Requested report")}" was found.
          </CardDescription>
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
            <FormRow label="Fund profile" labelFor="report-fund-profile">
              <Input
                id="report-fund-profile"
                value={standardDraft.fundProfileId}
                onChange={(event) => onStandardDraftChange("fundProfileId", event.target.value)}
                required
              />
            </FormRow>
            <FormRow label="Entity / fund / portfolio" labelFor="report-entity-scope">
              <Select
                id="report-entity-scope"
                value={standardDraft.entityScopeKind}
                onChange={(event) => onStandardDraftChange("entityScopeKind", event.target.value)}
              >
                <option value="AllEntities">All entities</option>
                <option value="Entity">Entity</option>
                <option value="Portfolio">Portfolio</option>
                <option value="Investor">Investor</option>
              </Select>
            </FormRow>
            {standardDraft.entityScopeKind === "Entity" ? (
              <FormRow label="Entity ID" labelFor="report-entity-id">
                <Input id="report-entity-id" value={standardDraft.entityId} onChange={(event) => onStandardDraftChange("entityId", event.target.value)} required />
              </FormRow>
            ) : null}
            {standardDraft.entityScopeKind === "Portfolio" ? (
              <FormRow label="Portfolio ID" labelFor="report-portfolio-id">
                <Input id="report-portfolio-id" value={standardDraft.portfolioId} onChange={(event) => onStandardDraftChange("portfolioId", event.target.value)} required />
              </FormRow>
            ) : null}
            {standardDraft.entityScopeKind === "Investor" ? (
              <FormRow label="Investor ID" labelFor="report-investor-id">
                <Input id="report-investor-id" value={standardDraft.investorId} onChange={(event) => onStandardDraftChange("investorId", event.target.value)} required />
              </FormRow>
            ) : null}
            <FormRow label="Accounting period ID" labelFor="report-period">
              <Input
                id="report-period"
                value={standardDraft.periodId}
                onChange={(event) => onStandardDraftChange("periodId", event.target.value)}
                placeholder="2026-06"
                required
              />
            </FormRow>
            <FormRow label="Ledger book code" labelFor="report-ledger-book">
              <Input
                id="report-ledger-book"
                value={standardDraft.ledgerBookCode}
                onChange={(event) => onStandardDraftChange("ledgerBookCode", event.target.value)}
              />
            </FormRow>
            <FormRow label="Ledger book ID (optional)" labelFor="report-ledger-book-id">
              <Input
                id="report-ledger-book-id"
                value={standardDraft.ledgerBookId}
                onChange={(event) => onStandardDraftChange("ledgerBookId", event.target.value)}
                placeholder="Server ledger book UUID"
              />
            </FormRow>
            <FormRow label="Accounting basis" labelFor="report-accounting-basis">
              <Select
                id="report-accounting-basis"
                value={standardDraft.accountingBasis}
                onChange={(event) => onStandardDraftChange("accountingBasis", event.target.value)}
              >
                <option value="Gaap">GAAP</option>
                <option value="Tax">Tax</option>
                <option value="Management">Management</option>
                <option value="Cash">Cash</option>
                <option value="Statutory">Statutory</option>
              </Select>
            </FormRow>
            <FormRow label="Presentation currency" labelFor="report-currency">
              <Input
                id="report-currency"
                value={standardDraft.presentationCurrency}
                onChange={(event) => onStandardDraftChange("presentationCurrency", event.target.value)}
                maxLength={3}
                required
              />
            </FormRow>
            <FormRow label="Consolidation level" labelFor="report-consolidation-level">
              <Select
                id="report-consolidation-level"
                value={standardDraft.consolidationLevel}
                onChange={(event) => onStandardDraftChange("consolidationLevel", event.target.value)}
              >
                <option value="Fund">Fund</option>
                <option value="Entity">Entity</option>
                <option value="Portfolio">Portfolio</option>
                <option value="Investor">Investor</option>
              </Select>
            </FormRow>
            <FormRow label="Output format" labelFor="report-output-format">
              <Select
                id="report-output-format"
                value={standardDraft.outputFormat}
                onChange={(event) => onStandardDraftChange("outputFormat", event.target.value)}
              >
                <option value="Pdf">PDF</option>
                <option value="Xlsx">XLSX</option>
                <option value="Csv">CSV</option>
                <option value="EvidenceVault">Evidence Vault</option>
              </Select>
            </FormRow>
            <FormRow label="Draft vs final" labelFor="report-finality">
              <Select
                id="report-finality"
                value={standardDraft.finality}
                onChange={(event) => onStandardDraftChange("finality", event.target.value)}
              >
                <option value="Draft">Draft</option>
                <option value="Final">Final</option>
              </Select>
            </FormRow>
            <div className="flex flex-wrap gap-4 md:col-span-2">
              <Checkbox
                label="Include supporting schedules"
                checked={standardDraft.includeSupportingSchedules}
                onCheckedChange={(checked) => onStandardDraftChange("includeSupportingSchedules", checked)}
              />
              <Checkbox
                label="Include evidence appendix"
                checked={standardDraft.includeEvidenceAppendix}
                onCheckedChange={(checked) => onStandardDraftChange("includeEvidenceAppendix", checked)}
              />
            </div>
            <FormRow label="Template parameters (JSON)" labelFor="report-template-parameters" className="md:col-span-2">
              <textarea
                id="report-template-parameters"
                className="min-h-24 w-full rounded-sm border border-input bg-background px-3 py-2 font-mono text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                value={standardDraft.templateParametersJson}
                onChange={(event) => onStandardDraftChange("templateParametersJson", event.target.value)}
                aria-describedby="report-template-parameters-help"
              />
              <span id="report-template-parameters-help" className="text-xs text-muted-foreground">
                Supply required template values as a JSON object, for example {`{"reportingRegion":"US"}`}.
              </span>
            </FormRow>
          </CardContent>
        </Card>

        <Card className="panel-surface" aria-live="polite">
          <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <CardTitle>Can this report run?</CardTitle>
              <CardDescription>
                {readinessPreflight.phase === "checking"
                  ? "The server is resolving the exact template, scope, accounting dependencies, dataset, and evidence posture."
                  : readinessPreflight.phase === "complete"
                    ? authoritativeReadinessGate?.summary
                    : readinessPreflight.phase === "error"
                      ? readinessPreflight.error
                      : "Complete the required parameters to start the server-owned readiness preflight."}
              </CardDescription>
            </div>
            <Badge variant={authoritativeReadinessGate?.canRun ? "success" : readinessPreflight.phase === "error" ? "danger" : "warning"}>
              {readinessPreflight.phase === "checking"
                ? "Checking"
                : readinessPreflight.phase === "complete"
                  ? authoritativeReadinessGate?.statusLabel
                  : readinessPreflight.phase === "error"
                    ? "Unavailable"
                    : "Parameters required"}
            </Badge>
          </CardHeader>
          <CardContent>
            <ul className="grid gap-2" aria-label="Report run readiness checks">
              {parameterValidation.issues.map((issue) => (
                <li key={issue} className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                  {issue}
                </li>
              ))}
              {readinessPreflight.phase === "checking" ? (
                <li className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2 text-sm text-foreground">
                  Server readiness preflight in progress.
                </li>
              ) : null}
              {readinessPreflight.phase === "complete" ? readinessPreflight.readiness.checks.map((check) => (
                <li key={check.checkId} className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
                  <div className="flex items-start justify-between gap-2">
                    <span>
                      <span className="block text-sm font-medium text-foreground">{check.label}</span>
                      <span className="mt-1 block text-xs leading-5 text-muted-foreground">{check.summary}</span>
                    </span>
                    <Badge variant={check.status === "Ready" ? "success" : check.status === "Blocked" || check.status === "Unavailable" ? "danger" : "warning"}>
                      {check.status}
                    </Badge>
                  </div>
                  {check.route ? (
                    <a className="mt-1 inline-block text-xs text-primary underline-offset-2 hover:underline" href={check.route}>
                      Resolve in workstation
                    </a>
                  ) : null}
                </li>
              )) : null}
            </ul>
            {readinessPreflight.phase === "complete" ? (
              <details className="mt-3 rounded-sm border border-border/70 bg-background/40 px-3 py-2">
                <summary className="cursor-pointer text-xs font-medium text-foreground">Readiness evidence</summary>
                <dl className="mt-2 grid gap-1 text-xs text-muted-foreground">
                  <div><dt className="inline font-medium">Template: </dt><dd className="inline font-mono">{readinessPreflight.readiness.resolvedTemplate.name}@v{readinessPreflight.readiness.resolvedTemplate.version}</dd></div>
                  <div><dt className="inline font-medium">Evaluation: </dt><dd className="inline font-mono">{readinessPreflight.readiness.evaluationId}</dd></div>
                  <div><dt className="inline font-medium">Evidence hash: </dt><dd className="inline break-all font-mono">{readinessPreflight.readiness.evidenceHash}</dd></div>
                </dl>
              </details>
            ) : null}
          </CardContent>
        </Card>
      </section>

      <ExportsReportRunner
        context="run"
        draft={draft}
        templates={templates}
        selectedTemplate={selectedTemplate}
        datasetSources={reporting?.reportWriterDatasetSources ?? []}
        recentRuns={runStatusRows}
        status={status}
        runningTemplateRunId={runningTemplateRunId}
        runBlockedReason={runBlockedReason}
        defaultRequester="browser-user"
        onDraftChange={onDraftChange}
        onRestatementTargetChange={onRestatementTargetChange}
        onRun={() => void onRun()}
      />

      {status ? <ReportingCommandStatusView status={status} /> : null}

      <Link className="text-xs text-primary underline-offset-2 hover:underline" to={WORKSTATION_ROUTE_CATALOG.reportingLibrary}>
        Back to Report Library
      </Link>
    </div>
  );
}
