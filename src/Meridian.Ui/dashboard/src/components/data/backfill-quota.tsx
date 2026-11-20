import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import type { BackfillJobReceipt, BackfillPreviewResult, BackfillQuotaReceipt } from "@/types";

const number = new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 });
const signedNumber = new Intl.NumberFormat(undefined, { maximumFractionDigits: 2, signDisplay: "exceptZero" });
const timestamp = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });
const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

function duration(seconds: number): string {
  if (seconds >= 3600) return `${number.format(seconds / 3600)} hr`;
  if (seconds >= 60) return `${number.format(seconds / 60)} min`;
  return `${number.format(seconds)} sec`;
}

function quotaFields(rows: { label: string; value: string }[]) {
  return (
    <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-xs sm:grid-cols-3">
      {rows.map(({ label, value }) => (
        <div key={label} className="min-w-0">
          <dt className="text-muted-foreground">{label}</dt>
          <dd className="mt-0.5 break-words font-mono text-foreground">{value}</dd>
        </div>
      ))}
    </dl>
  );
}

export function BackfillQuotaPanel({ preview, busy, submitted, onSelectProvider, onSchedule }: {
  preview: BackfillPreviewResult;
  busy: boolean;
  submitted: boolean;
  onSelectProvider: (provider: string) => void;
  onSchedule?: (notBeforeUtc: string) => void;
}) {
  const headingId = useId();
  const startId = useId();
  const section = useRef<HTMLElement>(null);
  const [scheduledStart, setScheduledStart] = useState("");
  const [showSchedule, setShowSchedule] = useState(false);
  const quota = preview.quota;
  const unit = quota?.quotaUnit ?? "requests";
  const limitAvailable = quota?.providerLimit != null && quota.windowSeconds != null;
  const start = scheduledStart ? new Date(scheduledStart) : null;
  const validStart = start !== null && Number.isFinite(start.getTime()) && start.getTime() > Date.now();

  useEffect(() => {
    section.current?.scrollIntoView?.({ block: "nearest" });
  }, [preview]);

  return (
    <section ref={section} aria-labelledby={headingId} className="mt-4 space-y-3 rounded-md border border-border bg-secondary/20 p-3">
      <h3 id={headingId} className="text-sm font-semibold">Provider quota estimate</h3>
      {limitAvailable ? quotaFields([
        { label: "Expected requests", value: quota.expectedRequests?.toLocaleString() ?? "Unavailable" },
        { label: "Known provider limit", value: `${number.format(quota.providerLimit!)} ${unit} / ${duration(quota.windowSeconds!)}` },
        { label: "Observed remaining", value: quota.observedRemainingRequests == null ? "Unavailable" : `${number.format(quota.observedRemainingRequests)} ${unit}` },
        { label: "Estimated duration", value: quota.estimatedDurationSeconds == null ? "Unavailable" : duration(quota.estimatedDurationSeconds) },
        ...(quota.reserveHeadroomPercent > 0 ? [{
          label: "Available after reserve",
          value: `${quota.availableBackfillRequests?.toLocaleString() ?? "Unknown"} of ${Math.min(quota.backfillRequestsPerWindow!, quota.dailyBackfillLimit ?? Number.POSITIVE_INFINITY).toLocaleString()} ${unit}`
        }] : []),
        ...(quota.dailyProviderLimit != null ? [
          { label: "Daily limit", value: `${number.format(quota.dailyProviderLimit)} ${unit}` },
          { label: "Daily remaining", value: quota.dailyRemaining == null ? "Unavailable" : `${number.format(quota.dailyRemaining)} ${unit}` }
        ] : []),
        ...(quota.retryAt ? [{ label: "Provider retry time", value: timestamp.format(new Date(quota.retryAt)) }] : [])
      ]) : <p className="text-sm font-medium">Limit unavailable</p>}
      {quota && (quota.reserveHeadroomPercent > 0 || quota.maxConcurrentRequests != null) ? (
        <p className="text-xs leading-5 text-muted-foreground">
          Provider administrator policy · {[
            quota.reserveHeadroomPercent > 0 ? `${quota.reserveHeadroomPercent}% headroom policy` : null,
            quota.maxConcurrentRequests != null ? `maximum ${quota.maxConcurrentRequests} concurrent requests` : null
          ].filter(Boolean).join(" · ")}
        </p>
      ) : null}
      <p className="text-xs leading-5 text-muted-foreground">
        {limitAvailable && quota.observedAt ? `Observed ${timestamp.format(new Date(quota.observedAt))}. ` : null}
        {quota?.uncertainty ?? "Historical provider quota data is unavailable for this preview."}
      </p>
      {quota?.exceedsBudget && !submitted ? (
        <div className="space-y-2 border-t border-border pt-3">
          <p className="text-sm font-medium text-warning">This plan exceeds the available backfill budget.</p>
          <p className="text-xs leading-5 text-muted-foreground">
            Shorten the date range or remove symbols and preview again. You can also queue the full plan with throttling.
          </p>
          <div className="flex flex-wrap gap-2">
            {preview.approvedEquivalentProviders?.map(({ provider, displayName }) => (
              <Button key={provider} size="sm" variant="outline" disabled={busy} onClick={() => onSelectProvider(provider)}>
                Use approved provider {displayName}
              </Button>
            ))}
            {onSchedule ? (
              <Button size="sm" variant="outline" disabled={busy} aria-expanded={showSchedule} onClick={() => setShowSchedule(!showSchedule)}>
                Schedule once
              </Button>
            ) : null}
          </div>
          {showSchedule && onSchedule ? (
            <div className="flex flex-wrap items-end gap-2">
              <label htmlFor={startId} className="grid gap-1 text-xs">
                Start after ({timeZone})
                <Input
                  id={startId}
                  type="datetime-local"
                  value={scheduledStart}
                  disabled={busy}
                  onChange={(event) => setScheduledStart(event.target.value)}
                  className="min-h-9"
                />
              </label>
              <Button size="sm" disabled={!validStart || busy} disabledReason={!validStart ? "Choose a future start time." : null}
                onClick={() => { if (validStart) onSchedule(start.toISOString()); }}>
                Schedule backfill
              </Button>
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

const throttleLabels: Record<string, string> = {
  concurrency: "Concurrency cap",
  "pacing-queue": "Request slot queue",
  headroom: "Reserved headroom",
  "quota-window": "Provider quota window",
  "daily-quota": "Daily provider quota",
  "request-pacing": "Request pacing",
  "provider-rate-limit": "Provider rate-limit response",
  "provider-cooldown": "Provider cooldown",
  "retry-after": "Retry delay"
};

export function BackfillQuotaActual({ receipt }: { receipt: BackfillQuotaReceipt }) {
  const expected = receipt.estimate?.expectedRequests;
  const estimatedDuration = receipt.estimate?.estimatedDurationSeconds;
  const requests = [
    receipt.actualRequests == null ? "Actual unavailable" : `${number.format(receipt.actualRequests)} actual`,
    expected == null ? "estimate unavailable" : `${number.format(expected)} estimated`,
    receipt.requestVariance == null ? null : `${signedNumber.format(receipt.requestVariance)} variance`
  ].filter(Boolean).join(" · ");
  const timing = [
    `${duration(receipt.actualDurationSeconds)} actual`,
    estimatedDuration == null ? "estimate unavailable" : `${duration(estimatedDuration)} estimated`,
    receipt.durationVarianceSeconds == null ? null : `${signedNumber.format(receipt.durationVarianceSeconds)} sec variance`
  ].filter(Boolean).join(" · ");
  return (
    <div className="space-y-2 text-xs">
      {quotaFields([
        { label: "Observed providers", value: receipt.observedProviders.join(", ") || "None observed" },
        { label: "Provider requests", value: requests },
        { label: "Execution duration", value: timing }
      ])}
      <div>
        <h4 className="font-medium">Throttling events</h4>
        {receipt.throttlingEvents.length > 0 ? (
          <ul className="mt-1 space-y-1">
            {receipt.throttlingEvents.map((event) => (
              <li key={`${event.provider}-${event.reason}`} className="text-muted-foreground">
                {event.provider} · {throttleLabels[event.reason] ?? event.reason} · {number.format(event.count)}
                {event.waitSeconds > 0 ? ` · ${duration(event.waitSeconds)} total wait` : null}
              </li>
            ))}
          </ul>
        ) : <p className="mt-1 text-muted-foreground">None observed</p>}
      </div>
    </div>
  );
}

export function BackfillJobDetails({ job, statusError }: { job: BackfillJobReceipt; statusError?: string | null }) {
  const detail = job.status === "Throttled" ? "Waiting for a provider request slot."
    : job.status === "Queued" ? "Waiting to start. Shared provider limits apply."
      : job.status === "Scheduled" && job.notBeforeUtc ? `Starts after ${timestamp.format(new Date(job.notBeforeUtc))}.`
        : job.error ?? (job.barsWritten == null ? null : `${number.format(job.barsWritten)} bars written.`);
  return (
    <section aria-label="Backfill job receipt" className="mt-3 space-y-3">
      <StatusBanner
        role="status"
        tone={job.status === "Failed" ? "danger" : job.status === "Completed" ? "success" : job.status === "Throttled" ? "warning" : "info"}
        title={`Backfill ${job.status.toLowerCase()}`}
        detail={detail}
      />
      {statusError ? <p role="status" className="text-xs text-warning">{statusError}</p> : null}
      {job.actual ? <BackfillQuotaActual receipt={job.actual} /> : null}
    </section>
  );
}
