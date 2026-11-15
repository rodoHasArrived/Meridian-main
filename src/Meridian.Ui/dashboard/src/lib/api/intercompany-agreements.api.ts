import { apiGetJson, apiPostJson, apiPutJson, type ApiRequestOptions } from "@/lib/api";
import type { AgreementCommand, AgreementCommandResult, AgreementDetail, AgreementJournalLines, AgreementList, AgreementQuery } from "@/types/intercompany-agreements";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

// Kept under the existing FundStructure API. No fixture fallback can create agreement authority.
export const intercompanyAgreementsEndpoint = UI_API_ROUTES.IntercompanyAgreements;
const detailPath = (id: string) => `${intercompanyAgreementsEndpoint}/${encodeURIComponent(id)}`;
const authoritative = (options: ApiRequestOptions) => ({ ...options, allowDevelopmentFallback: false });
export function listIntercompanyAgreements(query: AgreementQuery, options: ApiRequestOptions = {}) {
  const parameters = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) if (value) parameters.set(key, value);
  return apiGetJson<AgreementList>(`${intercompanyAgreementsEndpoint}?${parameters}`, authoritative(options));
}
export function getIntercompanyAgreement(id: string, asOf?: string, options: ApiRequestOptions = {}) {
  const query = asOf ? `?${new URLSearchParams({ asOf })}` : "";
  return apiGetJson<AgreementDetail>(`${detailPath(id)}${query}`, authoritative(options));
}
export type AgreementMutation = "create" | "propose" | "save" | "submit" | "approve" | "reject" | "withdraw";
export function getIntercompanyAgreementJournalLines(agreementId: string, query: { ledgerBookId: string; asOf: string; cursor?: string }, options: ApiRequestOptions = {}) {
  return apiGetJson<AgreementJournalLines>(`${detailPath(agreementId)}/journal-lines?${new URLSearchParams(query)}`, authoritative(options));
}
export function mutateIntercompanyAgreement(action: AgreementMutation, command: AgreementCommand, options: ApiRequestOptions = {}) {
  const path = action === "create" ? intercompanyAgreementsEndpoint
    : action === "propose" ? `${detailPath(command.agreementId)}/proposals`
    : `${detailPath(command.agreementId)}/proposals/${encodeURIComponent(command.proposalId)}${action === "save" ? "" : `/${action}`}`;
  return action === "save"
    ? apiPutJson<AgreementCommandResult>(path, command, authoritative(options))
    : apiPostJson<AgreementCommandResult>(path, command, authoritative(options));
}

export function previewIntercompanyAgreementSchedule(agreementId: string, query: { asOf: string; version?: number; proposalId?: string }, options: ApiRequestOptions = {}) {
  const parameters = new URLSearchParams({ asOf: query.asOf });
  if (query.version !== undefined) parameters.set("version", String(query.version));
  if (query.proposalId) parameters.set("proposalId", query.proposalId);
  return apiGetJson<import("@/types/intercompany-agreements").AgreementSchedulePreview>(`${detailPath(agreementId)}/schedule?${parameters}`, authoritative(options));
}
