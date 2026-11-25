import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { ExternalGlMappingProfile, LedgerBook } from "@/types";

export type GlStatus = "Pending" | "FullyAccepted" | "PartiallyAccepted" | "Rejected" | "OutcomeUnknown";
export type PostingPurpose = "Export" | "Correction" | "Reversal";

export interface PostingDestination {
  providerId: string;
  connectionId: string;
  externalLedgerId: string;
  environment: string;
}

export interface PostingPolicy {
  destination: PostingDestination;
  confirmationRequired: boolean;
  actor: string;
  recordedAtUtc: string;
}

export interface PostingSetup {
  policy: PostingPolicy | null;
  actor: string;
  canConfigure: boolean;
  canManage: boolean;
}

export interface PostingLine {
  meridianLineId: string;
  meridianAccount: string;
  externalAccountId: string;
  externalDimensions: Record<string, string>;
  debit: string;
  credit: string;
}

export interface PostingDocument {
  meridianJournalId: string;
  effectiveDate: string;
  currency: string;
  idempotencyKey: string;
  lines: PostingLine[];
  supersedesIdempotencyKey?: string | null;
  reversesJournalId?: string | null;
}

export interface PostingObservation {
  observedAtUtc: string;
  isLookup: boolean;
  receipt: {
    kind: string;
    providerBatchId?: string | null;
    providerDocumentId?: string | null;
    providerTraceId?: string | null;
    providerRecordedAtUtc?: string | null;
    errorCode?: string | null;
    providerErrorCode?: string | null;
    lines: Array<{
      meridianLineId: string;
      status: "Accepted" | "Rejected" | "OutcomeUnknown";
      providerLineId?: string | null;
      errorCode?: string | null;
      externalAccountId?: string | null;
      externalDimensions?: Record<string, string> | null;
      debit?: string | null;
      credit?: string | null;
    }>;
  };
}

export interface DocumentProgress {
  idempotencyKey: string;
  status: GlStatus;
  observations: PostingObservation[];
  postAttempts: number;
  lastPostAttemptAtUtc?: string | null;
}

interface PostingDecision {
  actor: string;
  notes: string;
  recordedAtUtc: string;
  payloadHash: string;
}

export interface PostingPackage {
  packageId: string;
  payloadHash: string;
  status: GlStatus;
  preparedBy: string;
  preparedAtUtc: string;
  certification: PostingDecision | null;
  approval: PostingDecision | null;
  payload: {
    scope: { periodId: string };
    destination: PostingDestination;
    purpose: PostingPurpose;
    rationale: string;
    predecessorPackageId?: string | null;
    mapping: { version: string; profile: ExternalGlMappingProfile; certifiedBy: string; evidence: string[] };
    source: { lastJournalSequence: string; contentHash: string; periodStart: string; periodEnd: string };
    documents: PostingDocument[];
    totalDebit: string;
    totalCredit: string;
  };
  progress: DocumentProgress[];
}

export interface PostingPackageSummary {
  packageId: string;
  purpose: PostingPurpose;
  status: GlStatus;
  preparedAtUtc: string;
  certified: boolean;
  approved: boolean;
}

interface PreparePostingPackageRequest {
  mappingProfileId?: string;
  journalIds?: string[];
  rationale: string;
  purpose: PostingPurpose;
  predecessorPackageId?: string;
  predecessorPeriodId?: string;
}

const bookRoute = (book: LedgerBook) => UI_API_ROUTES.ExternalGlPosting
  .replace("{fundProfileId}", encodeURIComponent(book.fundProfileId))
  .replace("{ledgerBookId:guid}", encodeURIComponent(book.ledgerBookId));

const packagesRoute = (book: LedgerBook, periodId: string) => `${bookRoute(book)}/${encodeURIComponent(periodId)}/packages`;
const packageRoute = (book: LedgerBook, periodId: string, packageId: string) =>
  `${packagesRoute(book, periodId)}/${encodeURIComponent(packageId)}`;
const authoritative = (options: ApiRequestOptions) => ({ ...options, allowDevelopmentFallback: false });

export function getPostingSetup(book: LedgerBook, options: ApiRequestOptions = {}) {
  return apiGetJson<PostingSetup>(`${bookRoute(book)}/policy`, authoritative(options));
}

export function listPostingMappings(book: LedgerBook, options: ApiRequestOptions = {}) {
  return apiGetJson<ExternalGlMappingProfile[]>(`${bookRoute(book)}/mapping-profiles`, authoritative(options));
}

export function savePostingPolicy(book: LedgerBook, connectionId: string, confirmationRequired: boolean) {
  return apiPostJson<PostingPolicy>(`${bookRoute(book)}/policy`, { providerId: "quickbooks", connectionId, confirmationRequired });
}

export function listPostingPackages(book: LedgerBook, periodId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<PostingPackageSummary[]>(packagesRoute(book, periodId), authoritative(options));
}

export function getPostingPackage(book: LedgerBook, periodId: string, packageId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<PostingPackage>(packageRoute(book, periodId, packageId), authoritative(options));
}

export function preparePostingPackage(book: LedgerBook, periodId: string, request: PreparePostingPackageRequest) {
  return apiPostJson<PostingPackage>(packagesRoute(book, periodId), request);
}

export function decidePostingPackage(book: LedgerBook, periodId: string, pkg: PostingPackage, approve: boolean, notes: string) {
  return apiPostJson<PostingPackage>(`${packageRoute(book, periodId, pkg.packageId)}/decision`, {
    payloadHash: pkg.payloadHash, approve, notes
  });
}

export function postingAuditUrl(book: LedgerBook, periodId: string, packageId: string) {
  return `${packageRoute(book, periodId, packageId)}/audit`;
}
