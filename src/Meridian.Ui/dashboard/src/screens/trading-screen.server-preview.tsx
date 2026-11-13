import type { ServerOrderPreview, SessionIntent } from "@/lib/api/execution-workflows.api";
import { formatCurrency, formatUtcTimestamp } from "@/lib/format";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";

export function ServerOrderPreviewPanel({ preview, error, loading, expired, sessionIntent, onSessionIntent, onRefresh }: {
  preview: ServerOrderPreview | null; error: string | null; loading: boolean; expired: boolean;
  sessionIntent: SessionIntent; onSessionIntent: (intent: SessionIntent) => void; onRefresh: () => void;
}) {
  return <section className="rounded-lg border border-border p-3 text-xs" aria-label="Authoritative order checks" aria-busy={loading}>
    <label className="grid gap-1">Execution session
      <Select value={sessionIntent} onChange={(event) => onSessionIntent(event.target.value as SessionIntent)}>
        <option value="RegularOnly">Regular hours only</option><option value="QueueNextSession">Explicitly queue for next session</option><option value="ExtendedHours">Eligible extended-hours execution</option>
      </Select>
    </label>
    <div className="mt-3 flex justify-between gap-2"><h4 className="font-semibold">Server validation</h4><Button type="button" size="sm" variant="ghost" onClick={onRefresh} disabled={loading} disabledReason={loading ? "Current order checks are loading." : null}>Refresh checks</Button></div>
    <p role="status" className="mt-2">{loading ? "Checking current account, prices and risk…" : error ?? (expired ? "Preview expired. Refresh before submitting." : preview ? preview.canSubmit ? preview.requiresApproval ? "Would require governed approval." : "Checked; submission will revalidate." : "Blocked or incompletely assessed." : "Complete the ticket and select an account to run checks.")}</p>
    {preview ? <><dl className="my-2 grid grid-cols-2 gap-1"><dt>Estimated notional</dt><dd>{preview.estimatedNotional == null ? "Unavailable" : formatCurrency(preview.estimatedNotional)}</dd><dt>Estimated fees</dt><dd>{preview.estimatedFees == null ? "Unknown" : formatCurrency(preview.estimatedFees)}</dd></dl>
      {preview.reference ? <p>Reference {preview.reference.price} · {preview.reference.source} · {formatUtcTimestamp(new Date(preview.reference.observedAt))}</p> : null}
      <ul className="mt-2 space-y-1">{preview.checks.map((check, index) => <li key={`${check.code}:${index}`}><strong>{check.status}: </strong>{check.message}</li>)}</ul>
    </> : null}
  </section>;
}
