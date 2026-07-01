import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { TabPanel, Tabs } from "@/components/ui/tabs";
import { getRunLedgerJournal } from "@/lib/api";
import { WORKSTATION_ROUTE_CATALOG, workstationRouteWithQuery } from "@/lib/workspace";
import type { AccountingWorkspaceResponse, LedgerJournalLine } from "@/types";

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
  const reporting = asRecord(data?.reporting);
  const run = firstRecord(reporting, "recentRuns");
  const template = firstRecord(reporting, "templates");
  const reportName = readString(run, "reportName", readString(template, "name", "Selected report"));
  const runId = readString(run, "runId", "preview-run");
  const validationWarnings = readStringArray(run, "validationWarnings");
  const generatedFiles = readStringArray(run, "generatedFiles");

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle>Report Preview &amp; Validation</CardTitle>
            <CardDescription>
              Review included rows, blockers, evidence, approvals, and output before a Finance report is released.
            </CardDescription>
          </div>
          <Badge variant={validationWarnings.length > 0 ? "warning" : "success"}>
            {validationWarnings.length > 0 ? "Validation review" : "Ready to run"}
          </Badge>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Report" value={reportName} />
          <FinanceFact label="Run" value={runId} mono />
          <FinanceFact label="Output" value={generatedFiles.length > 0 ? `${generatedFiles.length} file(s)` : "Preview only"} />
          <FinanceFact
            label="Validation"
            value={validationWarnings.length > 0 ? validationWarnings.join(", ") : "No blocking issues"}
          />
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
            items={[
              "Financial statement sections from the selected template.",
              "Ledger lines for the selected book, period, basis, currency, and consolidation level.",
              "Supporting schedules and evidence appendix when selected on Report Parameters."
            ]}
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
            items={[
              "Evidence appendix is checked before final output.",
              "Missing evidence, unlocked evidence, and unresolved proof links block final release."
            ]}
          />
        </TabPanel>
        <TabPanel>
          <FinanceList
            title="Approval checkpoint"
            items={[
              "Report release approval is required for final output.",
              "Approvers see what changed, evidence support, and downstream distribution before sign-off."
            ]}
          />
        </TabPanel>
        <TabPanel>
          <FinanceList
            title="Downstream distribution"
            items={generatedFiles.length > 0 ? generatedFiles : [
              "PDF, XLSX, CSV, Evidence Vault, secure portal, or internal route output is confirmed here."
            ]}
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
  const reportName = readString(run, "reportName", readString(run, "templateName", "Report run"));
  const status = readString(run, "status", "Draft");
  const generatedFiles = readStringArray(run, "generatedFiles");
  const recipients = readStringArray(run, "distributionRecipients");

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle>Report Run Detail</CardTitle>
            <CardDescription>{reportName}</CardDescription>
          </div>
          <Badge variant={status.toLowerCase().includes("fail") ? "danger" : "outline"}>{status}</Badge>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Run ID" value={runId} mono />
          <FinanceFact label="Actor" value={readString(run, "actor", readString(run, "requestedBy", "browser-user"))} />
          <FinanceFact label="Started" value={readString(run, "startedAtUtc", readString(run, "startedAt", "Not started"))} mono />
          <FinanceFact label="Completed" value={readString(run, "completedAtUtc", readString(run, "completedAt", "Not complete"))} mono />
          <FinanceFact label="Input datasets" value={readStringArray(run, "inputDatasets").join(", ") || "Retained reporting datasets"} />
          <FinanceFact label="Warnings" value={String(readStringArray(run, "validationWarnings").length)} />
        </CardContent>
      </Card>

      <section className="grid gap-4 xl:grid-cols-2">
        <FinanceList title="Parameters used" items={reportParameterFields} />
        <FinanceList title="Generated files" items={generatedFiles.length > 0 ? generatedFiles : ["No generated files have been retained for this run yet."]} />
        <FinanceList title="Distribution recipients" items={recipients.length > 0 ? recipients : ["No downstream recipients have been released yet."]} />
        <FinanceList title="Audit trail" items={[
          "Run request captured.",
          "Validation checkpoint retained.",
          "Generated output and distribution attempts are linked from Reporting evidence."
        ]} />
      </section>

      <div className="flex flex-wrap gap-2">
        <Button asChild size="sm" variant="outline">
          <Link to={workstationRouteWithQuery("reportingRunParameters", { cloneRunId: runId })}>Clone parameters</Link>
        </Button>
        <Button asChild size="sm" variant="outline">
          <Link to={WORKSTATION_ROUTE_CATALOG.reportingPreviewValidation}>Open preview</Link>
        </Button>
      </div>
    </div>
  );
}

export function AccountDetailScreen({ data }: FinanceStandardScreenProps) {
  const [searchParams] = useSearchParams();
  const accountId = searchParams.get("accountId") ?? "";
  const trialRows = readRecordArray(asRecord(data), "reconciliationQueue").flatMap((run) => readRecordArray(run, "trialBalance"));
  const account = trialRows.find((row) => (
    readString(row, "accountId", "") === accountId
    || readString(row, "accountCode", "") === accountId
    || readString(row, "accountName", "").toLowerCase() === accountId.toLowerCase()
  )) ?? trialRows[0] ?? null;
  const accountName = readString(account, "accountName", readString(account, "accountLabel", "Selected account"));
  const accountCode = readString(account, "accountCode", readString(account, "accountId", "No account code"));
  const accountType = readString(account, "accountType", "Unclassified");
  const endingBalance = readCurrencyLike(account, "balance", readCurrencyLike(account, "endingBalance", "Unavailable"));

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Account Detail</CardTitle>
          <CardDescription>{accountName}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Account number" value={accountCode} mono />
          <FinanceFact label="Account type" value={accountType} />
          <FinanceFact label="Ending balance" value={endingBalance} mono />
          <FinanceFact label="Beginning balance" value="Prior close balance" />
          <FinanceFact label="Period activity" value="Ledger activity for selected period" />
          <FinanceFact label="Reconciliation" value="Reconciled vs unreconciled review" />
        </CardContent>
      </Card>
      <section className="grid gap-4 xl:grid-cols-2">
        <FinanceList title="Monthly trend" items={["Current month", "Prior month", "Quarter-to-date", "Year-to-date"]} />
        <FinanceList title="Related journal entries" items={["Open Journal Entry Detail from ledger activity rows.", "Manual and system-generated entries stay linked to evidence."]} />
        <FinanceList title="Evidence gaps" items={["Missing support, unmatched reconciliation items, and stale source files appear here."]} />
        <FinanceList title="Report lines using this account" items={["Trial Balance", "Balance Sheet", "Cash Activity", "Audit Support Pack"]} />
      </section>
      <div className="flex flex-wrap gap-2">
        <Button asChild size="sm" variant="outline">
          <Link to={WORKSTATION_ROUTE_CATALOG.accountingTrialBalance}>Back to Trial Balance</Link>
        </Button>
        <Button asChild size="sm" variant="outline">
          <Link to={WORKSTATION_ROUTE_CATALOG.accountingLedger}>Open ledger activity</Link>
        </Button>
        <Button asChild size="sm" variant="outline">
          <Link to={WORKSTATION_ROUTE_CATALOG.accountingEvidenceDetail}>Review evidence detail</Link>
        </Button>
      </div>
    </div>
  );
}

export function LedgerExplorerScreen({ data }: FinanceStandardScreenProps) {
  const [searchParams, setSearchParams] = useSearchParams();
  const runs = readRecordArray(asRecord(data), "reconciliationQueue");
  const selectedRunId = searchParams.get("runId") ?? readString(runs[0] ?? null, "runId", "");
  const [searchText, setSearchText] = useState("");
  const [savedView, setSavedView] = useState("Today");
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
        <CardContent className="grid gap-3 lg:grid-cols-[minmax(0,1.5fr)_repeat(3,minmax(150px,0.5fr))]">
          <FormRow label="Search by account, amount, journal ID, source, security, entity" labelFor="ledger-search">
            <Input
              id="ledger-search"
              type="search"
              value={searchText}
              onChange={(event) => setSearchText(event.target.value)}
              placeholder="Cash, je-cash-1, 120500, AAPL"
            />
          </FormRow>
          <FormRow label="Date / period" labelFor="ledger-run-select">
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
            {["Today", "Unposted", "Reversals", "Manual JEs", "System Generated"].map((view) => (
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
                    <td className="px-3 py-2 font-mono text-xs">{line.timestamp}</td>
                    <td className="px-3 py-2">
                      <Link
                        className="font-semibold text-primary underline-offset-2 hover:underline"
                        to={workstationRouteWithQuery("accountingJournalEntryDetail", {
                          journalEntryId: line.journalEntryId,
                          runId: selectedRunId || null
                        })}
                      >
                        {line.journalEntryId}
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
        </CardContent>
      </Card>
    </div>
  );
}

export function ReconciliationMatchWorkbenchScreen({ data }: FinanceStandardScreenProps) {
  const breaks = readRecordArray(asRecord(data), "breakQueue");
  const selectedBreak = breaks[0] ?? null;
  const breakLabel = readString(selectedBreak, "label", readString(selectedBreak, "breakId", "No open break selected"));

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Reconciliation Match Workbench</CardTitle>
          <CardDescription>Clear breaks by matching source records to ledger records with suggested adjustments.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Open breaks" value={String(breaks.length)} />
          <FinanceFact label="Selected break" value={breakLabel} />
          <FinanceFact label="Adjustment candidate" value="Create adjustment journal" />
        </CardContent>
      </Card>
      <section className="grid gap-4 xl:grid-cols-[1fr_0.8fr_1fr]">
        <FinanceList title="Source statement / provider records" items={["Bank statement row", "Broker/provider record", "Source file extraction"]} />
        <FinanceList title="Suggested matches" items={["Match", "Split", "Ignore with reason", "Create adjustment journal"]} />
        <FinanceList title="Ledger records" items={["Posted ledger line", "Unposted journal", "Manual adjustment"]} />
      </section>
      <FinanceList title="Exceptions and adjustment candidates" items={[
        "Attach evidence before resolution.",
        "Send material adjustments for approval.",
        "Resolved breaks return to the close checklist."
      ]} />
      <div className="flex flex-wrap gap-2">
        {["Match", "Split", "Ignore with reason", "Create adjustment journal", "Attach evidence", "Send for approval", "Mark resolved"].map((action) => (
          <Button key={action} type="button" size="sm" variant="outline">{action}</Button>
        ))}
      </div>
    </div>
  );
}

export function CloseCalendarScreen({ data }: FinanceStandardScreenProps) {
  const accounting = asRecord(data);
  const closePlan = firstRecord(accounting, "closePlans") ?? readRecord(accounting, "operationsCloseCalendar");
  const tasks = readRecordArray(closePlan, "tasks");
  const milestones = readRecordArray(closePlan, "closeCalendar");
  const items = tasks.length > 0
    ? tasks.map((task) => `${readString(task, "label", readString(task, "taskName", "Close task"))} - ${readString(task, "status", "Pending")} - owner ${readString(task, "owner", "Controller")} - due ${readString(task, "dueDate", readString(task, "dueAt", "TBD"))}`)
    : [
        "Import bank statements - owner Cash operations - due Day 1",
        "Complete reconciliation - owner Fund accountant - due Day 2",
        "Review exceptions - owner Controller - due Day 2",
        "Post accruals - owner Accounting - due Day 3",
        "Run trial balance - owner Controller - due Day 3",
        "Generate report pack - owner Reporting - due Day 4",
        "Controller approval - owner Controller - due Day 4",
        "Lock period - owner Finance ops - due Day 5"
      ];

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Close Calendar</CardTitle>
          <CardDescription>Month-end tasks by owner, due date, blocker, evidence, dependency, and sign-off state.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Current close period" value={readString(closePlan, "period", readString(closePlan, "closePeriod", "Current period"))} />
          <FinanceFact label="Tasks" value={String(items.length)} />
          <FinanceFact label="Milestones" value={String(milestones.length)} />
        </CardContent>
      </Card>
      <FinanceList title="Close checklist" items={items} />
      <FinanceList title="Required evidence and sign-off state" items={[
        "Owner, due date, status, blocker, required evidence, dependency, and sign-off state stay visible on each close task.",
        "Blocked tasks route to reconciliation, journal entry, report preview, approval, or evidence detail."
      ]} />
      <FinanceList title="Dependencies and blockers" items={[
        "Reconciliation must clear before report release.",
        "Late adjustments require approval evidence.",
        "Period lock waits on controller sign-off."
      ]} />
    </div>
  );
}

export function EvidenceDetailScreen() {
  const [searchParams] = useSearchParams();
  const evidenceId = searchParams.get("evidenceId") ?? "selected-evidence";

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Evidence Detail</CardTitle>
          <CardDescription>Document-level evidence can support or block work; it does not approve, post, or release work.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Document name" value={evidenceId} mono />
          <FinanceFact label="Classification" value="Accounting support" />
          <FinanceFact label="Review status" value="Needs review" />
          <FinanceFact label="Source" value="Evidence Vault" />
          <FinanceFact label="Uploaded by" value="browser-user" />
          <FinanceFact label="Audit trail" value="Retained" />
        </CardContent>
      </Card>
      <section className="grid gap-4 xl:grid-cols-2">
        <FinanceList title="Extracted fields" items={["Entity", "Period", "Amount", "Counterparty", "Document date"]} />
        <FinanceList title="Linked journal entries" items={["Journal Entry Detail", "Manual JE evidence attachments"]} />
        <FinanceList title="Linked reconciliation cases" items={["Open break case", "Resolved match decision"]} />
        <FinanceList title="Linked reports" items={["Report Preview & Validation", "Report Run Detail", "Evidence Binder"]} />
        <FinanceList title="Audit trail" items={["Immutable source record retained.", "Review events and support requests stay linked without granting approval authority."]} />
      </section>
    </div>
  );
}

export function ApprovalInboxScreen({ data }: FinanceStandardScreenProps) {
  const closePlan = firstRecord(asRecord(data), "closePlans");
  const approvals = readRecordArray(closePlan, "approvals");
  const approvalItems = approvals.length > 0
    ? approvals.map((approval) => `${readString(approval, "label", readString(approval, "approvalId", "Approval"))}: ${readString(approval, "status", "Pending")}`)
    : [
        "Journal entry approval",
        "Close task sign-off",
        "Report release approval",
        "Reconciliation resolution",
        "Period lock approval",
        "Evidence acceptance"
      ];

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Approval Inbox</CardTitle>
          <CardDescription>Approvals show what is being approved, why it is ready, what changed, supporting evidence, downstream effects, and risk.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          <FinanceFact label="Pending approvals" value={String(approvalItems.length)} />
          <FinanceFact label="Evidence support" value="Required" />
          <FinanceFact label="After approval" value="Post, release, resolve, lock, or accept" />
        </CardContent>
      </Card>
      <FinanceList title="Approval queue" items={approvalItems} />
      <FinanceList title="Reviewer checklist" items={[
        "What am I approving?",
        "Why is it ready?",
        "What changed?",
        "What evidence supports it?",
        "What happens after approval?",
        "What are the risks?"
      ]} />
    </div>
  );
}

export function ReportParametersCoveragePanel() {
  return <FinanceList title="Standard report parameters" items={reportParameterFields} />;
}

export function ReportLibraryCoveragePanel() {
  return <FinanceList title="Report categories" items={financeReportCategories} />;
}

function FinanceFact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
      <div className="text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">{label}</div>
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

function readStringArray(record: UnknownRecord | null, key: string): string[] {
  const value = record?.[key];
  return Array.isArray(value) ? value.map((item) => String(item)).filter((item) => item.trim().length > 0) : [];
}

function readCurrencyLike(record: UnknownRecord | null, key: string, fallback: string): string {
  const value = record?.[key];
  if (typeof value === "string" && value.trim().length > 0) {
    return value;
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value);
  }
  return fallback;
}

function formatCurrency(value: number): string {
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value);
}
