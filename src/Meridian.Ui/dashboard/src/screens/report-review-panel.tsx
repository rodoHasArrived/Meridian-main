import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DenseDataTable } from "@/components/meridian/ui-kit-primitives";
import { AmountPassportTrigger } from "@/components/meridian/amount-passport";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { getReportRunReview, type ReportRunReview, type ReportReviewChange, type ReportReviewLine } from "@/lib/report-run-review-api";
import { parseExactDecimal } from "@/lib/exact-financial-value";
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
            <p>Current run · version {review.version}</p>
            {review.baselineRunId ? <p><Link className="underline" to={workstationRouteWithQuery("reportingRunDetail", { runId: review.baselineRunId })}>Prior approved run</Link> · version {review.baselineVersion}</p>
              : <p>No prior approved comparable run.</p>}
            <p className="md:col-span-2">Period {review.periodId} · Presentation currency {review.currency ?? "Not recorded"} · Accounting basis {review.accountingBasis ?? "Not recorded"}</p>
          </div>
          <TechnicalDetails label="Retained review identities"><dl className="grid gap-2 text-sm">
            <div><dt>Current run</dt><dd className="break-all font-mono">{review.runId}</dd></div>
            {review.baselineRunId ? <div><dt>Prior approved run</dt><dd className="break-all font-mono">{review.baselineRunId}</dd></div> : null}
            <div><dt>Review receipt</dt><dd className="break-all font-mono">{review.reviewId}</dd></div>
            <div><dt>Snapshot</dt><dd className="break-all font-mono">{review.snapshotHash}</dd></div>
          </dl></TechnicalDetails>
          <p>{review.materialChangeCount === null
            ? review.materialityOutcome === "MaterialException" ? "Material changes require review · assessment incomplete" : "Materiality not assessed"
            : `${review.materialChangeCount} material line change${review.materialChangeCount === 1 ? "" : "s"}`}</p>
          {review.materialityPolicy ? <div className="text-sm" aria-label="Authoritative materiality policy">
            <p>Policy {review.materialityPolicy.policyId} · version {review.materialityPolicy.version} · {review.materialityPolicy.currency}</p>
            <ul className="list-disc pl-5">{review.materialityPolicy.metrics.map((metric) => <li key={JSON.stringify([metric.gridId, metric.column])}>
              {metric.gridId} / {metric.column}: absolute tolerance {metric.absoluteVariance === null ? "Not assessed" : `${metric.absoluteVariance} ${review.materialityPolicy!.currency}`}; relative tolerance {metric.relativeVariancePercent === null ? "Not assessed" : `${metric.relativeVariancePercent}%`}.
            </li>)}</ul>
          </div> : null}
          {review.unavailableReasons.length > 0 ? <ul className="list-disc space-y-1 pl-5 text-sm text-muted-foreground">{review.unavailableReasons.map((reason) => <li key={reason}>{reason.replace(/^Materiality not assessed: /, "")}</li>)}</ul> : null}
          <Changes title="Parameter changes" changes={review.parameters} comparable={Boolean(review.baselineRunId)} />
          <Changes title="Certified dataset revisions" changes={review.sourceChanges} comparable={Boolean(review.baselineRunId)} />
          <section aria-label="Report line amounts"><h3 className="font-semibold">Line amounts</h3>
            <p className="text-sm text-muted-foreground">Variance = current − prior; percentage uses the absolute prior. A zero or missing prior has no percentage.</p>
            {review.lines.length === 0 ? <p className="text-sm text-muted-foreground">{review.baselineRunId ? "No changed line amounts were returned. Check evidence availability above." : "No comparison baseline."}</p>
              : <DenseDataTable<ReportReviewLine>
                  rows={review.lines}
                  getRowId={(line) => JSON.stringify([line.gridId, line.rowKey, line.column])}
                  ariaLabel="Changed retained report lines"
                  emptyText="No changed line amounts were returned."
                  columns={[
                    { id: "line", label: "Line", rowHeader: true, className: "whitespace-normal break-words", render: (line) => `${line.gridId} · ${line.rowKey} · ${line.column} (${line.state})` },
                    ...(["previous", "current", "delta"] as const).map((field, index) => ({
                      id: field, label: ["Prior", "Current", "Variance"][index], numeric: true,
                      render: (line: ReportReviewLine) => isMeasuredLine(line)
                        ? <ReviewAmount label={`${["Prior", "Current", "Variance"][index]} ${line.gridId} / ${line.rowKey} / ${line.column}`} value={line[field]} currency={review.currency}
                            basis={index === 2 ? "Current retained value minus prior retained value." : "Exact value retained on the selected report revision."} />
                        : line[field] ?? "Not recorded"
                    })),
                    { id: "percent", label: "Variance %", numeric: true, render: (line) => line.percent === null ? "Not applicable" : `${line.percent}%` },
                    ...(review.materialityPolicy ? [{ id: "materiality", label: "Materiality", render: (line: ReportReviewLine) => line.materialityOutcome === "MaterialException" ? "Material exception" : line.materialityOutcome === "WithinTolerance" ? "Within tolerance" : "Not assessed" }] : []),
                    { id: "evidence", label: "Evidence", render: () => <Link className="underline" to={workstationRouteWithQuery("reportingRunDetail", { runId: review.runId })}>Run evidence</Link> }
                  ]}
                />}

          </section>
        </>}
    </CardContent>
  </Card>;
}

function Changes({ title, changes, comparable }: { title: string; changes: ReportReviewChange[]; comparable: boolean }) {
  return <section aria-label={title}><h3 className="font-semibold">{title}</h3>
    {changes.length === 0 ? <p className="text-sm text-muted-foreground">{comparable ? "No changes returned. Check evidence availability above." : "No comparison baseline."}</p>
      : <DenseDataTable<ReportReviewChange>
          rows={changes}
          getRowId={(change) => change.key}
          ariaLabel={title}
          emptyText="No changes returned."
          columns={[
            { id: "field", label: "Field or row", rowHeader: true, className: "whitespace-normal break-words", render: (change) => change.key },
            { id: "previous", label: "Prior", className: "max-w-lg whitespace-normal break-words font-mono", render: (change) => change.previous ?? "Not present" },
            { id: "current", label: "Current", className: "max-w-lg whitespace-normal break-words font-mono", render: (change) => change.current ?? "Not present" }
          ]}
        />}

  </section>;
}

function ReviewAmount({ label, value, currency, basis }: { label: string; value: string | null; currency: string | null; basis: string }) {
  const displayValue = value ?? "Not recorded";
  return <AmountPassportTrigger compact ariaLabel={`Inspect ${label}: ${displayValue}${currency ? ` ${currency}` : ""}`}
    selection={{ label, displayValue, currency, unit: currency ?? "Unit unavailable", origin: "unavailable", basis,
      category: "Retained report comparison", zero: parseExactDecimal(value)?.coefficient === 0n,
      unavailableReason: "An amount-scoped passport reference was not retained for this report comparison. Run and snapshot evidence is available." }}>
    {displayValue}
  </AmountPassportTrigger>;
}

function isMeasuredLine(line: ReportReviewLine): boolean {
  return line.isMeasure ?? [line.previous, line.current, line.delta].some((value) => parseExactDecimal(value) !== null);
}
