import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { resolveProviderIntegrationIdentity } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import type { ProviderIntegrationStagingIdentityResolutionPreview, ProviderIntegrationStagingIdentityResolutionRow } from "@/types";

export function ProviderIdentityEvidence({ preview, onResolved }: {
  preview: ProviderIntegrationStagingIdentityResolutionPreview;
  onResolved: () => void;
}) {
  return <section className="mt-3 space-y-3" aria-label="Security identifier agreement">
    <h4 className="text-xs font-medium">Security identifier agreement</h4>
    {preview.rows.map((row) => <IdentityEvidenceRow key={`${preview.connectionId}:${row.stagingRecordId}:${row.evidenceHash ?? "legacy"}`}
      connectionId={preview.connectionId} row={row} onResolved={onResolved} />)}
  </section>;
}

function IdentityEvidenceRow({ connectionId, row, onResolved }: {
  connectionId: string;
  row: ProviderIntegrationStagingIdentityResolutionRow;
  onResolved: () => void;
}) {
  const [selection, setSelection] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const pending = useRef<AbortController | null>(null);
  useEffect(() => () => pending.current?.abort(), []);
  const choices = row.securityCandidates.filter((candidate, index, candidates) =>
    candidate.status === "Resolved" && candidate.internalSecurityId &&
    candidates.findIndex((other) => other.internalSecurityId === candidate.internalSecurityId) === index);
  const reviewable = (row.hasIdentifierConflict || row.securityStatus === "ReviewRequired") && !row.operatorResolution;

  const resolve = async () => {
    if (!selection || !reason.trim() || !row.evidenceHash || busy) return;
    const controller = new AbortController();
    pending.current = controller;
    setBusy(true);
    setMessage("");
    try {
      await resolveProviderIntegrationIdentity(connectionId, {
        syncRunId: row.syncRunId, stagingRecordId: row.stagingRecordId,
        selectedSecurityId: selection, evidenceHash: row.evidenceHash, reason: reason.trim()
      }, { signal: controller.signal });
      if (controller.signal.aborted) return;
      setMessage("Resolution retained. Refreshing matching evidence.");
      onResolved();
    } catch (error) {
      if (controller.signal.aborted) return;
      setMessage(describeApiError(error, "Resolution could not be confirmed. Reload matching evidence before trying again.").summary);
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  };

  return <div className="rounded-sm border border-border/60 bg-background/35 p-2 text-xs">
    <p className="font-mono">{row.stagingRecordId} · {row.securityStatus}</p>
    {row.hasIdentifierConflict ? <p className="mt-1 text-warning">Supplied identifiers match different securities. Review the source conflict.</p> : null}
    <div className="mt-2 overflow-x-auto">
      <table className="w-full text-left text-xs">
        <caption className="sr-only">Identifier matching evidence for {row.stagingRecordId}</caption>
        <thead><tr><th scope="col" className="pr-3">Identifier</th><th scope="col" className="pr-3">Provider</th><th scope="col" className="pr-3">Match</th><th scope="col">Status</th></tr></thead>
        <tbody>{row.securityCandidates.map((candidate) => <tr key={`${candidate.identifierKind}:${candidate.provider ?? ""}:${candidate.identifierValue}`}>
          <td className="py-1 pr-3"><span>{candidate.identifierKind}</span><br /><span className="font-mono">{candidate.identifierValue}</span></td>
          <td className="pr-3">{candidate.provider ?? "Canonical"}</td>
          <td className="pr-3">{candidate.displayName ?? "No match"}<br /><span className="font-mono">{candidate.internalSecurityId}</span></td>
          <td>{candidate.status}</td>
        </tr>)}</tbody>
      </table>
    </div>
    {row.operatorResolution ? <p className="mt-2 text-muted-foreground">Retained choice: {row.operatorResolution.selectedSecurityId} · {row.operatorResolution.reviewedBy} · {row.operatorResolution.reviewedAt}<br />{row.operatorResolution.reason}</p> : null}
    {reviewable && choices.length > 0 ? <div className="mt-2 grid gap-2">
      <label>Chosen security<select className="mt-1 w-full rounded-sm border border-input bg-background p-1" value={selection} onChange={(event) => setSelection(event.target.value)} disabled={busy}>
        <option value="">Select an active match</option>
        {choices.map((candidate) => <option key={candidate.internalSecurityId} value={candidate.internalSecurityId!}>{candidate.displayName ?? candidate.internalSecurityId} · {candidate.internalSecurityId}</option>)}
      </select></label>
      <label>Resolution reason<textarea className="mt-1 w-full rounded-sm border border-input bg-background p-1" value={reason} onChange={(event) => setReason(event.target.value)} disabled={busy} /></label>
      <Button size="sm" variant="outline" onClick={() => void resolve()} disabled={busy || !selection || !reason.trim() || !row.evidenceHash}>Resolve source conflict</Button>
    </div> : null}
    {message ? <p role="status" className="mt-2">{message}</p> : null}
  </div>;
}
