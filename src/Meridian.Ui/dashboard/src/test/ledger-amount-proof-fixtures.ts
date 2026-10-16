import type { EvidencePacket } from "@/types";
import type { LedgerAmountSelection } from "@/types/ledger-amount-proof";

export const ledgerAmountSelection: LedgerAmountSelection = {
  subjectId: "22222222-2222-2222-2222-222222222222:33333333-3333-3333-3333-333333333333:debit",
  ledgerBookId: "00000000-0000-0000-0000-0000000000aa",
  periodId: "11111111-1111-1111-1111-111111111111",
  fundProfileId: "fund-1", amount: 250, currency: "USD", label: "Cash AAPL debit $250.00"
};

/** Mirrors the posted amount service's guarded route and canonical artifact binding. */
export function createLedgerAmountProofPacket(selection = ledgerAmountSelection): EvidencePacket {
  const hash = "a".repeat(64);
  const retainedAt = "2026-07-31T11:00:00Z";
  const query = new URLSearchParams({ ledgerAmountSubjectId: selection.subjectId, ledgerBookId: selection.ledgerBookId,
    periodId: selection.periodId, fundProfileId: selection.fundProfileId, expectedContentHash: hash });
  const route = `/workstation/evidence/vault/vault-1?${query}`;
  const subject = { subjectId: selection.subjectId, subjectKind: "ledger-amount", label: "Cash AAPL", workspace: "Accounting", route: null, pageTag: "ledger" };
  const canonicalSubject = `${encodeURIComponent(selection.fundProfileId).replace(/[!'()*]/g, (value) => `%${value.charCodeAt(0).toString(16).toUpperCase()}`)}:${selection.ledgerBookId}:${selection.periodId}:${selection.subjectId}`;
  return {
    subject, generatedAt: "2026-10-01T12:00:00Z", edges: [], actions: [], warnings: [],
    nodes: [{ evidenceId: "evidence-1", subject, kind: "source-document", status: "Ready", freshness: { asOf: retainedAt, isStale: false, reason: null },
      sourceSystem: "Meridian", summary: "Retained journal line", relatedWorkItemIds: [],
      artifactRefs: [{ artifactId: "evidence-1", kind: "source-document", path: null, route, generatedAt: retainedAt,
        hash, retained: true, canonicalSubjectKind: "ledger-amount", canonicalSubjectId: canonicalSubject }] }],
    completeness: { score: 100, status: "Ready", requiredIds: [], readyIds: [], missingIds: [], staleIds: [], blockingWorkItemIds: [] },
    ledgerAmount: {
      subjectId: selection.subjectId,
      scope: { tenantId: "tenant-1", companyId: "company-1", fundProfileId: selection.fundProfileId, ledgerBookId: selection.ledgerBookId, periodId: selection.periodId },
      amount: selection.amount, currency: selection.currency, status: "Ready", warnings: [],
      evidence: [{ evidenceId: "evidence-1", kind: "source-document", label: "Retained journal line", sourceSystem: "Meridian", route,
        retainedAt, status: "Ready", contentHash: hash, reason: null }]
    }
  };
}
