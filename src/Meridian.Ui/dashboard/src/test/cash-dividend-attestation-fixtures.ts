import type {
  CashDividendAttestation, CashDividendAttestationWorkspace
} from "@/types/cash-dividend-attestation";
import { CASH_DIVIDEND_ATTESTATION_CONFIRMATION, CASH_DIVIDEND_ATTESTATION_POLICY } from "@/lib/cash-dividend-attestation-api";
import { PREPARER_ATTESTATION_DISCLOSURE } from "@/lib/single-principal-attestation-api";

export const cashDividendCaseId = "11111111-1111-4111-8111-111111111111";
export const cashDividendScope = { tenantId: "tenant-one", companyId: "company-one",
  ledgerBookId: "00000000-0000-4000-8000-0000000000aa", periodId: "00000000-0000-4000-8000-0000000000bb",
  accountingBasis: "US-GAAP", functionalCurrency: "USD" };

/** Deterministic synthetic UI inputs; these are not proof of live-provider data or accounting. */
export function cashDividendWorkspace(): CashDividendAttestationWorkspace {
  const processingCase = { caseId: cashDividendCaseId, proposalId: "proposal-synthetic", corporateActionId: "corporate-action-synthetic",
    securityId: "00000000-0000-4000-8000-0000000000cc", scope: cashDividendScope, state: "ReadyForApproval", version: 5,
    createdBy: "preparer-one", createdAtUtc: "2026-11-12T12:00:00Z", updatedBy: "preparer-one", updatedAtUtc: "2026-11-12T12:00:00Z" };
  const projection = { projectionId: "22222222-2222-4222-8222-222222222222", caseId: cashDividendCaseId,
    boundCaseVersion: 5, accountingEventId: "33333333-3333-4333-8333-333333333333", accountingEventVersion: 2,
    spineVersion: 3, projectionInputHash: "a".repeat(64), postingIntentHash: "b".repeat(64), postingIdempotencyKey: "retained-posting-key",
    draftedCandidateFingerprint: "c".repeat(64), policyDecisionId: "44444444-4444-4444-8444-444444444444", policyDecisionVersion: 1,
    rulePackId: "synthetic-cash-dividend", rulePackVersion: "1", selectedRuleId: "cash-income", selectedRuleVersion: "1",
    ledgerBookId: cashDividendScope.ledgerBookId, periodId: cashDividendScope.periodId, expectedPeriodVersion: 2,
    accountingBasis: "US-GAAP", fundProfileId: "synthetic-fund", currency: "USD", effectiveDate: "2026-11-12",
    totalDebits: 100, totalCredits: 100, lotSnapshotId: "55555555-5555-4555-8555-555555555555", lotSnapshotVersion: 1,
    hasAuthoritativeLotResolution: true, preparedBy: "preparer-one", preparedAtUtc: "2026-11-12T12:00:00Z", isCurrent: true,
    supersededAtUtc: null, isBalanced: true };
  const accountRoleBinding = { schemaVersion: 1, ledgerBookId: cashDividendScope.ledgerBookId,
    dividendReceivableAccount: "dividend-receivable", dividendIncomeAccount: "dividend-income",
    dividendReceivableAccountName: "Dividend receivable", dividendIncomeAccountName: "Dividend income",
    rulePackId: projection.rulePackId, rulePackVersion: projection.rulePackVersion,
    selectedRuleId: projection.selectedRuleId, selectedRuleVersion: projection.selectedRuleVersion };
  return { schemaVersion: 1, availability: "Available", policyVersion: CASH_DIVIDEND_ATTESTATION_POLICY,
    policyHash: "d".repeat(64), policyEnabled: true, actor: "preparer-one", scope: cashDividendScope, case: processingCase,
    canAdministerDeclaration: true, canBindAccountingRoles: true, canAttestAccounting: true, canPostAttestedAccounting: false,
    declaration: { declarationId: "66666666-6666-4666-8666-666666666666", revision: 2, tenantId: cashDividendScope.tenantId,
      companyId: cashDividendScope.companyId, ledgerBookId: cashDividendScope.ledgerBookId, enabled: true,
      principalId: "preparer-one", policyVersion: CASH_DIVIDEND_ATTESTATION_POLICY, policyHash: "d".repeat(64),
      changedBy: "preparer-one", recordedAtUtc: "2026-11-12T12:00:00Z", reason: "Synthetic single-principal book", accountRoleBinding },
    declarationHistory: [],
    preview: { case: processingCase, projection, eligibility: { securityId: processingCase.securityId, exDate: "2026-11-12",
      relatedEventKey: "related-synthetic-event", providerId: "synthetic-provider", sourceEventId: "synthetic-source-event",
      sourceEventVersion: "1", sourceEvidenceHash: "e".repeat(64), dividendPerShare: 1, grossImpactUsd: 100,
      wholeEventGrossUsd: 300, budgetMonth: "2026-11-01", entitlementSnapshotHash: "f".repeat(64),
      entitlementSnapshotJson: "{\"synthetic\":true}", accountRoleBinding }, disclosure: PREPARER_ATTESTATION_DISCLOSURE,
      confirmation: CASH_DIVIDEND_ATTESTATION_CONFIRMATION },
    attestation: null, posting: null, disclosure: PREPARER_ATTESTATION_DISCLOSURE,
    confirmation: CASH_DIVIDEND_ATTESTATION_CONFIRMATION, relatedEventGrossLimitUsd: 10000, bookCalendarMonthGrossLimitUsd: 100000,
    evidenceReference: "synthetic-retained-eligibility", evidenceHash: "f".repeat(64), blockers: [] };
}

export function retainedCashDividendAttestation(workspace = cashDividendWorkspace()): CashDividendAttestation {
  const eligibility = workspace.preview!.eligibility;
  return { attestationId: "77777777-7777-4777-8777-777777777777", caseId: cashDividendCaseId,
    projectionId: workspace.preview!.projection.projectionId, boundCaseVersion: 6,
    declarationId: workspace.declaration!.declarationId, declarationRevision: workspace.declaration!.revision,
    policyVersion: workspace.policyVersion, policyHash: workspace.policyHash,
    budgetReservationId: "88888888-8888-4888-8888-888888888888", relatedEventKey: eligibility.relatedEventKey,
    exDate: eligibility.exDate, providerId: eligibility.providerId, sourceEventId: eligibility.sourceEventId,
    sourceEventVersion: eligibility.sourceEventVersion, sourceEvidenceHash: eligibility.sourceEvidenceHash,
    entitlementSnapshotHash: eligibility.entitlementSnapshotHash, entitlementSnapshotJson: eligibility.entitlementSnapshotJson,
    grossImpactUsd: eligibility.grossImpactUsd, wholeEventGrossUsd: eligibility.wholeEventGrossUsd,
    budgetMonth: eligibility.budgetMonth, attestedBy: "preparer-one", attestedAtUtc: "2026-11-12T12:05:00Z",
    reason: "Inspected synthetic cash-dividend projection", evidenceReference: workspace.evidenceReference!,
    evidenceHash: workspace.evidenceHash!, confirmation: workspace.confirmation,
    disclosure: PREPARER_ATTESTATION_DISCLOSURE, voidedAtUtc: null, voidedBy: null, isActive: true,
    decisionKind: "PreparerAttestation", accountRoleBinding: eligibility.accountRoleBinding };
}
