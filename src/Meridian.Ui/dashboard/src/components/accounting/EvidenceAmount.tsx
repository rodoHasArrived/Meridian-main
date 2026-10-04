import { useEffect, useState } from "react";
import { AmountCell, type AmountCellProps } from "./AmountCell";
import { Button } from "@/components/ui/button";
import { Sheet, SheetBody, SheetCloseButton, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { getJson } from "@/lib/api";
import { workstationEvidencePacketEndpoint } from "@/lib/workstation-endpoints";
import { evidenceWorkbenchPath } from "@/lib/workspace";
import type { EvidencePacket } from "@/types";

export interface AmountEvidenceSubject {
  subjectKind: string;
  subjectId: string;
  ledgerBookId?: string | null;
}

/** An amount opens only its retained subject. It never derives provenance from a numeric value. */
export function EvidenceAmount({ subject, ...amount }: AmountCellProps & { subject: AmountEvidenceSubject | null }) {
  const [open, setOpen] = useState(false);
  const [result, setResult] = useState<{ key: string; packet: EvidencePacket | null; error: string | null } | null>(null);
  const subjectKind = subject?.subjectKind;
  const subjectId = subject?.subjectId;
  const ledgerBookId = subject?.ledgerBookId;
  const key = JSON.stringify([subjectKind, subjectId, ledgerBookId]);
  const current = result?.key === key ? result : null;
  useEffect(() => {
    if (!open || !subjectKind || !subjectId) return;
    const controller = new AbortController();
    setResult(null);
    const endpoint = workstationEvidencePacketEndpoint(subjectKind, subjectId);
    const query = ledgerBookId ? `?ledgerBookId=${encodeURIComponent(ledgerBookId)}` : "";
    getJson<EvidencePacket>(`${endpoint}${query}`, { signal: controller.signal, allowDevelopmentFallback: false })
      .then((packet) => {
        if (controller.signal.aborted) return;
        if (packet.subject.subjectId !== subjectId || packet.subject.subjectKind !== subjectKind
          || (ledgerBookId && packet.subject.ledgerBookId !== ledgerBookId)) {
          setResult({ key, packet: null, error: "The retained evidence does not match this amount's subject." });
        } else {
          setResult({ key, packet, error: null });
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) setResult({ key, packet: null, error: "Evidence is unavailable for this amount. The displayed amount has not changed." });
      });
    return () => controller.abort();
  }, [open, subjectKind, subjectId, ledgerBookId, key]);

  if (!subjectKind || !subjectId) {
    return <span><AmountCell {...amount} /><span className="ml-2 text-xs text-muted-foreground">Evidence unavailable</span></span>;
  }
  const proofHref = evidenceWorkbenchPath(subjectKind, subjectId);
  const fullHref = ledgerBookId ? `${proofHref}&ledgerBookId=${encodeURIComponent(ledgerBookId)}` : proofHref;
  return <>
    <button type="button" className="rounded-sm underline decoration-dotted underline-offset-4 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
      aria-label={`Inspect evidence for ${amount.currency ?? ""} ${amount.value}`.trim()}
      onClick={(event) => { event.stopPropagation(); setOpen(true); }}>
      <AmountCell {...amount} />
    </button>
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetContent aria-label="Amount evidence">
        <SheetHeader>
          <SheetTitle>Amount evidence</SheetTitle>
          <SheetDescription>Retained source, reconciliation, approval, and audit evidence for this entry. Completeness reflects the evidence service.</SheetDescription>
          <SheetCloseButton onClick={() => setOpen(false)} />
        </SheetHeader>
        <SheetBody>
          <p className="mb-3"><AmountCell {...amount} /></p>
          {!current ? <p role="status">Loading retained evidence…</p> : null}
          {current?.error ? <p role="alert">{current.error}</p> : null}
          {current?.packet ? <>
            <p className="mb-2">{current.packet.subject.label}: {current.packet.completeness.status}</p>
            {current.packet.nodes.length === 0 ? <p role="status">No retained evidence is available for this subject.</p> : null}
            {(current.packet.warnings ?? []).map((warning, index) => <p key={index} className="mb-2 text-sm">{warning}</p>)}
            <ul className="space-y-3">{current.packet.nodes.map((node) => <li key={node.evidenceId} className="rounded border border-border p-3">
              <p className="text-sm font-semibold">{node.kind} — {node.status}</p>
              <p className="text-sm">{node.summary}</p>
              <p className="text-xs text-muted-foreground">{node.sourceSystem}{node.freshness.isStale ? " · Stale" : ""}</p>
            </li>)}</ul>
            <Button asChild variant="outline" className="mt-4"><a href={fullHref}>Open full evidence</a></Button>
          </> : null}
        </SheetBody>
      </SheetContent>
    </Sheet>
  </>;
}
