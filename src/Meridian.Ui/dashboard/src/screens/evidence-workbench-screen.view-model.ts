import { useEffect, useMemo, useRef, useState } from "react";
import {
  exportEvidenceManifest,
  getEvidencePacket,
  getEvidenceSubjects,
  intakeEvidenceVaultDocument,
  listEvidenceVaultDocuments,
  listEvidenceVaultRequestLists,
  reviewEvidenceVaultDocument,
  validateEvidencePacket
} from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import type { ApiRequestOptions } from "@/lib/api";
import {
  appendOperatingScopeToRoute,
  buildOperatingScopeFromSearch,
  type AppShellOperatingScopeState
} from "@/app-shell.operating-scope";
import {
  evidenceWorkbenchPath,
  normalizeLocalWorkstationRoute,
  WORKSTATION_ROUTE_CATALOG,
  workflowTargetPath
} from "@/lib/workspace";
import type {
  EvidenceCompleteness,
  EvidenceEdge,
  EvidenceNode,
  EvidencePacket,
  EvidencePacketExportResponse,
  EvidenceProofChain,
  EvidenceRequestList,
  EvidenceRequestListKindCode,
  EvidenceSlaAssessment,
  EvidenceStatus,
  EvidenceSubject,
  EvidenceDocumentClassification,
  EvidenceDocumentReviewStatus,
  EvidenceVaultDocumentEntry,
  EvidenceVaultDocumentQuery,
  EvidenceVaultDocumentReviewRequest,
  EvidenceVaultDocumentReviewResponse,
  EvidenceVaultIntakeRequest,
  EvidenceVaultIntakeResponse,
  EvidenceVaultRequestListEntry,
  EvidenceVaultRequestListQuery,
  WorkflowAction
} from "@/types";

export interface EvidenceWorkbenchServices {
  getSubjects: (options?: ApiRequestOptions) => Promise<EvidenceSubject[]>;
  getPacket: (subjectKind: string, subjectId: string, options?: ApiRequestOptions) => Promise<EvidencePacket>;
  validatePacket: (subjectKind: string, subjectId: string, options?: ApiRequestOptions) => Promise<EvidenceCompleteness>;
  exportManifest: (subjectKind: string, subjectId: string, options?: ApiRequestOptions) => Promise<EvidencePacketExportResponse>;
  intakeDocument?: (request: EvidenceVaultIntakeRequest, options?: ApiRequestOptions) => Promise<EvidenceVaultIntakeResponse>;
  listRequestLists?: (query: EvidenceVaultRequestListQuery, options?: ApiRequestOptions) => Promise<EvidenceVaultRequestListEntry[]>;
  listDocuments?: (query: EvidenceVaultDocumentQuery, options?: ApiRequestOptions) => Promise<EvidenceVaultDocumentEntry[]>;
  reviewDocument?: (vaultId: string, documentId: string, request: EvidenceVaultDocumentReviewRequest, options?: ApiRequestOptions) => Promise<EvidenceVaultDocumentReviewResponse>;
}

export interface EvidenceNodeGroupViewModel {
  id: string;
  label: string;
  readyCount: number;
  reviewCount: number;
  nodes: EvidenceNode[];
  rows: EvidenceNodeRowViewModel[];
  tableLabel: string;
  summaryLabel: string;
  detailPanelId: string;
  hasRows: boolean;
  defaultSelectedNodeId: string | null;
  emptyTitle: string;
  emptyDetail: string;
}

export interface EvidenceNodeRowViewModel {
  id: string;
  evidenceId: string;
  kindLabel: string;
  statusLabel: string;
  statusTone: EvidenceStatusTone;
  sourceSystem: string;
  summary: string;
  freshnessLabel: string;
  freshnessTone: EvidenceStatusTone;
  artifactCountLabel: string;
  workItemCountLabel: string;
  subjectLabel: string;
  ariaLabel: string;
  selectAriaLabel: string;
  artifactRows: EvidenceNodeArtifactRowViewModel[];
  workItemRows: EvidenceNodeWorkItemRowViewModel[];
  fields: EvidenceLineageDetailFieldViewModel[];
}

export interface EvidenceNodeArtifactRowViewModel {
  id: string;
  kind: string;
  target: string;
  generatedLabel: string;
  retainedLabel: string;
  hashLabel: string;
  canonicalSubjectLabel: string | null;
  ariaLabel: string;
}

export interface EvidenceNodeWorkItemRowViewModel {
  id: string;
  label: string;
  ariaLabel: string;
}

export interface EvidenceNodeDetailViewModel {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusTone: EvidenceStatusTone;
  freshnessLabel: string;
  freshnessTone: EvidenceStatusTone;
  ariaLabel: string;
  fields: EvidenceLineageDetailFieldViewModel[];
  artifactRows: EvidenceNodeArtifactRowViewModel[];
  artifactEmptyText: string;
  workItemRows: EvidenceNodeWorkItemRowViewModel[];
  workItemEmptyText: string;
}

export interface EvidenceNodeSelectionViewModel {
  selectedRowId: string | null;
  selectedDetail: EvidenceNodeDetailViewModel | null;
  selectRow: (rowId: string) => void;
}

export interface EvidenceLineageRowViewModel {
  id: string;
  fromId: string;
  relationshipLabel: string;
  toId: string;
  reason: string;
  ariaLabel: string;
  selectAriaLabel: string;
}

export interface EvidenceLineagePanelViewModel {
  title: string;
  description: string;
  tableLabel: string;
  summaryLabel: string;
  detailPanelId: string;
  hasRows: boolean;
  defaultSelectedRowId: string | null;
  rows: EvidenceLineageRowViewModel[];
  emptyTitle: string;
  emptyDetail: string;
  emptyRole: "status";
  emptyAriaLive: "polite";
}

export interface EvidenceLineageDetailFieldViewModel {
  label: string;
  value: string;
}

export interface EvidenceLineageDetailViewModel {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  fields: EvidenceLineageDetailFieldViewModel[];
}

export interface EvidenceLineageSelectionViewModel {
  selectedRowId: string | null;
  selectedDetail: EvidenceLineageDetailViewModel | null;
  selectRow: (rowId: string) => void;
}

export interface EvidenceAssuranceComponentRowViewModel {
  id: string;
  label: string;
  scoreLabel: string;
  statusLabel: string;
  statusTone: EvidenceStatusTone;
  detail: string;
  ariaLabel: string;
}

export interface EvidenceSlaAssessmentRowViewModel {
  id: string;
  policyLabel: string;
  evidenceId: string;
  evidenceKindLabel: string;
  sourceSystem: string;
  ageLabel: string;
  freshnessLabel: string;
  severityLabel: string;
  breached: boolean;
  tone: EvidenceStatusTone;
  message: string;
  ariaLabel: string;
}

export interface EvidenceAssurancePanelViewModel {
  title: string;
  scoreLabel: string;
  statusLabel: string;
  statusTone: EvidenceStatusTone;
  summaryLabel: string;
  componentRows: EvidenceAssuranceComponentRowViewModel[];
  slaRows: EvidenceSlaAssessmentRowViewModel[];
  breachedSlaRows: EvidenceSlaAssessmentRowViewModel[];
  orphanEvidenceIds: string[];
  orphanSummaryLabel: string;
  orphanTone: EvidenceStatusTone;
  noOrphanRuleLabel: string;
  validationIssueLabel: string;
}

export interface EvidenceProofChainLayerRowViewModel {
  id: string;
  label: string;
  statusLabel: string;
  statusTone: EvidenceStatusTone;
  coverageLabel: string;
  readyLabel: string;
  reviewLabel: string;
  missingLabel: string;
  kindsLabel: string;
  summary: string;
  ariaLabel: string;
}

export interface EvidenceProofChainPanelViewModel {
  title: string;
  summaryLabel: string;
  statusLabel: string;
  statusTone: EvidenceStatusTone;
  coverageLabel: string;
  hasLayers: boolean;
  rows: EvidenceProofChainLayerRowViewModel[];
}

export type EvidencePacketActionControl = "link" | "validate" | "export";
export type EvidencePacketActionTone = "primary" | "success" | "warning" | "danger" | "muted";

export interface EvidencePacketActionViewModel {
  id: string;
  label: string;
  detail: string;
  targetLabel: string;
  tone: EvidencePacketActionTone;
  href: string;
  control: EvidencePacketActionControl;
  commandLabel: string;
  ariaLabel: string;
  busy: boolean;
  busyLabel: string | null;
  disabled: boolean;
  disabledReason: string | null;
}

export interface EvidenceWorkbenchCommandState {
  commandLabel: string;
  label: string;
  ariaLabel: string;
  busy: boolean;
  busyLabel: string | null;
  disabled: boolean;
  disabledReason: string | null;
}

export interface EvidenceWorkbenchErrorState {
  summary: string;
  details: string[];
}

export interface EvidenceExportResultViewModel {
  title: string;
  manifestPath: string;
  summaryLabel: string;
  routeHref: string | null;
  routeLabel: string | null;
  routeAriaLabel: string | null;
  vaultIdLabel: string | null;
  storageKindLabel: string | null;
  manifestPackageFamilyLabel: string | null;
  artifactSummaryLabel: string | null;
  artifactRows: EvidenceVaultArtifactRowViewModel[];
  requestListSummaryLabel: string | null;
  requestListRows: EvidenceVaultRequestListRowViewModel[];
  supportRequestSummaryLabel: string | null;
  supportRequestRows: EvidenceVaultSupportRequestRowViewModel[];
}

export interface EvidenceVaultArtifactRowViewModel {
  id: string;
  kind: string;
  relativePath: string;
  sizeLabel: string;
  hashLabel: string;
  sourceLabel: string;
  canonicalSubjectLabel: string;
  captureLabel: string;
  extractionLabel: string;
  retainedLabel: string;
  ariaLabel: string;
}

export interface EvidenceVaultRequestListRowViewModel {
  id: string;
  requestListKindLabel: string;
  requestListFamilyLabel: string;
  targetLabel: string;
  highestSeverityLabel: string;
  highestSeverityTone: EvidenceStatusTone;
  statusLabel: string;
  requestCountLabel: string;
  evidenceKindsLabel: string;
  blockedOutputsLabel: string;
  summary: string;
  ariaLabel: string;
}

export interface EvidenceVaultSupportRequestRowViewModel {
  id: string;
  requestKindLabel: string;
  evidenceLabel: string;
  evidenceKindLabel: string;
  severityLabel: string;
  severityTone: EvidenceStatusTone;
  statusLabel: string;
  summary: string;
  sourceLabel: string;
  workItemLabel: string;
  blockedOutputLabel: string;
  ariaLabel: string;
}

export interface EvidenceVaultRequestListIndexPanelViewModel {
  title: string;
  description: string;
  summaryLabel: string;
  scopeLabel: string;
  hasRows: boolean;
  rows: EvidenceVaultRequestListIndexRowViewModel[];
  emptyTitle: string;
  emptyDetail: string;
}

export interface EvidenceVaultRequestListIndexRowViewModel extends EvidenceVaultRequestListRowViewModel {
  vaultLabel: string;
  subjectLabel: string;
  openRequestCountLabel: string;
  retainedLabel: string;
  manifestHref: string | null;
  manifestLabel: string | null;
  manifestAriaLabel: string | null;
  supportRequestSummaryLabel: string;
  supportRequestRows: EvidenceVaultSupportRequestRowViewModel[];
}

export interface EvidenceVaultDocumentIndexPanelViewModel {
  title: string;
  description: string;
  summaryLabel: string;
  scopeLabel: string;
  hasRows: boolean;
  rows: EvidenceVaultDocumentIndexRowViewModel[];
  emptyTitle: string;
  emptyDetail: string;
}

export interface EvidenceVaultDocumentIndexRowViewModel {
  id: string;
  fileName: string;
  classificationLabel: string;
  extractionLabel: string;
  extractionTone: EvidenceStatusTone;
  reviewLabel: string;
  reviewTone: EvidenceStatusTone;
  sourceLabel: string;
  actorLabel: string;
  tenantScopeLabel: string;
  hashLabel: string;
  subjectLabel: string;
  vaultLabel: string;
  receivedLabel: string;
  objectLinksLabel: string;
  openRequestCountLabel: string;
  manifestHref: string | null;
  manifestLabel: string | null;
  manifestAriaLabel: string | null;
  ariaLabel: string;
  detail: EvidenceVaultDocumentDetailViewModel;
}

export interface EvidenceVaultDocumentDetailViewModel {
  id: string;
  title: string;
  subtitle: string;
  ariaLabel: string;
  statusLabel: string;
  statusTone: EvidenceStatusTone;
  fields: EvidenceLineageDetailFieldViewModel[];
  objectLinkRows: EvidenceVaultDocumentObjectLinkRowViewModel[];
  reviewFieldRows: EvidenceVaultDocumentReviewFieldRowViewModel[];
  auditRows: EvidenceVaultDocumentAuditRowViewModel[];
  supportRequestRows: EvidenceVaultSupportRequestRowViewModel[];
  manifestHref: string | null;
  manifestLabel: string | null;
  manifestAriaLabel: string | null;
  canAccept: boolean;
  canReject: boolean;
  reviewBusy: boolean;
  acceptReview: () => Promise<void>;
  rejectReview: () => Promise<void>;
  objectLinkEmptyText: string;
  auditEmptyText: string;
  supportRequestEmptyText: string;
  reviewFieldEmptyText: string;
}

export interface EvidenceVaultDocumentReviewFieldRowViewModel {
  id: string;
  fieldLabel: string;
  valueLabel: string;
  expectedLabel: string;
  confidenceLabel: string;
  reviewStateLabel: string;
  statusLabel: string;
  statusTone: EvidenceStatusTone;
  linkedRecordLabel: string;
  ariaLabel: string;
}

export interface EvidenceVaultDocumentObjectLinkRowViewModel {
  id: string;
  kindLabel: string;
  objectLabel: string;
  relationshipLabel: string;
  href: string | null;
  linkLabel: string | null;
  linkAriaLabel: string | null;
  ariaLabel: string;
}

export interface EvidenceVaultDocumentAuditRowViewModel {
  id: string;
  actionLabel: string;
  actorLabel: string;
  recordedLabel: string;
  summary: string;
  correlationLabel: string;
  ariaLabel: string;
}

export interface EvidenceWorkbenchViewModel {
  selectedSubjectKind: string | null;
  selectedSubjectId: string | null;
  title: string;
  subtitle: string;
  loading: boolean;
  error: EvidenceWorkbenchErrorState | null;
  showSubjectPicker: boolean;
  hasSelection: boolean;
  hasPacket: boolean;
  hasSubjects: boolean;
  loadingLabel: string;
  sourceWorkflowHref: string | null;
  sourceWorkflowLabel: string | null;
  sourceWorkflowAriaLabel: string | null;
  subjectsRegionLabel: string;
  subjectsSummaryLabel: string;
  subjectEmptyTitle: string;
  subjectEmptyDetail: string;
  subjectEmptyActionLabel: string;
  subjectEmptyActionHref: string;
  subjectEmptyActionAriaLabel: string;
  subjects: EvidenceSubject[];
  packet: EvidencePacket | null;
  scoreLabel: string;
  statusLabel: string;
  statusTone: EvidenceStatusTone;
  generatedLabel: string;
  assurancePanel: EvidenceAssurancePanelViewModel;
  proofChainPanel: EvidenceProofChainPanelViewModel;
  queueFilters: EvidenceWorkbenchQueueFiltersViewModel;
  requestListPanel: EvidenceVaultRequestListIndexPanelViewModel;
  documentPanel: EvidenceVaultDocumentIndexPanelViewModel;
  lineagePanel: EvidenceLineagePanelViewModel;
  nodeGroups: EvidenceNodeGroupViewModel[];
  hasPacketActions: boolean;
  packetActionsLabel: string;
  packetActionsSummaryLabel: string;
  packetActions: EvidencePacketActionViewModel[];
  missingEvidence: string[];
  staleEvidence: string[];
  orphanEvidence: string[];
  slaBreaches: string[];
  relatedWorkItemIds: string[];
  warnings: string[];
  canExport: boolean;
  reloadCommand: EvidenceWorkbenchCommandState;
  validateCommand: EvidenceWorkbenchCommandState;
  exportCommand: EvidenceWorkbenchCommandState;
  intakeCommand: EvidenceWorkbenchCommandState;
  exportBusy: boolean;
  exportResult: EvidencePacketExportResponse | null;
  exportResultDetail: EvidenceExportResultViewModel | null;
  intakeBusy: boolean;
  intakeResult: EvidenceVaultIntakeResponse | null;
  intakeResultLabel: string | null;
  validateBusy: boolean;
  validationResult: EvidenceCompleteness | null;
  openSubjectHref: (subject: EvidenceSubject) => string;
  reloadEvidence: () => void;
  exportManifest: () => Promise<void>;
  intakeDocument: (request: EvidenceVaultIntakeRequest) => Promise<void>;
  validatePacket: () => Promise<void>;
}

export interface EvidenceWorkbenchQueueFiltersViewModel {
  requestListFamily: EvidenceRequestListKindCode | null;
  documentClassification: EvidenceDocumentClassification | null;
  documentReviewStatus: EvidenceDocumentReviewStatus | null;
}

export type EvidenceStatusTone = "success" | "warning" | "danger" | "muted";

const defaultServices: EvidenceWorkbenchServices = {
  getSubjects: getEvidenceSubjects,
  getPacket: getEvidencePacket,
  validatePacket: validateEvidencePacket,
  exportManifest: (subjectKind, subjectId, options) => exportEvidenceManifest(subjectKind, subjectId, { includeWarnings: true }, options),
  intakeDocument: intakeEvidenceVaultDocument,
  listRequestLists: listEvidenceVaultRequestLists,
  listDocuments: listEvidenceVaultDocuments,
  reviewDocument: reviewEvidenceVaultDocument
};

const noopReloadEvidence = () => {};

function buildEvidenceVaultRequestListQuery(
  subjectKind: string | null,
  subjectId: string | null,
  requestListFamily: EvidenceRequestListKindCode | null
): EvidenceVaultRequestListQuery {
  return stripNullishQueryValues({
    subjectKind: subjectKind || null,
    subjectId: subjectId || null,
    requestListKindCode: requestListFamily,
    status: "Open",
    maxResults: subjectKind && subjectId ? 25 : 10
  });
}

function buildEvidenceVaultDocumentQuery(
  subjectKind: string | null,
  subjectId: string | null,
  documentClassification: EvidenceDocumentClassification | null,
  documentReviewStatus: EvidenceDocumentReviewStatus | null
): EvidenceVaultDocumentQuery {
  return stripNullishQueryValues({
    subjectKind: subjectKind || null,
    subjectId: subjectId || null,
    classification: documentClassification,
    reviewStatus: documentReviewStatus,
    maxResults: subjectKind && subjectId ? 25 : 10
  });
}

function stripNullishQueryValues<T extends Record<string, unknown>>(query: T): T {
  return Object.fromEntries(
    Object.entries(query).filter(([, value]) => value !== null && value !== undefined)
  ) as T;
}

const requestListFamilyFilters: readonly EvidenceRequestListKindCode[] = ["Close", "Audit", "Tax", "ReportPackage", "OperationalEvent"];
const documentClassificationFilters: readonly EvidenceDocumentClassification[] = [
  "BankStatement",
  "AdminPackage",
  "Statement",
  "Invoice",
  "CapitalNotice",
  "CustodianFile",
  "BankEvidence",
  "ValuationSupport",
  "Agreement",
  "TaxSupport",
  "AuditRequestSupport",
  "TaxAuditSupport"
];
const documentReviewStatusFilters: readonly EvidenceDocumentReviewStatus[] = ["Unreviewed", "NeedsReview", "Accepted", "Rejected"];

function normalizeRequestListFamilyFilter(value: string | null): EvidenceRequestListKindCode | null {
  return requestListFamilyFilters.includes(value as EvidenceRequestListKindCode)
    ? value as EvidenceRequestListKindCode
    : null;
}

function normalizeDocumentClassificationFilter(value: string | null): EvidenceDocumentClassification | null {
  return documentClassificationFilters.includes(value as EvidenceDocumentClassification)
    ? value as EvidenceDocumentClassification
    : null;
}

function normalizeDocumentReviewStatusFilter(value: string | null): EvidenceDocumentReviewStatus | null {
  return documentReviewStatusFilters.includes(value as EvidenceDocumentReviewStatus)
    ? value as EvidenceDocumentReviewStatus
    : null;
}

export function useEvidenceWorkbenchViewModel(
  search: string,
  services: EvidenceWorkbenchServices = defaultServices
): EvidenceWorkbenchViewModel {
  const params = useMemo(() => new URLSearchParams(search), [search]);
  const selectedSubjectKind = params.get("subjectKind");
  const selectedSubjectId = params.get("subjectId");
  const requestListFamily = normalizeRequestListFamilyFilter(params.get("requestListFamily"));
  const documentClassification = normalizeDocumentClassificationFilter(params.get("documentClassification"));
  const documentReviewStatus = normalizeDocumentReviewStatusFilter(params.get("documentReviewStatus"));
  const [subjects, setSubjects] = useState<EvidenceSubject[]>([]);
  const [packet, setPacket] = useState<EvidencePacket | null>(null);
  const [requestLists, setRequestLists] = useState<EvidenceVaultRequestListEntry[]>([]);
  const [documents, setDocuments] = useState<EvidenceVaultDocumentEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<EvidenceWorkbenchErrorState | null>(null);
  const [exportBusy, setExportBusy] = useState(false);
  const [exportResult, setExportResult] = useState<EvidencePacketExportResponse | null>(null);
  const [intakeBusy, setIntakeBusy] = useState(false);
  const [reviewBusyDocumentId, setReviewBusyDocumentId] = useState<string | null>(null);
  const [intakeResult, setIntakeResult] = useState<EvidenceVaultIntakeResponse | null>(null);
  const [validateBusy, setValidateBusy] = useState(false);
  const [validationResult, setValidationResult] = useState<EvidenceCompleteness | null>(null);
  const [reloadRevision, setReloadRevision] = useState(0);
  const operatingScope = useMemo(() => buildOperatingScopeFromSearch(search), [search]);
  const requestRevisionRef = useRef(0);
  const validateCommandRevisionRef = useRef(0);
  const exportCommandRevisionRef = useRef(0);
  const intakeCommandRevisionRef = useRef(0);
  const reviewCommandRevisionRef = useRef(0);
  const loadAbortRef = useRef<AbortController | null>(null);
  const validateAbortRef = useRef<AbortController | null>(null);
  const exportAbortRef = useRef<AbortController | null>(null);
  const intakeAbortRef = useRef<AbortController | null>(null);
  const reviewAbortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    loadAbortRef.current?.abort();
    validateAbortRef.current?.abort();
    exportAbortRef.current?.abort();
    intakeAbortRef.current?.abort();
    reviewAbortRef.current?.abort();
    const revision = requestRevisionRef.current + 1;
    requestRevisionRef.current = revision;
    validateCommandRevisionRef.current += 1;
    exportCommandRevisionRef.current += 1;
    intakeCommandRevisionRef.current += 1;
    reviewCommandRevisionRef.current += 1;
    const controller = new AbortController();
    loadAbortRef.current = controller;
    setLoading(true);
    setError(null);
    setPacket(null);
    setRequestLists([]);
    setDocuments([]);
    setExportResult(null);
    setValidationResult(null);
    setExportBusy(false);
    setIntakeBusy(false);
    setReviewBusyDocumentId(null);
    setValidateBusy(false);

    const load = async () => {
      try {
        const requestListQuery = buildEvidenceVaultRequestListQuery(selectedSubjectKind, selectedSubjectId, requestListFamily);
        const documentQuery = buildEvidenceVaultDocumentQuery(selectedSubjectKind, selectedSubjectId, documentClassification, documentReviewStatus);
        const listRequestLists: NonNullable<EvidenceWorkbenchServices["listRequestLists"]> =
          services.listRequestLists ?? (() => Promise.resolve([]));
        const listDocuments: NonNullable<EvidenceWorkbenchServices["listDocuments"]> =
          services.listDocuments ?? (() => Promise.resolve([]));
        const [subjectList, requestListEntries, documentEntries] = await Promise.all([
          services.getSubjects({ signal: controller.signal }),
          listRequestLists(requestListQuery, { signal: controller.signal }),
          listDocuments(documentQuery, { signal: controller.signal })
        ]);
        if (requestRevisionRef.current !== revision) {
          return;
        }
        setSubjects(subjectList);
        setRequestLists(requestListEntries);
        setDocuments(documentEntries);

        if (selectedSubjectKind && selectedSubjectId) {
          const nextPacket = await services.getPacket(selectedSubjectKind, selectedSubjectId, { signal: controller.signal });
          if (requestRevisionRef.current !== revision) {
            return;
          }
          setPacket(nextPacket);
        }
      } catch (loadError) {
        if (requestRevisionRef.current === revision && !isAbortError(loadError)) {
          setError(buildEvidenceWorkbenchError(loadError, "Evidence workbench failed to load."));
        }
      } finally {
        if (loadAbortRef.current === controller) {
          loadAbortRef.current = null;
        }
        if (requestRevisionRef.current === revision) {
          setLoading(false);
        }
      }
    };

    void load();

    return () => {
      controller.abort();
      validateAbortRef.current?.abort();
      exportAbortRef.current?.abort();
      intakeAbortRef.current?.abort();
      reviewAbortRef.current?.abort();
      requestRevisionRef.current += 1;
      validateCommandRevisionRef.current += 1;
      exportCommandRevisionRef.current += 1;
      intakeCommandRevisionRef.current += 1;
      reviewCommandRevisionRef.current += 1;
    };
  }, [documentClassification, documentReviewStatus, reloadRevision, requestListFamily, selectedSubjectId, selectedSubjectKind, services]);

  const exportCommand = async () => {
    if (!selectedSubjectKind || !selectedSubjectId) {
      return;
    }

    const revision = requestRevisionRef.current;
    const exportRevision = exportCommandRevisionRef.current + 1;
    exportCommandRevisionRef.current = exportRevision;
    exportAbortRef.current?.abort();
    const controller = new AbortController();
    exportAbortRef.current = controller;
    setExportBusy(true);
    setError(null);
    setExportResult(null);
    try {
      const result = await services.exportManifest(selectedSubjectKind, selectedSubjectId, { signal: controller.signal });
      if (requestRevisionRef.current === revision && exportCommandRevisionRef.current === exportRevision) {
        setExportResult(result);
      }
    } catch (exportError) {
      if (requestRevisionRef.current === revision && exportCommandRevisionRef.current === exportRevision && !isAbortError(exportError)) {
        setError(buildEvidenceWorkbenchError(exportError, "Evidence manifest export failed."));
      }
    } finally {
      if (exportAbortRef.current === controller) {
        exportAbortRef.current = null;
      }
      if (requestRevisionRef.current === revision && exportCommandRevisionRef.current === exportRevision) {
        setExportBusy(false);
      }
    }
  };

  const validateCommand = async () => {
    if (!selectedSubjectKind || !selectedSubjectId) {
      return;
    }

    const revision = requestRevisionRef.current;
    const validateRevision = validateCommandRevisionRef.current + 1;
    validateCommandRevisionRef.current = validateRevision;
    validateAbortRef.current?.abort();
    const controller = new AbortController();
    validateAbortRef.current = controller;
    setValidateBusy(true);
    setError(null);
    setValidationResult(null);
    try {
      const result = await services.validatePacket(selectedSubjectKind, selectedSubjectId, { signal: controller.signal });
      if (requestRevisionRef.current === revision && validateCommandRevisionRef.current === validateRevision) {
        setValidationResult(result);
      }
    } catch (validateError) {
      if (requestRevisionRef.current === revision && validateCommandRevisionRef.current === validateRevision && !isAbortError(validateError)) {
        setError(buildEvidenceWorkbenchError(validateError, "Evidence validation failed."));
      }
    } finally {
      if (validateAbortRef.current === controller) {
        validateAbortRef.current = null;
      }
      if (requestRevisionRef.current === revision && validateCommandRevisionRef.current === validateRevision) {
        setValidateBusy(false);
      }
    }
  };

  const intakeDocumentCommand = async (request: EvidenceVaultIntakeRequest) => {
    if (!selectedSubjectKind || !selectedSubjectId || !services.intakeDocument) {
      return;
    }

    const revision = requestRevisionRef.current;
    const intakeRevision = intakeCommandRevisionRef.current + 1;
    intakeCommandRevisionRef.current = intakeRevision;
    intakeAbortRef.current?.abort();
    const controller = new AbortController();
    intakeAbortRef.current = controller;
    setIntakeBusy(true);
    setError(null);
    setIntakeResult(null);
    try {
      const result = await services.intakeDocument(request, { signal: controller.signal });
      if (requestRevisionRef.current === revision && intakeCommandRevisionRef.current === intakeRevision) {
        setIntakeResult(result);
        setReloadRevision((current) => current + 1);
      }
    } catch (intakeError) {
      if (requestRevisionRef.current === revision && intakeCommandRevisionRef.current === intakeRevision && !isAbortError(intakeError)) {
        setError(buildEvidenceWorkbenchError(intakeError, "Evidence document intake failed."));
      }
    } finally {
      if (intakeAbortRef.current === controller) {
        intakeAbortRef.current = null;
      }
      if (requestRevisionRef.current === revision && intakeCommandRevisionRef.current === intakeRevision) {
        setIntakeBusy(false);
      }
    }
  };

  const reviewDocumentCommand = async (
    vaultId: string,
    documentId: string,
    status: EvidenceVaultDocumentReviewRequest["status"]
  ) => {
    if (!services.reviewDocument) {
      return;
    }

    const revision = requestRevisionRef.current;
    const reviewRevision = reviewCommandRevisionRef.current + 1;
    reviewCommandRevisionRef.current = reviewRevision;
    reviewAbortRef.current?.abort();
    const controller = new AbortController();
    reviewAbortRef.current = controller;
    setReviewBusyDocumentId(`${vaultId}:${documentId}`);
    setError(null);
    try {
      const retainedDocument = documents.find((entry) =>
        entry.vaultId === vaultId && entry.document.documentId === documentId
      )?.document;
      const confirmedAt = new Date().toISOString();
      const confirmedFields = status === "Accepted" && retainedDocument
        ? buildDocumentReviewConfirmedFields(retainedDocument, "evidence-workbench-operator", confirmedAt)
        : [];
      const result = await services.reviewDocument(
        vaultId,
        documentId,
        {
          status,
          reviewer: "evidence-workbench-operator",
          notes: status === "Accepted"
            ? "Evidence Workbench operator accepted this retained document."
            : "Evidence Workbench operator rejected this retained document.",
          correlationId: `evidence-workbench:${vaultId}:${documentId}:${status}`,
          confirmedFields
        },
        { signal: controller.signal }
      );
      if (requestRevisionRef.current === revision && reviewCommandRevisionRef.current === reviewRevision) {
        setDocuments((current) => current.map((entry) =>
          entry.vaultId === vaultId && entry.document.documentId === documentId ? result.entry : entry
        ));
        setReloadRevision((current) => current + 1);
      }
    } catch (reviewError) {
      if (requestRevisionRef.current === revision && reviewCommandRevisionRef.current === reviewRevision && !isAbortError(reviewError)) {
        setError(buildEvidenceWorkbenchError(reviewError, "Evidence document review failed."));
      }
    } finally {
      if (reviewAbortRef.current === controller) {
        reviewAbortRef.current = null;
      }
      if (requestRevisionRef.current === revision && reviewCommandRevisionRef.current === reviewRevision) {
        setReviewBusyDocumentId(null);
      }
    }
  };

  const reloadEvidence = () => {
    if (loading) {
      return;
    }

    setReloadRevision((revision) => revision + 1);
  };

  return buildEvidenceWorkbenchViewModel({
    selectedSubjectKind,
    selectedSubjectId,
    operatingScope,
    loading,
    error,
    subjects,
    packet,
    requestLists,
    documents,
    queueFilters: {
      requestListFamily,
      documentClassification,
      documentReviewStatus
    },
    exportBusy,
    exportResult,
    intakeBusy,
    reviewBusyDocumentId,
    intakeResult,
    validateBusy,
    validationResult,
    reloadEvidence,
    exportManifest: exportCommand,
    intakeDocument: intakeDocumentCommand,
    reviewDocument: reviewDocumentCommand,
    validatePacket: validateCommand
  });
}

export function buildEvidenceWorkbenchViewModel(input: {
  selectedSubjectKind: string | null;
  selectedSubjectId: string | null;
  operatingScope?: AppShellOperatingScopeState | null;
  loading: boolean;
  error: EvidenceWorkbenchErrorState | null;
  subjects: EvidenceSubject[];
  packet: EvidencePacket | null;
  requestLists?: EvidenceVaultRequestListEntry[];
  documents?: EvidenceVaultDocumentEntry[];
  queueFilters?: EvidenceWorkbenchQueueFiltersViewModel;
  exportBusy: boolean;
  exportResult: EvidencePacketExportResponse | null;
  intakeBusy?: boolean;
  reviewBusyDocumentId?: string | null;
  intakeResult?: EvidenceVaultIntakeResponse | null;
  validateBusy: boolean;
  validationResult: EvidenceCompleteness | null;
  reloadEvidence?: () => void;
  exportManifest: () => Promise<void>;
  intakeDocument?: (request: EvidenceVaultIntakeRequest) => Promise<void>;
  reviewDocument?: (vaultId: string, documentId: string, status: EvidenceVaultDocumentReviewRequest["status"]) => Promise<void>;
  validatePacket: () => Promise<void>;
}): EvidenceWorkbenchViewModel {
  const operatingScope = input.operatingScope ?? buildOperatingScopeFromSearch("");
  const completeness = input.validationResult ?? input.packet?.completeness ?? null;
  const hasSelection = Boolean(input.selectedSubjectKind && input.selectedSubjectId);
  const subject = input.packet?.subject ?? null;
  const sourceWorkflowHref = resolveSourceWorkflowHref(subject, operatingScope);
  const subjectLabel = subject?.label ?? "selected evidence subject";
  const reloadCommand = buildReloadCommand(
    input.loading,
    hasSelection,
    input.selectedSubjectKind,
    input.selectedSubjectId
  );
  const validateCommand = buildPrimaryActionCommand(
    "validate",
    subjectLabel,
    buildActionCommand("validate", subjectLabel, input.exportBusy, input.validateBusy, input.packet !== null)
  );
  const exportCommand = buildPrimaryActionCommand(
    "export",
    subjectLabel,
    buildActionCommand("export", subjectLabel, input.exportBusy, input.validateBusy, input.packet !== null)
  );
  const intakeCommand = buildIntakeCommand(
    subjectLabel,
    hasSelection,
    Boolean(input.intakeDocument),
    input.exportBusy,
    input.validateBusy,
    Boolean(input.intakeBusy)
  );
  const packetActions = buildEvidencePacketActions({
    actions: input.packet?.actions ?? [],
    subject,
    selectedSubjectKind: input.selectedSubjectKind,
    selectedSubjectId: input.selectedSubjectId,
    operatingScope,
    exportBusy: input.exportBusy,
    validateBusy: input.validateBusy
  });
  return {
    selectedSubjectKind: input.selectedSubjectKind,
    selectedSubjectId: input.selectedSubjectId,
    title: input.packet?.subject.label ?? "Evidence Workbench",
    subtitle: input.packet
      ? `${input.packet.subject.workspace} evidence packet`
      : hasSelection
        ? `${input.selectedSubjectKind}/${input.selectedSubjectId}`
        : "Choose a workflow subject to inspect packet completeness and lineage.",
    loading: input.loading,
    error: input.error,
    showSubjectPicker: !hasSelection && input.error === null,
    hasSelection,
    hasPacket: input.packet !== null,
    hasSubjects: input.subjects.length > 0,
    loadingLabel: hasSelection
      ? `Loading evidence packet for ${input.selectedSubjectKind}/${input.selectedSubjectId}.`
      : "Loading evidence subjects.",
    sourceWorkflowHref,
    sourceWorkflowLabel: sourceWorkflowHref && subject ? `Open ${subject.workspace} workflow` : null,
    sourceWorkflowAriaLabel: sourceWorkflowHref && subject ? `Open source workflow for ${subject.label}` : null,
    subjectsRegionLabel: "Evidence subjects available for packet inspection",
    subjectsSummaryLabel: input.subjects.length === 1 ? "1 subject" : `${input.subjects.length} subjects`,
    subjectEmptyTitle: "No evidence subjects returned",
    subjectEmptyDetail: "Readiness, reconciliation, report-pack, and provider evidence will appear here after the workstation APIs publish packet subjects.",
    subjectEmptyActionLabel: "Open readiness console",
    subjectEmptyActionHref: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    subjectEmptyActionAriaLabel: "Open readiness console to review upstream evidence sources",
    subjects: input.subjects,
    packet: input.packet,
    scoreLabel: completeness ? `${completeness.score}% complete` : "No score",
    statusLabel: completeness ? formatStatus(completeness.status) : "Not loaded",
    statusTone: completeness ? mapStatusTone(completeness.status) : "muted",
    generatedLabel: input.packet ? formatDate(input.packet.generatedAt) : "Not generated",
    assurancePanel: buildEvidenceAssurancePanel(completeness),
    proofChainPanel: buildEvidenceProofChainPanel(input.packet?.proofChain ?? null),
    queueFilters: input.queueFilters ?? {
      requestListFamily: null,
      documentClassification: null,
      documentReviewStatus: null
    },
    requestListPanel: buildEvidenceVaultRequestListIndexPanel(
      input.requestLists ?? [],
      hasSelection,
      input.selectedSubjectKind,
      input.selectedSubjectId
    ),
    documentPanel: buildEvidenceVaultDocumentIndexPanel(
      input.documents ?? [],
      hasSelection,
      input.selectedSubjectKind,
      input.selectedSubjectId,
      input.reviewBusyDocumentId ?? null,
      input.reviewDocument
    ),
    lineagePanel: buildEvidenceLineagePanel(input.packet?.edges ?? [], input.packet?.subject ?? null),
    nodeGroups: groupNodes(input.packet?.nodes ?? []),
    hasPacketActions: packetActions.length > 0,
    packetActionsLabel: "Evidence packet actions",
    packetActionsSummaryLabel: packetActions.length === 1 ? "1 workflow action" : `${packetActions.length} workflow actions`,
    packetActions,
    missingEvidence: completeness?.missingIds ?? [],
    staleEvidence: completeness?.staleIds ?? [],
    orphanEvidence: completeness?.orphanEvidenceIds ?? [],
    slaBreaches: (completeness?.slaAssessments ?? []).filter((assessment) => assessment.isBreached).map((assessment) => assessment.message),
    relatedWorkItemIds: collectWorkItemIds(input.packet?.nodes ?? []),
    warnings: input.packet?.warnings ?? [],
    canExport: input.packet !== null && !input.exportBusy,
    reloadCommand,
    validateCommand,
    exportCommand,
    intakeCommand,
    exportBusy: input.exportBusy,
    exportResult: input.exportResult,
    exportResultDetail: buildExportResultViewModel(input.exportResult),
    intakeBusy: Boolean(input.intakeBusy),
    intakeResult: input.intakeResult ?? null,
    intakeResultLabel: buildIntakeResultLabel(input.intakeResult ?? null),
    validateBusy: input.validateBusy,
    validationResult: input.validationResult,
    openSubjectHref: (subject) =>
      appendOperatingScopeToRoute(evidenceWorkbenchPath(subject.subjectKind, subject.subjectId), operatingScope),
    reloadEvidence: input.reloadEvidence ?? noopReloadEvidence,
    exportManifest: input.exportManifest,
    intakeDocument: input.intakeDocument ?? (() => Promise.resolve()),
    validatePacket: input.validatePacket
  };
}

export function buildEvidenceProofChainPanel(
  proofChain: EvidenceProofChain | null
): EvidenceProofChainPanelViewModel {
  const status = proofChain?.status ?? "Unknown";
  const rows = (proofChain?.layers ?? []).map((layer) => ({
    id: layer.layer,
    label: layer.label,
    statusLabel: formatStatus(layer.status),
    statusTone: mapStatusTone(layer.status),
    coverageLabel: `${layer.coveragePercent}%`,
    readyLabel: formatCount(layer.readyEvidenceIds.length, "ready node"),
    reviewLabel: formatCount(layer.reviewEvidenceIds.length, "review node"),
    missingLabel: formatCount(layer.missingEvidenceIds.length, "missing node"),
    kindsLabel: layer.evidenceKinds.length > 0 ? layer.evidenceKinds.map(formatKind).join(", ") : "No evidence kinds",
    summary: layer.summary,
    ariaLabel: `${layer.label} proof-chain layer, ${layer.coveragePercent}% ${formatStatus(layer.status)}. ${layer.summary}`
  }));

  return {
    title: "Operational Evidence Graph",
    summaryLabel: proofChain?.summary ?? "Proof-chain coverage was not returned by this packet.",
    statusLabel: formatStatus(status),
    statusTone: mapStatusTone(status),
    coverageLabel: proofChain
      ? `${proofChain.coveredLayerCount}/${proofChain.totalLayerCount} layers, ${proofChain.coveragePercent}% coverage`
      : "No proof-chain coverage",
    hasLayers: rows.length > 0,
    rows
  };
}

function buildEvidenceVaultRequestListIndexPanel(
  entries: EvidenceVaultRequestListEntry[],
  hasSelection: boolean,
  selectedSubjectKind: string | null,
  selectedSubjectId: string | null
): EvidenceVaultRequestListIndexPanelViewModel {
  const rows = entries.map(buildVaultRequestListIndexRow);
  const scopeLabel = hasSelection && selectedSubjectKind && selectedSubjectId
    ? `${formatKind(selectedSubjectKind)} ${selectedSubjectId}`
    : "All evidence subjects";
  const summaryLabel = rows.length === 0
    ? "No open request lists"
    : rows.length === 1
      ? "1 open request list"
      : `${rows.length} open request lists`;

  return {
    title: "Evidence Vault request lists",
    description: "Frozen support requests retained by Evidence Vault manifests and indexed by the shared workstation API.",
    summaryLabel,
    scopeLabel,
    hasRows: rows.length > 0,
    rows,
    emptyTitle: hasSelection ? "No open request lists for this subject" : "No open request lists returned",
    emptyDetail: hasSelection
      ? "Retained support requests for the selected subject will appear here after an evidence manifest export freezes them."
      : "Open close, audit, tax, report-package, and operator support requests will appear here after retained vault identities are available."
  };
}

function buildVaultRequestListIndexRow(entry: EvidenceVaultRequestListEntry): EvidenceVaultRequestListIndexRowViewModel {
  const base = buildVaultRequestListRow(entry);
  const manifestHref = normalizeManifestRoute(entry.manifestRoute);
  const supportRequestRows = (entry.supportRequests ?? []).map(buildVaultSupportRequestRow);
  const openRequestCountLabel = entry.openRequestCount === 1
    ? "1 open request"
    : `${entry.openRequestCount} open requests`;
  return {
    ...base,
    vaultLabel: entry.vaultId,
    subjectLabel: `${formatKind(entry.subjectKind)} ${entry.subjectId}`,
    openRequestCountLabel,
    retainedLabel: `Retained ${formatDate(entry.retainedAt)}`,
    manifestHref,
    manifestLabel: manifestHref ? "Open manifest" : null,
    manifestAriaLabel: manifestHref ? `Open retained manifest for request list ${entry.requestListId}` : null,
    supportRequestSummaryLabel: supportRequestRows.length === 1
      ? "1 support request"
      : `${supportRequestRows.length} support requests`,
    supportRequestRows,
    ariaLabel: `${base.ariaLabel}. ${openRequestCountLabel}; vault ${entry.vaultId}; subject ${entry.subjectKind}/${entry.subjectId}`
  };
}

function buildEvidenceVaultDocumentIndexPanel(
  entries: EvidenceVaultDocumentEntry[],
  hasSelection: boolean,
  selectedSubjectKind: string | null,
  selectedSubjectId: string | null,
  reviewBusyDocumentId: string | null = null,
  reviewDocument?: (vaultId: string, documentId: string, status: EvidenceVaultDocumentReviewRequest["status"]) => Promise<void>
): EvidenceVaultDocumentIndexPanelViewModel {
  const rows = entries.map((entry) => buildVaultDocumentIndexRow(entry, reviewBusyDocumentId, reviewDocument));
  const scopeLabel = hasSelection && selectedSubjectKind && selectedSubjectId
    ? `${formatKind(selectedSubjectKind)} ${selectedSubjectId}`
    : "All evidence subjects";
  const reviewCount = rows.filter((row) => row.reviewTone !== "success").length;
  const summaryLabel = rows.length === 0
    ? "No documents"
    : reviewCount === 0
      ? `${rows.length} ${rows.length === 1 ? "document" : "documents"} ready`
      : `${reviewCount} need review`;

  return {
    title: "Evidence Vault documents",
    description: "Uploaded and imported files retained by the shared vault with source hash, extraction status, reviewer state, and linked operational objects.",
    summaryLabel,
    scopeLabel,
    hasRows: rows.length > 0,
    rows,
    emptyTitle: hasSelection ? "No retained documents for this subject" : "No retained documents returned",
    emptyDetail: hasSelection
      ? "Uploaded and imported documents linked to the selected subject will appear here after intake."
      : "Retained statements, invoices, notices, custodian files, bank evidence, valuation support, agreements, tax support, and audit request support will appear here after intake."
  };
}

function buildVaultDocumentIndexRow(
  entry: EvidenceVaultDocumentEntry,
  reviewBusyDocumentId: string | null = null,
  reviewDocument?: (vaultId: string, documentId: string, status: EvidenceVaultDocumentReviewRequest["status"]) => Promise<void>
): EvidenceVaultDocumentIndexRowViewModel {
  const document = entry.document;
  const reviewKey = `${entry.vaultId}:${document.documentId}`;
  const reviewBusy = reviewBusyDocumentId === reviewKey;
  const manifestHref = normalizeManifestRoute(entry.manifestRoute || document.manifestRoute || "");
  const classificationLabel = formatKind(document.classification);
  const extractionLabel = formatKind(document.extractionStatus);
  const extractionTone = mapDocumentExtractionTone(document.extractionStatus);
  const reviewLabel = formatKind(document.reviewerState.status);
  const reviewTone = mapDocumentReviewTone(document.reviewerState.status);
  const channelLabel = document.channelKind && document.channelKind !== "Unknown"
    ? formatKind(document.channelKind)
    : formatKind(document.sourceChannel);
  const sourceParts = [channelLabel, document.sourceSystem, document.sourceReference]
    .filter((part): part is string => Boolean(part));
  const tenantScopeLabel = [document.tenantId, document.scope]
    .filter((part): part is string => Boolean(part))
    .join(" / ") || "No tenant scope";
  const objectLinksLabel = document.objectLinks.length === 0
    ? "No linked objects"
    : document.objectLinks
        .map((link) => `${formatKind(link.linkKind)} ${link.objectId}`)
        .join("; ");
  const openRequestCountLabel = entry.openRequestCount === 1
    ? "1 open request"
    : `${entry.openRequestCount} open requests`;
  const auditRows = document.auditTrail.map((audit, index) => buildVaultDocumentAuditRow(document.documentId, audit, index));
  const objectLinkRows = document.objectLinks.map((link, index) => buildVaultDocumentObjectLinkRow(document.documentId, link, index));
  const reviewFieldRows = (document.extractedFields ?? []).map((field, index) => buildVaultDocumentReviewFieldRow(document.documentId, field, index));
  const supportRequestRows = (entry.supportRequests ?? []).map(buildVaultSupportRequestRow);
  const confirmedFieldCount = document.reviewerState.confirmedFields?.length ?? 0;
  const confirmedFieldLabel = confirmedFieldCount === 0
    ? "No human-confirmed fields"
    : confirmedFieldCount === 1
      ? "1 human-confirmed field"
      : `${confirmedFieldCount} human-confirmed fields`;
  const authorityLabel = formatEvidenceDocumentAuthority(document.authority);
  const sourceRecord = document.sourceRecord;
  const sourceRecordLabel = sourceRecord
    ? `${formatKind(sourceRecord.sourceChannel)} receipt ${sourceRecord.receiptHash || sourceRecord.sourceHashSha256}`
    : "No source record";
  const detailFields: EvidenceLineageDetailFieldViewModel[] = [
    { label: "Document id", value: document.documentId },
    { label: "Classification", value: classificationLabel },
    { label: "Extraction", value: extractionLabel },
    { label: "Reviewer state", value: reviewLabel },
    { label: "Reviewer", value: document.reviewerState.reviewer || "No reviewer" },
    { label: "Reviewed at", value: document.reviewerState.reviewedAt ? formatDate(document.reviewerState.reviewedAt) : "Not reviewed" },
    { label: "Reviewer notes", value: document.reviewerState.notes || "No reviewer notes" },
    { label: "Confirmed fields", value: confirmedFieldLabel },
    { label: "Authority", value: authorityLabel },
    { label: "Source record", value: sourceRecordLabel },
    { label: "Source record received", value: sourceRecord?.receivedAt ? formatDate(sourceRecord.receivedAt) : "No source record timestamp" },
    { label: "Source record actor", value: sourceRecord?.actor || "No source record actor" },
    { label: "Source record tenant/scope", value: [sourceRecord?.tenantId, sourceRecord?.scope].filter((part): part is string => Boolean(part)).join(" / ") || "No source record tenant scope" },
    { label: "Source hash", value: document.sourceHashSha256 },
    { label: "Received", value: formatDate(document.receivedAt) },
    { label: "Source channel", value: formatKind(document.sourceChannel) },
    { label: "Channel kind", value: channelLabel },
    { label: "Source system", value: document.sourceSystem || "No source system" },
    { label: "Source reference", value: document.sourceReference || "No source reference" },
    { label: "Actor", value: document.actor || "No actor" },
    { label: "Tenant/scope", value: tenantScopeLabel },
    { label: "Vault", value: entry.vaultId },
    { label: "Subject", value: `${formatKind(entry.subjectKind)} ${entry.subjectId}` },
    { label: "Artifact", value: document.artifactId || "No artifact id" },
    { label: "Storage", value: formatKind(entry.storageKind) },
    { label: "Retained", value: formatDate(entry.retainedAt) },
    { label: "Extractor", value: document.extractorId || "No extractor" },
    { label: "Open requests", value: openRequestCountLabel }
  ];
  const detail: EvidenceVaultDocumentDetailViewModel = {
    id: `${entry.vaultId}:${document.documentId}:detail`,
    title: document.fileName,
    subtitle: `${classificationLabel} retained for ${formatKind(entry.subjectKind)} ${entry.subjectId}`,
    ariaLabel: `Selected Evidence Vault document: ${document.fileName}`,
    statusLabel: `${extractionLabel} / ${reviewLabel}`,
    statusTone: reviewTone === "success" && extractionTone === "success" ? "success" : reviewTone === "danger" || extractionTone === "danger" ? "danger" : "warning",
    fields: detailFields,
    objectLinkRows,
    reviewFieldRows,
    auditRows,
    supportRequestRows,
    manifestHref,
    manifestLabel: manifestHref ? "Open manifest" : null,
    manifestAriaLabel: manifestHref ? `Open retained manifest for selected document ${document.fileName}` : null,
    canAccept: Boolean(reviewDocument) && document.reviewerState.status !== "Accepted",
    canReject: Boolean(reviewDocument) && document.reviewerState.status !== "Rejected",
    reviewBusy,
    acceptReview: () => reviewDocument?.(entry.vaultId, document.documentId, "Accepted") ?? Promise.resolve(),
    rejectReview: () => reviewDocument?.(entry.vaultId, document.documentId, "Rejected") ?? Promise.resolve(),
    objectLinkEmptyText: "No linked operational objects were retained for this document.",
    reviewFieldEmptyText: "No extracted fields were retained for human confirmation.",
    auditEmptyText: "No document audit events were returned for this retained document.",
    supportRequestEmptyText: "No open support requests are currently linked to this document."
  };

  return {
    id: `${entry.vaultId}:${document.documentId}`,
    fileName: document.fileName,
    classificationLabel,
    extractionLabel,
    extractionTone,
    reviewLabel,
    reviewTone,
    sourceLabel: sourceParts.length === 0 ? "Unknown source" : sourceParts.join(" / "),
    actorLabel: document.actor || "No actor",
    tenantScopeLabel,
    hashLabel: document.sourceHashSha256,
    subjectLabel: `${formatKind(entry.subjectKind)} ${entry.subjectId}`,
    vaultLabel: entry.vaultId,
    receivedLabel: `Received ${formatDate(document.receivedAt)}`,
    objectLinksLabel,
    openRequestCountLabel,
    manifestHref,
    manifestLabel: manifestHref ? "Open manifest" : null,
    manifestAriaLabel: manifestHref ? `Open retained manifest for document ${document.fileName}` : null,
    ariaLabel: `${document.fileName}, ${classificationLabel}, ${extractionLabel}, ${reviewLabel}. ${objectLinksLabel}. Vault ${entry.vaultId}.`,
    detail
  };
}

function buildDocumentReviewConfirmedFields(
  document: EvidenceVaultDocumentEntry["document"],
  confirmedBy: string,
  confirmedAt: string
) {
  const extractedFields = (document.extractedFields ?? [])
    .filter((field) => field.extractedValue !== null && field.extractedValue !== undefined && field.extractedValue.trim().length > 0)
    .map((field) => ({
      fieldName: field.fieldName,
      confirmedValue: field.extractedValue?.trim() ?? "",
      confirmedBy,
      confirmedAt,
      sourceFieldName: field.fieldName,
      notes: [
        `Confirmed retained extracted field ${field.fieldName}.`,
        field.validationMessage
      ].filter(Boolean).join(" ")
    }));

  return [
    ...extractedFields,
    {
      fieldName: "sourceHashSha256",
      confirmedValue: document.sourceHashSha256,
      confirmedBy,
      confirmedAt,
      sourceFieldName: "sourceHashSha256",
      notes: `Confirmed immutable retained document hash for ${document.fileName}.`
    }
  ];
}

function buildVaultDocumentReviewFieldRow(
  documentId: string,
  field: NonNullable<EvidenceVaultDocumentEntry["document"]["extractedFields"]>[number],
  index: number
): EvidenceVaultDocumentReviewFieldRowViewModel {
  const fieldLabel = formatKind(field.fieldName);
  const valueLabel = field.extractedValue || "No extracted value";
  const expectedLabel = field.expectedValue || "No expected value";
  const confidenceLabel = field.confidenceScore === null || field.confidenceScore === undefined
    ? "No confidence score"
    : `${Math.round(field.confidenceScore * 100)}% confidence`;
  const reviewStateLabel = formatKind(field.reviewState || "Unreviewed");
  const statusLabel = formatKind(field.validationStatus);
  const statusTone = mapStatusTone(field.validationStatus);
  const linkedRecordLabel = [field.linkedRecordKind, field.linkedRecordId]
    .filter((part): part is string => Boolean(part))
    .join(" / ") || "No linked record";
  return {
    id: `${documentId}:review-field:${index}:${field.fieldName}`,
    fieldLabel,
    valueLabel,
    expectedLabel,
    confidenceLabel,
    reviewStateLabel,
    statusLabel,
    statusTone,
    linkedRecordLabel,
    ariaLabel: `${fieldLabel}: extracted ${valueLabel}; expected ${expectedLabel}; ${statusLabel}; ${reviewStateLabel}; ${linkedRecordLabel}`
  };
}

function buildVaultDocumentObjectLinkRow(
  documentId: string,
  link: EvidenceVaultDocumentEntry["document"]["objectLinks"][number],
  index: number
): EvidenceVaultDocumentObjectLinkRowViewModel {
  const kindLabel = formatKind(link.linkKind);
  const objectLabel = link.label || link.objectId;
  const relationshipLabel = link.relationship || "supports";
  const href = normalizeLocalWorkstationRoute(link.route || "");
  return {
    id: `${documentId}:object-link:${index}:${link.linkKind}:${link.objectId}`,
    kindLabel,
    objectLabel,
    relationshipLabel,
    href,
    linkLabel: href ? "Open object" : null,
    linkAriaLabel: href ? `Open linked ${kindLabel} ${objectLabel}` : null,
    ariaLabel: `${kindLabel} ${objectLabel}; ${relationshipLabel}`
  };
}

function buildVaultDocumentAuditRow(
  documentId: string,
  audit: EvidenceVaultDocumentEntry["document"]["auditTrail"][number],
  index: number
): EvidenceVaultDocumentAuditRowViewModel {
  const actionLabel = formatKind(audit.action);
  const actorLabel = audit.actor || "Unknown actor";
  const recordedLabel = formatDate(audit.recordedAt);
  const correlationLabel = audit.correlationId || "No correlation id";
  return {
    id: `${documentId}:audit:${index}:${audit.action}:${audit.recordedAt}`,
    actionLabel,
    actorLabel,
    recordedLabel,
    summary: audit.summary,
    correlationLabel,
    ariaLabel: `${actionLabel} by ${actorLabel} at ${recordedLabel}. ${audit.summary}`
  };
}

function mapDocumentExtractionTone(status: string): EvidenceStatusTone {
  switch (status) {
    case "Accepted":
    case "Extracted":
      return "success";
    case "NeedsReview":
    case "NotExtracted":
      return "warning";
    case "Rejected":
      return "danger";
    default:
      return "muted";
  }
}

function mapDocumentReviewTone(status: string): EvidenceStatusTone {
  switch (status) {
    case "Accepted":
      return "success";
    case "NeedsReview":
    case "Unreviewed":
      return "warning";
    case "Rejected":
      return "danger";
    default:
      return "muted";
  }
}

export function buildEvidenceAssurancePanel(
  completeness: EvidenceCompleteness | null
): EvidenceAssurancePanelViewModel {
  const assurance = completeness?.assuranceScore;
  const score = assurance?.score ?? completeness?.score ?? null;
  const status = assurance?.status ?? completeness?.status ?? "Unknown";
  const slaRows = (assurance?.slaAssessments ?? completeness?.slaAssessments ?? []).map(buildSlaAssessmentRow);
  const breachedSlaRows = slaRows.filter((row) => row.breached);
  const orphanEvidenceIds = completeness?.orphanEvidenceIds ?? [];
  const blockingIssueCount = completeness?.blockingIssueCount ?? 0;
  const warningIssueCount = completeness?.warningIssueCount ?? 0;
  const componentRows = (assurance?.components ?? []).map((component) => ({
    id: component.componentId,
    label: component.label,
    scoreLabel: `${component.score}%`,
    statusLabel: formatStatus(component.status),
    statusTone: mapStatusTone(component.status),
    detail: component.detail,
    ariaLabel: `${component.label} assurance component, ${component.score}% ${formatStatus(component.status)}. ${component.detail}`
  }));

  return {
    title: "Meridian Assurance",
    scoreLabel: score === null ? "No assurance score" : `${score}% assurance`,
    statusLabel: formatStatus(status),
    statusTone: mapStatusTone(status),
    summaryLabel: `${componentRows.length} ${componentRows.length === 1 ? "component" : "components"}, ${breachedSlaRows.length} SLA ${breachedSlaRows.length === 1 ? "breach" : "breaches"}`,
    componentRows,
    slaRows,
    breachedSlaRows,
    orphanEvidenceIds,
    orphanSummaryLabel: orphanEvidenceIds.length === 0
      ? "No orphan evidence"
      : `${orphanEvidenceIds.length} orphan ${orphanEvidenceIds.length === 1 ? "node" : "nodes"}`,
    orphanTone: orphanEvidenceIds.length === 0 ? "success" : "danger",
    noOrphanRuleLabel: orphanEvidenceIds.length === 0
      ? "No-orphan rule satisfied"
      : "No-orphan rule breached",
    validationIssueLabel: `${blockingIssueCount} blocking, ${warningIssueCount} warning`
  };
}

function buildSlaAssessmentRow(assessment: EvidenceSlaAssessment): EvidenceSlaAssessmentRowViewModel {
  const policyLabel = formatKind(assessment.policyId);
  const evidenceKindLabel = formatKind(assessment.evidenceKind);
  const ageLabel = assessment.ageMinutes === null ? "No age" : `${assessment.ageMinutes} min`;
  const freshnessLabel = `${assessment.freshnessMinutes} min limit`;
  const severityLabel = formatKind(assessment.severity.toLowerCase());
  const tone = assessment.isBreached
    ? assessment.severity === "Critical" ? "danger" : "warning"
    : "success";

  return {
    id: `${assessment.policyId}:${assessment.evidenceId}`,
    policyLabel,
    evidenceId: assessment.evidenceId,
    evidenceKindLabel,
    sourceSystem: assessment.sourceSystem || "Unknown source",
    ageLabel,
    freshnessLabel,
    severityLabel,
    breached: assessment.isBreached,
    tone,
    message: assessment.message,
    ariaLabel: `${policyLabel} for ${assessment.evidenceId}, ${assessment.isBreached ? "breached" : "fresh"}. ${assessment.message}`
  };
}

function buildEvidenceWorkbenchError(error: unknown, fallback: string): EvidenceWorkbenchErrorState {
  return describeApiError(error, fallback);
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException
    ? error.name === "AbortError"
    : error instanceof Error && error.name === "AbortError";
}

export function buildEvidenceLineagePanel(
  edges: EvidenceEdge[],
  subject: EvidenceSubject | null
): EvidenceLineagePanelViewModel {
  const subjectLabel = subject?.label ?? "selected evidence subject";
  const rows = edges.map((edge, index) => {
    const relationshipLabel = formatRelationship(edge.relationship);
    return {
      id: `${edge.fromId}:${edge.relationship}:${edge.toId}:${index}`,
      fromId: edge.fromId,
      relationshipLabel,
      toId: edge.toId,
      reason: edge.reason,
      ariaLabel: `${relationshipLabel} from ${edge.fromId} to ${edge.toId}. ${edge.reason}`,
      selectAriaLabel: `Inspect lineage edge: ${relationshipLabel} from ${edge.fromId} to ${edge.toId}`
    };
  });

  return {
    title: "Lineage",
    description: `Graph edges show how evidence nodes support ${subjectLabel}.`,
    tableLabel: `Evidence lineage edges for ${subjectLabel}`,
    summaryLabel: rows.length === 1 ? "1 edge" : `${rows.length} edges`,
    detailPanelId: "evidence-lineage-selected-edge-detail",
    hasRows: rows.length > 0,
    defaultSelectedRowId: rows[0]?.id ?? null,
    rows,
    emptyTitle: "No lineage edges",
    emptyDetail: "This packet returned no graph edges, so the dependency path between evidence nodes is not available in this view.",
    emptyRole: "status",
    emptyAriaLive: "polite"
  };
}

export function useEvidenceLineageSelectionViewModel(
  panel: EvidenceLineagePanelViewModel
): EvidenceLineageSelectionViewModel {
  const [selectedRowId, setSelectedRowId] = useState<string | null>(panel.defaultSelectedRowId);

  useEffect(() => {
    setSelectedRowId(panel.defaultSelectedRowId);
  }, [panel.defaultSelectedRowId]);

  const selectedRow = useMemo(
    () => panel.rows.find((row) => row.id === selectedRowId) ?? panel.rows[0] ?? null,
    [panel.rows, selectedRowId]
  );

  return {
    selectedRowId: selectedRow?.id ?? null,
    selectedDetail: selectedRow ? buildEvidenceLineageDetail(selectedRow) : null,
    selectRow: setSelectedRowId
  };
}

export function buildEvidenceLineageDetail(
  row: EvidenceLineageRowViewModel
): EvidenceLineageDetailViewModel {
  return {
    id: row.id,
    eyebrow: "Selected lineage edge",
    title: row.relationshipLabel,
    subtitle: `${row.fromId} to ${row.toId}`,
    description: row.reason,
    ariaLabel: `Selected lineage edge: ${row.relationshipLabel}`,
    fields: [
      { label: "From node", value: row.fromId },
      { label: "Relationship", value: row.relationshipLabel },
      { label: "To node", value: row.toId },
      { label: "Edge ID", value: row.id }
    ]
  };
}

export function groupNodes(nodes: EvidenceNode[]): EvidenceNodeGroupViewModel[] {
  const groups = new Map<string, EvidenceNode[]>();
  for (const node of nodes) {
    const groupId = resolveGroupId(node.kind);
    groups.set(groupId, [...(groups.get(groupId) ?? []), node]);
  }

  return Array.from(groups.entries()).map(([id, groupNodesForId]) => {
    const label = formatKind(id);
    const rows = groupNodesForId.map(buildEvidenceNodeRow);
    return {
      id,
      label,
      readyCount: groupNodesForId.filter((node) => node.status === "Ready").length,
      reviewCount: groupNodesForId.filter((node) => node.status !== "Ready").length,
      nodes: groupNodesForId,
      rows,
      tableLabel: `${label} evidence nodes`,
      summaryLabel: `${rows.length} ${rows.length === 1 ? "node" : "nodes"}; select a row to inspect retained artifacts, freshness, and work items.`,
      detailPanelId: `evidence-node-${slugifyId(id)}-selected-detail`,
      hasRows: rows.length > 0,
      defaultSelectedNodeId: rows[0]?.id ?? null,
      emptyTitle: `No ${label.toLowerCase()} evidence nodes`,
      emptyDetail: "This packet did not return evidence nodes for this lifecycle group."
    };
  });
}

export function useEvidenceNodeSelectionViewModel(
  group: EvidenceNodeGroupViewModel
): EvidenceNodeSelectionViewModel {
  const [selectedRowId, setSelectedRowId] = useState<string | null>(group.defaultSelectedNodeId);

  useEffect(() => {
    setSelectedRowId(group.defaultSelectedNodeId);
  }, [group.defaultSelectedNodeId]);

  const selectedRow = useMemo(
    () => group.rows.find((row) => row.id === selectedRowId) ?? group.rows[0] ?? null,
    [group.rows, selectedRowId]
  );

  return {
    selectedRowId: selectedRow?.id ?? null,
    selectedDetail: selectedRow ? buildEvidenceNodeDetail(selectedRow) : null,
    selectRow: setSelectedRowId
  };
}

export function buildEvidenceNodeDetail(row: EvidenceNodeRowViewModel): EvidenceNodeDetailViewModel {
  return {
    id: row.id,
    eyebrow: "Selected evidence node",
    title: row.kindLabel,
    subtitle: row.evidenceId,
    description: row.summary,
    statusLabel: row.statusLabel,
    statusTone: row.statusTone,
    freshnessLabel: row.freshnessLabel,
    freshnessTone: row.freshnessTone,
    ariaLabel: `Selected evidence node: ${row.kindLabel}`,
    fields: row.fields,
    artifactRows: row.artifactRows,
    artifactEmptyText: "No retained artifact references are attached to this node.",
    workItemRows: row.workItemRows,
    workItemEmptyText: "No related operator work items are attached to this node."
  };
}

function buildEvidenceNodeRow(node: EvidenceNode): EvidenceNodeRowViewModel {
  const kindLabel = formatKind(node.kind);
  const statusLabel = formatStatus(node.status);
  const statusTone = mapStatusTone(node.status);
  const freshness = buildFreshnessLabel(node);
  const artifactRows = node.artifactRefs.map(buildEvidenceArtifactRow);
  const workItemRows = Array.from(new Set(node.relatedWorkItemIds.filter(Boolean))).map((id) => ({
    id,
    label: id,
    ariaLabel: `Related work item ${id}`
  }));
  const subjectLabel = node.subject.label;

  return {
    id: node.evidenceId,
    evidenceId: node.evidenceId,
    kindLabel,
    statusLabel,
    statusTone,
    sourceSystem: node.sourceSystem || "Unknown source",
    summary: node.summary || "No summary was returned for this evidence node.",
    freshnessLabel: freshness.label,
    freshnessTone: freshness.tone,
    artifactCountLabel: formatCount(node.artifactRefs.length, "artifact"),
    workItemCountLabel: formatCount(workItemRows.length, "work item"),
    subjectLabel,
    ariaLabel: `${kindLabel} evidence node for ${subjectLabel}. ${statusLabel}. ${freshness.label}.`,
    selectAriaLabel: `Inspect evidence node ${kindLabel} ${node.evidenceId}`,
    artifactRows,
    workItemRows,
    fields: [
      { label: "Evidence ID", value: node.evidenceId },
      { label: "Subject", value: `${node.subject.subjectKind}/${node.subject.subjectId}` },
      { label: "Source", value: node.sourceSystem || "Unknown source" },
      { label: "Status", value: statusLabel },
      { label: "Freshness", value: freshness.label },
      ...(node.freshness.reason ? [{ label: "Freshness reason", value: node.freshness.reason }] : [])
    ]
  };
}

function buildEvidenceArtifactRow(artifact: EvidenceNode["artifactRefs"][number]): EvidenceNodeArtifactRowViewModel {
  const target = artifact.path ?? artifact.route ?? artifact.artifactId;
  const canonicalSubjectLabel = formatCanonicalSubject(artifact.canonicalSubjectKind, artifact.canonicalSubjectId);
  return {
    id: artifact.artifactId,
    kind: formatKind(artifact.kind),
    target,
    generatedLabel: formatDate(artifact.generatedAt),
    retainedLabel: artifact.retained ? "Retained" : "Transient",
    hashLabel: artifact.hash ?? "No hash",
    canonicalSubjectLabel,
    ariaLabel: `${formatKind(artifact.kind)} artifact ${artifact.artifactId}, ${artifact.retained ? "retained" : "transient"}${canonicalSubjectLabel ? `, ${canonicalSubjectLabel}` : ""}`
  };
}

export function buildEvidencePacketActions(input: {
  actions: WorkflowAction[];
  subject: EvidenceSubject | null;
  selectedSubjectKind: string | null;
  selectedSubjectId: string | null;
  operatingScope: AppShellOperatingScopeState;
  exportBusy: boolean;
  validateBusy: boolean;
}): EvidencePacketActionViewModel[] {
  return input.actions.map((action) => {
    const control = resolveActionControl(action.actionId);
    const subjectKind = input.subject?.subjectKind ?? input.selectedSubjectKind;
    const subjectId = input.subject?.subjectId ?? input.selectedSubjectId;
    const href = action.targetPageTag === "EvidenceWorkbench" && subjectKind && subjectId
      ? appendOperatingScopeToRoute(evidenceWorkbenchPath(subjectKind, subjectId), input.operatingScope)
      : appendOperatingScopeToRoute(workflowTargetPath(action.targetPageTag, input.subject?.workspace), input.operatingScope);
    const subjectLabel = input.subject?.label ?? "selected evidence subject";
    const command = buildActionCommand(control, subjectLabel, input.exportBusy, input.validateBusy, Boolean(subjectKind && subjectId));

    return {
      id: action.actionId,
      detail: action.detail,
      targetLabel: formatPageTag(action.targetPageTag),
      tone: mapWorkflowActionTone(action.tone),
      href,
      control,
      ...command,
      label: action.label // row title; keep it from being clobbered by the command's button-verb label
    };
  });
}

function collectWorkItemIds(nodes: EvidenceNode[]) {
  return Array.from(new Set(nodes.flatMap((node) => node.relatedWorkItemIds).filter(Boolean))).sort();
}

function resolveGroupId(kind: string) {
  if (kind.includes("run") || kind.includes("promotion")) {
    return "run-lifecycle";
  }
  if (kind.includes("readiness") || kind.includes("replay") || kind.includes("control")) {
    return "readiness";
  }
  if (kind.includes("reconciliation") || kind.includes("ledger")) {
    return "accounting";
  }
  if (kind.includes("report") || kind.includes("export")) {
    return "reporting";
  }
  if (kind.includes("provider")) {
    return "provider-trust";
  }
  return "other";
}

export function mapStatusTone(status: EvidenceStatus): EvidenceStatusTone {
  switch (status) {
    case "Ready":
      return "success";
    case "ReviewRequired":
    case "Stale":
      return "warning";
    case "Blocked":
    case "Missing":
      return "danger";
    default:
      return "muted";
  }
}

export function formatStatus(status: EvidenceStatus) {
  return status.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function resolveActionControl(actionId: string): EvidencePacketActionControl {
  if (actionId.endsWith(".validate")) {
    return "validate";
  }
  if (actionId.endsWith(".export-manifest")) {
    return "export";
  }
  return "link";
}

function buildActionCommand(
  control: EvidencePacketActionControl,
  subjectLabel: string,
  exportBusy: boolean,
  validateBusy: boolean,
  hasSubject: boolean
): EvidenceWorkbenchCommandState {
  switch (control) {
    case "validate":
      return {
        commandLabel: "Validate",
        label: "Validate",
        ariaLabel: `Validate evidence for ${subjectLabel}`,
        busy: validateBusy,
        busyLabel: "Validating",
        disabled: validateBusy || exportBusy || !hasSubject,
        disabledReason: validateBusy
          ? "Evidence validation is already running."
          : exportBusy
            ? "Evidence export is already running."
            : hasSubject
              ? null
              : "Select an evidence packet before validating."
      };
    case "export":
      return {
        commandLabel: "Export",
        label: "Export manifest",
        ariaLabel: `Export evidence for ${subjectLabel}`,
        busy: exportBusy,
        busyLabel: "Exporting",
        disabled: exportBusy || validateBusy || !hasSubject,
        disabledReason: exportBusy
          ? "Evidence export is already running."
          : validateBusy
            ? "Evidence validation is already running."
            : hasSubject
              ? null
              : "Select an evidence packet before exporting."
      };
    default:
      return {
        commandLabel: "Open",
        label: "Open",
        ariaLabel: `Open evidence packet for ${subjectLabel}`,
        busy: false,
        busyLabel: null,
        disabled: false,
        disabledReason: null
      };
  }
}

function buildReloadCommand(
  loading: boolean,
  hasSelection: boolean,
  selectedSubjectKind: string | null,
  selectedSubjectId: string | null
): EvidenceWorkbenchCommandState {
  const targetLabel = hasSelection && selectedSubjectKind && selectedSubjectId
    ? `evidence packet for ${selectedSubjectKind}/${selectedSubjectId}`
    : "evidence subjects";

  return {
    commandLabel: "Retry",
    label: loading ? "Retrying" : "Retry",
    ariaLabel: `Retry loading ${targetLabel}`,
    busy: loading,
    busyLabel: "Retrying",
    disabled: loading,
    disabledReason: loading ? "Evidence load is already running." : null
  };
}

function buildIntakeCommand(
  subjectLabel: string,
  hasSelection: boolean,
  hasIntakeService: boolean,
  exportBusy: boolean,
  validateBusy: boolean,
  intakeBusy: boolean
): EvidenceWorkbenchCommandState {
  return {
    commandLabel: "Retain document",
    label: intakeBusy ? "Retaining" : "Retain document",
    ariaLabel: `Retain document for ${subjectLabel}`,
    busy: intakeBusy,
    busyLabel: "Retaining",
    disabled: intakeBusy || exportBusy || validateBusy || !hasSelection || !hasIntakeService,
    disabledReason: intakeBusy
      ? "Document intake is already running."
      : exportBusy
        ? "Evidence export is already running."
        : validateBusy
          ? "Evidence validation is already running."
          : !hasSelection
            ? "Select an evidence subject before retaining a document."
            : hasIntakeService
              ? null
              : "Document intake service is not available."
  };
}

function buildPrimaryActionCommand(
  control: "validate" | "export",
  subjectLabel: string,
  command: EvidenceWorkbenchCommandState
): EvidenceWorkbenchCommandState {
  return {
    ...command,
    ariaLabel: control === "validate"
      ? `Validate selected evidence packet for ${subjectLabel}`
      : `Export selected evidence manifest for ${subjectLabel}`
  };
}

function buildIntakeResultLabel(result: EvidenceVaultIntakeResponse | null): string | null {
  if (!result) {
    return null;
  }

  const sizeLabel = `${result.sizeBytes.toLocaleString()} bytes`;
  return `Retained ${result.fileName} as ${result.intakeId}; ${sizeLabel}; ${result.contentHashSha256}.`;
}

function buildExportResultViewModel(result: EvidencePacketExportResponse | null): EvidenceExportResultViewModel | null {
  if (!result) {
    return null;
  }

  const routeHref = normalizeManifestRoute(result.manifestRoute);
  const nodeLabel = result.evidenceCount === 1 ? "1 node" : `${result.evidenceCount} nodes`;
  const warningLabel = result.warningCount === 1 ? "1 warning" : `${result.warningCount} warnings`;
  const artifactRows = (result.vaultIdentity?.artifacts ?? []).map(buildVaultArtifactRow);
  const artifactLabel = artifactRows.length === 1 ? "1 retained artifact" : `${artifactRows.length} retained artifacts`;
  const requestListRows = (result.vaultIdentity?.requestLists ?? []).map(buildVaultRequestListRow);
  const requestListLabel = requestListRows.length === 1 ? "1 request list" : `${requestListRows.length} request lists`;
  const supportRequestRows = (result.vaultIdentity?.supportRequests ?? []).map(buildVaultSupportRequestRow);
  const supportRequestLabel = supportRequestRows.length === 1 ? "1 support request" : `${supportRequestRows.length} support requests`;
  const manifestPackageFamilyLabel = formatManifestPackageFamily(result.vaultIdentity?.manifestSnapshot ?? null);
  const vaultSummaryParts = result.vaultIdentity
    ? [
        ...(manifestPackageFamilyLabel ? [manifestPackageFamilyLabel] : []),
        artifactLabel,
        ...(requestListRows.length > 0 ? [requestListLabel] : []),
        ...(supportRequestRows.length > 0 ? [supportRequestLabel] : [])
      ]
    : [];
  return {
    title: result.retained ? "Manifest retained" : "Manifest generated",
    manifestPath: result.manifestPath,
    summaryLabel: `${nodeLabel}, ${warningLabel}${vaultSummaryParts.length > 0 ? `, ${vaultSummaryParts.join(", ")}` : ""}`,
    routeHref,
    routeLabel: routeHref ? "Open manifest" : null,
    routeAriaLabel: routeHref ? `Open retained evidence manifest at ${result.manifestPath}` : null,
    vaultIdLabel: result.vaultIdentity?.vaultId ?? null,
    storageKindLabel: result.vaultIdentity ? formatKind(result.vaultIdentity.storageKind) : null,
    manifestPackageFamilyLabel,
    artifactSummaryLabel: result.vaultIdentity ? artifactLabel : null,
    artifactRows,
    requestListSummaryLabel: result.vaultIdentity ? requestListLabel : null,
    requestListRows,
    supportRequestSummaryLabel: result.vaultIdentity ? supportRequestLabel : null,
    supportRequestRows
  };
}

function buildVaultArtifactRow(artifact: NonNullable<EvidencePacketExportResponse["vaultIdentity"]>["artifacts"][number]): EvidenceVaultArtifactRowViewModel {
  const sourceLabel = artifact.sourceRoute ?? artifact.sourcePath ?? "No source";
  const canonicalSubjectLabel = formatCanonicalSubject(artifact.canonicalSubjectKind, artifact.canonicalSubjectId) ?? "No canonical subject";
  const captureLabel = formatVaultArtifactCapture(artifact.capture);
  const extractionLabel = formatVaultArtifactExtraction(artifact.extractedFields ?? []);
  return {
    id: artifact.artifactId,
    kind: formatKind(artifact.kind),
    relativePath: artifact.relativePath,
    sizeLabel: formatBytes(artifact.sizeBytes),
    hashLabel: artifact.contentHashSha256,
    sourceLabel,
    canonicalSubjectLabel,
    captureLabel,
    extractionLabel,
    retainedLabel: `Retained ${formatDate(artifact.retainedAt)}`,
    ariaLabel: `${formatKind(artifact.kind)} retained vault artifact ${artifact.artifactId}, ${formatBytes(artifact.sizeBytes)}, ${canonicalSubjectLabel}, ${captureLabel}, ${extractionLabel}`
  };
}

function formatManifestPackageFamily(
  manifest: NonNullable<EvidencePacketExportResponse["vaultIdentity"]>["manifestSnapshot"] | null | undefined
): string | null {
  if (!manifest) {
    return null;
  }

  if (manifest.packageKindCode && manifest.packageKindCode !== "Unknown") {
    return formatKind(manifest.packageKindCode);
  }

  const packageKind = manifest.packageKind ? formatKind(manifest.packageKind) : "Evidence Package";
  return manifest.packageId ? `${packageKind} ${manifest.packageId}` : packageKind;
}

function formatVaultArtifactCapture(
  capture: NonNullable<EvidencePacketExportResponse["vaultIdentity"]>["artifacts"][number]["capture"]
): string {
  if (!capture) {
    return "No capture metadata";
  }

  const channel = formatKind(capture.captureChannel);
  const source = capture.sourceSystem ?? "unknown source";
  const received = capture.receivedAt ? `received ${formatDate(capture.receivedAt)}` : "received time unknown";
  const reference = capture.sourceReference ? `reference ${capture.sourceReference}` : "no source reference";
  return `${channel} via ${source}; ${received}; ${reference}`;
}

function formatVaultArtifactExtraction(
  fields: NonNullable<EvidencePacketExportResponse["vaultIdentity"]>["artifacts"][number]["extractedFields"]
): string {
  if (!fields || fields.length === 0) {
    return "No extracted fields";
  }

  const validatedCount = fields.filter((field) => field.validationStatus === "Ready").length;
  const reviewedCount = fields.filter((field) => field.reviewState.toLowerCase() === "reviewed").length;
  return `${fields.length} extracted ${fields.length === 1 ? "field" : "fields"}; ${validatedCount} validated; ${reviewedCount} reviewed`;
}

function buildVaultRequestListRow(requestList: EvidenceRequestList): EvidenceVaultRequestListRowViewModel {
  const requestListKindLabel = formatKind(requestList.requestListKind);
  const requestListFamilyLabel = requestList.requestListKindCode && requestList.requestListKindCode !== "Unknown"
    ? `${formatKind(requestList.requestListKindCode)} family`
    : "Evidence family";
  const targetLabel = `${formatKind(requestList.targetKind)} ${requestList.targetId}`;
  const highestSeverityLabel = formatKind(requestList.highestSeverity.toLowerCase());
  const requestCountLabel = requestList.requestCount === 1 ? "1 support request" : `${requestList.requestCount} support requests`;
  const evidenceKindsLabel = requestList.evidenceKinds.length > 0
    ? requestList.evidenceKinds.map(formatKind).join(", ")
    : "No evidence kinds";
  const blockedOutputsLabel = requestList.blockedOutputs.length > 0
    ? requestList.blockedOutputs.join(", ")
    : "No blocked outputs";
  return {
    id: requestList.requestListId,
    requestListKindLabel,
    requestListFamilyLabel,
    targetLabel,
    highestSeverityLabel,
    highestSeverityTone: mapValidationSeverityTone(requestList.highestSeverity),
    statusLabel: requestList.status,
    requestCountLabel,
    evidenceKindsLabel,
    blockedOutputsLabel,
    summary: requestList.summary,
    ariaLabel: `${requestListKindLabel} ${requestListFamilyLabel} for ${targetLabel}, ${highestSeverityLabel}, ${requestList.status}. ${requestList.summary}`
  };
}

function buildVaultSupportRequestRow(
  request: NonNullable<EvidencePacketExportResponse["vaultIdentity"]>["supportRequests"][number]
): EvidenceVaultSupportRequestRowViewModel {
  const requestKindLabel = formatKind(request.requestKind);
  const evidenceKindLabel = request.evidenceKind ? formatKind(request.evidenceKind) : "Unclassified evidence";
  const severityLabel = formatKind(request.severity.toLowerCase());
  const sourceLabel = request.sourceSystem ?? "No source system";
  const workItemLabel = request.workItemId ?? "No work item";
  const blockedOutputLabel = request.blockedOutput ?? "No blocked output";
  return {
    id: request.requestId,
    requestKindLabel,
    evidenceLabel: request.evidenceId,
    evidenceKindLabel,
    severityLabel,
    severityTone: mapValidationSeverityTone(request.severity),
    statusLabel: request.status,
    summary: request.summary,
    sourceLabel,
    workItemLabel,
    blockedOutputLabel,
    ariaLabel: `${requestKindLabel} support request for ${request.evidenceId}, ${severityLabel}, ${request.status}. ${request.summary}`
  };
}

function mapValidationSeverityTone(severity: "Info" | "Warning" | "Critical"): EvidenceStatusTone {
  switch (severity) {
    case "Critical":
      return "danger";
    case "Warning":
      return "warning";
    default:
      return "muted";
  }
}

function formatCanonicalSubject(kind?: string | null, id?: string | null) {
  if (!kind || !id) {
    return null;
  }

  return `${formatKind(kind)} ${id}`;
}

function formatBytes(value: number) {
  if (!Number.isFinite(value) || value < 0) {
    return "Unknown size";
  }

  if (value < 1024) {
    return `${value} B`;
  }

  const kib = value / 1024;
  if (kib < 1024) {
    return `${kib.toFixed(kib >= 10 ? 0 : 1)} KiB`;
  }

  const mib = kib / 1024;
  return `${mib.toFixed(mib >= 10 ? 0 : 1)} MiB`;
}

function normalizeManifestRoute(value: string): string | null {
  const route = value.trim();
  if (!route.startsWith("/workstation/evidence/") || route.startsWith("//")) {
    return null;
  }

  return route;
}

function mapWorkflowActionTone(tone: string): EvidencePacketActionTone {
  switch (tone.trim().toLowerCase()) {
    case "primary":
      return "primary";
    case "success":
    case "ready":
      return "success";
    case "warning":
    case "review":
      return "warning";
    case "danger":
    case "critical":
    case "error":
    case "blocked":
      return "danger";
    default:
      return "muted";
  }
}

function formatKind(kind: string) {
  return kind
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .split(/[-_\s]+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

function formatEvidenceDocumentAuthority(
  authority: EvidenceVaultDocumentEntry["document"]["authority"] | null | undefined
) {
  if (!authority) {
    return "Support, block, suggest, and link only";
  }

  const allowed = [
    authority.canSupport ? "support" : null,
    authority.canBlock ? "block" : null,
    authority.canSuggest ? "suggest" : null,
    authority.canLink ? "link" : null
  ].filter((value): value is string => Boolean(value));
  const prohibited = [
    authority.canApprove ? null : "approve",
    authority.canPost ? null : "post",
    authority.canCertify ? null : "certify",
    authority.canRelease ? null : "release"
  ].filter((value): value is string => Boolean(value));

  if (allowed.length === 0 && prohibited.length === 0) {
    return authority.boundary;
  }

  const allowedLabel = allowed.length === 0 ? "no authority actions" : allowed.join(", ");
  const prohibitedLabel = prohibited.length === 0 ? "none" : prohibited.join(", ");
  return `${allowedLabel}; cannot ${prohibitedLabel}`;
}

function formatPageTag(value: string) {
  const tag = normalizePageTagForDisplay(value);
  return tag.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function normalizePageTagForDisplay(value: string) {
  const trimmed = value.trim();
  const separatorIndex = trimmed.indexOf(":");
  if (separatorIndex < 0) {
    return trimmed;
  }

  const prefix = trimmed.slice(0, separatorIndex).trim();
  return prefix.toLowerCase() === "evidenceworkbench" ? "EvidenceWorkbench" : trimmed;
}

function formatRelationship(value: string) {
  return value
    .replace(/[-_]+/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .trim()
    .replace(/\s+/g, " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function buildFreshnessLabel(node: EvidenceNode): { label: string; tone: EvidenceStatusTone } {
  if (!node.freshness.asOf) {
    return { label: "No as-of timestamp", tone: node.status === "Ready" ? "muted" : mapStatusTone(node.status) };
  }

  const asOfLabel = formatDate(node.freshness.asOf);
  if (node.freshness.isStale || node.status === "Stale") {
    return { label: `Stale as of ${asOfLabel}`, tone: "warning" };
  }

  if (node.status === "Blocked" || node.status === "Missing") {
    return { label: asOfLabel, tone: "danger" };
  }

  if (node.status === "Ready") {
    return { label: `Fresh as of ${asOfLabel}`, tone: "success" };
  }

  return { label: asOfLabel, tone: "muted" };
}

function formatCount(count: number, singular: string) {
  return count === 1 ? `1 ${singular}` : `${count} ${singular}s`;
}

function slugifyId(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "") || "group";
}

function resolveSourceWorkflowHref(
  subject: EvidenceSubject | null,
  operatingScope: AppShellOperatingScopeState
) {
  const directRoute = normalizeLocalWorkstationRoute(subject?.route);
  if (directRoute) {
    return appendOperatingScopeToRoute(directRoute, operatingScope);
  }

  return subject ? appendOperatingScopeToRoute(workflowTargetPath(subject.pageTag, subject.workspace), operatingScope) : null;
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function padUtc(value: number) {
  return value.toString().padStart(2, "0");
}
