import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { PrivateCapitalChoices, PrivateCapitalRecordCommitment, PrivateCapitalEvidence, PrivateCapitalPreview,
  PrivateCapitalReceipt, PrivateCapitalRecords, PrivateCapitalSnapshotInput } from "@/types/private-capital.types";

const authoritative = (options: ApiRequestOptions): ApiRequestOptions => ({ ...options, allowDevelopmentFallback: false });
export const privateCapitalApi = {
  records: (options: ApiRequestOptions = {}) => apiGetJson<PrivateCapitalRecords>(UI_API_ROUTES.PrivateCapitalRecords, authoritative(options)),
  choices: (options: ApiRequestOptions = {}) => apiGetJson<PrivateCapitalChoices>(UI_API_ROUTES.PrivateCapitalRecordChoices, authoritative(options)),
  register: (request: { commandId: string; holderEntityId: string; vehicleSecurityId: string; currency: string;
    sourceReference: string; evidence: PrivateCapitalEvidence }, options: ApiRequestOptions = {}) =>
    apiPostJson<PrivateCapitalRecordCommitment>(UI_API_ROUTES.PrivateCapitalRecordCommitments, request, authoritative(options)),
  preview: (request: { rows: PrivateCapitalSnapshotInput[] | null; csv: string | null;
    evidence: PrivateCapitalEvidence; rationale: string }, options: ApiRequestOptions = {}) =>
    apiPostJson<PrivateCapitalPreview>(UI_API_ROUTES.PrivateCapitalRecordImportPreview, request, authoritative(options)),
  accept: (request: { batchId: string; fingerprint: string; commandId: string }, options: ApiRequestOptions = {}) =>
    apiPostJson<PrivateCapitalReceipt>(UI_API_ROUTES.PrivateCapitalRecordImportAccept, request, authoritative(options))
};
