import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { formatCurrency } from "@/lib/format";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { OperationalTrustSummary } from "@/components/meridian/operational-trust-summary";
import { getOperationsCloseCalendar, getRunLedgerJournal, getRunTrialBalance } from "@/lib/api";
import { evidenceWorkbenchPath, WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import { financeBreakLabel } from "@/screens/accounting-screen.reconciliation.view-model";
import { formatDateTimeLabel } from "@/screens/accounting-screen.formatting";
import {
  buildTemplateRows,
  hasRetainedReportingAsOfDate,
  presentReportingAsOfDate,
  presentReportingIdentifier,
  presentReportingStatusLabel
} from "@/screens/reporting-screen.view-model";
import type { AccountingWorkspaceResponse, LedgerJournalLine, LedgerTrialBalanceLine, OperationsCloseCalendar } from "@/types";

interface FinanceStandardScreenProps {
  data: AccountingWorkspaceResponse | null;
}

type UnknownRecord = Record<string, unknown>;

const financeReportCategories = [
  "Financial Statements",
  "Investor Reporting",
  "Reconciliation",
  "Operations",
  "Exceptions",
  "Tax",
  "Audit",
  "Custom"
];

const reportParameterFields = [
  "Entity / fund / portfolio",
  "Period or as-of date",
  "Ledger book",
  "Accounting basis",
  "Currency",
  "Consolidation level",
  "Output format",
  "Draft vs final",
  "Supporting schedules",
  "Evidence appendix"
];

export function ReportPreviewValidationScreen({ data }: FinanceStandardScreenProps) {
  const [searchParams] = useSearchParams();
  const reporting = asRecord(data?.reporting);
  const requestedRunId = searchParams.get("runId") ?? "";
  const runs = readRecordArray(reporting, "recentRuns");
  const run = requestedRunId
    ? runs.find((candidate) => readString(candidate, "runId", "") === requestedRunId) ?? null
    : runs[0] ?? null;
  const templates = buildTemplateRows(data?.reporting.templates ?? []);
  const runTemplateId = readString(run, "templateId", "");
  const selectedTemplate = runTemplateId
    ? templates.find((candidate) => candidate.templateName === runTemplateId || candidate.id === runTemplateId) ?? null
    : requestedRunId
      ? null
      : templates[0] ?? null;
  const hasPreviewSubject = Boolean(run || selectedTemplate);
  const reportName = readString(run, "reportName", selectedTemplate?.name ?? (runTemplateId || "No report selected"));
  const runId = readString(run, "runId", "No run selected");
  const templateId = selectedTemplate?.id ?? "";
  const runStatus = readString(run, "status", "Draft");
  const validationWarnings = readStringArray(run, "validationWarnings");
  const generatedFiles = readStringArray(run, "generatedFiles");
  const retainedArtifacts = readStringArray(run, "artifacts");
  const retainedOutputReferences = generatedFiles.length > 0 ? generatedFiles : retainedArtifacts;
  const selectedTemplateMetadata = runTemplateId
    ? data?.reporting.templates.find((candidate) => candidate.templateId === runTemplateId) ?? null
    : selectedTemplate
      ? data?.reporting.templates.find((candidate) => candidate.templateId === selectedTemplate.templateName) ?? null
      : null;
  const previewSections = (selectedTemplateMetadata?.sections ?? []).map((section) => presentReportingIdentifier(section, "Report section"));
  const sectionCount = readNumber(run, "sectionCount", previewSections.length);
  const lineageLinkedSections = readNumber(run, "lineageLinkedSections", 0);
  const retainedAsOfDate = readString(run, "asOfDate", "");
  const isAwaitingApproval = normalizeStatus(runStatus).includes("awaitingapproval")
    || normalizeStatus(runStatus).includes("pendingapproval");
  const canRun = selectedTemplate?.canRunOnDemand === true
    && validationWarnings.length === 0
    && isRunnablePreviewStatus(runStatus);
  const previewStatus = !hasPreviewSubject
    ? "No report selected"
    : validationWarnings.length > 0
      ? "Validation review"
      : !selectedTemplate
        ? "Template unavailable"
        : !selectedTemplate.canRunOnDemand
          ? "Template review"
          : canRun
            ? "Ready to run"
            : presentStatusLabel(runStatus);

  return (
    <div className="space-y-4">
      <OperationalTrustSummary
        source={{ value: run ? "Retained report run" : selectedTemplate ? "Governed template" : "No report selected", tone: hasPreviewSubject ? "ready" : "unknown" }}
        scope={{ value: reportName, detail: run ? presentStatusLabel(runStatus) : "Template preview", tone: hasPreviewSubject ? "ready" : "unknown" }}
        freshness={{
          value: run ? presentReportingAsOfDate(retainedAsOfDate) : "Preview not generated",
          detail: run && !hasRetainedReportingAsOfDate(retainedAsOfDate) ? "Confirm the reporting period before approval." : undefined,
          tone: run ? hasRetainedReportingAsOfDate(retainedAsOfDate) ? "ready" : "review" : selectedTemplate ? "review" : "unknown"
        }}
        completeness={{
          value: `${sectionCount} sections · ${retainedOutputReferences.length} retained references`,
          detail: `${validationWarnings.length} validation warnings`,
          tone: validationWarnings.length > 0 || isAwaitingApproval || sectionCount === 0 ? "review" : hasPreviewSubject ? "ready" : "unknown"
        }}
        blocker={validationWarnings.length > 0 ? { value: "Validation issues", detail: "Resolve the listed issues before running the report.", tone: "blocked" } : undefined}
        label="Report preview confidence"
      />
      <Card className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle>Report Preview &amp; Validation</CardTitle>
            <CardDescription>
              Review included rows, blockers, evidence, approvals, and output before a Finance report is released.
            </CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={!hasPreviewSubject ? "outline" : validationWarnings.length > 0 ? "warning" : canRun ? "success" : "outline"}>
              {previewStatus}
            </Badge>
            {!hasPreviewSubject ? (
              <Button asChild size="sm" variant="outline">
                <Link to={WORKSTATION_ROUTE_CATALOG.reportingLibrary}>Choose report</Link>
              </Button>
            ) : validationWarnings.length > 0 ? (
              <Button type="button" size="sm" disabled disabledReason="Resolve validation issues before running this report.">
                Run report
              </Button>
            ) : run && isAwaitingApproval ? (
              <Button asChild size="sm">
                <Link to={workstationRouteWithQuery("reportingRunStatus", { runId })}>Review approval</Link>
              </Button>
            ) : !selectedTemplate ? (
              <Button asChild size="sm" variant="outline">
                <Link to={WORKSTATION_ROUTE_CATALOG.reportingLibrary}>Review templates</Link>
              </Button>
            ) : !selectedTemplate.canRunOnDemand ? (
              <Button asChild size="sm" variant="outline">
                <Link to={workstationRouteWithQuery("reportingGovernance", { templateId: selectedTemplate.id })}>
                  Review template
                </Link>
              </Button>
            ) : canRun ? (
              <Button asChild size="sm">
                <Link to={workstationRouteWithQuery("reportingRunParameters", { templateId })}>
                  Run report
                </Link>
              </Button>
            ) : (
              <Button asChild size="sm" variant="outline">
                <Link to={workstationRouteWithQuery("reportingRunDetail", { runId })}>Open run detail</Link>
              </Button>
            )}
          </div>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Report" value={reportName} />
          <FinanceFact
            label="Output"
            value={`${sectionCount} section${sectionCount === 1 ? "" : "s"} · ${retainedOutputReferences.length} retained reference${retainedOutputReferences.length === 1 ? "" : "s"}`}
          />
          <FinanceFact
            label="Validation"
            value={validationWarnings.length > 0 ? validationWarnings.join(", ") : "No blocking issues"}
          />
          {run ? (
            <TechnicalDetails label="Run audit details" className="md:col-span-3">
              <FinanceFact label="Run ID" value={runId} mono />
            </TechnicalDetails>
          ) : null}
        </CardContent>
      </Card>

      <Tabs
        tabs={[
          { id: "preview", label: "Preview" },
          { id: "validation", label: "Validation Issues" },
          { id: "changes", label: "Changes Since Prior Run" },
          { id: "evidence", label: "Evidence" },
          { id: "approval", label: "Approval" },
          { id: "output", label: "Output" }
        ]}
      >
        <TabPanel>
          <FinanceList
            title="What will be included"
            items={buildReportPreviewItems({
              sections: previewSections,
              sectionCount,
              lineageLinkedSections,
              retainedReferenceCount: retainedOutputReferences.length
            })}
          />
        </TabPanel>
        <TabPanel>
          <FinanceList
            title="Rows blocked before output"
            items={validationWarnings.length > 0 ? validationWarnings : [
              "No open validation issues are blocking the selected report preview."
            ]}
          />
        </TabPanel>
        <TabPanel>
          <FinanceList
            title="Changes since prior run"
            items={[
              "Parameter changes, retained dataset revisions, and report-writer grid changes are reviewed here.",
              "Use Report Run Detail to compare the run audit trail and generated files."
            ]}
          />
        </TabPanel>
        <TabPanel>
          <FinanceList
            title="Evidence checkpoint"
            items={buildReportOutputItems(retainedArtifacts, "No retained evidence references are available for this preview.")}
          />
        </TabPanel>
        <TabPanel>
          <FinanceList
            title="Approval checkpoint"
            items={[
              `Current state: ${presentReportingStatusLabel(runStatus)}.`,
              isAwaitingApproval
                ? "This generated report is ready for approval review before release."
                : "Report release approval is required for final output."
            ]}
          />
        </TabPanel>
        <TabPanel>
          <FinanceList
            title="Downstream distribution"
            items={buildReportOutputItems(
              retainedOutputReferences,
              "No retained output is available yet; generate the report before distribution."
            )}
          />
        </TabPanel>
      </Tabs>
    </div>
  );
}

export function ReportRunDetailScreen({ data }: FinanceStandardScreenProps) {
  const [searchParams] = useSearchParams();
  const reporting = asRecord(data?.reporting);
  const requestedRunId = searchParams.get("runId") ?? "";
  const runs = readRecordArray(reporting, "recentRuns");
  const run = runs.find((candidate) => readString(candidate, "runId", "") === requestedRunId) ?? runs[0] ?? null;
  const runId = readString(run, "runId", requestedRunId || "No run selected");
  const runTemplateId = readString(run, "templateId", "");
  const retainedTemplateName = data?.reporting.templates.find((candidate) => candidate.templateId === runTemplateId)?.name;
  const reportName = readString(
    run,
    "reportName",
    readString(run, "templateName", retainedTemplateName ?? presentReportingIdentifier(runTemplateId, "Report run"))
  );
  const status = readString(run, "status", "Draft");
  const generatedFiles = readStringArray(run, "generatedFiles").length > 0
    ? readStringArray(run, "generatedFiles")
    : readStringArray(run, "artifacts");
  const recipients = readStringArray(run, "distributionRecipients");
  const started = resolveStartedLabel(run, status);
  const completed = resolveCompletedLabel(run, status);
  const parameters = buildRetainedParameterItems(run);
  const auditTrail = readStringArray(run, "auditActions").map(presentStatusLabel);
  const hasSelectedRun = run !== null && runId !== "No run selected";
  const validationWarnings = readStringArray(run, "validationWarnings");
  const isAwaitingApproval = status.toLowerCase().replace(/[^a-z]/g, "").includes("awaitingapproval");
  const retainedAsOfDate = readString(run, "asOfDate", "");

  return (
    <div className="space-y-4">
      <OperationalTrustSummary
        source={{ value: hasSelectedRun ? "Retained report run" : "No run selected", tone: hasSelectedRun ? "ready" : "unknown" }}
        scope={{ value: reportName, detail: presentRunPhase(status), tone: hasSelectedRun ? "ready" : "unknown" }}
        freshness={{
          value: presentReportingAsOfDate(retainedAsOfDate),
          detail: hasRetainedReportingAsOfDate(retainedAsOfDate) ? completed : "Confirm the reporting period before approval.",
          tone: hasSelectedRun && hasRetainedReportingAsOfDate(retainedAsOfDate) ? "ready" : hasSelectedRun ? "review" : "unknown"
        }}
        completeness={{
          value: `${generatedFiles.length} outputs · ${validationWarnings.length} warnings`,
          tone: validationWarnings.length > 0 || isAwaitingApproval || generatedFiles.length === 0 ? "review" : hasSelectedRun ? "ready" : "unknown"
        }}
        blocker={status.toLowerCase().includes("fail") ? { value: "Run failed", detail: "Review the audit trail before retrying.", tone: "blocked" } : undefined}
        label="Report run confidence"
      />
      <Card className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle>Report Run Detail</CardTitle>
            <CardDescription>{reportName}</CardDescription>
          </div>
          <Badge variant={reportRunStatusVariant(status)}>{presentStatusLabel(status)}</Badge>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Workflow phase" value={presentRunPhase(status)} />
          <FinanceFact label="Completed" value={completed} mono />
          <FinanceFact label="Warnings" value={String(validationWarnings.length)} />
          <TechnicalDetails label="Run audit details" className="md:col-span-3">
            <dl className="grid gap-3 md:grid-cols-2">
              <FinanceFact label="Run ID" value={runId} mono />
              <FinanceFact label="Actor" value={readString(run, "actor", readString(run, "requestedBy", "browser-user"))} />
              <FinanceFact label="Started" value={started} mono />
              <FinanceFact label="Input datasets" value={readStringArray(run, "inputDatasets").join(", ") || "Retained reporting datasets"} />
            </dl>
          </TechnicalDetails>
        </CardContent>
      </Card>

      <section className="grid gap-4 xl:grid-cols-2">
        <FinanceList title="Parameters used" items={parameters} />
        <TechnicalDetails label="Generated artifact references">
          <FinanceList title="Generated files" items={generatedFiles.length > 0 ? generatedFiles : ["No generated files have been retained for this run yet."]} />
        </TechnicalDetails>
        <FinanceList title="Distribution recipients" items={recipients.length > 0 ? recipients : ["No downstream recipients have been released yet."]} />
        <TechnicalDetails label="Run audit trail">
          <FinanceList
            title="Audit trail"
            items={auditTrail.length > 0 ? auditTrail : [`Current workflow state: ${presentStatusLabel(status)}.`]}
          />
        </TechnicalDetails>
      </section>

      <div className="flex flex-wrap gap-2">
        {hasSelectedRun && isAwaitingApproval ? (
          <Button asChild size="sm">
            <Link to={workstationRouteWithQuery("reportingRunStatus", { runId })}>Review approval</Link>
          </Button>
        ) : null}
        {hasSelectedRun ? (
          <Button asChild size="sm" variant="outline">
            <Link to={workstationRouteWithQuery("reportingRunParameters", { cloneRunId: runId })}>Clone parameters</Link>
          </Button>
        ) : null}
        {hasSelectedRun && !status.toLowerCase().includes("fail") ? (
          <Button asChild size="sm" variant="outline">
            <Link to={workstationRouteWithQuery("reportingPreviewValidation", { runId })}>Open preview</Link>
          </Button>
        ) : null}
      </div>
    </div>
  );
}

export function AccountDetailScreen({ data }: FinanceStandardScreenProps) {
  const [searchParams] = useSearchParams();
  const accountId = searchParams.get("accountId") ?? "";
  const requestedRunId = searchParams.get("runId") ?? data?.reconciliationQueue?.[0]?.runId ?? "";
  const [trialRows, setTrialRows] = useState<LedgerTrialBalanceLine[]>([]);
  const [loading, setLoading] = useState(Boolean(requestedRunId));
  const [errorText, setErrorText] = useState<string | null>(null);

  useEffect(() => {
    if (!requestedRunId) {
      setTrialRows([]);
      setLoading(false);
      setErrorText(null);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setErrorText(null);
    getRunTrialBalance(requestedRunId)
      .then((rows) => {
        if (!cancelled) {
          setTrialRows(rows);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setTrialRows([]);
          setErrorText("The retained trial balance could not be loaded. Account balances and lineage are hidden until the source responds.");
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [requestedRunId]);

  const normalizedAccountId = accountId.trim().toLowerCase();
  const account = trialRows.find((row) => [
    row.financialAccountId,
    row.accountScopeId,
    row.symbol,
    row.accountName
  ].some((value) => value?.toLowerCase() === normalizedAccountId)) ?? (!accountId ? trialRows[0] ?? null : null);
  const accountName = account?.accountName ?? (accountId ? "Account not found" : "No account selected");
  const hasAccountingBasis = Boolean(account?.accountingBasis?.trim());

  return (
    <div className="space-y-4">
      <OperationalTrustSummary
        source={{ value: requestedRunId ? "Trial balance" : "No run selected", tone: requestedRunId ? "ready" : "unknown" }}
        scope={{ value: requestedRunId ? "Selected ledger run" : "Select a run", detail: account ? account.accountName : accountId ? "Selected account reference" : "No account selected", tone: requestedRunId ? "ready" : "unknown" }}
        freshness={{
          value: loading ? "Loading" : errorText ? "Unavailable" : requestedRunId ? "Current response" : "No response loaded",
          tone: loading ? "review" : errorText ? "blocked" : requestedRunId ? "ready" : "unknown"
        }}
        completeness={{
          value: account ? (hasAccountingBasis ? "Account located" : "Accounting basis missing") : "No account record",
          tone: account ? (hasAccountingBasis ? "ready" : "review") : loading ? "review" : "blocked"
        }}
        blocker={errorText ? { value: "Trial balance unavailable", detail: errorText, tone: "blocked" } : undefined}
      />
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Account Detail</CardTitle>
          <CardDescription>{accountName}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {loading ? <StatusBanner role="status" tone="info" title="Loading account" detail="Loading the retained trial-balance row and its posting lineage." /> : null}
          {errorText ? <StatusBanner role="alert" tone="danger" title="Account data unavailable" detail={errorText} /> : null}
          {!loading && !errorText && !account ? (
            <StatusBanner role="status" tone="warning" title="Account not found" detail="Choose an account from Trial Balance so Meridian can retain the run and account scope." />
          ) : null}
          {account ? (
            <>
              <div className="grid gap-3 md:grid-cols-3">
                <FinanceFact label="Account" value={account.accountName} />
                <FinanceFact label="Account type" value={account.accountType} />
                <FinanceFact label="Ending balance" value={formatCurrency(account.balance)} mono />
                <FinanceFact label="Journal entries" value={String(account.entryCount)} />
                <FinanceFact label="Entity" value={account.entityScopeDisplayName ?? "All entities"} />
                <FinanceFact label="Accounting basis" value={account.accountingBasis ?? "Not supplied"} />
              </div>
              <TechnicalDetails label="Audit details">
                <dl className="grid gap-2 md:grid-cols-2">
                  <FinanceFact label="Run ID" value={requestedRunId} mono />
                  <FinanceFact label="Financial account ID" value={account.financialAccountId ?? "Not supplied"} mono />
                  <FinanceFact label="Source journal entry" value={account.sourceJournalEntryId ?? "Not supplied"} mono />
                  <FinanceFact label="Approval IDs" value={account.approvalIds?.join(", ") || "None supplied"} mono />
                  <FinanceFact label="Policy" value={account.accountingPolicyId ?? "Not supplied"} mono />
                  <FinanceFact label="Rule" value={account.ruleId ?? "Not supplied"} mono />
                </dl>
              </TechnicalDetails>
            </>
          ) : null}
        </CardContent>
      </Card>
      <div className="flex flex-wrap gap-2">
        <Button asChild size="sm" variant="outline">
          <Link to={workstationRouteWithQuery("accountingTrialBalance", { runId: requestedRunId || null })}>Back to Trial Balance</Link>
        </Button>
        <Button asChild size="sm" variant="outline">
          <Link to={workstationRouteWithQuery("accountingLedger", { runId: requestedRunId || null })}>Open ledger activity</Link>
        </Button>
        {account?.sourceJournalEntryId ? (
          <Button asChild size="sm" variant="outline">
            <Link to={workstationRouteWithQuery("accountingJournalEntryDetail", { journalEntryId: account.sourceJournalEntryId, runId: requestedRunId || null })}>Open source journal entry</Link>
          </Button>
        ) : null}
      </div>
    </div>
  );
}

export function LedgerExplorerScreen({ data }: FinanceStandardScreenProps) {
  const [searchParams, setSearchParams] = useSearchParams();
  const runs = readRecordArray(asRecord(data), "reconciliationQueue");
  const selectedRunId = searchParams.get("runId") ?? readString(runs[0] ?? null, "runId", "");
  const [searchText, setSearchText] = useState("");
  const [savedView, setSavedView] = useState("Selected run");
  const [statusFilter, setStatusFilter] = useState("All statuses");
  const [sourceTypeFilter, setSourceTypeFilter] = useState("All source types");
  const [journalLines, setJournalLines] = useState<LedgerJournalLine[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!selectedRunId) {
      setJournalLines([]);
      return;
    }

    let cancelled = false;
    setLoading(true);
    getRunLedgerJournal(selectedRunId)
      .then((lines) => {
        if (!cancelled) {
          setJournalLines(lines);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setJournalLines([]);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [selectedRunId]);

  const filteredRows = useMemo(() => {
    const needle = searchText.trim().toLowerCase();
    if (!needle) {
      return journalLines;
    }
    return journalLines.filter((line) => [
      line.journalEntryId,
      line.description,
      line.accountScopeDisplayName,
      line.entityScopeDisplayName,
      line.dimensions?.instrumentId,
      String(line.totalDebits),
      String(line.totalCredits)
    ].some((value) => String(value ?? "").toLowerCase().includes(needle)));
  }, [journalLines, searchText]);

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Ledger Explorer</CardTitle>
          <CardDescription>Search journal activity by account, amount, journal ID, source, security, or entity.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 lg:grid-cols-2 xl:grid-cols-[minmax(0,1.25fr)_minmax(15rem,0.7fr)_minmax(9rem,0.45fr)_minmax(10rem,0.5fr)]">
          <FormRow label="Search by account, amount, journal ID, source, security, entity" labelFor="ledger-search">
            <Input
              id="ledger-search"
              type="search"
              value={searchText}
              onChange={(event) => setSearchText(event.target.value)}
              placeholder="Cash, $120,500, AAPL, cash sweep"
            />
          </FormRow>
          <FormRow label="Run / period" labelFor="ledger-run-select">
            <Select
              id="ledger-run-select"
              value={selectedRunId}
              onChange={(event) => setSearchParams({ runId: event.target.value })}
            >
              {runs.length > 0 ? runs.map((run) => (
                <option key={readString(run, "runId", "run")} value={readString(run, "runId", "")}>
                  {readString(run, "strategyName", readString(run, "runId", "Ledger run"))}
                </option>
              )) : <option value="">No run available</option>}
            </Select>
          </FormRow>
          <FormRow label="Status" labelFor="ledger-status-filter">
            <Select id="ledger-status-filter" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
              <option>All statuses</option>
              <option>Posted</option>
              <option>Unposted</option>
              <option>Reversed</option>
            </Select>
          </FormRow>
          <FormRow label="Source type" labelFor="ledger-source-filter">
            <Select id="ledger-source-filter" value={sourceTypeFilter} onChange={(event) => setSourceTypeFilter(event.target.value)}>
              <option>All source types</option>
              <option>Manual JEs</option>
              <option>System Generated</option>
              <option>Reversals</option>
            </Select>
          </FormRow>
        </CardContent>
      </Card>

      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Saved views</CardTitle>
          <CardDescription>Use standard accounting cuts before drilling into Journal Entry Detail.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex flex-wrap gap-2" role="group" aria-label="Saved ledger views">
            {["Selected run", "Unposted", "Reversals", "Manual JEs", "System Generated"].map((view) => (
              <Button
                key={view}
                type="button"
                size="sm"
                variant={savedView === view ? "default" : "outline"}
                aria-pressed={savedView === view}
                onClick={() => setSavedView(view)}
              >
                {view}
              </Button>
            ))}
          </div>
        </CardContent>
      </Card>

      <Card className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle>Ledger search results</CardTitle>
            <CardDescription>{loading ? "Loading ledger rows." : `${filteredRows.length} row(s) for ${savedView}.`}</CardDescription>
          </div>
          <Badge variant={filteredRows.length > 0 ? "success" : "outline"}>{filteredRows.length}</Badge>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto rounded-md border border-border/70">
            <table className="min-w-full text-sm" aria-label="Ledger Explorer results">
              <thead className="bg-secondary/30 text-left text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
                <tr>
                  <th className="px-3 py-2" scope="col">Date</th>
                  <th className="px-3 py-2" scope="col">Journal ID</th>
                  <th className="px-3 py-2" scope="col">Account</th>
                  <th className="px-3 py-2 text-right" scope="col">Debit</th>
                  <th className="px-3 py-2 text-right" scope="col">Credit</th>
                  <th className="px-3 py-2" scope="col">Entity</th>
                  <th className="px-3 py-2" scope="col">Source</th>
                  <th className="px-3 py-2" scope="col">Status</th>
                  <th className="px-3 py-2" scope="col">Evidence status</th>
                </tr>
              </thead>
              <tbody>
                {filteredRows.length > 0 ? filteredRows.map((line) => (
                  <tr key={line.journalEntryId} className="border-t border-border/70">
                    <td className="px-3 py-2 text-xs">{formatDateTimeLabel(line.timestamp)}</td>
                    <td className="px-3 py-2">
                      <Link
                        className="font-semibold text-primary underline-offset-2 hover:underline"
                        to={workstationRouteWithQuery("accountingJournalEntryDetail", {
                          journalEntryId: line.journalEntryId,
                          runId: selectedRunId || null
                        })}
                      >
                        Open journal detail
                      </Link>
                    </td>
                    <td className="px-3 py-2">{line.accountScopeDisplayName ?? "Multiple accounts"}</td>
                    <td className="px-3 py-2 text-right font-mono">{formatCurrency(line.totalDebits)}</td>
                    <td className="px-3 py-2 text-right font-mono">{formatCurrency(line.totalCredits)}</td>
                    <td className="px-3 py-2">{line.entityScopeDisplayName ?? "All entities"}</td>
                    <td className="px-3 py-2">{line.description || "Ledger posting"}</td>
                    <td className="px-3 py-2">Posted</td>
                    <td className="px-3 py-2">{line.lineCount > 0 ? "Linked" : "Needs evidence"}</td>
                  </tr>
                )) : (
                  <tr>
                    <td className="px-3 py-4 text-muted-foreground" colSpan={9}>
                      No ledger rows match the current search and filters.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
          {filteredRows.length > 0 ? (
            <TechnicalDetails label="Journal references" className="mt-3">
              <ul className="space-y-1 text-xs text-muted-foreground">
                {filteredRows.map((line) => (
                  <li key={line.journalEntryId} className="break-all font-mono">{line.journalEntryId}</li>
                ))}
              </ul>
            </TechnicalDetails>
          ) : null}
        </CardContent>
      </Card>
    </div>
  );
}

export function ReconciliationMatchWorkbenchScreen({ data }: FinanceStandardScreenProps) {
  const breaks = data?.breakQueue ?? [];
  const selectedBreak = breaks[0] ?? null;
  const breakLabel = selectedBreak ? financeBreakLabel(selectedBreak.category) : "No open break selected";
  const freshness = buildFinanceFreshness(selectedBreak?.lastUpdatedAt);

  return (
    <div className="space-y-4">
      <OperationalTrustSummary
        source={{ value: "Reconciliation queue", tone: data ? "ready" : "unknown" }}
        scope={{ value: selectedBreak ? "Selected case" : "No case selected", detail: selectedBreak?.strategyName, tone: selectedBreak ? "ready" : "unknown" }}
        freshness={freshness}
        completeness={{ value: selectedBreak ? "Case loaded" : "No case data", tone: selectedBreak ? "review" : "unknown" }}
        blocker={selectedBreak ? { value: "Open cash variance", detail: selectedBreak.reason, tone: "review" } : undefined}
      />
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Reconciliation Match Workbench</CardTitle>
          <CardDescription>Clear breaks by matching source records to ledger records with suggested adjustments.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Open breaks" value={String(breaks.length)} />
          <FinanceFact label="Selected break" value={breakLabel} />
          <FinanceFact label="Status" value={selectedBreak ? presentStatusLabel(selectedBreak.status) : "No case"} />
          <FinanceFact label="Variance" value={selectedBreak ? formatCurrency(selectedBreak.variance) : "Not supplied"} mono />
          <FinanceFact label="Owner" value={selectedBreak?.assigneeDisplayName ?? selectedBreak?.assignedTo ?? "Unassigned"} />
          <FinanceFact label="Next action" value={selectedBreak?.recommendedAction ?? "Review the governed case before taking action."} />
        </CardContent>
      </Card>
      {selectedBreak ? (
        <TechnicalDetails label="Audit details">
          <dl className="grid gap-2 md:grid-cols-2">
            <FinanceFact label="Case ID" value={selectedBreak.breakId} mono />
            <FinanceFact label="Run ID" value={selectedBreak.runId} mono />
            <FinanceFact label="Raw category" value={selectedBreak.category} mono />
            <FinanceFact label="Source system" value={selectedBreak.sourceSystem ?? "Not supplied"} mono />
            <FinanceFact label="Source reference" value={selectedBreak.sourceReference ?? "Not supplied"} mono />
            <FinanceFact label="Tolerance profile" value={selectedBreak.toleranceProfileId ?? "Not supplied"} mono />
            <FinanceFact label="SLA policy" value={selectedBreak.slaPolicyId ?? "Not supplied"} mono />
          </dl>
        </TechnicalDetails>
      ) : (
        <StatusBanner role="status" tone="info" title="No reconciliation case selected" detail="Open a break from Accounting Reconciliation to retain its case scope here." />
      )}
      <div className="flex flex-wrap gap-2">
        <Button asChild size="sm"><Link to={WORKSTATION_ROUTE_CATALOG.accountingReconciliation}>Open reconciliation casework</Link></Button>
        {selectedBreak ? (
          <Button asChild size="sm" variant="outline"><Link to={evidenceWorkbenchPath("reconciliation-break", selectedBreak.breakId)}>Review retained evidence</Link></Button>
        ) : null}
        <Button type="button" size="sm" variant="outline" disabled disabledReason="Match decisions are recorded from the governed reconciliation casework route.">Record match decision</Button>
      </div>
    </div>
  );
}

export function CloseCalendarScreen({ data: _data }: FinanceStandardScreenProps) {
  const [searchParams] = useSearchParams();
  const [calendar, setCalendar] = useState<OperationsCloseCalendar | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorText, setErrorText] = useState<string | null>(null);
  const fundAccountId = searchParams.get("fundAccountId") ?? undefined;
  const periodId = searchParams.get("periodId") ?? undefined;

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setErrorText(null);
    getOperationsCloseCalendar({ fundAccountId, periodId })
      .then((nextCalendar) => {
        if (!cancelled) {
          setCalendar(nextCalendar);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setCalendar(null);
          setErrorText("The governed close calendar could not be loaded. No synthetic tasks or readiness state are shown.");
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [fundAccountId, periodId]);

  const items = calendar?.items ?? [];
  const closeItems = items.map((item) => {
    const nextTask = item.nextDueLabel ?? "No next task retained";
    const owner = item.nextDueOwner ?? "Unassigned";
    const due = item.nextDueDate ? presentFinanceDate(item.nextDueDate) : "No due date";
    return `${presentFinancePeriod(item.periodId)}: ${nextTask} · ${presentStatusLabel(item.status)} · owner ${owner} · due ${due} · ${item.blockerCount} blocker(s) · ${item.completedApprovalCount}/${item.requiredApprovalCount} approvals`;
  });
  const calendarFreshness = buildFinanceFreshness(calendar?.generatedAtUtc);

  return (
    <div className="space-y-4">
      <OperationalTrustSummary
        source={{ value: "Close calendar", tone: calendar ? "ready" : errorText ? "blocked" : "review" }}
        scope={{ value: periodId ? presentFinancePeriod(periodId) : "All periods", detail: fundAccountId ? "Selected fund account" : "All fund accounts", tone: "ready" }}
        freshness={loading ? { value: "Loading", tone: "review" } : errorText ? { value: "Unavailable", tone: "blocked" } : calendarFreshness}
        completeness={{ value: `${items.length} workflow${items.length === 1 ? "" : "s"} retained`, tone: items.length > 0 ? "ready" : errorText ? "blocked" : "unknown" }}
        blocker={errorText ? { value: "Calendar unavailable", detail: errorText, tone: "blocked" } : undefined}
      />
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Close Calendar</CardTitle>
          <CardDescription>Month-end tasks by owner, due date, blocker, evidence, dependency, and sign-off state.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Selected period" value={periodId ?? "All periods"} />
          <FinanceFact label="Workflows" value={String(items.length)} />
          <FinanceFact label="Blocked workflows" value={String(items.filter((item) => item.blockerCount > 0).length)} />
        </CardContent>
      </Card>
      {loading ? <StatusBanner role="status" tone="info" title="Loading close calendar" detail="Loading retained workflow deadlines, blockers, and approvals." /> : null}
      {errorText ? <StatusBanner role="alert" tone="danger" title="Close calendar unavailable" detail={errorText} /> : null}
      {!loading && !errorText ? <FinanceList title="Close workflow queue" items={closeItems.length > 0 ? closeItems : ["No close workflows match the selected scope."]} /> : null}
      {items.length > 0 || fundAccountId ? (
        <TechnicalDetails label="Close workflow references">
          <dl className="grid gap-2 text-xs md:grid-cols-2">
            {fundAccountId ? <FinanceFact label="Selected fund account" value={fundAccountId} mono /> : null}
            {items.map((item) => (
              <FinanceFact key={item.workflowId} label={`${presentFinancePeriod(item.periodId)} workflow`} value={`${item.workflowId} / ${item.fundAccountId}`} mono />
            ))}
          </dl>
        </TechnicalDetails>
      ) : null}
      <Button asChild size="sm" variant="outline"><Link to={WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity}>Open Operations Continuity</Link></Button>
    </div>
  );
}

export function EvidenceDetailScreen() {
  const [searchParams] = useSearchParams();
  const evidenceId = searchParams.get("evidenceId")?.trim() || null;

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Evidence Detail</CardTitle>
          <CardDescription>Document-level evidence can support or block work; it does not approve, post, or release work.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Selected evidence" value={evidenceId ? "Reference retained" : "No reference selected"} />
          <FinanceFact label="Metadata source" value="Shared Evidence Workbench" />
          <FinanceFact label="Review status" value={evidenceId ? "Ready to inspect" : "Selection required"} />
        </CardContent>
      </Card>
      <StatusBanner
        role="status"
        tone={evidenceId ? "info" : "warning"}
        title={evidenceId ? "Evidence reference ready for inspection" : "Select evidence to continue"}
        detail={evidenceId
          ? "Open the shared Evidence Workbench to load source-owned classification, extraction, links, and audit events."
          : "Return to the Evidence Workbench and select a retained document or record."}
      />
      {evidenceId ? (
        <TechnicalDetails label="Evidence reference">
          <p className="break-all font-mono text-xs text-muted-foreground">{evidenceId}</p>
        </TechnicalDetails>
      ) : null}
      <Button asChild size="sm">
        <Link to={evidenceId ? evidenceWorkbenchPath("evidence", evidenceId) : WORKSTATION_ROUTE_CATALOG.reportingEvidence}>
          {evidenceId ? "Inspect selected evidence" : "Open Evidence Workbench"}
        </Link>
      </Button>
    </div>
  );
}

export function ApprovalInboxScreen({ data }: FinanceStandardScreenProps) {
  const closePlan = firstRecord(asRecord(data), "closePlans");
  const approvals = readRecordArray(closePlan, "approvals");
  const approvalItems = approvals.length > 0
    ? approvals.map((approval) => `${readString(approval, "label", readString(approval, "approvalId", "Approval"))}: ${readString(approval, "status", "Pending")}`)
    : [];

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Approval Inbox</CardTitle>
          <CardDescription>Approvals show what is being approved, why it is ready, what changed, supporting evidence, downstream effects, and risk.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Pending approvals" value={String(approvalItems.length)} />
          <FinanceFact label="Queue source" value={approvals.length > 0 ? "Accounting workspace" : "No approval feed supplied"} />
          <FinanceFact label="Decision authority" value="Governed approval workflow" />
        </CardContent>
      </Card>
      {approvalItems.length > 0 ? (
        <>
          <FinanceList title="Approval queue" items={approvalItems} />
          <TechnicalDetails label="Approval references">
            <ul className="space-y-2 text-xs text-muted-foreground">
              {approvals.map((approval) => (
                <li key={readString(approval, "approvalId", readString(approval, "label", "approval"))} className="font-mono">
                  {readString(approval, "approvalId", "Reference unavailable")}
                </li>
              ))}
            </ul>
          </TechnicalDetails>
        </>
      ) : (
        <StatusBanner
          role="status"
          tone="info"
          title="No approvals in the supplied accounting scope"
          detail="Open governed approvals to inspect the authoritative queue, or review the close calendar to resolve prerequisites that create approval work."
        />
      )}
      <FinanceList title="Reviewer checklist" items={[
        "What am I approving?",
        "Why is it ready?",
        "What changed?",
        "What evidence supports it?",
        "What happens after approval?",
        "What are the risks?"
      ]} />
      <div className="flex flex-wrap gap-2">
        <Button asChild size="sm"><Link to={WORKSTATION_ROUTE_CATALOG.accountingApprovals}>Open governed approvals</Link></Button>
        <Button asChild size="sm" variant="outline"><Link to={WORKSTATION_ROUTE_CATALOG.accountingCloseCalendar}>Review close calendar</Link></Button>
      </div>
    </div>
  );
}

export function ReportParametersCoveragePanel() {
  return <FinanceList title="Standard report parameters" items={reportParameterFields} />;
}

export function ReportLibraryCoveragePanel() {
  return <FinanceList title="Report categories" items={financeReportCategories} />;
}

function isRunnablePreviewStatus(status: string): boolean {
  const normalized = normalizeStatus(status);
  return normalized === "" || normalized === "draft" || normalized === "ready" || normalized === "preview";
}

function presentStatusLabel(status: string): string {
  const spaced = status
    .trim()
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/\s+/g, " ");
  return spaced ? `${spaced.charAt(0).toUpperCase()}${spaced.slice(1)}` : "Status unavailable";
}

function normalizeStatus(status: string): string {
  return status.trim().toLowerCase().replace(/[^a-z0-9]+/g, "");
}

function buildFinanceFreshness(value: string | null | undefined): {
  value: string;
  detail?: string;
  tone: "ready" | "review" | "unknown";
} {
  if (!value) {
    return { value: "No timestamp", tone: "unknown" };
  }

  const timestamp = new Date(value).getTime();
  if (Number.isNaN(timestamp)) {
    return { value: "Invalid timestamp", detail: value, tone: "review" };
  }

  const ageMs = Date.now() - timestamp;
  const isCurrent = ageMs >= 0 && ageMs <= 24 * 60 * 60 * 1000;
  return {
    value: isCurrent ? "Current" : "Stale update",
    detail: formatDateTimeLabel(value),
    tone: isCurrent ? "ready" : "review"
  };
}

function presentFinancePeriod(value: string): string {
  const match = /^(\d{4})-(\d{2})$/.exec(value.trim());
  if (!match) {
    return value;
  }

  const date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, 1));
  return date.toLocaleDateString("en-US", { month: "long", year: "numeric", timeZone: "UTC" });
}

function presentFinanceDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" });
}

function presentRunPhase(status: string): string {
  const normalized = normalizeStatus(status);
  if (normalized.includes("fail") || normalized.includes("reject")) {
    return "Needs correction";
  }
  if (normalized.includes("awaiting") || normalized.includes("pendingapproval") || normalized.includes("inreview")) {
    return "Approval review";
  }
  if (normalized.includes("approved") || normalized.includes("released") || normalized.includes("published")) {
    return "Released output";
  }
  if (normalized.includes("running") || normalized.includes("generating")) {
    return "Generating output";
  }
  return "Run preparation";
}

function reportRunStatusVariant(status: string): "outline" | "warning" | "danger" | "success" {
  const normalized = normalizeStatus(status);
  if (normalized.includes("fail") || normalized.includes("reject")) {
    return "danger";
  }
  if (normalized.includes("awaiting") || normalized.includes("pending") || normalized.includes("review")) {
    return "warning";
  }
  if (normalized.includes("approved") || normalized.includes("released") || normalized.includes("published") || normalized.includes("complete")) {
    return "success";
  }
  return "outline";
}

function resolveStartedLabel(run: UnknownRecord | null, status: string): string {
  const retained = readString(run, "startedAtUtc", readString(run, "startedAt", ""));
  if (retained) {
    return retained;
  }
  return normalizeStatus(status) === "draft" ? "Not started" : "Start timestamp not retained";
}

function resolveCompletedLabel(run: UnknownRecord | null, status: string): string {
  const retained = readString(run, "completedAtUtc", readString(run, "completedAt", ""));
  if (retained) {
    return retained;
  }

  const normalized = normalizeStatus(status);
  if (normalized.includes("awaiting") || normalized.includes("pendingapproval") || normalized.includes("inreview")) {
    return "Generation complete; approval pending";
  }
  if (normalized.includes("running") || normalized.includes("generating")) {
    return "In progress";
  }
  if (normalized.includes("fail") || normalized.includes("reject")) {
    return "Stopped before completion";
  }
  if (normalized.includes("approved") || normalized.includes("released") || normalized.includes("published") || normalized.includes("complete")) {
    return "Completion timestamp not retained";
  }
  if (readString(run, "startedAtUtc", readString(run, "startedAt", ""))) {
    return "In progress";
  }
  return "Not started";
}

function buildRetainedParameterItems(run: UnknownRecord | null): string[] {
  const parameters = readRecord(run, "parameters");
  const entries = parameters ? Object.entries(parameters) : [];
  const retained = entries.flatMap(([key, value]) => {
    const presented = presentParameterValue(value);
    return presented ? [`${presentParameterLabel(key)}: ${presented}`] : [];
  });

  if (retained.length > 0) {
    return retained;
  }

  const knownFields = [
    "fundProfileId",
    "portfolioId",
    "entityId",
    "asOfDate",
    "ledgerBookId",
    "accountingBasis",
    "currency",
    "consolidationLevel",
    "outputFormat",
    "datasetSourceId"
  ];
  const known = knownFields.flatMap((key) => {
    const value = presentParameterValue(run?.[key]);
    return value ? [`${presentParameterLabel(key)}: ${value}`] : [];
  });

  return known.length > 0 ? known : ["No retained parameter values are available for this run."];
}

function buildReportPreviewItems({
  sections,
  sectionCount,
  lineageLinkedSections,
  retainedReferenceCount
}: {
  sections: string[];
  sectionCount: number;
  lineageLinkedSections: number;
  retainedReferenceCount: number;
}): string[] {
  return [
    sections.length > 0
      ? `Template sections: ${sections.join(", ")}.`
      : `${sectionCount} retained report section${sectionCount === 1 ? "" : "s"} will be rendered from the governed template.`,
    sectionCount > 0
      ? `${lineageLinkedSections} of ${sectionCount} sections carry retained lineage into approval review.`
      : "No retained section manifest is available; generate a preview before release.",
    retainedReferenceCount > 0
      ? `${retainedReferenceCount} retained output or evidence reference${retainedReferenceCount === 1 ? " is" : "s are"} attached to this run.`
      : "No retained output references are attached yet."
  ];
}

function buildReportOutputItems(references: string[], fallback: string): string[] {
  if (references.length === 0) {
    return [fallback];
  }

  return references.map((reference, index) => {
    const normalized = reference.toLowerCase();
    if (normalized.includes("evidence-bundle")) {
      return "Evidence bundle retained for approval review.";
    }
    if (normalized.includes("publication-manifest")) {
      return "Publication manifest retained for release control.";
    }
    if (normalized.includes("restatement")) {
      return "Restatement support retained with the generated run.";
    }
    if (normalized.endsWith(".pdf") || normalized.includes("format=pdf")) {
      return "PDF output retained.";
    }
    if (normalized.endsWith(".xlsx") || normalized.endsWith(".xls") || normalized.includes("format=xls")) {
      return "Workbook output retained.";
    }
    if (normalized.endsWith(".csv") || normalized.includes("format=csv")) {
      return "CSV output retained.";
    }
    return `Retained output reference ${index + 1}.`;
  });
}

function presentParameterLabel(key: string): string {
  const spaced = key
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/\s+/g, " ")
    .trim();
  return spaced ? `${spaced.charAt(0).toUpperCase()}${spaced.slice(1)}` : "Parameter";
}

function presentParameterValue(value: unknown): string | null {
  if (typeof value === "string" && value.trim()) {
    return value.trim();
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }
  if (typeof value === "boolean") {
    return value ? "Yes" : "No";
  }
  if (Array.isArray(value)) {
    const items = value.map((item) => presentParameterValue(item)).filter((item): item is string => Boolean(item));
    return items.length > 0 ? items.join(", ") : null;
  }
  return null;
}

function FinanceFact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
      <div className="text-xs font-medium text-muted-foreground">{label}</div>
      <div className={mono ? "mt-1 break-words font-mono text-sm text-foreground" : "mt-1 text-sm text-foreground"}>{value}</div>
    </div>
  );
}

function FinanceList({ title, items }: { title: string; items: string[] }) {
  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle className="text-base">{title}</CardTitle>
      </CardHeader>
      <CardContent>
        <ul className="space-y-2">
          {items.map((item) => (
            <li key={item} className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2 text-sm text-foreground">
              {item}
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  );
}

function asRecord(value: unknown): UnknownRecord | null {
  return value && typeof value === "object" ? value as UnknownRecord : null;
}

function readRecordArray(record: UnknownRecord | null, key: string): UnknownRecord[] {
  const value = record?.[key];
  return Array.isArray(value) ? value.map(asRecord).filter((item): item is UnknownRecord => item !== null) : [];
}

function readRecord(record: UnknownRecord | null, key: string): UnknownRecord | null {
  return asRecord(record?.[key]);
}

function firstRecord(record: UnknownRecord | null, key: string): UnknownRecord | null {
  return readRecordArray(record, key)[0] ?? null;
}

function readString(record: UnknownRecord | null, key: string, fallback: string): string {
  const value = record?.[key];
  if (typeof value === "string" && value.trim().length > 0) {
    return value;
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }
  return fallback;
}

function readNumber(record: UnknownRecord | null, key: string, fallback: number): number {
  const value = record?.[key];
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

function readStringArray(record: UnknownRecord | null, key: string): string[] {
  const value = record?.[key];
  return Array.isArray(value) ? value.map((item) => String(item)).filter((item) => item.trim().length > 0) : [];
}
