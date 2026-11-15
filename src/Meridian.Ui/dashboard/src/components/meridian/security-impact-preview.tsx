import { useEffect, useRef, useState } from "react";
import { getSecurityImpactPreview, type SecurityImpactPreviewReceipt, type SecurityImpactProjection } from "@/lib/api/security-impact-preview.api";

export function SecurityImpactPreview({ receiptId, onInspected }: { receiptId: string; onInspected?: (receipt: SecurityImpactPreviewReceipt) => void }) {
  const inspected = useRef(onInspected);
  inspected.current = onInspected;
  const [receipt, setReceipt] = useState<SecurityImpactPreviewReceipt | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    setReceipt(null); setError(null);
    getSecurityImpactPreview(receiptId, controller.signal).then(value => {
      if (!controller.signal.aborted) { setReceipt(value); inspected.current?.(value); }
    }).catch(() => { if (!controller.signal.aborted) setError("Impact evidence could not be loaded. Approval requires current evidence."); });
    return () => controller.abort();
  }, [receiptId]);
  if (error) return <p role="alert">{error}</p>;
  if (!receipt) return <p role="status">Loading retained impact evidence…</p>;
  return <section aria-label="Retained edit impact" className="space-y-3 rounded border p-3 text-sm">
    <p>Canonical version {receipt.canonicalVersion} · Economic date {receipt.asOf} · Captured {receipt.capturedAt}</p>
    <p>Retained fund profile: {receipt.fundProfileId ?? "Unavailable in this legacy receipt"}.</p>
    <p>Books: {receipt.books.completeness}. {receipt.books.basis}</p>
    <p>{receipt.books.bookIds.length} candidate books. Exact live lot impact is unverified.</p>
    <div className="overflow-x-auto"><table className="w-full text-left"><caption>Potentially affected retained periods</caption><thead><tr><th>Book</th><th>Period</th><th>Status</th><th>Version</th></tr></thead>
      <tbody>{receipt.periods.map(period => <tr key={`${period.bookId}:${period.periodId}`}><td>{period.bookId}</td><td>{period.startDate}–{period.endDate}</td><td>{period.status}</td><td>{period.version}</td></tr>)}</tbody></table></div>
    <p>Released reports: {receipt.reports.completeness}{receipt.reports.completeness === "Complete" ? ` · ${receipt.reports.runIds.length} matched runs` : " · a zero-impact conclusion is unavailable"}.</p>
    <details><summary>Inspect previous and proposed contractual terms</summary>
      {receipt.previousTerms && receipt.proposedTerms ? <div className="grid grid-cols-2 gap-3">
        <pre aria-label="Previous terms" className="max-h-64 overflow-auto">{JSON.stringify(receipt.previousTerms.assetSpecificTerms, null, 2)}</pre>
        <pre aria-label="Proposed terms" className="max-h-64 overflow-auto">{JSON.stringify(receipt.proposedTerms.assetSpecificTerms, null, 2)}</pre>
      </div> : <p>Full contractual term evidence is unavailable.</p>}
    </details>
    <div className="grid grid-cols-2 gap-3"><Projection label="Before" value={receipt.before} /><Projection label="Proposed" value={receipt.after} /></div>
    <ul>{[...receipt.books.issues, ...receipt.reports.issues, ...receipt.issues].map((issue, index) => <li key={`${issue.code}:${index}`}><strong>{issue.code}</strong>: {issue.reason}</li>)}</ul>
    <p>If publication reports stale evidence after approval, create a fresh governed draft and approval workflow. The existing approval and receipt remain retained.</p>
    <details><summary>Retained evidence identity</summary><p className="break-all">Receipt {receipt.contentSha256}</p><p className="break-all">Dependencies {receipt.dependencyFingerprint}</p></details>
  </section>;
}
function Projection({ label, value }: { label: string; value: SecurityImpactProjection | null }) {
  const reason = value?.projection.blockedReason;
  return <section aria-label={`${label} cash flows`}><h4>{label} cash flows</h4>
    {!value || reason ? <p>{reason ?? "Unavailable; no zero delta is implied."}</p> : <><p>{value.projection.schedule.length} scheduled entries · {value.modelVersion}</p><details><summary>Inspect scheduled values</summary><pre className="max-h-64 overflow-auto">{JSON.stringify(value.projection.schedule, null, 2)}</pre></details></>}
  </section>;
}
