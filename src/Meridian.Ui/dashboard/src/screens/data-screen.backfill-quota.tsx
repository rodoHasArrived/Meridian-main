import { Button } from "@/components/ui/button";
import { TimeText } from "@/components/ui/time-text";
import { formatCount, formatNumber, pluralizeCount } from "@/lib/format";
import type { ReactNode, RefObject } from "react";
import { Input } from "@/components/ui/input";
import type { useDataViewModel } from "./data-screen.view-model";
import type { BackfillExecutionHistoryRow, BackfillPreviewResult, BackfillProviderAlternative, BackfillQuotaEstimate, BackfillQuotaReceipt } from "@/types";
import { BackfillBatchReceipts } from "@/screens/data-screen.backfill-batch-receipts";

function duration(seconds: number) {
  if (seconds >= 3600) return `${formatNumber(seconds / 3600, { maximumFractionDigits: 1 })} hr`;
  if (seconds >= 60) {
    const minutes = Math.floor(seconds / 60);
    const remainder = seconds - minutes * 60;
    return remainder > 0 ? `${formatCount(minutes)} min ${formatNumber(remainder, { maximumFractionDigits: 1 })} sec` : `${formatCount(minutes)} min`;
  }
  return `${formatNumber(seconds, { maximumFractionDigits: 1 })} sec`;
}

function quotaUnits(value: number, unit: string | undefined) {
  return pluralizeCount(value, unit === "credits" ? "credit" : "request", { localizeCount: true });
}

function EvidenceFields({ fields }: { fields: ReadonlyArray<readonly [string, ReactNode]> }) {
  return (
    <dl className="grid grid-cols-2 gap-x-4 gap-y-2 sm:grid-cols-3">
      {fields.map(([label, value]) => (
        <div key={label}>
          <dt className="text-xs text-muted-foreground">{label}</dt>
          <dd className="mt-0.5 font-mono text-xs text-foreground">{value}</dd>
        </div>
      ))}
    </dl>
  );
}

export function BackfillQuotaPanel({ preview, submissionEstimate, busy, selectAlternative }: {
  preview: BackfillPreviewResult;
  submissionEstimate?: BackfillQuotaEstimate;
  busy: boolean;
  selectAlternative: (alternative: BackfillProviderAlternative) => void;
}) {
  const quota = submissionEstimate ?? preview.quota;
  const available = quota?.requestsPerWindow != null && quota.windowSeconds != null;
  const fields: [string, string][] = [];
  if (available) {
    fields.push(
      ["Expected requests", formatCount(quota.expectedRequests, { fallback: "Unavailable" })],
      ["Known historical limit", `${quotaUnits(quota.requestsPerWindow!, quota.quotaUnit)} / ${duration(quota.windowSeconds!)}`],
      ["Observed remaining", quota.observedRemainingRequests == null ? "Unavailable" : quotaUnits(quota.observedRemainingRequests, quota.quotaUnit)],
      ["Estimated duration", quota.estimatedDurationSeconds == null ? "Unavailable" : duration(quota.estimatedDurationSeconds)]
    );
    if (quota.reservedHeadroomPercent > 0 && quota.backfillRequestsPerWindow != null) {
      fields.push(["Backfill allowance", `${quotaUnits(quota.backfillRequestsPerWindow, quota.quotaUnit)} per window`]);
      if (quota.backfillRemainingRequests != null) {
        fields.push(["Available for backfill", quotaUnits(quota.backfillRemainingRequests, quota.quotaUnit)]);
      }
    }
    if (quota.dailyLimit != null) {
      fields.push(["Daily limit", `${quotaUnits(quota.dailyLimit, quota.quotaUnit)} per day`]);
      fields.push(["Observed daily remaining", quota.observedDailyRemaining == null ? "Unavailable" : quotaUnits(quota.observedDailyRemaining, quota.quotaUnit)]);
      if (quota.reservedHeadroomPercent > 0 && quota.dailyBackfillAllowance != null) {
        fields.push(["Daily backfill allowance", quotaUnits(quota.dailyBackfillAllowance, quota.quotaUnit)]);
      }
    }
  }

  const policies = [
    quota?.reservedHeadroomPercent ? `${quota.reservedHeadroomPercent}% headroom target` : null,
    quota?.maxConcurrentRequests != null ? `Maximum concurrent requests ${formatCount(quota.maxConcurrentRequests)}` : null
  ].filter(Boolean);

  return (
    <section aria-labelledby="backfill-quota-title" className="mt-4 space-y-2 border-t border-border/70 pt-3" aria-live="polite">
      <h3 id="backfill-quota-title" className="text-sm font-semibold text-foreground">
        {submissionEstimate ? "Provider quota at submission" : "Provider quota"}
      </h3>
      {available ? <EvidenceFields fields={fields} /> : <p className="text-sm text-foreground">Limit unavailable</p>}
      {quota?.observedAt && <p className="text-xs text-muted-foreground">
        Observed <TimeText value={quota.observedAt} />
        {quota.resetAt && <> · Reset expected <TimeText value={quota.resetAt} /></>}
        {quota.dailyResetAt && quota.dailyResetAt !== quota.resetAt && <> · Daily reset <TimeText value={quota.dailyResetAt} /></>}
      </p>}
      {policies.length > 0 && <p className="text-xs text-muted-foreground">Provider administrator policy · {policies.join(" · ")}</p>}
      {quota && <p className="text-xs leading-5 text-muted-foreground">{quota.uncertainty}</p>}
      {quota?.exceedsBudget && !submissionEstimate && (
        <div className="space-y-2 border-l-2 border-warning pl-3 text-sm">
          <p className="font-medium text-foreground">Plan exceeds the available backfill budget.</p>
          <p className="text-xs leading-5 text-muted-foreground">
            Reduce the date range or symbol count and preview again, schedule for later, or queue with throttling.
          </p>
          {preview.approvedAlternatives?.map((alternative) => (
            <Button key={alternative.provider} size="sm" variant="outline" disabled={busy}
              onClick={() => selectAlternative(alternative)}>
              Use {alternative.displayName}
            </Button>
          ))}
        </div>
      )}
    </section>
  );
}

export function BackfillReceiptEvidence({ receipt }: { receipt: BackfillQuotaReceipt }) {
  const signed = (value: number, magnitude: (number: number) => string) => `${value > 0 ? "+" : value < 0 ? "−" : ""}${magnitude(Math.abs(value))}`;
  return (
    <div className="space-y-3">
      <EvidenceFields fields={[
        ["Observed providers", receipt.observedProviders.join(", ") || "None observed"],
        ...(receipt.providersThatReturnedBars == null ? [] : [["Providers that returned bars", receipt.providersThatReturnedBars.join(", ") || "None"] as const]),
        ["Requests (at submission / actual)", `${formatCount(receipt.estimate.expectedRequests, { fallback: "Unavailable" })} / ${formatCount(receipt.actualRequests, { fallback: "Unavailable" })}`],
        ["Request variance", formatCount(receipt.requestVariance, { signed: true, fallback: "Unavailable" })],
        ["Duration (at submission / actual)", `${receipt.estimate.estimatedDurationSeconds == null ? "Unavailable" : duration(receipt.estimate.estimatedDurationSeconds)} / ${duration(receipt.actualDurationSeconds)}`],
        ["Duration variance", receipt.durationVarianceSeconds == null ? "Unavailable" : signed(receipt.durationVarianceSeconds, duration)],
        ["Throttling events", formatCount(receipt.throttlingEventCount)]
      ]} />
      {receipt.throttlingEvents.length > 0 && (
        <ul className="list-disc space-y-1 pl-4 text-xs text-muted-foreground">
          {receipt.throttlingEvents.map((event, index) => (
            <li key={`${event.observedAt}-${index}`}>
              <TimeText value={event.observedAt} /> · {event.provider} · {event.reason.replaceAll("-", " ")}
              {event.waitSeconds != null && ` · wait ${duration(event.waitSeconds)}`}
            </li>
          ))}
          {receipt.throttlingEventCount > receipt.throttlingEvents.length && <li>Showing the first {receipt.throttlingEvents.length} events.</li>}
        </ul>
      )}
    </div>
  );
}

export function WorkstationBackfillReceipts({ rows, error }: { rows: BackfillExecutionHistoryRow[]; error: string | null }) {
  if (rows.length === 0 && !error) return null;
  return (
    <section aria-labelledby="workstation-backfill-receipts" className="space-y-2 border-t border-border/70 pt-4">
      <h3 id="workstation-backfill-receipts" className="text-sm font-semibold text-foreground">Workstation backfill receipts</h3>
      {error && <p role="status" className="text-xs text-muted-foreground">{error}</p>}
      {rows.map((row) => (
        <details key={row.executionId} className="rounded-md border border-border/70 px-3 py-2 text-sm">
          <summary className="cursor-pointer focus-visible:outline-primary">
            <span className="font-medium">{row.status}</span> · {row.requestedProvider} · {row.symbols.slice(0, 4).join(", ")}
            {row.symbols.length > 4 && ` +${row.symbols.length - 4}`}
          </summary>
          <div className="mt-3 space-y-2">
            <EvidenceFields fields={[
              ["Job ID", row.executionId],
              ["Scope", `${row.fromDate} to ${row.toDate}`],
              ["Eligible from", <TimeText value={row.scheduledAt} fallback="Unavailable" />],
              ["Bars written", formatCount(row.barsDownloaded)]
            ]} />
            {row.throttledUntil && <p className="text-xs text-muted-foreground">Waiting for budget until approximately <TimeText value={row.throttledUntil} />.</p>}
            {row.quotaReceipt ? <BackfillReceiptEvidence receipt={row.quotaReceipt} /> : (
              <p className="text-xs text-muted-foreground">
                {row.completedAt ? "Execution measurements unavailable." : "Execution measurements appear after the run."}
              </p>
            )}
            {row.errorMessage && <p className="text-xs text-muted-foreground">{row.errorMessage}</p>}
            {row.batchReceipts?.length ? <BackfillBatchReceipts receipts={row.batchReceipts} /> : null}
          </div>
        </details>
      ))}
    </section>
  );
}

export function BackfillSubmissionPanel({ vm, providerField }: {
  vm: Pick<ReturnType<typeof useDataViewModel>,
    "preview" | "submission" | "busy" | "scheduleForLater" | "setScheduleForLater" |
    "scheduledStart" | "setScheduledStart" | "selectBackfillAlternative" | "queuedBackfills">;
  providerField: RefObject<HTMLSelectElement | null>;
}) {
  const quota = vm.preview?.quota;
  const latestSubmission = vm.queuedBackfills.find((row) => row.executionId === vm.submission?.executionId);
  const submissionStatus = latestSubmission?.status ?? vm.submission?.status;
  return (
    <>
      {vm.preview && <BackfillQuotaPanel preview={vm.preview} submissionEstimate={vm.submission?.quota} busy={vm.busy}
        selectAlternative={(alternative) => {
          providerField.current?.focus();
          vm.selectBackfillAlternative(alternative);
        }} />}
      {quota && !vm.submission && (
        <div className="mt-3 space-y-2 text-sm">
          <label className="flex w-fit items-center gap-2">
            <input type="checkbox" checked={vm.scheduleForLater} disabled={vm.busy}
              onChange={(event) => vm.setScheduleForLater(event.target.checked)} />
            Schedule for later
          </label>
          {vm.scheduleForLater && (
            <label className="grid max-w-xs gap-1">
              Earliest start (UTC)
              <Input type="datetime-local" value={vm.scheduledStart} disabled={vm.busy}
                onChange={(event) => vm.setScheduledStart(event.target.value)} />
            </label>
          )}
        </div>
      )}
      {vm.submission && (
        <div className="mt-4 space-y-2 rounded-md border border-border/70 p-3" role="status">
          <p className="text-sm font-medium">{submissionStatus} · {vm.submission.provider}</p>
          <p className="break-all font-mono text-xs text-muted-foreground">Job {vm.submission.executionId}</p>
          {submissionStatus === "Scheduled" && <p className="text-xs">Eligible from <TimeText value={vm.submission.scheduledAt} />.</p>}
          {vm.submission.quota.exceedsBudget && <p className="text-xs">Requests are paced to the shared provider budget.</p>}
          {latestSubmission?.quotaReceipt && <BackfillReceiptEvidence receipt={latestSubmission.quotaReceipt} />}
        </div>
      )}
      {latestSubmission?.batchReceipts?.length ? <BackfillBatchReceipts receipts={latestSubmission.batchReceipts} /> : null}
    </>
  );
}
