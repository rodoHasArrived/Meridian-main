// Evidence packet, graph, vault, and template endpoints. Re-exported through @/lib/api.
import type { EvidenceCompleteness, EvidenceGraph, EvidencePacket, EvidencePacketExportRequest, EvidencePacketExportResponse, EvidenceSubject, EvidenceTemplate, EvidenceVaultIdentity, EvidenceVaultDocumentEntry, EvidenceVaultDocumentQuery, EvidenceVaultDocumentReviewRequest, EvidenceVaultDocumentReviewResponse, EvidenceVaultIntakeRequest, EvidenceVaultIntakeResponse, EvidenceVaultLookupRequest, EvidenceVaultRequestListEntry, EvidenceVaultRequestListQuery } from "@/types";
import { WORKSTATION_API_ENDPOINTS, workstationEvidenceExportManifestEndpoint, workstationEvidenceGraphEndpoint, workstationEvidencePacketEndpoint, workstationEvidenceValidateEndpoint, evidenceVaultDocumentReviewEndpoint } from "@/lib/workstation-endpoints";
import { getJson, postJson, type ApiRequestOptions } from "@/lib/api/http-client";

export function getEvidenceSubjects(options: ApiRequestOptions = {}) {
  return getJson<EvidenceSubject[]>(WORKSTATION_API_ENDPOINTS.evidenceSubjects, options);
}

export function getEvidencePacket(subjectKind: string, subjectId: string, options: ApiRequestOptions = {}) {
  return getJson<EvidencePacket>(workstationEvidencePacketEndpoint(subjectKind, subjectId), options);
}

export function getEvidenceGraph(subjectKind: string, subjectId: string, options: ApiRequestOptions = {}) {
  return getJson<EvidenceGraph>(workstationEvidenceGraphEndpoint(subjectKind, subjectId), options);
}

export function validateEvidencePacket(subjectKind: string, subjectId: string, options: ApiRequestOptions = {}) {
  return postJson<EvidenceCompleteness>(workstationEvidenceValidateEndpoint(subjectKind, subjectId), undefined, options);
}

export function exportEvidenceManifest(
  subjectKind: string,
  subjectId: string,
  request: EvidencePacketExportRequest = { includeWarnings: true },
  options: ApiRequestOptions = {}
) {
  return postJson<EvidencePacketExportResponse>(workstationEvidenceExportManifestEndpoint(subjectKind, subjectId), request, options);
}

export function searchEvidenceVault(request: EvidenceVaultLookupRequest, options: ApiRequestOptions = {}) {
  return postJson<EvidenceVaultIdentity[]>(WORKSTATION_API_ENDPOINTS.evidenceVaultSearch, request, options);
}

export function intakeEvidenceVaultDocument(
  request: EvidenceVaultIntakeRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<EvidenceVaultIntakeResponse>(WORKSTATION_API_ENDPOINTS.evidenceVaultIntake, request, options);
}

export function listEvidenceVaultRequestLists(
  query: EvidenceVaultRequestListQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.requestListKind) {
    params.set("requestListKind", query.requestListKind);
  }

  if (query.requestListKindCode) {
    params.set("requestListKindCode", query.requestListKindCode);
  }

  if (query.targetKind) {
    params.set("targetKind", query.targetKind);
  }

  if (query.targetId) {
    params.set("targetId", query.targetId);
  }

  if (query.status) {
    params.set("status", query.status);
  }

  if (query.subjectKind) {
    params.set("subjectKind", query.subjectKind);
  }

  if (query.subjectId) {
    params.set("subjectId", query.subjectId);
  }

  if (query.maxResults !== undefined && query.maxResults !== null) {
    params.set("maxResults", String(query.maxResults));
  }

  const suffix = params.toString();
  return getJson<EvidenceVaultRequestListEntry[]>(
    suffix ? `${WORKSTATION_API_ENDPOINTS.evidenceVaultRequestLists}?${suffix}` : WORKSTATION_API_ENDPOINTS.evidenceVaultRequestLists,
    options
  );
}

export function listEvidenceVaultDocuments(
  query: EvidenceVaultDocumentQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.classification) {
    params.set("classification", query.classification);
  }

  if (query.channelKind) {
    params.set("channelKind", query.channelKind);
  }

  if (query.extractionStatus) {
    params.set("extractionStatus", query.extractionStatus);
  }

  if (query.reviewStatus) {
    params.set("reviewStatus", query.reviewStatus);
  }

  if (query.linkKind) {
    params.set("linkKind", query.linkKind);
  }

  if (query.objectId) {
    params.set("objectId", query.objectId);
  }

  if (query.subjectKind) {
    params.set("subjectKind", query.subjectKind);
  }

  if (query.subjectId) {
    params.set("subjectId", query.subjectId);
  }

  if (query.tenantId) {
    params.set("tenantId", query.tenantId);
  }

  if (query.scope) {
    params.set("scope", query.scope);
  }

  if (query.maxResults !== undefined && query.maxResults !== null) {
    params.set("maxResults", String(query.maxResults));
  }

  const suffix = params.toString();
  return getJson<EvidenceVaultDocumentEntry[]>(
    suffix ? `${WORKSTATION_API_ENDPOINTS.evidenceVaultDocuments}?${suffix}` : WORKSTATION_API_ENDPOINTS.evidenceVaultDocuments,
    options
  );
}

export function reviewEvidenceVaultDocument(
  vaultId: string,
  documentId: string,
  request: EvidenceVaultDocumentReviewRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<EvidenceVaultDocumentReviewResponse>(
    evidenceVaultDocumentReviewEndpoint(vaultId, documentId),
    request,
    options
  );
}

export function getEvidenceTemplates(options: ApiRequestOptions = {}) {
  return getJson<EvidenceTemplate[]>(WORKSTATION_API_ENDPOINTS.evidenceTemplates, options);
}
