import { createHash } from "node:crypto";
import type { AssetApprovalDecision, AssetApprovalReceipt, AssetWorkflowScope } from "@/types/asset-accounting-workflow";

/** Mirrors ApprovalDecision field order and JsonSerializerDefaults.Web casing in AssetApproval.cs. */
export function createAssetApprovalFixture(scope: AssetWorkflowScope, candidateFingerprint = "a".repeat(64)) {
  const approvalId = `asset-approval-${scope.journalEntryId.replace(/-/g, "")}`;
  const eventId = "33333333-3333-3333-3333-333333333333";
  const subjectId = [eventId, "1", scope.fundProfileId, scope.ledgerBookId, scope.periodId, "Primary", approvalId,
    candidateFingerprint, "tenant-1", "company-1"].map((part, index) => [2, 6, 8, 9].includes(index) ? encodeURIComponent(part) : part).join("|");
  const decision: AssetApprovalDecision = {
    protocolVersion: 1, decision: "Approve", journalEntryId: scope.journalEntryId, approvalId, actor: "independent-checker",
    approvedAtUtc: "2026-10-28T00:00:00+00:00", tenantId: "tenant-1", companyId: "company-1", fundProfileId: scope.fundProfileId,
    ledgerBookId: scope.ledgerBookId, periodId: scope.periodId, accountingBasis: "Primary", eventId, eventVersion: 1,
    candidateFingerprint, subjectId, notes: "Independent source review"
  };
  const bytes = Buffer.from(JSON.stringify(decision));
  const approval: AssetApprovalReceipt = { approvalId, approver: decision.actor, approvedAtUtc: decision.approvedAtUtc,
    candidateFingerprint, subjectId, canonicalContentBase64: bytes.toString("base64"), contentHashSha256: createHash("sha256").update(bytes).digest("hex") };
  return { approval, decision, bytes };
}
