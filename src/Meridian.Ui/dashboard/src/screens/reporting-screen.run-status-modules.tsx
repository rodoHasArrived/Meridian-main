import type { ReportingRunStatusRow } from "@/screens/reporting-screen.view-model";

interface ReportingRunStatusModuleProps {
  run: ReportingRunStatusRow;
}

export function ReportingRunVersionFields({ run }: ReportingRunStatusModuleProps) {
  return (
    <>
      <div>
        <dt className="text-[11px] uppercase text-muted-foreground">Run series</dt>
        <dd className="break-all font-mono text-foreground">{run.runSeriesLabel}</dd>
      </div>
      <div>
        <dt className="text-[11px] uppercase text-muted-foreground">Run version</dt>
        <dd className="text-foreground">{run.runAttemptLabel}</dd>
      </div>
      <div>
        <dt className="text-[11px] uppercase text-muted-foreground">Latest generated</dt>
        <dd className="break-all font-mono text-foreground">{run.latestGeneratedLabel}</dd>
      </div>
      <div>
        <dt className="text-[11px] uppercase text-muted-foreground">Latest approved</dt>
        <dd className="break-all font-mono text-foreground">{run.latestApprovedLabel}</dd>
      </div>
      <div>
        <dt className="text-[11px] uppercase text-muted-foreground">Prior attempt</dt>
        <dd className="break-all font-mono text-foreground">{run.priorRunLabel}</dd>
      </div>
      <div>
        <dt className="text-[11px] uppercase text-muted-foreground">Retry reason</dt>
        <dd className="text-foreground">{run.retryReasonLabel}</dd>
      </div>
      <div>
        <dt className="text-[11px] uppercase text-muted-foreground">Changed lines</dt>
        <dd className="text-foreground">{run.changedLineLabel}</dd>
      </div>
      <div className="sm:col-span-2 xl:col-span-3">
        <dt className="text-[11px] uppercase text-muted-foreground">Comparison</dt>
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
          className="inline-flex min-w-0 items-center gap-1.5 rounded-sm border border-border/60 bg-secondary/20 px-2 py-1 text-[11px] text-muted-foreground"
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
