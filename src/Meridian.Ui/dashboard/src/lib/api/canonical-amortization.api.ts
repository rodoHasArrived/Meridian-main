import { apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { AccountingBasisKind } from "@/types";

export interface CanonicalAmortizationRequest {
  ledgerBookId: string; taxLotRecordId: string; periodId: string; expectedPeriodVersion: number;
  accountingBasis: AccountingBasisKind; fundProfileId: string; asOfDate: string;
  securityEvidence: Record<string, unknown>; eventEvidence: Record<string, unknown>;
  assetAccountPath: string; incomeAccountPath: string;
  scheduleEndDate?: string | null; scheduleFrequency?: "Monthly" | "Quarterly" | "Annual";
  amortizeRemainingFace: boolean; automaticYieldToWorst: boolean;
}
interface Scope { ledgerBookId: string; periodId: string; fundProfileId: string; accountingBasis: AccountingBasisKind }
export interface CanonicalAmortizationPreview {
  scope: Scope;
  preparationFingerprint: string;
  instruction: {
    calculationVersion: string; asOfDate: string;
    expectedLot: { taxLotRecordId: string; ledgerBookId: string; lotId: string; version: number; acquisition: { functionalCurrency: string; faceValueTerms?: { amortizationMethod: string; effectiveYield: string | null } | null } };
    security: { securityId: string; version: number };
    yieldToWorst?: unknown;
  };
  projection: { transactionCostBasis: string; functionalCostBasis: string; transactionMovement: string; functionalMovement: string };
  occurrences?: { date: string; transactionCostBasis: string; functionalCostBasis: string }[];
  lines: { accountId: string; debit: string; credit: string; currency: string }[];
}
export interface CanonicalAmortizationDraft {
  spine: { scope: Scope };
  candidate: { journalEntryId: string | null; hasBlockingIssues: boolean; issues: { message: string }[] };
}
const route = (bookId: string, action: "prepare" | "drafts") => (action === "prepare" ? UI_API_ROUTES.LedgerCanonicalAmortizationPreparation : UI_API_ROUTES.LedgerCanonicalAmortizationDrafts).replace("{ledgerBookId:guid}", encodeURIComponent(bookId));
function sameScope(scope: Scope | undefined, request: CanonicalAmortizationRequest): boolean {
  return !!scope && scope.ledgerBookId.toLowerCase() === request.ledgerBookId.toLowerCase() && scope.periodId.toLowerCase() === request.periodId.toLowerCase() && scope.fundProfileId === request.fundProfileId && scope.accountingBasis === request.accountingBasis;
}
export async function prepareCanonicalAmortization(request: CanonicalAmortizationRequest, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<CanonicalAmortizationPreview>(route(request.ledgerBookId, "prepare"), request, { ...options, allowDevelopmentFallback: false });
  if (!sameScope(result.scope, request) || result.instruction?.expectedLot?.taxLotRecordId.toLowerCase() !== request.taxLotRecordId.toLowerCase() || result.instruction.asOfDate !== request.asOfDate)
    throw new Error("Amortization preparation does not match the selected scope, lot and occurrence.");
  if (!result.preparationFingerprint || !result.lines || Object.values(result.projection ?? {}).length !== 4 || Object.values(result.projection).some(value => typeof value !== "string") || result.lines.some(line => typeof line.debit !== "string" || typeof line.credit !== "string"))
    throw new Error("Amortization preparation requires exact decimal strings and a retained fingerprint.");
  return result;
}
export async function createCanonicalAmortizationDraft(request: CanonicalAmortizationRequest, expectedPreparationFingerprint: string, description: string, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<CanonicalAmortizationDraft>(route(request.ledgerBookId, "drafts"), { request, expectedPreparationFingerprint, description }, { ...options, allowDevelopmentFallback: false });
  if (!sameScope(result.spine?.scope, request)) throw new Error("Amortization draft does not match the reviewed scope.");
  return result;
}
