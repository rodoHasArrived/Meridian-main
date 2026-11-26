import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import type { AgreementWorkItem, AgreementWorkQueue, AgreementWorkQueueScope, ObserveAgreementWorkItem, UpdateAgreementWorkItem } from "@/types/intercompany-work-queue";

const route = "/api/ledger/intercompany/work-queue";
const authoritative = (options: ApiRequestOptions) => ({ ...options, allowDevelopmentFallback: false });
function query(scope: AgreementWorkQueueScope) {
  const values = new URLSearchParams({ ledgerBookId: scope.ledgerBookId, asOf: scope.asOf, expiryHorizonDays: String(scope.expiryHorizonDays) });
  if (scope.fundProfileId) values.set("fundProfileId", scope.fundProfileId);
  if (scope.entityId) values.set("entityId", scope.entityId);
  return values;
}
const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();
export async function getAgreementWorkQueue(scope: AgreementWorkQueueScope, options: ApiRequestOptions = {}) {
  const result = await apiGetJson<AgreementWorkQueue>(`${route}?${query(scope)}`, authoritative(options));
  if (!result || !sameId(result.ledgerBookId ?? "", scope.ledgerBookId) || result.asOf !== scope.asOf || result.expiryHorizonDays !== scope.expiryHorizonDays
    || (scope.fundProfileId && result.fundProfileId !== scope.fundProfileId) || !Array.isArray(result.cases) || !Array.isArray(result.guarantees) || !Array.isArray(result.diagnostics)
    || result.cases.some(item => !sameId(item.ledgerBookId, scope.ledgerBookId) || !Number.isSafeInteger(item.version) || !Array.isArray(item.history)))
    throw new Error("The lifecycle queue returned a different book, date or invalid case history. Refresh before acting.");
  return result;
}
function receipt(result: AgreementWorkItem, scope: AgreementWorkQueueScope, request: ObserveAgreementWorkItem | UpdateAgreementWorkItem) {
  if (!result || result.caseId !== request.caseId || !sameId(result.ledgerBookId ?? "", scope.ledgerBookId)
    || !Number.isSafeInteger(result.version) || !result.isTracked || !Array.isArray(result.history)
    || !result.history.some(step => sameId(step.commandId, request.commandId) && (!("action" in request) || step.action === request.action)))
    throw new Error("The lifecycle response does not contain this command's retained case receipt. Refresh before acting.");
  return result;
}
export async function observeAgreementWorkItem(scope: AgreementWorkQueueScope, request: ObserveAgreementWorkItem, options: ApiRequestOptions = {}) {
  return receipt(await apiPostJson<AgreementWorkItem>(`${route}/observe?${query(scope)}`, request, authoritative(options)), scope, request);
}
export async function updateAgreementWorkItem(scope: AgreementWorkQueueScope, request: UpdateAgreementWorkItem, options: ApiRequestOptions = {}) {
  return receipt(await apiPostJson<AgreementWorkItem>(`${route}/update?${query(scope)}`, request, authoritative(options)), scope, request);
}
