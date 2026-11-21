import type { AmountPassport, AmountPassportIdentity, AmountPassportReference } from "@/types/amount-passport";

export function passportIdentity(overrides: Partial<AmountPassportIdentity> = {}): AmountPassportIdentity {
  return {
    subjectKind: "ledger-amount",
    subjectId: "journal:line:debit",
    scope: { tenantId: "tenant-1", companyId: "company-1", basis: "Primary", fundProfileId: "fund-1", ledgerBookId: "book-1", periodId: "period-1" },
    asOf: "2026-07-31T12:00:00.0000000+00:00",
    revision: "1",
    ...overrides
  };
}

export function createAmountPassport(identity = passportIdentity(), label = "Cash debit", exactValue = "125.50"): AmountPassport {
  const reference: AmountPassportReference = {
    identity, currency: "USD", unit: "currency",
    origin: identity.subjectId.startsWith("report:") ? "calculated" : "sourced", isZero: exactValue === "0", exactValue
  };
  const evidenceQuery = new URLSearchParams({ ledgerAmountSubjectId: identity.subjectId, ledgerBookId: identity.scope.ledgerBookId ?? "",
    periodId: identity.scope.periodId ?? "", fundProfileId: identity.scope.fundProfileId ?? "", expectedContentHash: "a".repeat(64) });
  return {
    reference, label, exactValue, status: "complete", effectiveDate: "2026-07-31",
    calculation: "Retained server calculation.", exchangeRateRule: "Read in functional currency.",
    freshness: { status: "current", evaluatedAt: "2026-08-01T12:00:00Z", policy: "Retained evidence review", detail: "Verified." },
    permissions: { view: "allowed", amend: "read-only", detail: "Posted amounts are immutable.", periodStatus: "HardClosed" },
    components: ["scope", "source", "calculation", "freshness", "evidence", "history", "permissions"]
      .map(component => ({ component, status: "available", detail: "Verified." })),
    sources: [{ kind: "journal", id: "journal", label: "Original posting" }],
    evidence: [{ kind: "source-document", id: "retained-source", label: "Retained journal line",
      href: `/workstation/evidence/vault/retained-source?${evidenceQuery}`, contentHash: "a".repeat(64) }],
    history: [{ kind: "original", id: "journal", label: "Original journal" }],
    gaps: []
  };
}
