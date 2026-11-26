import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { CorporateActionCalendarPage, CorporateActionCaseDeadlines, CorporateActionDeadlineImportPreview, CorporateActionDeadlineImportRequest, CorporateActionDeadlineImportResult, CorporateActionDeadlineMutationResult, RecordCorporateActionDeadlineRequest } from "@/types/corporate-action-calendar.types";

export interface CorporateActionCalendarQuery { from: string; to: string; financialAccountId?: string; custodyAccountId?: string }
const casePath = (route: string, caseId: string) => route.replace("{caseId:guid}", encodeURIComponent(caseId));
const authoritative = (options: ApiRequestOptions) => ({ ...options, allowDevelopmentFallback: false });

/** Pages are a single retained revision. A changed/repeated cursor never yields a silently partial calendar. */
export async function getCorporateActionCalendar(query: CorporateActionCalendarQuery, options: ApiRequestOptions = {}): Promise<CorporateActionCalendarPage> {
  let cursor: string | undefined;
  const seen = new Set<string>();
  let result: CorporateActionCalendarPage | undefined;
  do {
    const params = new URLSearchParams({ from: query.from, to: query.to, take: "250" });
    if (query.financialAccountId) params.set("financialAccountId", query.financialAccountId);
    if (query.custodyAccountId) params.set("custodyAccountId", query.custodyAccountId);
    if (cursor) params.set("cursor", cursor);
    const page = await apiGetJson<CorporateActionCalendarPage>(`${UI_API_ROUTES.SecurityMasterCorporateActionsCalendar}?${params}`, authoritative(options));
    if (page.from !== query.from || page.to !== query.to || result && (page.revision !== result.revision || page.totalCount !== result.totalCount)) throw new Error("Calendar scope or retained revision changed between pages. Refresh the range.");
    result = result ? { ...result, items: [...result.items, ...page.items], nextCursor: page.nextCursor } : page;
    cursor = page.nextCursor || undefined;
    if (cursor && seen.has(cursor)) throw new Error("Calendar paging repeated a cursor. Refresh the range.");
    if (cursor) seen.add(cursor);
  } while (cursor);
  if (result.items.length !== result.totalCount || new Set(result.items.map((item) => item.eventId)).size !== result.items.length) throw new Error("Calendar coverage is incomplete or contains duplicate event identities. Refresh the range.");
  return result;
}
export const getCorporateActionCaseDeadlines = (caseId: string, options: ApiRequestOptions = {}) => apiGetJson<CorporateActionCaseDeadlines>(casePath(UI_API_ROUTES.SecurityMasterCorporateActionCaseDeadlines, caseId), authoritative(options));
export const recordCorporateActionDeadline = (request: RecordCorporateActionDeadlineRequest, options: ApiRequestOptions = {}) => apiPostJson<CorporateActionDeadlineMutationResult>(casePath(UI_API_ROUTES.SecurityMasterCorporateActionCaseDeadlines, request.caseId), request, authoritative(options));
export const previewCorporateActionDeadlineImport = (request: CorporateActionDeadlineImportRequest, options: ApiRequestOptions = {}) => apiPostJson<CorporateActionDeadlineImportPreview>(casePath(UI_API_ROUTES.SecurityMasterCorporateActionDeadlineImportPreview, request.caseId), request, authoritative(options));
export const importCorporateActionDeadlines = (request: CorporateActionDeadlineImportRequest, options: ApiRequestOptions = {}) => apiPostJson<CorporateActionDeadlineImportResult>(casePath(UI_API_ROUTES.SecurityMasterCorporateActionDeadlineImport, request.caseId), request, authoritative(options));
