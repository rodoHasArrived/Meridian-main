import { useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import { evidenceWorkbenchPath, WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { EvidenceVaultDocumentEntry } from "@/types";
import type { SignedAgreementDocument } from "@/types/entity-operations";
import { OperationError, OperationField, useEntityData } from "./accounting-screen.entity-operations.shared";

export function agreementSignedEvidencePath(agreementId: string) {
  return `${WORKSTATION_ROUTE_CATALOG.reportingEvidence}?${new URLSearchParams({ subjectKind: "accounting-record", subjectId: "current", classification: "Agreement", linkKind: "IntercompanyAgreement", objectId: agreementId })}`;
}

export function SignedAgreementEvidencePicker({ agreementId, onSelect }: { agreementId: string; onSelect: (document: SignedAgreementDocument) => void }) {
  const [enabled, setEnabled] = useState(false);
  const [selected, setSelected] = useState("");
  const query = new URLSearchParams({ classification: "Agreement", reviewStatus: "Accepted", linkKind: "IntercompanyAgreement", objectId: agreementId, maxResults: "100" });
  const documents = useEntityData<EvidenceVaultDocumentEntry[]>(enabled ? `${UI_API_ROUTES.WorkstationEvidenceVaultDocuments}?${query}` : null);
  const eligible = documents.data?.filter((entry) => entry.document.artifactId && entry.document.scope && /^[a-fA-F0-9]{64}$/.test(entry.document.sourceHashSha256)
    && entry.document.reviewerState.status === "Accepted"
    && entry.document.objectLinks.some((link) => String(link.linkKind) === "IntercompanyAgreement" && link.objectId.toLowerCase() === agreementId.toLowerCase())) ?? [];
  const identity = (entry: EvidenceVaultDocumentEntry) => `${entry.vaultId}:${entry.document.documentId}:${entry.document.artifactId}`;
  const chosen = eligible.find((entry) => identity(entry) === selected);
  function choose(value: string) {
    setSelected(value);
    const entry = eligible.find((row) => identity(row) === value);
    if (entry) onSelect({ vaultId: entry.vaultId, documentId: entry.document.documentId, artifactId: entry.document.artifactId!,
      contentHashSha256: entry.document.sourceHashSha256, scope: entry.document.scope! });
  }
  return <div className="mb-3 space-y-3"><Button type="button" size="sm" variant="outline" onClick={() => { setEnabled(true); documents.refresh(); }} disabled={documents.loading}>Find retained signed agreements</Button>
    <OperationError error={documents.error} />
    {enabled ? <OperationField label="Retained signed agreement document" value={selected} onChange={choose}><option value="">Select a retained agreement artifact</option>{eligible.map((entry) => <option value={identity(entry)} key={identity(entry)}>{entry.document.fileName} · {entry.document.receivedAt.slice(0, 10)} · {entry.document.reviewerState.status}</option>)}</OperationField> : null}
    {enabled && !documents.loading && !documents.error && eligible.length === 0 ? <p className="text-xs text-muted-foreground">No accepted signed agreement artifact is linked to this agreement record. Save the agreement draft, retain and accept its signed bytes in Evidence Vault with the IntercompanyAgreement link, then revise the draft to attach it.</p> : null}
    {chosen ? <Link className="text-sm underline underline-offset-4" to={evidenceWorkbenchPath(chosen.subjectKind, chosen.subjectId)}>Inspect selected signed evidence</Link> : null}
  </div>;
}
