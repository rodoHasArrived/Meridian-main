/** Versioned reference; presence of context or a route never establishes proof readiness. */
export interface NumberPassportDeclaredScope {
  tenantId: string;
  companyId: string;
  organizationId?: string | null;
  entityId?: string | null;
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  periodId?: string | null;
  accountId?: string | null;
  runId?: string | null;
}

export interface NumberPassportReference {
  schemaVersion: 1;
  subjectType: string | null;
  subjectId: string | null;
  scope: NumberPassportDeclaredScope | null;
  unavailableReason?: string | null;
  /** Exact source text; never parse display labels to manufacture this field. */
  amount?: string | null;
  currency?: string | null;
  basis?: string | null;
  asOf?: string | null;
}
