import type { AccountingPreparerAttestationDisclosure } from "./ledger-amount-proof";

export type AmountPassportKind = "ledger-amount" | "position-valuation";
export type AmountOrigin = "sourced" | "calculated" | "overridden" | "estimated" | "unavailable";

export interface AmountPassportScope {
  tenantId: string;
  companyId: string;
  basis: string;
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  periodId?: string | null;
  entityId?: string | null;
  accountId?: string | null;
  organizationId?: string | null;
}

export interface AmountPassportIdentity {
  subjectKind: AmountPassportKind;
  subjectId: string;
  scope: AmountPassportScope;
  asOf: string;
  revision: string;
}

export interface AmountPassportReference {
  identity: AmountPassportIdentity;
  currency: string | null;
  unit: string;
  origin: AmountOrigin;
  isZero: boolean;
  freshness?: string | null;
  exactValue?: string | null;
}

export interface AmountPassportRecord {
  kind: string;
  id: string;
  label: string;
  href?: string | null;
  passport?: AmountPassportIdentity | null;
  contentHash?: string | null;
}

export interface AmountPassport {
  reference: AmountPassportReference;
  label: string;
  exactValue: string | null;
  status: "complete" | "review-required" | "stale" | "blocked" | "superseded" | "unavailable";
  effectiveDate: string | null;
  calculation: string | null;
  exchangeRateRule: string | null;
  freshness: {
    status: string;
    evaluatedAt: string;
    policy: string;
    detail: string;
    observedAt?: string | null;
    ageMinutes?: number | null;
  };
  permissions: { view: string; amend: string; detail: string; periodStatus?: string | null };
  components: { component: string; status: string; detail: string }[];
  sources: AmountPassportRecord[];
  evidence: AmountPassportRecord[];
  history: AmountPassportRecord[];
  gaps: string[];
  accountingPreparerAttestations?: AccountingPreparerAttestationDisclosure[] | null;
}
