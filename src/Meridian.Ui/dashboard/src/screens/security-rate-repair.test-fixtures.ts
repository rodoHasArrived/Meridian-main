import type { CreateRateRepairBatch, RateRepairBatch, RateRepairCandidate, RateRepairDiscovery } from "@/lib/api/security-rate-repair.api";

export const repairSecurityId = "11111111-1111-1111-1111-111111111111";
export const repairBatchId = "22222222-2222-2222-2222-222222222222";
export const candidate = (changes: Partial<RateRepairCandidate> = {}): RateRepairCandidate => ({
  securityId: repairSecurityId, displayName: "Synthetic CD", assetClass: "CertificateOfDeposit", version: 7,
  sourceSystem: "vendor-a", sourceField: "assetSpecificTerms.interestRate", sourceSchemaVersion: "vendor-2026-v2",
  issuer: "Synthetic Bank", rawValue: "0.500000000000", currentUnit: null, rateRole: "AnnualRate",
  allowedUnits: ["Fraction", "Percent", "BasisPoints"], eligible: true, blockerCode: null, blockerReason: null,
  fixPath: `/accounting/security-master?securityId=${repairSecurityId}`, ...changes
});
export const discovery = (): RateRepairDiscovery => ({ asOfUtc: "2026-11-14T10:00:00Z", candidates: [candidate()],
  skip: 0, pageSize: 200, scannedCount: 1, nextSkip: null, isComplete: true, scopeLabel: "Authorized synthetic discovery", issues: [],
  actions: [{ command: "Create", enabled: true, disabledReason: null }] });
export const createInput = (): CreateRateRepairBatch => ({
  requestId: repairBatchId, scope: { fundAccountId: "33333333-3333-3333-3333-333333333333",
    ledgerBookId: "44444444-4444-4444-4444-444444444444", fundProfileId: "synthetic-fund", periodId: "2026-11" },
  cohort: { sourceSystem: "vendor-a", sourceField: "assetSpecificTerms.interestRate", sourceSchemaVersion: "vendor-2026-v2", rateRole: "AnnualRate" },
  items: [{ securityId: repairSecurityId, expectedVersion: 7 }], unit: "Percent", effectiveFrom: "2026-11-01T00:00:00Z",
  evidenceId: "vault:synthetic-source-definition", justification: "Reviewed vendor field definition.", evidenceAppliesToAllRecords: true
});
export const retainedBatch = (changes: Partial<RateRepairBatch> = {}): RateRepairBatch => ({
  batchId: repairBatchId, version: 1, createdAtUtc: "2026-11-14T10:01:00Z", createdBy: "synthetic-maker",
  scope: createInput().scope, cohort: createInput().cohort, unit: "Percent", effectiveFrom: createInput().effectiveFrom,
  evidenceId: createInput().evidenceId, evidenceSha256: "a".repeat(64), justification: createInput().justification,
  items: [{ securityId: repairSecurityId, baseVersion: 7, displayName: "Synthetic CD", assetClass: "CertificateOfDeposit",
    rawValue: "0.500000000000", state: "Selected", revisionId: null, workflowId: null, impactReceiptId: null,
    approvalReceiptId: null, errorCode: null, error: null }],
  actions: ["Stage", "Submit", "Approve", "Publish"].map(command => ({ command, enabled: command === "Stage", disabledReason: command === "Stage" ? null : "Stage the current records first." })), ...changes
});
