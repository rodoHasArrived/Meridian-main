import { useEffect, useMemo, useState } from "react";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import { DesignSystemStatus } from "@/design-system/status";
import { EntityLink } from "@/components/meridian/entity-inspector";
import { LedgerAmountProofDrawer } from "@/components/meridian/proof-drawer";
import {
  nextDenseSort,
  sortRowsByValue,
  type DenseDataTableSortState
} from "@/components/meridian/ui-kit-primitives";
import type { LedgerAmountSelection } from "@/types/ledger-amount-proof";
import { Link, useSearchParams } from "react-router-dom";
import { formatCurrency, formatMoney, formatUtcDate, formatUtcMonthYear } from "@/lib/format";
import { formatNumber } from "@/lib/format";
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
import { getOperationsCloseCalendar, getRunTrialBalance } from "@/lib/api";
import {
  normalizeReportingWorkspace,
  type ReportingWorkspacePayload
} from "@/lib/reporting-workspace";
import { evidenceWorkbenchPath, WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import { financeBreakLabel } from "@/screens/accounting-screen.reconciliation.view-model";
import { formatDateTimeLabel } from "@/screens/accounting-screen.formatting";
import { ReportRunGovernanceScreen } from "@/screens/report-run-governance-screen";
import { TrialBalanceScreen } from "@/screens/trial-balance-screen";
import { useAccountingPostedLedgerViewModel, type PostedLedgerJournalLine } from "@/screens/accounting-screen.posted-ledger.view-model";
import { AccountingPeriodComparison } from "@/screens/accounting-period-comparison";
import { usePostedLedgerRouteScope } from "@/screens/posted-ledger-route-scope";
import { ConsolidationPanel } from "@/screens/accounting-screen.consolidation-panel";
import { EntityOperationsPanel } from "@/screens/accounting-screen.entity-operations";
import { FactorPaydownPanel } from "@/screens/accounting-screen.factor-paydown-panel";
import { CanonicalAmortizationPanel } from "@/screens/accounting-screen.canonical-amortization-panel";
import {
  buildTemplateRows,
  hasRetainedReportingAsOfDate,
  presentReportingAsOfDate,
  presentReportingIdentifier,
  presentReportingStatusLabel
} from "@/screens/reporting-screen.view-model";
import type {
  AccountingWorkspaceResponse,
  LedgerTrialBalanceLine,
  OperationsCloseCalendar,
  OperationsCloseCalendarItem
} from "@/types";

interface FinanceStandardScreenProps {
  data: AccountingWorkspaceResponse | null;
}

interface ReportingStandardScreenProps {
  data: ReportingWorkspacePayload | null;
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

export function ReportPreviewValidationScreen({ data }: ReportingStandardScreenProps) {
  const [searchParams] = useSearchParams();
  const reportingData = normalizeReportingWorkspace(data);
  const reporting = asRecord(reportingData);
  const requestedRunId = searchParams.get("runId") ?? "";
  const runs = readRecordArray(reporting, "recentRuns");
  const run = requestedRunId
    ? runs.find((candidate) => readString(candidate, "runId", "") === requestedRunId) ?? null
    : runs[0] ?? null;
  const templates = buildTemplateRows(reportingData?.templates ?? []);
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
    ? reportingData?.templates?.find((candidate) => candidate.templateId === runTemplateId) ?? null
    : selectedTemplate
      ? reportingData?.templates?.find((candidate) => candidate.templateId === selectedTemplate.templateName) ?? null
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

export function ReportRunDetailScreen(_props: ReportingStandardScreenProps) {
  return <ReportRunGovernanceScreen />;
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
          <Link to={workstationRouteWithQuery("strategyRunLedger", { runId: requestedRunId || null })}>Back to Run Ledger</Link>
        </Button>
        <Button asChild size="sm" variant="outline">
          <Link to={workstationRouteWithQuery("strategyRunLedger", { runId: requestedRunId || null })}>Open run ledger activity</Link>
        </Button>
        {account?.sourceJournalEntryId ? (
          <Button asChild size="sm" variant="outline">
            <Link to={workstationRouteWithQuery("accountingJournalEntryDetail", { journalEntryId: account.sourceJournalEntryId, runId: requestedRunId || null })}>Open source journal entry</Link>
          </Button>
        ) : null}
        {account?.approvalIds && account.approvalIds.length > 0 ? (
          <Button asChild size="sm" variant="outline">
            <Link to={workstationRouteWithQuery("accountingApprovals", { approvalId: account.approvalIds[0] })}>Review approvals</Link>
          </Button>
        ) : null}
      </div>
    </div>
  );
}

const LEDGER_EXPLORER_TABS = [
  { id: "ledger", label: "Ledger" },
  { id: "trial-balance", label: "Trial balance" }
];

interface LedgerExplorerColumn {
  id: string;
  label: string;
  numeric?: boolean;
  /** Orders whole journal entries; their posting lines travel with them. */
  sortValue?: (line: PostedLedgerJournalLine) => string | number | null | undefined;
}

const LEDGER_EXPLORER_COLUMNS: LedgerExplorerColumn[] = [
  { id: "date", label: "Date", sortValue: (line) => timestampSortValue(line.timestamp) },
  { id: "journalEntryId", label: "Journal ID", sortValue: (line) => line.journalEntryId },
  { id: "account", label: "Account", sortValue: (line) => line.accountScopeDisplayName },
  { id: "debit", label: "Debit", numeric: true, sortValue: (line) => line.totalDebits },
  { id: "credit", label: "Credit", numeric: true, sortValue: (line) => line.totalCredits },
  { id: "entity", label: "Entity", sortValue: (line) => line.entityScopeDisplayName },
  { id: "source", label: "Source", sortValue: (line) => line.description },
  // Every row is posted, so status has nothing to order by.
  { id: "status", label: "Status" }
];

const LEDGER_SEARCH_PARAM = "ledgerSearch";
const LEDGER_SORT_PARAM = "ledgerSort";

/** "debit:desc" back to a sort state; unknown columns or directions read as no sort. */
export function parseLedgerSort(value: string | null): DenseDataTableSortState | null {
  const [columnId, direction] = (value ?? "").split(":");
  const column = LEDGER_EXPLORER_COLUMNS.find((candidate) => candidate.id === columnId && candidate.sortValue);
  return column && (direction === "asc" || direction === "desc") ? { columnId: column.id, direction } : null;
}

function timestampSortValue(timestamp: string | null | undefined): number | null {
  const time = timestamp ? Date.parse(timestamp) : Number.NaN;
  return Number.isNaN(time) ? null : time;
}

/**
 * Accounting's canonical ledger surface.
 *
 * Both tabs read the fund's posted journal. The Ledger tab used to load
 * <c>getRunLedgerJournal</c> for whichever strategy run happened to head the reconciliation
 * queue — a simulation artifact, rendered under the name an operator reads as the book of record,
 * on the very screen the workspace links to as "Validate the ledger"
 * (adversarial-program-review-2026-08-25 §1). Run evidence lives in the strategy run ledger
 * explorer under Strategy.
 */
export function LedgerExplorerScreen(_props: FinanceStandardScreenProps) {
  const [searchParams, setSearchParams] = useSearchParams();
  const view = searchParams.get("view") === "trial-balance" ? "trial-balance" : "ledger";

  return (
    <Tabs
      tabs={LEDGER_EXPLORER_TABS}
      value={view}
      onValueChange={(nextView) => {
        // Only `view` changes: ledgerBookId and periodId are the shared scope both tabs read.
        const nextParams = new URLSearchParams(searchParams);
        if (nextView === "ledger") {
          nextParams.delete("view");
        } else {
          nextParams.set("view", nextView);
        }
        setSearchParams(nextParams, { replace: true });
      }}
    >
      <TabPanel>
        {/*
          Always mounted, and idle unless it is the tab on screen. Held unconditionally live, its
          posted-ledger hook duplicated every request TrialBalanceScreen already makes on the other
          tab; unmounted instead, it lost the operator's chosen book and period every time they
          looked at the trial balance and came back. `active` pauses the requests and keeps the
          book and period selection. Amount proof is cleared when the tab becomes inactive.
        */}
        <PostedLedgerJournalTab active={view === "ledger"} />
      </TabPanel>
      <TabPanel>
        {view === "trial-balance" ? <TrialBalanceScreen /> : null}
      </TabPanel>
    </Tabs>
  );
}

/** The Ledger tab's own body, so its requests belong to the tab that renders them. */
function PostedLedgerJournalTab({ active }: { active: boolean }) {
  const [selectedAmount, setSelectedAmount] = useState<LedgerAmountSelection | null>(null);
  // Search and sort live in the link (C4), so Copy link, saved views and saved layouts carry them.
  const [tabSearchParams, setTabSearchParams] = useSearchParams();
  const searchText = tabSearchParams.get(LEDGER_SEARCH_PARAM) ?? "";
  const ledgerSort = parseLedgerSort(tabSearchParams.get(LEDGER_SORT_PARAM));
  const writeLedgerParam = (key: string, value: string | null) => {
    const next = new URLSearchParams(tabSearchParams);
    if (value) next.set(key, value);
    else next.delete(key);
    setTabSearchParams(next, { replace: true });
  };
  const setSearchText = (value: string) => writeLedgerParam(LEDGER_SEARCH_PARAM, value || null);
  const setLedgerSort = (update: (current: DenseDataTableSortState | null) => DenseDataTableSortState | null) => {
    const next = update(ledgerSort);
    writeLedgerParam(LEDGER_SORT_PARAM, next ? `${next.columnId}:${next.direction}` : null);
  };
  const postedLedger = useAccountingPostedLedgerViewModel(
    "ledger",
    undefined,
    {
      includeJournal: true,
      enabled: active,
      // Read straight from the URL rather than back from the route binding below, so the hook has
      // it in the same render: the binding runs after it, and a value fed back would arrive a
      // render late.
      requestedPeriodId: tabSearchParams.get("periodId")
    });
  // The same route binding the trial-balance tab uses, so the two tabs share one scope rather
  // than holding two. This tab never wrote to the route before: selecting book B here and
  // switching tabs showed A, and switching back showed B under a URL that said A. Only the tab
  // on screen syncs — two active writers would race each other's edits.
  usePostedLedgerRouteScope(postedLedger, active);
  const journalLines = postedLedger.journalLines;
  const loading = postedLedger.journalLoading;

  const baseCurrency = postedLedger.view.baseCurrency;
  const selectedFundId = postedLedger.selectedBook?.fundProfileId ?? "";
  const selectedBookId = postedLedger.selectedBook?.ledgerBookId ?? "";
  const sourceLedgerContextHref = workstationRouteWithQuery("accountingLedger", {
    ...Object.fromEntries(tabSearchParams), ledgerBookId: selectedBookId, periodId: postedLedger.selectedPeriodId
  });
  const currentAmount = selectedAmount && active && !loading && !postedLedger.journalErrorText &&
    (!selectedAmount.ledgerBookId || selectedAmount.ledgerBookId === selectedBookId) &&
    (!selectedAmount.periodId || selectedAmount.periodId === postedLedger.selectedPeriodId) &&
    selectedAmount.fundProfileId === selectedFundId && journalLines.some((entry) => entry.postedLines?.some((line) =>
      selectedAmount.subjectId === `${entry.journalEntryId}:${line.entryId}:debit` ||
      selectedAmount.subjectId === `${entry.journalEntryId}:${line.entryId}:credit`)) ? selectedAmount : null;
  // Discard invalid selections as well as hiding their drawer. This tab stays mounted while
  // inactive, and retaining the selection would reopen its proof when the old scope loads again.
  if (selectedAmount && !currentAmount) {
    setSelectedAmount(null);
  }

  // The trial balance on the sibling tab is labelled in the book's base currency; these are the
  // same governed debits and credits, so they carry it too rather than defaulting to dollars.
  const postedMoney = (value: number) => baseCurrency
    ? formatMoney(value, { context: "accounting", currency: baseCurrency })
    : `${formatNumber(value)} (currency unavailable)`;

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
      // The retained dimensions, not only the derived display names: an entry scoped to an entity
      // or fund was otherwise impossible to find by the id it is actually tagged with.
      line.dimensions?.entityId,
      line.dimensions?.fundId,
      line.dimensions?.instrumentId,
      String(line.totalDebits),
      String(line.totalCredits),
      ...(line.postedLines ?? []).flatMap((posting) => [posting.accountName, posting.symbol, posting.dimensions?.entityId, posting.dimensions?.fundId, posting.dimensions?.instrumentId, posting.description, String(posting.debit), String(posting.credit)])
    ].some((value) => String(value ?? "").toLowerCase().includes(needle)));
  }, [journalLines, searchText]);
  const sortedRows = useMemo(() => {
    const sortValue = ledgerSort ? LEDGER_EXPLORER_COLUMNS.find((column) => column.id === ledgerSort.columnId)?.sortValue : undefined;
    return ledgerSort && sortValue ? sortRowsByValue(filteredRows, sortValue, ledgerSort.direction) : filteredRows;
  }, [filteredRows, ledgerSort]);

  type PostedLine = (typeof journalLines)[number]["postedLines"][number];
  const ledgerRows = sortedRows.flatMap((journal) => [
    { id: `journal:${journal.journalEntryId}`, journal, posting: null as PostedLine | null },
    ...(journal.postedLines ?? []).map((posting) => ({ id: `posting:${journal.journalEntryId}:${posting.entryId}`, journal, posting }))
  ]);
  function postingAmountCell(row: (typeof ledgerRows)[number], side: "debit" | "credit") {
    const posting = row.posting;
    const value = posting ? posting[side] : side === "debit" ? row.journal.totalDebits : row.journal.totalCredits;
    return posting && value !== 0 ? <button type="button" className="text-foreground underline decoration-dotted underline-offset-4 hover:decoration-solid focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
      aria-label={`Inspect ${side} ${postedMoney(value)} for ${posting.accountName} in journal ${row.journal.journalEntryId}`}
      onClick={() => setSelectedAmount({ subjectId: `${row.journal.journalEntryId}:${posting.entryId}:${side}`, ledgerBookId: row.journal.ledgerBookId ?? "", periodId: row.journal.periodId ?? "", fundProfileId: selectedFundId, amount: value, currency: baseCurrency ?? "", label: `${posting.accountName} ${side} ${postedMoney(value)}` })}>{postedMoney(value)}</button> : postedMoney(value);
  }

  if (!active) {
    return null;
  }

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Ledger Explorer</CardTitle>
          <CardDescription>Search journal activity by account, amount, journal ID, source, security, or entity.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 lg:grid-cols-2 xl:grid-cols-[minmax(15rem,1fr)_minmax(15rem,1fr)]">

          <FormRow label="Ledger book" labelFor="ledger-book-select">
            <Select
              id="ledger-book-select"
              value={postedLedger.view.bookOptions.find((option) => option.isSelected)?.id ?? ""}
              onChange={(event) => postedLedger.selectBook(event.target.value)}
            >
              {postedLedger.view.bookOptions.length > 0 ? postedLedger.view.bookOptions.map((option) => (
                <option key={option.id} value={option.id}>{option.label} · {option.baseCurrency}</option>
              )) : <option value="">No ledger book available</option>}
            </Select>
          </FormRow>
          {/* The retained list survives a failed refresh so the period stays named, which leaves
              the selector looking entirely normal. Without this the operator had no sign that
              discovery failed, or that the list may be missing periods closed since. */}
          <FormRow
            label="Ledger period"
            labelFor="ledger-period-select"
            error={postedLedger.view.periodSelector.errorText}
          >
            <Select
              id="ledger-period-select"
              value={postedLedger.selectedPeriodId ?? ""}
              onChange={(event) => postedLedger.selectPeriod(event.target.value)}
            >
              {postedLedger.view.periodSelector.options.length > 0
                ? postedLedger.view.periodSelector.options.map((option) => (
                  <option key={option.id} value={option.id}>{option.label} · {option.statusLabel}</option>
                ))
                : <option value="">No ledger period available</option>}
            </Select>
          </FormRow>
        </CardContent>
      </Card>

      <AccountingPeriodComparison bookId={selectedBookId} periodId={postedLedger.selectedPeriodId} expectedBasis={postedLedger.selectedBook?.accountingBasis} enabled={postedLedger.periodsSettled} periodOptions={postedLedger.view.periodSelector.options} />
      <Card className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle>Ledger search results</CardTitle>
            <CardDescription>
              {loading
                ? "Loading the posted journal."
                : postedLedger.journalErrorText
                  ? postedLedger.journalErrorText
                  : `${filteredRows.length} posted entry(ies)${postedLedger.selectedPeriodLabel ? ` for ${postedLedger.selectedPeriodLabel}` : ""}.${filteredRows.length > 0 ? " Select a posting amount to open its proof." : ""}`}
            </CardDescription>
          </div>
          <Badge variant={filteredRows.length > 0 ? "success" : "outline"}>{filteredRows.length}</Badge>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto rounded-md border border-border/70">
            <TableWorkbench<(typeof ledgerRows)[number]>
              workbenchId="accounting-ledger-search" sourceKey={`${selectedBookId}:${postedLedger.selectedPeriodId ?? "unavailable"}`}
              exportContext={{ "Ledger book": selectedBookId, "Ledger period": postedLedger.selectedPeriodId ?? "Unavailable", Basis: postedLedger.selectedBook?.accountingBasis ?? "Unavailable", Currency: baseCurrency ?? "Unavailable" }}
              sourceDescription={`Posted journal · ${postedLedger.view.selectedBookLabel ?? "Book unavailable"} · ${postedLedger.selectedPeriodLabel ?? "Period unavailable"} · ${postedLedger.selectedBook?.accountingBasis ?? "Basis unavailable"} · ${baseCurrency ?? "Currency unavailable"}. Journal summaries and their posting lines are separate rows; do not sum both.`}
              completePopulation={postedLedger.periodsSettled && !loading && !postedLedger.journalErrorText}
              filters={<FormRow label="Search by account, amount, journal ID, source, security, entity" labelFor="ledger-search">
            <Input
              id="ledger-search"
              type="search"
              value={searchText}
              onChange={(event) => setSearchText(event.target.value)}
              placeholder="Cash, $120,500, AAPL, cash sweep"
            />
          </FormRow>}
              filterState={{ search: searchText }}
              sort={ledgerSort}
              onToggleSort={(columnId) => {
                const column = LEDGER_EXPLORER_COLUMNS.find(candidate => candidate.id === columnId);
                if (column?.sortValue) setLedgerSort(current => nextDenseSort(current, column));
              }}
              onRestoreView={(view) => {
                const next = new URLSearchParams(tabSearchParams);
                const search = view.filters?.search ?? "";
                if (search) next.set(LEDGER_SEARCH_PARAM, search); else next.delete(LEDGER_SEARCH_PARAM);
                const restoredSort = parseLedgerSort(`${view.sortColumnId ?? ""}:${view.sortDirection ?? ""}`);
                if (restoredSort) next.set(LEDGER_SORT_PARAM, `${restoredSort.columnId}:${restoredSort.direction}`); else next.delete(LEDGER_SORT_PARAM);
                setTabSearchParams(next, { replace: true });
              }}
              ariaLabel="Ledger Explorer results" rows={ledgerRows} getRowId={(row) => row.id}
              emptyText={postedLedger.view.periodSelector.options.length === 0 ? postedLedger.view.periodSelector.errorText ?? postedLedger.view.periodSelector.loadingText ?? postedLedger.view.periodSelector.emptyText ?? "No ledger periods are available for this accounting scope." : "No posted entries match the current search."}
              columns={[
                { id: "date", label: "Date", className: "whitespace-nowrap", render: (row) => row.posting ? "Posting amount" : formatDateTimeLabel(row.journal.timestamp), exportValue: (row) => row.journal.timestamp },
                { id: "journalEntryId", label: "Journal ID", sortable: true, render: (row) => row.posting ? null : <EntityLink ariaLabel={`Open journal ${row.journal.journalEntryId}`} record={{ kind: "journal", id: row.journal.journalEntryId, label: row.journal.journalEntryId, workspaceHref: workstationRouteWithQuery("accountingJournalEntryDetail", { journalEntryId: row.journal.journalEntryId, periodId: postedLedger.selectedPeriodId }), summary: [{ label: "Status", value: "Posted" }, { label: "Date", value: formatDateTimeLabel(row.journal.timestamp) }, { label: "Description", value: row.journal.description || "Ledger posting" }] }}>{row.journal.journalEntryId}</EntityLink>, exportValue: (row) => row.journal.journalEntryId },
                { id: "account", label: "Account", render: (row) => row.posting ? `${row.posting.accountName}${row.posting.symbol ? ` - ${row.posting.symbol}` : ""}` : row.journal.accountScopeId ? <EntityLink record={{ kind: "account", id: row.journal.accountScopeId, label: row.journal.accountScopeDisplayName ?? "Account name unavailable", workspaceHref: sourceLedgerContextHref, summary: [{ label: "Source", value: "Posted ledger dimensions" }, { label: "Workspace destination", value: "Source Ledger context · selected book and period" }] }} /> : row.journal.accountScopeDisplayName ?? "Multiple accounts", exportValue: (row) => row.posting?.accountName ?? row.journal.accountScopeDisplayName },
                { id: "debit", label: "Debit", align: "right", className: "tabular-nums", render: (row) => postingAmountCell(row, "debit"), exportValue: (row) => row.posting?.debit ?? row.journal.totalDebits },
                { id: "credit", label: "Credit", align: "right", className: "tabular-nums", render: (row) => postingAmountCell(row, "credit"), exportValue: (row) => row.posting?.credit ?? row.journal.totalCredits },
                { id: "entity", label: "Entity", render: (row) => { const entityId = row.posting ? row.posting.dimensions?.entityId : row.journal.entityScopeId; return entityId ? <EntityLink record={{ kind: "entity", id: entityId, label: row.journal.entityScopeDisplayName && row.journal.entityScopeDisplayName !== entityId && row.journal.entityScopeId === entityId ? row.journal.entityScopeDisplayName : "Entity name unavailable", workspaceHref: sourceLedgerContextHref, summary: [{ label: "Source", value: "Posted ledger dimensions" }, { label: "Workspace destination", value: "Source Ledger context · selected book and period" }] }} /> : "Not scoped to one entity"; }, exportValue: (row) => row.posting ? row.posting.dimensions?.entityId : row.journal.entityScopeId },
                { id: "source", label: "Source", render: (row) => row.posting?.description || row.journal.description || "Ledger posting", exportValue: (row) => row.posting?.description || row.journal.description },
                { id: "status", label: "Status", render: (row) => row.posting ? null : <DesignSystemStatus status="posted">Posted</DesignSystemStatus>, exportValue: (row) => row.posting ? "Posting line" : "Posted journal summary" }
              ]}
            />
          </div>
          {filteredRows.length > 0 ? (
            <TechnicalDetails label="Journal references" className="mt-3">
              <ul className="space-y-1 text-xs text-muted-foreground">
                {filteredRows.map((line) => (
                  <li key={line.journalEntryId} className="break-all font-mono">{line.journalEntryId}{line.entityScopeId ? ` · Entity reference: ${line.entityScopeId}` : ""}</li>
                ))}
              </ul>
            </TechnicalDetails>
          ) : null}
        </CardContent>
      </Card>
      <LedgerAmountProofDrawer selection={currentAmount} onClose={() => setSelectedAmount(null)} />
      <nav className="flex flex-wrap gap-3 text-sm" aria-label="Related accounting workflows">
        <Link className="text-foreground underline underline-offset-4" to={scopedAccountingWorkflowPath(WORKSTATION_ROUTE_CATALOG.accountingFactorPaydown, tabSearchParams)}>Prepare factor paydown</Link>
        <Link className="text-foreground underline underline-offset-4" to={scopedAccountingWorkflowPath(WORKSTATION_ROUTE_CATALOG.accountingConsolidation, tabSearchParams)}>Open consolidation</Link>
      </nav>
      <details className="rounded border border-border p-4">
        <summary className="cursor-pointer font-semibold">Prepare canonical lot amortization</summary>
        <div className="mt-4">
      <CanonicalAmortizationPanel periodVersion={postedLedger.selectedPeriodVersion} active={active} bookId={selectedBookId} periodId={postedLedger.periodsSettled ? postedLedger.selectedPeriodId : null} fundProfileId={selectedFundId} accountingBasis={postedLedger.selectedBook?.accountingBasis} />
        </div>
      </details>
    </div>
  );
}

function scopedAccountingWorkflowPath(path: string, search: URLSearchParams): string {
  const next = new URLSearchParams(search);
  next.delete("view");
  const query = next.toString();
  return `${path}${query ? `?${query}` : ""}`;
}

/** Dedicated mutation surfaces share the posted-ledger scope resolver, never a guessed book. */
function AccountingScopedWorkflowScreen({ kind }: { kind: "factor-paydown" | "consolidation" }) {
  const [searchParams] = useSearchParams();
  const postedLedger = useAccountingPostedLedgerViewModel("ledger", undefined, {
    requestedPeriodId: searchParams.get("periodId")
  });
  usePostedLedgerRouteScope(postedLedger, true);
  const bookId = postedLedger.selectedBook?.ledgerBookId ?? "";
  const periodId = postedLedger.periodsSettled ? postedLedger.selectedPeriodId : null;
  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Accounting scope</CardTitle>
          <CardDescription>Choose the governed book and period before preparing a review draft.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2">
          <FormRow label="Ledger book" labelFor={`${kind}-book`}>
            <Select id={`${kind}-book`} value={bookId} onChange={(event) => postedLedger.selectBook(event.target.value)}>
              {postedLedger.view.bookOptions.length ? postedLedger.view.bookOptions.map((option) => (
                <option key={option.id} value={option.id}>{option.label} · {option.baseCurrency}</option>
              )) : <option value="">No ledger book available</option>}
            </Select>
          </FormRow>
          <FormRow label="Ledger period" labelFor={`${kind}-period`} error={postedLedger.view.periodSelector.errorText}>
            <Select id={`${kind}-period`} value={postedLedger.selectedPeriodId ?? ""} onChange={(event) => postedLedger.selectPeriod(event.target.value)}>
              {postedLedger.view.periodSelector.options.length ? postedLedger.view.periodSelector.options.map((option) => (
                <option key={option.id} value={option.id}>{option.label} · {option.statusLabel}</option>
              )) : <option value="">No ledger period available</option>}
            </Select>
          </FormRow>
          <Link className="text-sm text-foreground underline underline-offset-4" to={scopedAccountingWorkflowPath(WORKSTATION_ROUTE_CATALOG.accountingLedger, searchParams)}>Return to Ledger Explorer</Link>
        </CardContent>
      </Card>
      {kind === "factor-paydown" ? (
        <FactorPaydownPanel key={`${bookId}:${periodId ?? "pending"}`} periodVersion={postedLedger.selectedPeriodVersion} active={postedLedger.periodsSettled} bookId={bookId} periodId={periodId} fundProfileId={postedLedger.selectedBook?.fundProfileId ?? ""} accountingBasis={postedLedger.selectedBook?.accountingBasis} bookLabel={postedLedger.selectedBook?.displayName} periodLabel={postedLedger.selectedPeriodLabel} />
      ) : periodId && bookId ? (
        <ConsolidationPanel key={`${bookId}:${periodId}`} initialBookId={bookId} initialPeriodId={periodId} />
      ) : <StatusBanner tone="warning" title="Accounting scope unavailable" detail={postedLedger.view.periodSelector.errorText ?? postedLedger.view.periodSelector.loadingText ?? "Choose an available ledger book and period before preparing consolidation."} />}
      {kind === "consolidation" && postedLedger.selectedBook ? <EntityOperationsPanel key={bookId} book={postedLedger.selectedBook} /> : null}
    </div>
  );
}

export function FactorPaydownScreen() {
  return <AccountingScopedWorkflowScreen kind="factor-paydown" />;
}

export function ConsolidationWorkflowScreen() {
  return <AccountingScopedWorkflowScreen kind="consolidation" />;
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
  const closeRows = items.map((item) => {
    const nextTask = item.nextDueLabel ?? "No next task retained";
    const owner = item.nextDueOwner ?? "Unassigned";
    const due = item.nextDueDate ? presentFinanceDate(item.nextDueDate) : "No due date";
    const summary = `${presentFinancePeriod(item.periodId)}: ${nextTask} · ${presentStatusLabel(item.status)} · owner ${owner} · due ${due} · ${item.blockerCount} blocker(s) · ${item.completedApprovalCount}/${item.requiredApprovalCount} approvals`;
    return { item, summary };
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
      {!loading && !errorText ? (
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="text-base">Close workflow queue</CardTitle>
            <CardDescription>Open a period to acknowledge tasks, submit approvals, or close from the governed workflow.</CardDescription>
          </CardHeader>
          <CardContent>
            {closeRows.length > 0 ? (
              <ul className="space-y-2" aria-label="Close workflow queue">
                {closeRows.map(({ item, summary }) => (
                  <li
                    key={item.workflowId}
                    className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-border/70 bg-secondary/15 px-3 py-2"
                  >
                    <div className="min-w-0 space-y-2">
                      <div className="text-sm text-foreground">{summary}</div>
                      <div className="flex flex-wrap gap-2">
                        <Badge variant={item.blockerCount > 0 ? "warning" : "outline"}>
                          {item.blockerCount} blocker{item.blockerCount === 1 ? "" : "s"}
                        </Badge>
                        <Badge
                          variant={item.requiredApprovalCount > 0 && item.completedApprovalCount >= item.requiredApprovalCount ? "success" : "outline"}
                        >
                          {item.completedApprovalCount}/{item.requiredApprovalCount} approvals
                        </Badge>
                      </div>
                    </div>
                    <Button asChild size="sm" variant="outline">
                      <Link
                        to={resolveCloseWorkflowHref(item)}
                        aria-label={`Open close workflow for ${presentFinancePeriod(item.periodId)}`}
                      >
                        Open close workflow
                      </Link>
                    </Button>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground">No close workflows match the selected scope.</p>
            )}
          </CardContent>
        </Card>
      ) : null}
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

export function ApprovalInboxScreen({ data }: FinanceStandardScreenProps) {
  const closePlan = firstRecord(asRecord(data), "closePlans");
  const approvals = readRecordArray(closePlan, "approvals");
  const approvalRows = approvals.map((approval, index) => {
    const approvalId = readString(approval, "approvalId", "");
    return {
      approvalId,
      label: readString(approval, "label", approvalId || `Approval ${index + 1}`),
      status: readString(approval, "status", "Pending")
    };
  });

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Approval Inbox</CardTitle>
          <CardDescription>Approvals show what is being approved, why it is ready, what changed, supporting evidence, downstream effects, and risk.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Pending approvals" value={String(approvalRows.length)} />
          <FinanceFact label="Queue source" value={approvals.length > 0 ? "Accounting workspace" : "No approval feed supplied"} />
          <FinanceFact label="Decision authority" value="Governed approval workflow" />
        </CardContent>
      </Card>
      {approvalRows.length > 0 ? (
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="text-base">Approval queue</CardTitle>
            <CardDescription>Open a pending approval to review its evidence and record the decision in the governed workflow.</CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="space-y-2" aria-label="Pending approvals">
              {approvalRows.map((row) => (
                <li
                  key={row.approvalId || row.label}
                  className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-border/70 bg-secondary/15 px-3 py-2"
                >
                  <div className="min-w-0">
                    <div className="text-sm font-semibold text-foreground">{row.label}</div>
                    <div className="break-all font-mono text-xs text-muted-foreground">{row.approvalId || "No approval reference retained"}</div>
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge variant={approvalStatusVariant(row.status)}>{presentStatusLabel(row.status)}</Badge>
                    <Button asChild size="sm" variant="outline">
                      <Link
                        to={row.approvalId
                          ? workstationRouteWithQuery("accountingApprovals", { approvalId: row.approvalId })
                          : WORKSTATION_ROUTE_CATALOG.accountingApprovals}
                        aria-label={`Review and decide ${row.label}`}
                      >
                        Review &amp; decide
                      </Link>
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
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
  return formatUtcMonthYear(date);
}

function presentFinanceDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : formatUtcDate(date);
}

function approvalStatusVariant(status: string): "outline" | "warning" | "danger" | "success" {
  const normalized = normalizeStatus(status);
  if (normalized.includes("reject") || normalized.includes("needsfix") || normalized.includes("block")) {
    return "danger";
  }
  if (normalized.includes("approved") || normalized.includes("complete") || normalized.includes("released")) {
    return "success";
  }
  if (normalized.includes("pending") || normalized.includes("await") || normalized.includes("submit") || normalized.includes("review")) {
    return "warning";
  }
  return "outline";
}

/**
 * Prefer the close item's own retained deep link into its governed workflow.
 * Fall back to the Operations Continuity queue scoped to the item's fund
 * account and period when the retained route is missing or points outside the
 * in-app workstation (for example an /api/ read-model path).
 */
function resolveCloseWorkflowHref(item: OperationsCloseCalendarItem): string {
  const route = item.route?.trim();
  if (route && route.startsWith("/") && !route.startsWith("//") && !route.startsWith("/api/")) {
    return route;
  }
  return workstationRouteWithQuery("accountingOperationsContinuity", {
    fundAccountId: item.fundAccountId || null,
    periodId: item.periodId || null
  });
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
