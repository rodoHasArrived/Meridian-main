import type { AccountingBasisKind } from "@/types";

/** Meridian.Contracts.Ledger.PrepareFactorPaydownRequestDto. Decimal inputs stay exact text. */
export interface FactorPaydownRequest {
  ledgerBookId: string;
  securityId: string;
  bookPositionId: string;
  periodId: string;
  expectedPeriodVersion: number;
  accountingBasis: AccountingBasisKind;
  fundProfileId: string;
  effectiveDate: string;
  priorFactor: string;
  newFactor: string;
  sourceDomain: string;
  sourceEntityId: string;
  sourceContentHash: string;
  occurredAtUtc: string;
  factorEvidence: Record<string, unknown>[];
  cashAccountPath: string;
  carryingAccountPath: string;
  realizedGainAccountPath: string;
  realizedLossAccountPath: string;
}

export interface FactorPaydownScope { ledgerBookId: string; periodId: string; fundProfileId: string; securityId: string; bookPositionId: string; accountingBasis: AccountingBasisKind }

export interface FactorPaydownPreview {
  preparationFingerprint: string;
  instruction: {
    scope: FactorPaydownScope;
    currency: string;
    priorFactor: string;
    newFactor: string;
    principalPaydown: string;
    lots: { expectedLot: { lotId: string }; principal: string; transactionBasisAfter: string; functionalBasisAfter: string }[];
  };
  components: Record<string, string>;
}

export interface FactorPaydownDraftResult {
  spine: { scope: FactorPaydownScope };
  candidate: { journalEntryId: string | null; hasBlockingIssues: boolean; issues: { message: string }[] };
}
