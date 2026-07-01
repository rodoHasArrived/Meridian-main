import { useMemo } from "react";
import { Link } from "react-router-dom";
import { ReportingHub } from "@/components/meridian/reporting-hub";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { buildReportingHubModel } from "@/lib/reporting-hub";
import { workstationRouteWithQuery } from "@/lib/workspace";
import { buildRunStatusRows, buildTemplateRows, type ReportingTemplateRow } from "@/screens/reporting-screen.view-model";
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
  { name: "Trial Balance", family: "Financial Statements", produces: "Account-balance proof with debit, credit, and ending balance support.", requiredData: "Ledger, chart of accounts, accounting basis, period close posture" },
  { name: "Balance Sheet", family: "Financial Statements", produces: "Asset, liability, and equity statement by entity and consolidation level.", requiredData: "Trial balance, entity hierarchy, currency policy" },
  { name: "Income Statement", family: "Financial Statements", produces: "Revenue, expense, and net-income statement for the selected period.", requiredData: "Ledger activity, account mapping, close adjustments" },
  { name: "Cash Activity", family: "Operations", produces: "Cash movement, financing, and broker/custodian variance package.", requiredData: "Bank/provider records, ledger cash, reconciliation state" },
  { name: "Capital Account Statement", family: "Investor Reporting", produces: "Investor capital-account roll-forward and allocation support.", requiredData: "Capital account ledger, fund events, allocation policy" },
  { name: "Investor Statement", family: "Investor Reporting", produces: "Investor-facing statement package with retained evidence.", requiredData: "Investor register, capital account balances, report approval" },
  { name: "Reconciliation Summary", family: "Reconciliation", produces: "Matched, unmatched, adjusted, and unresolved reconciliation posture.", requiredData: "Source statement records, ledger lines, break decisions" },
  { name: "Exception Report", family: "Exceptions", produces: "Open blockers, owners, SLA, risk, and next action by workflow.", requiredData: "Exception queue, close calendar, approval state" },
  { name: "Evidence Binder", family: "Audit", produces: "Frozen supporting-document package for a close, report, tax, or audit request.", requiredData: "Evidence Vault documents, manifest, linked work objects" },
  { name: "Audit Support Pack", family: "Audit", produces: "Audit-ready ledger, reconciliation, evidence, and approval support pack.", requiredData: "Ledger, approvals, evidence manifest, audit trail" }
];

export function ReportLibraryScreen({ data }: ReportLibraryScreenProps) {
  const reporting = data?.reporting ?? null;
  const templateRows = useMemo(() => buildTemplateRows(reporting?.templates ?? []), [reporting?.templates]);
  const runStatusRows = useMemo(() => buildRunStatusRows(reporting?.recentRuns ?? []), [reporting?.recentRuns]);
  const hubModel = useMemo(
    () => buildReportingHubModel(runStatusRows, templateRows, reporting?.dailyWork ?? []),
    [runStatusRows, templateRows, reporting?.dailyWork]
  );

  const templatesByFamily = useMemo(() => groupTemplatesByFamily(templateRows), [templateRows]);

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
      <ReportingHub model={hubModel} />

      <Card className="panel-surface">
        <CardHeader>
          <CardTitle>Report Library</CardTitle>
          <CardDescription>Choose what Finance wants to run, then open parameters and readiness before output.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex flex-wrap gap-2" aria-label="Report categories">
            {standardReportCategories.map((category) => (
              <Badge key={category} variant={templatesByFamily.has(category) ? "success" : "outline"}>
                {category}
              </Badge>
            ))}
          </div>
        </CardContent>
      </Card>

      <section className="grid gap-4 xl:grid-cols-2" aria-label="Standard report catalog">
        {standardReportCatalog.map((report) => {
          const template = findTemplateForStandardReport(templateRows, report.name, report.family);
          const lastRun = findLastRunLabel(runStatusRows, report.name);
          return (
            <Card key={report.name} className="panel-surface">
              <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <CardTitle className="text-base">{report.name}</CardTitle>
                  <CardDescription>{report.family}</CardDescription>
                </div>
                <Badge variant={template?.canRunOnDemand ? "success" : template ? "warning" : "outline"}>
                  {template?.canRunOnDemand ? "Ready" : template ? template.statusLabel : "Template needed"}
                </Badge>
              </CardHeader>
              <CardContent className="space-y-3">
                <ReportLibraryFact label="Produces" value={report.produces} />
                <ReportLibraryFact label="Required data" value={report.requiredData} />
                <ReportLibraryFact label="Last run" value={lastRun} />
                <ReportLibraryFact label="Owner" value={resolveReportOwner(report.family)} />
                {template ? (
                  <Button asChild size="sm" variant="outline">
                    <Link to={workstationRouteWithQuery("reportingRunParameters", { templateId: template.id })}>
                      Run {template.name}
                    </Link>
                  </Button>
                ) : (
                  <Button asChild size="sm" variant="outline">
                    <Link to={workstationRouteWithQuery("reportingRunParameters", { report: report.name })}>
                      Set up {report.name}
                    </Link>
                  </Button>
                )}
              </CardContent>
            </Card>
          );
        })}
      </section>
    </div>
  );
}

function groupTemplatesByFamily(templateRows: ReportingTemplateRow[]): Map<string, ReportingTemplateRow[]> {
  const groups = new Map<string, ReportingTemplateRow[]>();
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
}

function findTemplateForStandardReport(
  templateRows: ReportingTemplateRow[],
  reportName: string,
  family: string
): ReportingTemplateRow | null {
  const normalizedReportName = normalizeReportText(reportName);
  return templateRows.find((template) => (
    template.family === family
    && (normalizeReportText(template.name).includes(normalizedReportName)
      || normalizedReportName.includes(normalizeReportText(template.name).replace("pack", "").trim()))
  )) ?? null;
}

function findLastRunLabel(runStatusRows: ReturnType<typeof buildRunStatusRows>, reportName: string): string {
  const normalizedReportName = normalizeReportText(reportName);
  const row = runStatusRows.find((run) => (
    normalizeReportText(run.templateLabel).includes(normalizedReportName)
    || normalizeReportText(run.templateId).includes(normalizedReportName)
  ));
  return row ? `${row.status} - ${row.asOfDateLabel}` : "No retained run";
}

function resolveReportOwner(family: string): string {
  if (family === "Investor Reporting") {
    return "Investor reporting";
  }
  if (family === "Audit" || family === "Tax") {
    return `${family} support`;
  }
  return "Controller";
}

function normalizeReportText(value: string | null | undefined): string {
  return String(value ?? "").toLowerCase().replace(/[^a-z0-9]+/g, " ").trim();
}

function ReportLibraryFact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">{label}</div>
      <div className="mt-1 text-sm text-foreground">{value}</div>
    </div>
  );
}
