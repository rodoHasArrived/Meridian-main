import { useEffect, useMemo, useRef, useState } from "react";
import {
  exportEvidenceManifest,
  getEvidencePacket,
  getEvidenceSubjects,
  validateEvidencePacket
} from "@/lib/api";
import type { ApiRequestOptions } from "@/lib/api";
import { evidenceWorkbenchPath, normalizeLocalWorkstationRoute, workflowTargetPath } from "@/lib/workspace";
import type {
  EvidenceCompleteness,
  EvidenceEdge,
  EvidenceNode,
  EvidencePacket,
  EvidencePacketExportResponse,
  EvidenceStatus,
  EvidenceSubject,
  WorkflowAction
} from "@/types";

export interface EvidenceWorkbenchServices {
  getSubjects: (options?: ApiRequestOptions) => Promise<EvidenceSubject[]>;
  getPacket: (subjectKind: string, subjectId: string, options?: ApiRequestOptions) => Promise<EvidencePacket>;
  validatePacket: (subjectKind: string, subjectId: string, options?: ApiRequestOptions) => Promise<EvidenceCompleteness>;
  exportManifest: (subjectKind: string, subjectId: string, options?: ApiRequestOptions) => Promise<EvidencePacketExportResponse>;
}

export interface EvidenceNodeGroupViewModel {
  id: string;
  label: string;
  readyCount: number;
  reviewCount: number;
  nodes: EvidenceNode[];
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

export interface EvidenceExportResultViewModel {
  title: string;
  manifestPath: string;
  summaryLabel: string;
  routeHref: string | null;
  routeLabel: string | null;
  routeAriaLabel: string | null;
}

export interface EvidenceWorkbenchViewModel {
  selectedSubjectKind: string | null;
  selectedSubjectId: string | null;
  title: string;
  subtitle: string;
  loading: boolean;
  error: string | null;
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
  lineagePanel: EvidenceLineagePanelViewModel;
  nodeGroups: EvidenceNodeGroupViewModel[];
  hasPacketActions: boolean;
  packetActionsLabel: string;
  packetActionsSummaryLabel: string;
  packetActions: EvidencePacketActionViewModel[];
  missingEvidence: string[];
  staleEvidence: string[];
  relatedWorkItemIds: string[];
  warnings: string[];
  canExport: boolean;
  reloadCommand: EvidenceWorkbenchCommandState;
  validateCommand: EvidenceWorkbenchCommandState;
  exportCommand: EvidenceWorkbenchCommandState;
  exportBusy: boolean;
  exportResult: EvidencePacketExportResponse | null;
  exportResultDetail: EvidenceExportResultViewModel | null;
  validateBusy: boolean;
  validationResult: EvidenceCompleteness | null;
  openSubjectHref: (subject: EvidenceSubject) => string;
  reloadEvidence: () => void;
  exportManifest: () => Promise<void>;
  validatePacket: () => Promise<void>;
}

export type EvidenceStatusTone = "success" | "warning" | "danger" | "muted";

const defaultServices: EvidenceWorkbenchServices = {
  getSubjects: getEvidenceSubjects,
  getPacket: getEvidencePacket,
  validatePacket: validateEvidencePacket,
  exportManifest: (subjectKind, subjectId, options) => exportEvidenceManifest(subjectKind, subjectId, { includeWarnings: true }, options)
};

const noopReloadEvidence = () => {};

export function useEvidenceWorkbenchViewModel(
  search: string,
  services: EvidenceWorkbenchServices = defaultServices
): EvidenceWorkbenchViewModel {
  const params = useMemo(() => new URLSearchParams(search), [search]);
  const selectedSubjectKind = params.get("subjectKind");
  const selectedSubjectId = params.get("subjectId");
  const [subjects, setSubjects] = useState<EvidenceSubject[]>([]);
  const [packet, setPacket] = useState<EvidencePacket | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [exportBusy, setExportBusy] = useState(false);
  const [exportResult, setExportResult] = useState<EvidencePacketExportResponse | null>(null);
  const [validateBusy, setValidateBusy] = useState(false);
  const [validationResult, setValidationResult] = useState<EvidenceCompleteness | null>(null);
  const [reloadRevision, setReloadRevision] = useState(0);
  const requestRevisionRef = useRef(0);
  const validateCommandRevisionRef = useRef(0);
  const exportCommandRevisionRef = useRef(0);
  const loadAbortRef = useRef<AbortController | null>(null);
  const validateAbortRef = useRef<AbortController | null>(null);
  const exportAbortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    loadAbortRef.current?.abort();
    validateAbortRef.current?.abort();
    exportAbortRef.current?.abort();
    const revision = requestRevisionRef.current + 1;
    requestRevisionRef.current = revision;
    validateCommandRevisionRef.current += 1;
    exportCommandRevisionRef.current += 1;
    const controller = new AbortController();
    loadAbortRef.current = controller;
    setLoading(true);
    setError(null);
    setPacket(null);
    setExportResult(null);
    setValidationResult(null);
    setExportBusy(false);
    setValidateBusy(false);

    const load = async () => {
      try {
        const subjectList = await services.getSubjects({ signal: controller.signal });
        if (requestRevisionRef.current !== revision) {
          return;
        }
        setSubjects(subjectList);

        if (selectedSubjectKind && selectedSubjectId) {
          const nextPacket = await services.getPacket(selectedSubjectKind, selectedSubjectId, { signal: controller.signal });
          if (requestRevisionRef.current !== revision) {
            return;
          }
          setPacket(nextPacket);
        }
      } catch (loadError) {
        if (requestRevisionRef.current === revision && !isAbortError(loadError)) {
          setError(loadError instanceof Error ? loadError.message : "Evidence workbench failed to load.");
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
      requestRevisionRef.current += 1;
      validateCommandRevisionRef.current += 1;
      exportCommandRevisionRef.current += 1;
    };
  }, [reloadRevision, selectedSubjectId, selectedSubjectKind, services]);

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
        setError(exportError instanceof Error ? exportError.message : "Evidence manifest export failed.");
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
        setError(validateError instanceof Error ? validateError.message : "Evidence validation failed.");
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

  const reloadEvidence = () => {
    if (loading) {
      return;
    }

    setReloadRevision((revision) => revision + 1);
  };

  return buildEvidenceWorkbenchViewModel({
    selectedSubjectKind,
    selectedSubjectId,
    loading,
    error,
    subjects,
    packet,
    exportBusy,
    exportResult,
    validateBusy,
    validationResult,
    reloadEvidence,
    exportManifest: exportCommand,
    validatePacket: validateCommand
  });
}

export function buildEvidenceWorkbenchViewModel(input: {
  selectedSubjectKind: string | null;
  selectedSubjectId: string | null;
  loading: boolean;
  error: string | null;
  subjects: EvidenceSubject[];
  packet: EvidencePacket | null;
  exportBusy: boolean;
  exportResult: EvidencePacketExportResponse | null;
  validateBusy: boolean;
  validationResult: EvidenceCompleteness | null;
  reloadEvidence?: () => void;
  exportManifest: () => Promise<void>;
  validatePacket: () => Promise<void>;
}): EvidenceWorkbenchViewModel {
  const completeness = input.validationResult ?? input.packet?.completeness ?? null;
  const hasSelection = Boolean(input.selectedSubjectKind && input.selectedSubjectId);
  const subject = input.packet?.subject ?? null;
  const sourceWorkflowHref = resolveSourceWorkflowHref(subject);
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
  const packetActions = buildEvidencePacketActions({
    actions: input.packet?.actions ?? [],
    subject,
    selectedSubjectKind: input.selectedSubjectKind,
    selectedSubjectId: input.selectedSubjectId,
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
    subjectEmptyActionHref: "/trading/readiness",
    subjectEmptyActionAriaLabel: "Open readiness console to review upstream evidence sources",
    subjects: input.subjects,
    packet: input.packet,
    scoreLabel: completeness ? `${completeness.score}% complete` : "No score",
    statusLabel: completeness ? formatStatus(completeness.status) : "Not loaded",
    statusTone: completeness ? mapStatusTone(completeness.status) : "muted",
    generatedLabel: input.packet ? formatDate(input.packet.generatedAt) : "Not generated",
    lineagePanel: buildEvidenceLineagePanel(input.packet?.edges ?? [], input.packet?.subject ?? null),
    nodeGroups: groupNodes(input.packet?.nodes ?? []),
    hasPacketActions: packetActions.length > 0,
    packetActionsLabel: "Evidence packet actions",
    packetActionsSummaryLabel: packetActions.length === 1 ? "1 workflow action" : `${packetActions.length} workflow actions`,
    packetActions,
    missingEvidence: completeness?.missingIds ?? [],
    staleEvidence: completeness?.staleIds ?? [],
    relatedWorkItemIds: collectWorkItemIds(input.packet?.nodes ?? []),
    warnings: input.packet?.warnings ?? [],
    canExport: input.packet !== null && !input.exportBusy,
    reloadCommand,
    validateCommand,
    exportCommand,
    exportBusy: input.exportBusy,
    exportResult: input.exportResult,
    exportResultDetail: buildExportResultViewModel(input.exportResult),
    validateBusy: input.validateBusy,
    validationResult: input.validationResult,
    openSubjectHref: (subject) =>
      evidenceWorkbenchPath(subject.subjectKind, subject.subjectId),
    reloadEvidence: input.reloadEvidence ?? noopReloadEvidence,
    exportManifest: input.exportManifest,
    validatePacket: input.validatePacket
  };
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

  return Array.from(groups.entries()).map(([id, groupNodesForId]) => ({
    id,
    label: formatKind(id),
    readyCount: groupNodesForId.filter((node) => node.status === "Ready").length,
    reviewCount: groupNodesForId.filter((node) => node.status !== "Ready").length,
    nodes: groupNodesForId
  }));
}

export function buildEvidencePacketActions(input: {
  actions: WorkflowAction[];
  subject: EvidenceSubject | null;
  selectedSubjectKind: string | null;
  selectedSubjectId: string | null;
  exportBusy: boolean;
  validateBusy: boolean;
}): EvidencePacketActionViewModel[] {
  return input.actions.map((action) => {
    const control = resolveActionControl(action.actionId);
    const subjectKind = input.subject?.subjectKind ?? input.selectedSubjectKind;
    const subjectId = input.subject?.subjectId ?? input.selectedSubjectId;
    const href = action.targetPageTag === "EvidenceWorkbench" && subjectKind && subjectId
      ? evidenceWorkbenchPath(subjectKind, subjectId)
      : workflowTargetPath(action.targetPageTag, input.subject?.workspace);
    const subjectLabel = input.subject?.label ?? "selected evidence subject";
    const command = buildActionCommand(control, subjectLabel, input.exportBusy, input.validateBusy, Boolean(subjectKind && subjectId));

    return {
      id: action.actionId,
      label: action.label,
      detail: action.detail,
      targetLabel: formatPageTag(action.targetPageTag),
      tone: mapWorkflowActionTone(action.tone),
      href,
      control,
      ...command
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

function buildExportResultViewModel(result: EvidencePacketExportResponse | null): EvidenceExportResultViewModel | null {
  if (!result) {
    return null;
  }

  const routeHref = normalizeManifestRoute(result.manifestRoute);
  const nodeLabel = result.evidenceCount === 1 ? "1 node" : `${result.evidenceCount} nodes`;
  const warningLabel = result.warningCount === 1 ? "1 warning" : `${result.warningCount} warnings`;
  return {
    title: result.retained ? "Manifest retained" : "Manifest generated",
    manifestPath: result.manifestPath,
    summaryLabel: `${nodeLabel}, ${warningLabel}`,
    routeHref,
    routeLabel: routeHref ? "Open manifest" : null,
    routeAriaLabel: routeHref ? `Open retained evidence manifest at ${result.manifestPath}` : null
  };
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
    .split("-")
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

function formatPageTag(value: string) {
  return value.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function formatRelationship(value: string) {
  return value
    .replace(/[-_]+/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .trim()
    .replace(/\s+/g, " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function resolveSourceWorkflowHref(subject: EvidenceSubject | null) {
  const directRoute = normalizeLocalWorkstationRoute(subject?.route);
  if (directRoute) {
    return directRoute;
  }

  return subject ? workflowTargetPath(subject.pageTag, subject.workspace) : null;
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
