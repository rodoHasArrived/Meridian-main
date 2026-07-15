import { Badge } from "@/components/ui/badge";
import { Accordion } from "@/components/ui/accordion";
import type { ReportingRunStatusRow } from "@/screens/reporting-screen.view-model";

interface ReportingRunStatusModuleProps {
  run: ReportingRunStatusRow;
}

export function ReportingRunAuditDisclosure({ run }: ReportingRunStatusModuleProps) {
  return (
    <Accordion
      className="mt-3"
      items={[{
        id: `${run.id}-audit-lineage`,
        title: "Audit, lineage, and artifacts",
        badge: run.artifactLabel,
        content: (
          <div className="space-y-3">
            <dl className="grid gap-3 text-xs sm:grid-cols-2 xl:grid-cols-3" aria-label={`${run.id} audit metadata`}>
              <div>
                <dt className="font-medium text-muted-foreground">Run ID</dt>
                <dd className="mt-1 break-all font-mono text-foreground">{run.runIdLabel}</dd>
              </div>
              <ReportingRunVersionFields run={run} />
              <div>
                <dt className="font-medium text-muted-foreground">Template</dt>
                <dd className="mt-1 break-all font-mono text-foreground">{run.templateLabel}</dd>
              </div>
              <div>
                <dt className="font-medium text-muted-foreground">As of</dt>
                <dd className="mt-1 font-mono text-foreground">{run.asOfDateLabel}</dd>
              </div>
              <div>
                <dt className="font-medium text-muted-foreground">Trigger</dt>
                <dd className="mt-1 text-foreground">{run.trigger}</dd>
              </div>
              <div>
                <dt className="font-medium text-muted-foreground">Attempts</dt>
                <dd className="mt-1 text-foreground">{run.attemptLabel}</dd>
              </div>
              <div>
                <dt className="font-medium text-muted-foreground">Sections</dt>
                <dd className="mt-1 text-foreground">{run.sectionLabel}</dd>
              </div>
              <div>
                <dt className="font-medium text-muted-foreground">Lineage</dt>
                <dd className="mt-1 text-foreground">{run.lineageLabel}</dd>
              </div>
              <div className="sm:col-span-2 xl:col-span-3">
                <dt className="font-medium text-muted-foreground">Artifacts</dt>
                <dd className="mt-1 break-all font-mono text-foreground">
                  {run.hasArtifacts ? `${run.artifactLabel}: ${run.artifactNames.join(", ")}` : run.artifactLabel}
                </dd>
              </div>
              <div className="sm:col-span-2 xl:col-span-3">
                <dt className="font-medium text-muted-foreground">Dataset source</dt>
                <dd className="mt-1 break-all font-mono text-foreground">{run.datasetSourceLabel}</dd>
              </div>
              <div className="sm:col-span-2 xl:col-span-3">
                <dt className="font-medium text-muted-foreground">Generated grids</dt>
                <dd className="mt-1 break-all font-mono text-foreground">
                  {run.hasGeneratedGrids ? `${run.generatedGridLabel}: ${run.generatedGridNames.join(", ")}` : run.generatedGridLabel}
                </dd>
                <ReportingGeneratedGridExportLinks run={run} />
              </div>
            </dl>
            {run.hasDrilldownLinks ? (
              <div className="flex flex-wrap gap-2" aria-label={`${run.id} drilldown links`}>
                {run.drilldownLinks.map((link) => link.isBrowserNavigable ? (
                  <a
                    key={link.id}
                    href={link.href}
                    target="_blank"
                    rel="noreferrer"
                    aria-label={link.ariaLabel}
                    className="inline-flex min-h-9 min-w-0 items-center gap-2 rounded-sm border border-border/70 bg-secondary/35 px-2.5 py-1.5 text-xs text-foreground hover:bg-secondary/55 focus:outline-none focus:ring-2 focus:ring-primary/40"
                  >
                    <Badge variant="outline">{link.kind}</Badge>
                    <span className="truncate">{link.label}</span>
                  </a>
                ) : (
                  <span
                    key={link.id}
                    role="group"
                    aria-label={link.ariaLabel}
                    className="inline-flex min-h-9 min-w-0 items-center gap-2 rounded-sm border border-border/60 bg-secondary/20 px-2.5 py-1.5 text-xs text-muted-foreground"
                  >
                    <Badge variant="outline">{link.kind}</Badge>
                    <span className="truncate">{link.label}</span>
                  </span>
                ))}
              </div>
            ) : null}
          </div>
        )
      }]}
    />
  );
}

export function ReportingRunVersionFields({ run }: ReportingRunStatusModuleProps) {
  return (
    <>
      <div>
        <dt className="font-medium text-muted-foreground">Run series</dt>
        <dd className="break-all font-mono text-foreground">{run.runSeriesLabel}</dd>
      </div>
      <div>
        <dt className="font-medium text-muted-foreground">Run version</dt>
        <dd className="text-foreground">{run.runAttemptLabel}</dd>
      </div>
      <div>
        <dt className="font-medium text-muted-foreground">Latest generated</dt>
        <dd className="break-all font-mono text-foreground">{run.latestGeneratedLabel}</dd>
      </div>
      <div>
        <dt className="font-medium text-muted-foreground">Latest approved</dt>
        <dd className="break-all font-mono text-foreground">{run.latestApprovedLabel}</dd>
      </div>
      <div>
        <dt className="font-medium text-muted-foreground">Prior attempt</dt>
        <dd className="break-all font-mono text-foreground">{run.priorRunLabel}</dd>
      </div>
      <div>
        <dt className="font-medium text-muted-foreground">Retry reason</dt>
        <dd className="text-foreground">{run.retryReasonLabel}</dd>
      </div>
      <div>
        <dt className="font-medium text-muted-foreground">Changed lines</dt>
        <dd className="text-foreground">{run.changedLineLabel}</dd>
      </div>
      <div className="sm:col-span-2 xl:col-span-3">
        <dt className="font-medium text-muted-foreground">Comparison</dt>
        <dd className="break-words text-foreground">{run.comparisonSummary}</dd>
      </div>
    </>
  );
}

export function ReportingGeneratedGridExportLinks({ run }: ReportingRunStatusModuleProps) {
  if (run.generatedGridArtifacts.length === 0) {
    return null;
  }

  return (
    <div className="mt-1 flex flex-wrap gap-1.5" aria-label={`${run.id} report-writer grid exports`}>
      {run.generatedGridArtifacts.map((grid) => (
        <span
          key={grid.id}
          className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/60 bg-secondary/20 px-2.5 py-1.5 text-xs text-muted-foreground"
        >
          <span className="max-w-[16rem] truncate text-foreground">{grid.label}</span>
          <a className="text-primary underline-offset-2 hover:underline" href={grid.jsonHref} target="_blank" rel="noreferrer">
            JSON
          </a>
          <a className="text-primary underline-offset-2 hover:underline" href={grid.csvHref} target="_blank" rel="noreferrer">
            CSV
          </a>
          <a className="text-primary underline-offset-2 hover:underline" href={grid.pdfHref} target="_blank" rel="noreferrer">
            PDF
          </a>
          <a className="text-primary underline-offset-2 hover:underline" href={grid.xlsHref} target="_blank" rel="noreferrer">
            XLS
          </a>
          <a className="text-primary underline-offset-2 hover:underline" href={grid.xlsxHref} target="_blank" rel="noreferrer">
            XLSX
          </a>
        </span>
      ))}
    </div>
  );
}
