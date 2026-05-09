import { useEffect, useMemo, useRef, useState } from "react";
import {
  exportEvidenceManifest,
  getEvidencePacket,
  getEvidenceSubjects,
  validateEvidencePacket
} from "@/lib/api";
import { evidenceWorkbenchPath } from "@/lib/workspace";
import type {
  EvidenceCompleteness,
  EvidenceNode,
  EvidencePacket,
  EvidencePacketExportResponse,
  EvidenceStatus,
  EvidenceSubject
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

export interface EvidenceWorkbenchViewModel {
  selectedSubjectKind: string | null;
  selectedSubjectId: string | null;
  title: string;
  subtitle: string;
  loading: boolean;
  error: string | null;
  hasSelection: boolean;
  hasPacket: boolean;
  hasSubjects: boolean;
  loadingLabel: string;
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
  missingEvidence: string[];
  staleEvidence: string[];
  relatedWorkItemIds: string[];
  warnings: string[];
  canExport: boolean;
  exportBusy: boolean;
  exportResult: EvidencePacketExportResponse | null;
  validateBusy: boolean;
  validationResult: EvidenceCompleteness | null;
  openSubjectHref: (subject: EvidenceSubject) => string;
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
  const requestRevisionRef = useRef(0);

  useEffect(() => {
    const revision = requestRevisionRef.current + 1;
    requestRevisionRef.current = revision;
    setLoading(true);
    setError(null);
    setPacket(null);
    setExportResult(null);
    setValidationResult(null);

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
  }, [selectedSubjectId, selectedSubjectKind, services]);

  const exportCommand = async () => {
    if (!selectedSubjectKind || !selectedSubjectId) {
      return;
    }

    setExportBusy(true);
    setError(null);
    try {
      setExportResult(await services.exportManifest(selectedSubjectKind, selectedSubjectId));
    } catch (exportError) {
      setError(exportError instanceof Error ? exportError.message : "Evidence manifest export failed.");
    } finally {
      setExportBusy(false);
    }
  };

  const validateCommand = async () => {
    if (!selectedSubjectKind || !selectedSubjectId) {
      return;
    }

    setValidateBusy(true);
    setError(null);
    try {
      setValidationResult(await services.validatePacket(selectedSubjectKind, selectedSubjectId));
    } catch (validateError) {
      setError(validateError instanceof Error ? validateError.message : "Evidence validation failed.");
    } finally {
      setValidateBusy(false);
    }
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
  exportManifest: () => Promise<void>;
  validatePacket: () => Promise<void>;
}): EvidenceWorkbenchViewModel {
  const completeness = input.validationResult ?? input.packet?.completeness ?? null;
  const hasSelection = Boolean(input.selectedSubjectKind && input.selectedSubjectId);
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
    hasSelection,
    hasPacket: input.packet !== null,
    hasSubjects: input.subjects.length > 0,
    loadingLabel: hasSelection
      ? `Loading evidence packet for ${input.selectedSubjectKind}/${input.selectedSubjectId}.`
      : "Loading evidence subjects.",
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
    missingEvidence: completeness?.missingIds ?? [],
    staleEvidence: completeness?.staleIds ?? [],
    relatedWorkItemIds: collectWorkItemIds(input.packet?.nodes ?? []),
    warnings: input.packet?.warnings ?? [],
    canExport: input.packet !== null && !input.exportBusy,
    exportBusy: input.exportBusy,
    exportResult: input.exportResult,
    validateBusy: input.validateBusy,
    validationResult: input.validationResult,
    openSubjectHref: (subject) =>
      evidenceWorkbenchPath(subject.subjectKind, subject.subjectId),
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

function formatKind(kind: string) {
  return kind
    .split("-")
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
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
