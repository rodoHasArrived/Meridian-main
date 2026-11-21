import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";
import { getReportRunReview, type ReportRunReview, type ReportReviewChange } from "@/lib/report-run-review-api";
import { workstationRouteWithQuery } from "@/lib/workspace";

export function ReportReviewPanel({ runId, version, onReview }: {
  runId: string; version?: number; onReview?: (review: ReportRunReview | null) => void;
}) {
  const [review, setReview] = useState<ReportRunReview | null>(null);
  const [error, setError] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setReview(null); setError(false); onReview?.(null);
    getReportRunReview(runId, { signal: controller.signal }).then((value) => {
      if (controller.signal.aborted) return;
      if (version !== undefined && value.version !== version) throw new Error("Review version mismatch");
      setReview(value); onReview?.(value);
    }).catch(() => { if (!controller.signal.aborted) setError(true); });
    return () => controller.abort();
  }, [runId, version, retry, onReview]);
  return <Card className="panel-surface" aria-label="Changes since prior approved run">
    <CardHeader><CardTitle>Changes since prior approved run</CardTitle><CardDescription>
      Compare retained parameters, certified rows and report lines before approval.
    </CardDescription></CardHeader>
    <CardContent className="space-y-4">
      {error ? <><StatusBanner tone="warning" title="Review evidence unavailable" detail="The exact retained comparison could not be loaded. Retry before approving this run." />
        <Button variant="outline" size="sm" onClick={() => setRetry((value) => value + 1)}>Retry review evidence</Button></>
        : !review ? <p role="status">Loading retained review evidence…</p>
        : <>
          <div className="grid gap-2 text-sm md:grid-cols-2">
            <p>Current run <span className="font-mono">{review.runId}</span> · version {review.version}</p>
            {review.baselineRunId ? <p>Prior approved run <Link className="underline" to={workstationRouteWithQuery("reportingRunDetail", { runId: review.baselineRunId })}>{review.baselineRunId}</Link> · version {review.baselineVersion}</p>
              : <p>No prior approved comparable run.</p>}
            <p className="md:col-span-2">Period {review.periodId} · Presentation currency {review.currency ?? "Not recorded"} · Accounting basis {review.accountingBasis ?? "Not recorded"}</p>
          </div>
          <p>{review.materialChangeCount === null
            ? review.materialityOutcome === "MaterialException" ? "Material changes require review · assessment incomplete" : "Materiality not assessed"
            : `${review.materialChangeCount} material line change${review.materialChangeCount === 1 ? "" : "s"}`}</p>
          {review.materialityPolicy ? <div className="text-sm" aria-label="Authoritative materiality policy">
            <p>Policy {review.materialityPolicy.policyId} · version {review.materialityPolicy.version} · {review.materialityPolicy.currency}</p>
            <ul className="list-disc pl-5">{review.materialityPolicy.metrics.map((metric) => <li key={`${metric.gridId}:${metric.column}`}>
              {metric.gridId} / {metric.column}: absolute tolerance {metric.absoluteVariance === null ? "Not assessed" : `${metric.absoluteVariance} ${review.materialityPolicy!.currency}`}; relative tolerance {metric.relativeVariancePercent === null ? "Not assessed" : `${metric.relativeVariancePercent}%`}.
            </li>)}</ul>
          </div> : null}
          {review.unavailableReasons.length > 0 ? <ul className="list-disc space-y-1 pl-5 text-sm text-muted-foreground">{review.unavailableReasons.map((reason) => <li key={reason}>{reason.replace(/^Materiality not assessed: /, "")}</li>)}</ul> : null}
          <Changes title="Parameter changes" changes={review.parameters} comparable={Boolean(review.baselineRunId)} />
          <Changes title="Certified dataset revisions" changes={review.sourceChanges} comparable={Boolean(review.baselineRunId)} />
          <section aria-label="Report line amounts"><h3 className="font-semibold">Line amounts</h3>
            <p className="text-sm text-muted-foreground">Variance = current − prior; percentage uses the absolute prior. A zero or missing prior has no percentage.</p>
            {review.lines.length === 0 ? <p className="text-sm text-muted-foreground">{review.baselineRunId ? "No changed line amounts were returned. Check evidence availability above." : "No comparison baseline."}</p>
              : <div className="overflow-x-auto"><table className="w-full text-sm"><caption className="sr-only">Changed retained report lines</caption>
                <thead><tr>{["Line", "Prior", "Current", "Variance", "Variance %", ...(review.materialityPolicy ? ["Materiality"] : []), "Evidence"].map((label) => <th key={label} className="p-2 text-left">{label}</th>)}</tr></thead>
                <tbody>{review.lines.map((line) => <tr key={`${line.gridId}:${line.rowKey}:${line.column}`}>
                  <th scope="row" className="p-2 text-left font-normal">{line.gridId} · {line.rowKey} · {line.column} ({line.state})</th>
                  {[line.previous, line.current, line.delta, line.percent === null ? null : `${line.percent}%`].map((value, i) => <td key={i} className="p-2 font-mono tabular-nums">{value ?? "Not applicable"}</td>)}
                  {review.materialityPolicy ? <td className="p-2">{line.materialityOutcome === "MaterialException" ? "Material exception" : line.materialityOutcome === "WithinTolerance" ? "Within tolerance" : "Not assessed"}</td> : null}
                  <td className="p-2"><Link className="underline" to={workstationRouteWithQuery("reportingRunDetail", { runId: review.runId })}>Run evidence</Link></td>
                </tr>)}</tbody></table></div>}
          </section>
        </>}
    </CardContent>
  </Card>;
}

function Changes({ title, changes, comparable }: { title: string; changes: ReportReviewChange[]; comparable: boolean }) {
  return <section aria-label={title}><h3 className="font-semibold">{title}</h3>
    {changes.length === 0 ? <p className="text-sm text-muted-foreground">{comparable ? "No changes returned. Check evidence availability above." : "No comparison baseline."}</p>
      : <div className="overflow-x-auto"><table className="w-full text-sm"><caption className="sr-only">{title}</caption>
        <thead><tr><th className="p-2 text-left">Field or row</th><th className="p-2 text-left">Prior</th><th className="p-2 text-left">Current</th></tr></thead>
        <tbody>{changes.map((change) => <tr key={change.key}><th scope="row" className="p-2 text-left font-normal">{change.key}</th>
          <td className="max-w-lg break-words p-2 font-mono">{change.previous ?? "Not present"}</td><td className="max-w-lg break-words p-2 font-mono">{change.current ?? "Not present"}</td></tr>)}</tbody>
      </table></div>}
  </section>;
}
