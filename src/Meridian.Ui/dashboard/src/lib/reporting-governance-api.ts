import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import {
  governedReportingRestatementApprovalPath,
  governedReportingRestatementRequestPath,
  governedReportingRunPath,
  governedReportingSeriesHistoryPath,
  governedReportingTransitionPath,
  secureReportingAccessGrantHistoryPath,
  secureReportingAccessGrantIssuePath,
  secureReportingAccessGrantRevokePath,
  secureReportingDeliveryHistoryPath,
  secureReportingDeliveryPath,
  secureReportingDeliveryQueuePath,
  secureReportingTransportCapabilitiesPath
} from "@/lib/reporting-governance-routes";
import type {
  GovernedReportingRun,
  ReportingGovernanceAccessPrincipal,
  ReportingGovernanceAccessPrincipalKind,
  ReportingGovernanceAccessScope,
  ReportingGovernanceActionAvailability,
  ReportingGovernanceRestatement,
  ReportingGovernanceRestatementApproval,
  ReportingGovernanceSeriesHistory,
  SecureReportingAccessGrant,
  SecureReportingAccessGrantIssueRequest,
  SecureReportingDelivery,
  SecureReportingDeliveryQueueRequest,
  SecureReportingDistributionCapabilityCatalog,
  SecureReportingIssuedAccessGrant
} from "@/types/reporting-governance";
import type { ReportingRunParameters } from "@/types/workstation-4";

interface LegacyGovernedReportingRunWire extends Omit<
  GovernedReportingRun,
  "access" | "normalizedParameters" | "actionAvailability"
> {
  access?: unknown;
  normalizedParameters?: ReportingRunParameters | null;
  parameters?: ReportingRunParameters | null;
  actionAvailability?: unknown;
  allowedActions?: unknown;
  availableActions?: unknown;
}

interface LegacyReportingRestatementWire extends Omit<
  ReportingGovernanceRestatement,
  "actionAvailability"
> {
  actionAvailability?: unknown;
  allowedActions?: unknown;
  availableActions?: unknown;
}

export function getGovernedReportingRun(runId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<unknown>(governedReportingRunPath(runId), canonicalReadOptions(options))
    .then(adaptGovernedReportingRunResponse);
}

export function validateGovernedReportingRun(
  runId: string,
  expectedVersion: number,
  options: ApiRequestOptions = {}
) {
  return apiPostJson<unknown>(
    governedReportingTransitionPath(runId, "validate"),
    { expectedVersion },
    options
  ).then(adaptGovernedReportingRunResponse);
}

export function submitGovernedReportingRun(
  runId: string,
  expectedVersion: number,
  options: ApiRequestOptions = {}
) {
  return apiPostJson<unknown>(
    governedReportingTransitionPath(runId, "submit"),
    { expectedVersion },
    options
  ).then(adaptGovernedReportingRunResponse);
}

export function approveGovernedReportingRun(
  runId: string,
  expectedVersion: number,
  decisionNote: string,
  options: ApiRequestOptions = {}
) {
  return apiPostJson<unknown>(
    governedReportingTransitionPath(runId, "approve"),
    { expectedVersion, decisionNote },
    options
  ).then(adaptGovernedReportingRunResponse);
}

export function releaseGovernedReportingRun(
  runId: string,
  expectedVersion: number,
  options: ApiRequestOptions = {}
) {
  return apiPostJson<unknown>(
    governedReportingTransitionPath(runId, "release"),
    { expectedVersion },
    options
  ).then(adaptGovernedReportingRunResponse);
}

export function requestGovernedReportingRestatement(
  runId: string,
  expectedVersion: number,
  reason: string,
  options: ApiRequestOptions = {}
) {
  return apiPostJson<unknown>(
    governedReportingRestatementRequestPath(runId),
    { expectedVersion, reason },
    options
  ).then(adaptReportingRestatementResponse);
}

export function approveGovernedReportingRestatement(
  requestId: string,
  expectedVersion: number,
  options: ApiRequestOptions = {}
) {
  return apiPostJson<unknown>(
    governedReportingRestatementApprovalPath(requestId),
    { expectedVersion },
    options
  ).then(adaptReportingRestatementApprovalResponse);
}

export function getGovernedReportingSeriesHistory(
  seriesId: string,
  options: ApiRequestOptions = {}
): Promise<ReportingGovernanceSeriesHistory> {
  return apiGetJson<unknown>(
    governedReportingSeriesHistoryPath(seriesId),
    canonicalReadOptions(options)
  ).then(adaptReportingSeriesHistoryResponse);
}

export function queueSecureReportingDelivery(
  request: SecureReportingDeliveryQueueRequest,
  options: ApiRequestOptions = {}
) {
  return apiPostJson<SecureReportingDelivery>(secureReportingDeliveryQueuePath(), request, options);
}

export function getSecureReportingDelivery(jobId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<SecureReportingDelivery>(secureReportingDeliveryPath(jobId), canonicalReadOptions(options));
}

export function getSecureReportingDeliveryHistory(runId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<SecureReportingDelivery[]>(secureReportingDeliveryHistoryPath(runId), canonicalReadOptions(options));
}

export function issueSecureReportingAccessGrant(
  request: SecureReportingAccessGrantIssueRequest,
  options: ApiRequestOptions = {}
) {
  return apiPostJson<SecureReportingIssuedAccessGrant>(secureReportingAccessGrantIssuePath(), request, options);
}

export function revokeSecureReportingAccessGrant(
  grantId: string,
  reason: string,
  options: ApiRequestOptions = {}
) {
  return apiPostJson<{ grantId: string; revoked: boolean }>(
    secureReportingAccessGrantRevokePath(grantId),
    { reason },
    options
  );
}

export function getSecureReportingAccessGrantHistory(
  runId: string,
  options: ApiRequestOptions = {}
): Promise<SecureReportingAccessGrant[]> {
  return apiGetJson<SecureReportingAccessGrant[]>(
    secureReportingAccessGrantHistoryPath(runId),
    canonicalReadOptions(options)
  );
}

export function getSecureReportingTransportCapabilities(
  options: ApiRequestOptions = {}
): Promise<SecureReportingDistributionCapabilityCatalog> {
  return apiGetJson<SecureReportingDistributionCapabilityCatalog>(
    secureReportingTransportCapabilitiesPath(),
    canonicalReadOptions(options)
  );
}

function canonicalReadOptions(options: ApiRequestOptions): ApiRequestOptions {
  return { ...options, allowDevelopmentFallback: false };
}

/** Converts additive legacy aliases once, before canonical governed-run data reaches React. */
export function adaptGovernedReportingRunResponse(value: unknown): GovernedReportingRun {
  const wire = requireObject(value, "Governed reporting run") as unknown as LegacyGovernedReportingRunWire;
  const {
    access,
    normalizedParameters,
    parameters: legacyParameters,
    actionAvailability,
    allowedActions,
    availableActions,
    ...canonical
  } = wire;
  const retainedVersion = requireVersion(wire.version, "Governed reporting run version");
  const retainedParameters = normalizedParameters
    ?? legacyParameters
    ?? parseCertifiedParameters(wire.snapshot?.parametersCanonicalJson);
  if (!isReportingRunParameters(retainedParameters)) {
    throw new TypeError("Governed reporting run did not include canonical normalizedParameters.");
  }

  return {
    ...canonical,
    version: retainedVersion,
    access: adaptReportingAccessScope(access),
    normalizedParameters: retainedParameters,
    actionAvailability: adaptActionAvailability(
      actionAvailability,
      allowedActions,
      availableActions,
      retainedVersion
    )
  };
}

function adaptReportingAccessScope(value: unknown): ReportingGovernanceAccessScope {
  const access = requireObject(value, "Governed reporting access scope");
  if (typeof access.allowOwnerAccess !== "boolean") {
    throw new TypeError("Governed reporting access scope did not include canonical allowOwnerAccess evidence.");
  }
  if (!Array.isArray(access.principals)) {
    throw new TypeError("Governed reporting access scope did not include canonical typed principals.");
  }

  const ownerPrincipalId = access.ownerPrincipalId === null
    ? null
    : requireString(access.ownerPrincipalId, "Governed reporting owner principal");

  return {
    policyId: requireString(access.policyId, "Governed reporting policy ID"),
    policyVersion: requireString(access.policyVersion, "Governed reporting policy version"),
    mode: requireString(access.mode, "Governed reporting access mode"),
    ownerPrincipalId,
    allowOwnerAccess: access.allowOwnerAccess,
    principals: access.principals.map(adaptReportingAccessPrincipal),
    policyHash: requireString(access.policyHash, "Governed reporting policy hash")
  };
}

function adaptReportingAccessPrincipal(value: unknown): ReportingGovernanceAccessPrincipal {
  const principal = requireObject(value, "Governed reporting access principal");
  const kind = requireString(principal.kind, "Governed reporting access principal kind");
  if (!isReportingAccessPrincipalKind(kind)) {
    throw new TypeError(`Governed reporting access principal kind ${kind} is unsupported.`);
  }

  return {
    kind,
    principalId: requireString(principal.principalId, "Governed reporting access principal ID")
  };
}

function isReportingAccessPrincipalKind(value: string): value is ReportingGovernanceAccessPrincipalKind {
  return value === "User" || value === "Group" || value === "Company";
}

function adaptReportingRestatementResponse(value: unknown): ReportingGovernanceRestatement {
  const wire = requireObject(value, "Reporting restatement") as unknown as LegacyReportingRestatementWire;
  const { actionAvailability, allowedActions, availableActions, ...canonical } = wire;
  const retainedVersion = requireVersion(wire.version, "Reporting restatement version");
  return {
    ...canonical,
    version: retainedVersion,
    actionAvailability: adaptActionAvailability(
      actionAvailability,
      allowedActions,
      availableActions,
      retainedVersion
    )
  };
}

function adaptReportingRestatementApprovalResponse(value: unknown): ReportingGovernanceRestatementApproval {
  const wire = requireObject(value, "Reporting restatement approval");
  return {
    request: adaptReportingRestatementResponse(wire.request),
    draftRun: adaptGovernedReportingRunResponse(wire.draftRun)
  };
}

function adaptReportingSeriesHistoryResponse(value: unknown): ReportingGovernanceSeriesHistory {
  const wire = requireObject(value, "Reporting series history");
  if (!Array.isArray(wire.runs) || !Array.isArray(wire.restatementRequests)) {
    throw new TypeError("Reporting series history did not include canonical runs and restatementRequests arrays.");
  }
  return {
    seriesId: requireString(wire.seriesId, "Reporting series ID"),
    runs: wire.runs.map(adaptGovernedReportingRunResponse),
    restatementRequests: wire.restatementRequests.map(adaptReportingRestatementResponse)
  };
}

function adaptActionAvailability(
  canonicalValue: unknown,
  legacyAllowed: unknown,
  legacyAvailable: unknown,
  retainedVersion: number
): ReportingGovernanceActionAvailability[] {
  const projected: ReportingGovernanceActionAvailability[] = [];
  const seen = new Set<string>();

  appendActionSource(canonicalValue, false);
  appendActionSource(legacyAllowed, true);
  appendActionSource(legacyAvailable, true);
  return projected;

  function appendActionSource(source: unknown, defaultAllowed: boolean) {
    if (Array.isArray(source)) {
      source.forEach((candidate) => appendCandidate(candidate, undefined, defaultAllowed));
      return;
    }
    if (source && typeof source === "object") {
      Object.entries(source).forEach(([action, candidate]) =>
        appendCandidate(candidate, action, defaultAllowed));
    }
  }

  function appendCandidate(candidate: unknown, fallbackAction: string | undefined, defaultAllowed: boolean) {
    if (typeof candidate === "string") {
      append(candidate, defaultAllowed, null, retainedVersion);
      return;
    }
    if (typeof candidate === "boolean" && fallbackAction) {
      append(fallbackAction, candidate, null, retainedVersion);
      return;
    }
    if (!candidate || typeof candidate !== "object") return;

    const record = candidate as Record<string, unknown>;
    const action = typeof record.action === "string" ? record.action : fallbackAction;
    if (!action) return;
    const isAllowed = typeof record.isAllowed === "boolean" ? record.isAllowed : defaultAllowed;
    const blockedReason = typeof record.blockedReason === "string" ? record.blockedReason : null;
    const expectedVersion = typeof record.expectedVersion === "number"
      && Number.isSafeInteger(record.expectedVersion)
      ? record.expectedVersion
      : retainedVersion;
    append(action, isAllowed, blockedReason, expectedVersion);
  }

  function append(action: string, isAllowed: boolean, blockedReason: string | null, expectedVersion: number) {
    const normalizedAction = action.trim();
    const identity = normalizedAction.toLowerCase().replace(/[^a-z0-9]/g, "");
    if (!normalizedAction || seen.has(identity)) return;
    seen.add(identity);
    projected.push({ action: normalizedAction, isAllowed, blockedReason, expectedVersion });
  }
}

function parseCertifiedParameters(value: string | null | undefined): unknown {
  if (!value?.trim()) return null;
  try {
    return JSON.parse(value);
  } catch {
    return null;
  }
}

function isReportingRunParameters(value: unknown): value is ReportingRunParameters {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const record = value as Record<string, unknown>;
  const scope = record.scope;
  const ledgerBook = record.ledgerBook;
  return Boolean(
    scope && typeof scope === "object" && !Array.isArray(scope)
    && ledgerBook && typeof ledgerBook === "object" && !Array.isArray(ledgerBook)
    && typeof record.periodId === "string"
    && typeof record.asOfDate === "string"
    && typeof record.accountingBasis === "string"
    && typeof record.presentationCurrency === "string"
    && typeof record.consolidationLevel === "string"
    && typeof record.outputFormat === "string"
    && typeof record.finality === "string"
    && typeof record.includeSupportingSchedules === "boolean"
    && typeof record.includeEvidenceAppendix === "boolean"
    && record.templateParameters && typeof record.templateParameters === "object"
    && !Array.isArray(record.templateParameters)
  );
}

function requireObject(value: unknown, label: string): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new TypeError(`${label} response was unavailable or malformed.`);
  }
  return value as Record<string, unknown>;
}

function requireString(value: unknown, label: string): string {
  if (typeof value !== "string" || !value.trim()) {
    throw new TypeError(`${label} was unavailable or malformed.`);
  }
  return value;
}

function requireVersion(value: unknown, label: string): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value < 0) {
    throw new TypeError(`${label} was unavailable or malformed.`);
  }
  return value;
}
