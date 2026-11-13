import { useEffect, useId, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { listIntercompanyAgreements } from "@/lib/api/intercompany-agreements.api";
import type { ManualJournalEntryLine } from "@/types";

const guidPattern = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";
interface Props { line: ManualJournalEntryLine; entityId: string; onApply: (line: Partial<ManualJournalEntryLine>) => void; loadCapability?: typeof listIntercompanyAgreements }

/** Apply both reference fields in one draft change; unknown host capability never enables authoring. */
export function IntercompanyJournalReference({ line, entityId, onApply, loadCapability = listIntercompanyAgreements }: Props) {
  const id = useId();
  const [canAuthor, setCanAuthor] = useState(false);
  const [agreement, setAgreement] = useState(line.dimensions?.intercompanyAgreementId ?? "");
  const [version, setVersion] = useState(line.dimensions?.intercompanyAgreementVersion?.toString() ?? "");
  const [counterparty, setCounterparty] = useState(line.dimensions?.counterpartyId ?? "");
  useEffect(() => {
    const controller = new AbortController();
    loadCapability({ entityId: entityId || undefined }, { signal: controller.signal })
      .then(result => { if (!controller.signal.aborted) setCanAuthor(result.referencePostingSupported === true); })
      .catch(() => { if (!controller.signal.aborted) setCanAuthor(false); });
    return () => controller.abort();
  }, [entityId, loadCapability]);
  const validPair = Boolean(agreement.trim()) === Boolean(version.trim())
    && (!agreement.trim() || (new RegExp(`^${guidPattern}$`).test(agreement.trim()) && Number.isSafeInteger(Number(version)) && Number(version) > 0 && new RegExp(`^${guidPattern}$`).test(counterparty.trim())));
  const retained = line.dimensions?.intercompanyAgreementId;
  return <div className="space-y-3 rounded-md border border-border p-3" aria-label="Intercompany agreement reference">
    <h5 className="font-semibold">Intercompany agreement</h5>
    {retained ? <p className="break-all text-sm"><Link className="underline underline-offset-4" to={`/accounting/configure?${new URLSearchParams({ entityId, agreementId: retained })}#configure-section-agreements`}>Inspect agreement {retained}</Link> · approved version {line.dimensions?.intercompanyAgreementVersion ?? "missing"}</p> : <p className="text-sm text-muted-foreground">No agreement is referenced on this line. Agreement coverage remains advisory.</p>}
    {canAuthor ? <>
      <FormGrid columns={3}>
        <FormRow label="Line agreement ID" labelFor={`${id}-agreement`}><Input id={`${id}-agreement`} value={agreement} onChange={event => setAgreement(event.target.value)} /></FormRow>
        <FormRow label="Line approved agreement version" labelFor={`${id}-version`}><Input id={`${id}-version`} inputMode="numeric" value={version} onChange={event => setVersion(event.target.value)} /></FormRow>
        <FormRow label="Line counterparty entity" labelFor={`${id}-counterparty`}><Input id={`${id}-counterparty`} value={counterparty} onChange={event => setCounterparty(event.target.value)} /></FormRow>
      </FormGrid>
      <p className="text-xs text-muted-foreground">Supply both agreement fields together and the other legal entity. Clear both fields to remove the reference. The server validates the version for the accounting date.</p>
      <Button variant="outline" disabled={!validPair} onClick={() => onApply({ dimensions: { ...line.dimensions,
        counterpartyId: counterparty.trim() || null, intercompanyAgreementId: agreement.trim() || null,
        intercompanyAgreementVersion: version.trim() ? Number(version) : null } })}>Apply line agreement reference</Button>
    </> : <p className="text-xs text-muted-foreground">Reference authoring is unavailable until this host confirms compatible posting writers.</p>}
  </div>;
}
