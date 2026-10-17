import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { workstationEvidencePacketEndpoint } from "@/lib/workstation-endpoints";
import type { EvidencePacket } from "@/types";
import type { LedgerAmountSelection } from "@/types/ledger-amount-proof";

export function getLedgerAmountProof(selection: LedgerAmountSelection, options: ApiRequestOptions = {}) {
  const query = new URLSearchParams({
    ledgerBookId: selection.ledgerBookId,
    periodId: selection.periodId,
    fundProfileId: selection.fundProfileId
  });
  return apiGetJson<EvidencePacket>(
    `${workstationEvidencePacketEndpoint("ledger-amount", selection.subjectId)}?${query}`,
    { ...options, allowDevelopmentFallback: false }
  );
}
