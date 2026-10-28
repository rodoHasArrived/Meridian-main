import { apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { FactorPaydownDraftResult, FactorPaydownPreview, FactorPaydownRequest, FactorPaydownScope } from "@/types/factor-paydown";

function route(bookId: string, action: "prepare" | "draft") {
  const template = action === "prepare" ? UI_API_ROUTES.LedgerFactorPaydownPreparation : UI_API_ROUTES.LedgerFactorPaydownDrafts;
  return template.replace("{ledgerBookId:guid}", encodeURIComponent(bookId));
}

function matchesScope(scope: FactorPaydownScope | undefined, request: FactorPaydownRequest) {
  return scope && scope.ledgerBookId?.toLowerCase() === request.ledgerBookId.toLowerCase()
    && scope.periodId?.toLowerCase() === request.periodId.toLowerCase() && scope.fundProfileId === request.fundProfileId
    && scope.securityId?.toLowerCase() === request.securityId.toLowerCase()
    && scope.bookPositionId?.toLowerCase() === request.bookPositionId.toLowerCase()
    && scope.accountingBasis === request.accountingBasis;
}

export async function prepareFactorPaydown(request: FactorPaydownRequest, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<FactorPaydownPreview>(route(request.ledgerBookId, "prepare"), request,
    { ...options, allowDevelopmentFallback: false });
  if (!result.preparationFingerprint || !matchesScope(result.instruction?.scope, request)) {
    throw new Error("Factor-paydown preparation does not match the selected scope.");
  }
  const instruction = result.instruction;
  if (!Array.isArray(instruction.lots) || !result.components ||
    Object.values(result.components).some((amount) => typeof amount !== "string") ||
    instruction.lots.some((lot) => !lot.expectedLot?.lotId ||
      [lot.principal, lot.transactionBasisAfter, lot.functionalBasisAfter].some((amount) => typeof amount !== "string"))) {
    throw new Error("Factor-paydown amounts must retain exact decimal strings.");
  }
  return result;
}

export async function createFactorPaydownDraft(request: FactorPaydownRequest, expectedPreparationFingerprint: string,
  description: string, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<FactorPaydownDraftResult>(route(request.ledgerBookId, "draft"),
    { request, expectedPreparationFingerprint, description }, { ...options, allowDevelopmentFallback: false });
  if (!matchesScope(result.spine?.scope, request)) throw new Error("Factor-paydown draft does not match the reviewed scope. Reload retained journals before retrying.");
  return result;
}
