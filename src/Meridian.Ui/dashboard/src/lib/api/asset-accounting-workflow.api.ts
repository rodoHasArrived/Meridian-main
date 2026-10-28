import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { AssetAccountingDraftCapabilities, AssetApprovalDecision, AssetPostedJournalReceipt, AssetWorkflowScope } from "@/types/asset-accounting-workflow";

const sameId = (a: string | null | undefined, b: string) => a?.toLowerCase() === b.toLowerCase();
export const sameAssetPrincipal = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.toLowerCase() === b.toLowerCase();
export function assetCommandMatchesScope(command: { journalEntryId: string; fundProfileId: string; ledgerBookId?: string | null }, scope: AssetWorkflowScope) {
  return sameId(command.journalEntryId, scope.journalEntryId) && command.fundProfileId === scope.fundProfileId && sameId(command.ledgerBookId, scope.ledgerBookId);
}
export function postedAssetJournalMatchesScope(receipt: AssetPostedJournalReceipt, scope: AssetWorkflowScope) {
  return sameId(receipt.journalEntryId, scope.journalEntryId) && sameId(receipt.ledgerBookId, scope.ledgerBookId) && sameId(receipt.periodId, scope.periodId) && (!scope.accountingBasis || receipt.accountingBasis === scope.accountingBasis);
}
export function assetCapabilitiesMatchScope(result: AssetAccountingDraftCapabilities, scope: AssetWorkflowScope) {
  return result.protocolVersion === 1 && assetCommandMatchesScope(result, scope) && sameId(result.periodId, scope.periodId)
    && result.draftVersion === scope.draftVersion && /^[a-f0-9]{64}$/i.test(result.candidateFingerprint) && typeof result.hasPendingPost === "boolean" && typeof result.authenticatedActor === "string" && !!result.authenticatedActor.trim();
}
export async function getAssetAccountingDraftCapabilities(scope: AssetWorkflowScope, options: ApiRequestOptions = {}) {
  const path = UI_API_ROUTES.LedgerFactorPaydownDraftCapabilities.replace("{ledgerBookId:guid}", encodeURIComponent(scope.ledgerBookId)).replace("{journalEntryId:guid}", encodeURIComponent(scope.journalEntryId));
  const result = await apiGetJson<AssetAccountingDraftCapabilities>(`${path}?${new URLSearchParams({ fundProfileId: scope.fundProfileId })}`, { ...options, allowDevelopmentFallback: false });
  if (!assetCapabilitiesMatchScope(result, scope)) {
    throw new Error("The asset workflow does not match the selected journal version. Reload the retained workbench before continuing.");
  }
  let approvalDecision: AssetApprovalDecision | null = null;
  if (result.approval) {
    const approval = result.approval;
    if (!approval.approvalId || !approval.approver || !approval.subjectId || !approval.approvedAtUtc
      || approval.candidateFingerprint !== result.candidateFingerprint || !approval.canonicalContentBase64 || !approval.contentHashSha256) {
      throw new Error("The retained approval is incomplete or belongs to another candidate.");
    }
    if (approval.canonicalContentBase64.length > 1_398_104) throw new Error("Retained approval receipt exceeds its supported byte limit.");
    const bytes = Uint8Array.from(atob(approval.canonicalContentBase64), char => char.charCodeAt(0));
    if (bytes.length === 0 || bytes.length > 1024 * 1024) throw new Error("Retained approval receipt exceeds its supported byte limit.");
    const digest = Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", bytes))).map(value => value.toString(16).padStart(2, "0")).join("");
    if (digest !== approval.contentHashSha256.toLowerCase()) throw new Error("Retained approval receipt bytes do not match their digest.");
    const decision = JSON.parse(new TextDecoder().decode(bytes)) as AssetApprovalDecision;
    if (decision.protocolVersion !== 1 || decision.decision !== "Approve" || !assetCommandMatchesScope(decision, scope)
      || !sameId(decision.periodId, scope.periodId) || (scope.accountingBasis && decision.accountingBasis !== scope.accountingBasis)
      || decision.approvalId !== approval.approvalId || decision.actor !== approval.approver || decision.subjectId !== approval.subjectId
      || decision.candidateFingerprint !== result.candidateFingerprint || !decision.eventId || (!Number.isSafeInteger(decision.eventVersion) || decision.eventVersion <= 0)
      || !Number.isFinite(Date.parse(decision.approvedAtUtc)) || Date.parse(decision.approvedAtUtc) !== Date.parse(approval.approvedAtUtc)) {
      throw new Error("The retained approval decision bytes do not match their declared identity, candidate or scope.");
    }
    approvalDecision = decision;
  }
  if (result.hasPendingPost && result.canPost && !result.pendingPostCommand) throw new Error("Posting recovery requires its retained original command.");
  if (result.pendingPostCommand && (!result.hasPendingPost || !assetCommandMatchesScope(result.pendingPostCommand, scope) || result.pendingPostCommand.action !== "Post"
    || !result.pendingPostCommand.correlationId || !Number.isSafeInteger(result.pendingPostCommand.version))) {
    throw new Error("The retained posting command does not match this journal.");
  }
  if (result.canPost === true && !approvalDecision) throw new Error("Posting requires a verified retained independent approval receipt.");
  if (result.pendingPostCommand && result.approval && (!sameAssetPrincipal(result.pendingPostCommand.actor, result.approval.approver) || !sameAssetPrincipal(result.pendingPostCommand.actor, result.authenticatedActor))) throw new Error("Posting recovery does not belong to the retained approver.");
  if (result.postedJournal && !postedAssetJournalMatchesScope(result.postedJournal, scope)) throw new Error("The posted receipt does not match this journal, book and period.");
  return { ...result, approvalDecision };
}
