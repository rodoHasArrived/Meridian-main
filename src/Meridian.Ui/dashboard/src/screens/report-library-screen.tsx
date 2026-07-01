import { useMemo } from "react";
import { Link } from "react-router-dom";
import { ReportingHub } from "@/components/meridian/reporting-hub";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { buildReportingHubModel } from "@/lib/reporting-hub";
import { workstationRouteWithQuery } from "@/lib/workspace";
import { buildRunStatusRows, buildTemplateRows } from "@/screens/reporting-screen.view-model";
import type { AccountingWorkspaceResponse } from "@/types";

interface ReportLibraryScreenProps {
  data: AccountingWorkspaceResponse | null;
}

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
