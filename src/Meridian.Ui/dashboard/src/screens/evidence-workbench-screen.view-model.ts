import { useEffect, useMemo, useRef, useState } from "react";
import {
  exportEvidenceManifest,
  getEvidencePacket,
  getEvidenceSubjects,
  validateEvidencePacket
} from "@/lib/api";
import { evidenceWorkbenchPath, legacyWorkspaceRedirect, WORKSPACES, workflowTargetPath } from "@/lib/workspace";
import type {
  EvidenceCompleteness,
  EvidenceNode,
  EvidencePacket,
  EvidencePacketExportResponse,
  EvidenceStatus,
  EvidenceSubject,
  WorkflowAction
} from "@/types";

export interface EvidenceWorkbenchServices {
  getSubjects: () => Promise<EvidenceSubject[]>;
  getPacket: (subjectKind: string, subjectId: string) => Promise<EvidencePacket>;
  validatePacket: (subjectKind: string, subjectId: string) => Promise<EvidenceCompleteness>;
  exportManifest: (subjectKind: string, subjectId: string) => Promise<EvidencePacketExportResponse>;
}

export interface EvidenceNodeGroupViewModel {
  id: string;
  label: string;
  readyCount: number;
  reviewCount: number;
  nodes: EvidenceNode[];
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
  exportManifest: (subjectKind, subjectId) => exportEvidenceManifest(subjectKind, subjectId)
};

const workstationWorkspaceKeys = new Set<string>(WORKSPACES.map((workspace) => workspace.key));
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

  useEffect(() => {
    const revision = requestRevisionRef.current + 1;
    requestRevisionRef.current = revision;
    validateCommandRevisionRef.current += 1;
    exportCommandRevisionRef.current += 1;
    setLoading(true);
    setError(null);
    setPacket(null);
    setExportResult(null);
    setValidationResult(null);
    setExportBusy(false);
    setValidateBusy(false);

    const load = async () => {
      try {
        const subjectList = await services.getSubjects();
        if (requestRevisionRef.current !== revision) {
          return;
        }
        setSubjects(subjectList);

        if (selectedSubjectKind && selectedSubjectId) {
          const nextPacket = await services.getPacket(selectedSubjectKind, selectedSubjectId);
          if (requestRevisionRef.current !== revision) {
            return;
          }
          setPacket(nextPacket);
        }
      } catch (loadError) {
        if (requestRevisionRef.current === revision) {
          setError(loadError instanceof Error ? loadError.message : "Evidence workbench failed to load.");
        }
      } finally {
        if (requestRevisionRef.current === revision) {
          setLoading(false);
        }
      }
    };

    void load();

    return () => {
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
    setExportBusy(true);
    setError(null);
    setExportResult(null);
    try {
      const result = await services.exportManifest(selectedSubjectKind, selectedSubjectId);
      if (requestRevisionRef.current === revision && exportCommandRevisionRef.current === exportRevision) {
        setExportResult(result);
      }
    } catch (exportError) {
      if (requestRevisionRef.current === revision && exportCommandRevisionRef.current === exportRevision) {
        setError(exportError instanceof Error ? exportError.message : "Evidence manifest export failed.");
      }
    } finally {
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
    setValidateBusy(true);
    setError(null);
    setValidationResult(null);
    try {
      const result = await services.validatePacket(selectedSubjectKind, selectedSubjectId);
      if (requestRevisionRef.current === revision && validateCommandRevisionRef.current === validateRevision) {
        setValidationResult(result);
      }
    } catch (validateError) {
      if (requestRevisionRef.current === revision && validateCommandRevisionRef.current === validateRevision) {
        setError(validateError instanceof Error ? validateError.message : "Evidence validation failed.");
      }
    } finally {
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
    validateBusy: input.validateBusy,
    validationResult: input.validationResult,
    openSubjectHref: (subject) =>
      evidenceWorkbenchPath(subject.subjectKind, subject.subjectId),
    reloadEvidence: input.reloadEvidence ?? noopReloadEvidence,
    exportManifest: input.exportManifest,
    validatePacket: input.validatePacket
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
        disabled: validateBusy || !hasSubject,
        disabledReason: validateBusy
          ? "Evidence validation is already running."
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
        disabled: exportBusy || !hasSubject,
        disabledReason: exportBusy
          ? "Evidence export is already running."
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

function normalizeSubjectRoute(route: string | null | undefined) {
  const trimmed = route?.trim();
  if (!trimmed) {
    return null;
  }

  const localRoute = trimmed.startsWith("/workstation/")
    ? trimmed.slice("/workstation".length)
    : trimmed;

  if (!localRoute.startsWith("/") || localRoute.startsWith("//") || localRoute.startsWith("/api/")) {
    return null;
  }

  const { pathname, search, hash } = splitRouteParts(localRoute);
  const canonicalRoute = legacyWorkspaceRedirect(pathname, search, hash) ?? `${pathname}${search}${hash}`;
  const routeWorkspace = canonicalRoute.split(/[/?#]/).filter(Boolean)[0];
  if (!routeWorkspace || !workstationWorkspaceKeys.has(routeWorkspace)) {
    return null;
  }

  return canonicalRoute;
}

function splitRouteParts(route: string): { pathname: string; search: string; hash: string } {
  const hashIndex = route.indexOf("#");
  const routeWithoutHash = hashIndex >= 0 ? route.slice(0, hashIndex) : route;
  const hash = hashIndex >= 0 ? route.slice(hashIndex) : "";
  const searchIndex = routeWithoutHash.indexOf("?");
  return {
    pathname: searchIndex >= 0 ? routeWithoutHash.slice(0, searchIndex) : routeWithoutHash,
    search: searchIndex >= 0 ? routeWithoutHash.slice(searchIndex) : "",
    hash
  };
}

function resolveSourceWorkflowHref(subject: EvidenceSubject | null) {
  const directRoute = normalizeSubjectRoute(subject?.route);
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

  return new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit"
  }).format(date);
}
