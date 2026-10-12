import { apiGetJson, apiPostJson, getLedgerCloseManagementPeriodPlan, getOperationsContinuityWorkflows } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { LedgerBook, LedgerPeriod } from "@/types";
import type { CaptureClosePlanTemplateRequest, ClosePlanTemplate, ClosePreparationPreview, PreparedClosePlanResult, PreviewClosePreparationRequest } from "@/types/close-preparation";

const options = (signal?: AbortSignal) => ({ signal, allowDevelopmentFallback: false });
const sameId = (a: string | null | undefined, b: string) => a?.toLowerCase() === b.toLowerCase();

export const listClosePreparationSources = (signal?: AbortSignal) => getOperationsContinuityWorkflows({}, options(signal));

export async function getClosePreparationSource(workflowId: string, signal?: AbortSignal) {
  const plan = await getLedgerCloseManagementPeriodPlan(workflowId, options(signal));
  if (!sameId(plan.workflowId, workflowId) || !plan.ledgerBookId) throw new Error("The source plan does not match the selected workflow or has no authoritative book.");
  return plan;
}

export async function getClosePreparationBooks(sourceBookId: string, signal?: AbortSignal) {
  const source = await apiGetJson<LedgerBook>(UI_API_ROUTES.LedgerBookById.replace("{ledgerBookId:guid}", encodeURIComponent(sourceBookId)), options(signal));
  if (!sameId(source.ledgerBookId, sourceBookId)) throw new Error("The source book does not match the selected plan.");
  const books = await apiGetJson<LedgerBook[]>(`${UI_API_ROUTES.LedgerBooks}?${new URLSearchParams({ fundProfileId: source.fundProfileId })}`, options(signal));
  if (!Array.isArray(books) || books.some(book => book.fundProfileId !== source.fundProfileId)) throw new Error("Target books do not match the source book's fund.");
  return books;
}

export async function getClosePreparationPeriods(ledgerBookId: string, signal?: AbortSignal) {
  const periods = await apiGetJson<LedgerPeriod[]>(`${UI_API_ROUTES.LedgerPeriods}?${new URLSearchParams({ ledgerBookId })}`, options(signal));
  if (!Array.isArray(periods) || periods.some(period => !sameId(period.ledgerBookId, ledgerBookId))) throw new Error("Target periods do not match the selected book.");
  return periods;
}

export async function listClosePlanTemplates(sourceWorkflowId: string, signal?: AbortSignal) {
  const templates = await apiGetJson<ClosePlanTemplate[]>(`${UI_API_ROUTES.LedgerCloseManagementTemplates}?${new URLSearchParams({ sourceWorkflowId })}`, options(signal));
  if (!Array.isArray(templates) || templates.some(template => !sameId(template.sourceWorkflowId, sourceWorkflowId))) throw new Error("Retained templates do not match the selected source plan.");
  return templates;
}

export function captureClosePlanTemplate(request: CaptureClosePlanTemplateRequest, signal?: AbortSignal) {
  return apiPostJson<ClosePlanTemplate>(UI_API_ROUTES.LedgerCloseManagementTemplates, request, options(signal));
}

export async function previewClosePreparation(request: PreviewClosePreparationRequest, signal?: AbortSignal) {
  const result = await apiPostJson<ClosePreparationPreview>(UI_API_ROUTES.LedgerCloseManagementPreparationPreview, request, options(signal));
  if (!sameId(result.templateId, request.templateId) || result.templateVersion !== request.templateVersion
    || !sameId(result.targetBook.ledgerBookId, request.targetLedgerBookId) || !sameId(result.targetPeriod.periodId, request.targetPeriodId)
    || !sameId(result.targetPeriod.ledgerBookId, request.targetLedgerBookId)) throw new Error("The preparation preview does not match the selected template, book and period.");
  return result;
}

export function createPreparedClosePlan(request: { previewId: string; idempotencyKey: string }, signal?: AbortSignal) {
  return apiPostJson<PreparedClosePlanResult>(UI_API_ROUTES.LedgerCloseManagementPreparationCreate, request, options(signal));
}
