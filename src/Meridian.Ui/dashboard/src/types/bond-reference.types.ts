export interface BondReferenceLifecycle {
  securityId: string;
  lifecycleStat?: string | number;
  issueDate: string | null;
  callDate: string | null;
  maturityDate: string;
  isCallable: boolean;
  version: number;
}

/** Retained bond reference row; optional term blocks are omitted when null. */
export interface BondReferenceRow {
  securityId: string;
  displayName: string;
  currency: string;
  issuerName: string | null;
  seniority: string | null;
  primaryIdentifier: string;
  lifecycle: BondReferenceLifecycle | null;
  version: number;
}

/** A bounded issuer or maturity page. The cursor is opaque and bound to its query. */
export interface BondReferencePage {
  items: BondReferenceRow[];
  nextCursor: string | null;
}

export interface BondIssuerLadderPageQuery {
  issuerName: string;
  pageSize?: number;
  cursor?: string | null;
  asOf?: string | null;
}

export interface BondMaturityLadderPageQuery {
  from: string;
  to: string;
  pageSize?: number;
  cursor?: string | null;
  asOf?: string | null;
}
