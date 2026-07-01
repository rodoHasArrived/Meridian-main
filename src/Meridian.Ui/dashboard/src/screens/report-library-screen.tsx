import { useMemo } from "react";
import { Link } from "react-router-dom";
import { ReportingHub } from "@/components/meridian/reporting-hub";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { buildReportingHubModel } from "@/lib/reporting-hub";
import { workstationRouteWithQuery } from "@/lib/workspace";
import { buildRunStatusRows, buildTemplateRows, type ReportingRunStatusRow, type ReportingTemplateRow } from "@/screens/reporting-screen.view-model";
import type { AccountingWorkspaceResponse } from "@/types";

interface ReportLibraryScreenProps {
  data: AccountingWorkspaceResponse | null;
}

const standardReportCategories = [
  "Financial Statements",
  "Investor Reporting",
  "Reconciliation",
  "Operations",
  "Exceptions",
  "Tax",
  "Audit",
  "Custom"
];

const standardReportCatalog = [
  {
    name: "Trial Balance",
    family: "Financial Statements",
    produces: "Account-balance proof with debit, credit, and ending balance support.",
    requiredData: "Ledger, chart of accounts, accounting basis, period close posture",
    owner: "Controller",
    keywords: ["trial", "balance"]
  },
  {
    name: "Balance Sheet",
    family: "Financial Statements",
    produces: "Assets, liabilities, and capital roll-forward for the selected scope.",
    requiredData: "Ledger, consolidation, valuation, period close posture",
    owner: "Controller",
    keywords: ["balance sheet", "statement of financial position"]
  },
  {
    name: "Income Statement",
    family: "Financial Statements",
    produces: "Period P&L, income, expense, realized, and unrealized activity.",
    requiredData: "Ledger, accounting basis, realized/unrealized P&L, allocations",
    owner: "Fund accounting",
    keywords: ["income", "profit", "loss", "p&l"]
  },
  {
    name: "Cash Activity",
    family: "Operations",
    produces: "Opening cash, inflows, outflows, financing, and ending cash tie-out.",
    requiredData: "Bank activity, brokerage cash, ledger cash, financing records",
    owner: "Cash operations",
    keywords: ["cash"]
  },
  {
    name: "Capital Account Statement",
    family: "Investor Reporting",
    produces: "Investor capital, allocations, contributions, withdrawals, and NAV.",
    requiredData: "Investor registry, allocations, capital activity, NAV support",
    owner: "Investor reporting",
    keywords: ["capital", "account"]
  },
  {
    name: "Investor Statement",
    family: "Investor Reporting",
    produces: "Investor-facing statement package with evidence-backed values.",
    requiredData: "Investor registry, report pack, delivery rules, approvals",
    owner: "Investor reporting",
    keywords: ["investor", "statement"]
  },
  {
    name: "Reconciliation Summary",
    family: "Reconciliation",
    produces: "Open breaks, matched items, adjustments, and unresolved blockers.",
    requiredData: "Provider statements, ledger activity, match decisions, evidence",
    owner: "Fund accounting",
    keywords: ["reconciliation", "match", "break"]
  },
  {
    name: "Exception Report",
    family: "Exceptions",
    produces: "A queue of stale, blocked, unreconciled, or approval-pending items.",
    requiredData: "Close tasks, reconciliation breaks, approvals, evidence gaps",
    owner: "Controller",
    keywords: ["exception", "blocker", "warning"]
  },
  {
    name: "Evidence Binder",
    family: "Audit",
    produces: "A retained support package linked to reports, journals, and close work.",
    requiredData: "Evidence vault, journal links, report runs, audit trail",
    owner: "Audit support",
    keywords: ["evidence", "binder", "audit"]
  },
  {
    name: "Tax Package",
    family: "Tax",
    produces: "Tax-support extracts for investor, entity, and activity review.",
    requiredData: "Investor registry, realized activity, allocations, tax elections",
    owner: "Tax",
    keywords: ["tax"]
  },
  {
    name: "Custom Report",
    family: "Custom",
    produces: "A configurable report writer template for ad hoc Finance analysis.",
    requiredData: "Approved report writer dataset and retained template metadata",
    owner: "Finance ops",
    keywords: ["custom", "ad hoc"]
  }
];

export function ReportLibraryScreen({ data }: ReportLibraryScreenProps) {
  const reporting = data?.reporting ?? null;
  const templateRows = useMemo(() => buildTemplateRows(reporting?.templates ?? []), [reporting?.templates]);
  const runStatusRows = useMemo(() => buildRunStatusRows(reporting?.recentRuns ?? []), [reporting?.recentRuns]);
  const hubModel = useMemo(
    () => buildReportingHubModel(runStatusRows, templateRows, reporting?.dailyWork ?? []),
    [runStatusRows, templateRows, reporting?.dailyWork]
  );

  const templatesByFamily = useMemo(() => {
    const groups = new Map<string, typeof templateRows>();
    for (const template of templateRows) {
      const family = template.family.trim() || "Uncategorized";
      const bucket = groups.get(family);
      if (bucket) {
        bucket.push(template);
      } else {
        groups.set(family, [template]);
      }
    }
    return groups;
  }, [templateRows]);

  if (!data) {
    return (
      <Card
        className="panel-surface"
        role="status"
        aria-busy="true"
        aria-live="polite"
        aria-labelledby="report-library-loading-title"
      >
        <CardHeader>
          <CardTitle id="report-library-loading-title">Loading Report Library</CardTitle>
          <CardDescription>Waiting for reporting workspace data.</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <div className="space-y-4">
      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Report Library</CardTitle>
          <CardDescription>Select a standard Finance report, then configure its parameters before preview, validation, run, and release.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap gap-2" aria-label="Report categories">
            {standardReportCategories.map((category) => (
              <Badge key={category} variant="outline">{category}</Badge>
            ))}
          </div>
          <section className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" aria-label="Standard report catalog">
            {standardReportCatalog.map((report) => {
              const template = findTemplateForStandardReport(report, templateRows);
              const lastRun = findLastRunLabel(report, runStatusRows);
              return (
                <article key={report.name} className="rounded-md border border-border/70 bg-secondary/15 p-3">
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <h3 className="text-sm font-semibold text-foreground">{report.name}</h3>
                      <p className="mt-1 text-xs text-muted-foreground">{report.family}</p>
                    </div>
                    <Badge variant={template ? "success" : "warning"}>{template ? "Runnable" : "Needs template"}</Badge>
                  </div>
                  <dl className="mt-3 grid gap-2 text-xs">
                    <ReportLibraryFact label="Produces" value={report.produces} />
                    <ReportLibraryFact label="Required data" value={report.requiredData} />
                    <ReportLibraryFact label="Last run" value={lastRun} />
                    <ReportLibraryFact label="Owner" value={report.owner} />
                  </dl>
                  <div className="mt-3">
                    <Button asChild key={template?.id ?? report.name} size="sm" variant={template ? "default" : "outline"}>
                      <Link
                        to={template
                          ? workstationRouteWithQuery("reportingRunParameters", { templateId: template.id })
                          : workstationRouteWithQuery("reportingRunParameters", { report: report.name })}
                      >
                        {template ? `Run ${template.name}` : `Set up ${report.name}`}
                      </Link>
                    </Button>
                  </div>
                </article>
              );
            })}
          </section>
        </CardContent>
      </Card>

      <ReportingHub model={hubModel} />

      {templatesByFamily.size > 0 ? (
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Run a report</CardTitle>
            <CardDescription>Pick a template to open the guided parameters and readiness flow.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {Array.from(templatesByFamily.entries()).map(([family, templates]) => (
              <div key={family}>
                <h3 className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{family}</h3>
                <div className="mt-2 flex flex-wrap gap-2">
                  {templates.map((template) => (
                    <Button asChild key={template.id} size="sm" variant="outline">
                      <Link to={workstationRouteWithQuery("reportingRunParameters", { templateId: template.id })}>
                        Run {template.name}
                      </Link>
                    </Button>
                  ))}
                </div>
              </div>
            ))}
          </CardContent>
        </Card>
      ) : null}
    </div>
  );
}

function ReportLibraryFact({ label, value }: { label: string; value: string }) {
  return (
    <div className="grid gap-1">
      <dt className="font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</dt>
      <dd className="text-foreground">{value}</dd>
    </div>
  );
}

function findTemplateForStandardReport(
  report: (typeof standardReportCatalog)[number],
  templates: ReportingTemplateRow[]
): ReportingTemplateRow | null {
  const terms = [report.name, ...report.keywords].map(normalizeReportText);
  return templates.find((template) => {
    const haystack = normalizeReportText(`${template.name} ${template.templateName} ${template.family}`);
    return terms.some((term) => term.length > 0 && haystack.includes(term));
  }) ?? null;
}

function findLastRunLabel(
  report: (typeof standardReportCatalog)[number],
  runs: ReportingRunStatusRow[]
): string {
  const terms = [report.name, ...report.keywords].map(normalizeReportText);
  const match = runs.find((run) => {
    const haystack = normalizeReportText(`${run.templateLabel} ${run.templateId} ${run.family}`);
    return terms.some((term) => term.length > 0 && haystack.includes(term));
  });
  return match ? `${match.status} (${match.asOfDateLabel})` : "No retained run";
}

function normalizeReportText(value: string | null | undefined): string {
  return (value ?? "").toLowerCase().replace(/[^a-z0-9]+/g, " ").trim();
}
