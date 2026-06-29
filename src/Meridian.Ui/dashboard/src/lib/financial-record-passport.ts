import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerRowDto,
  FinancialRecordExplorerSelectedRecordDto,
  FinancialRecordExplorerTone
} from "@/types";

export interface NumberPassportItem {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: FinancialRecordExplorerTone;
  href: string;
}

interface PassportCandidate {
  id: string;
  label: string;
  value: string;
  detail: string;
  href: string;
  tone: FinancialRecordExplorerTone;
}

export function buildNumberPassportItems(
  record: FinancialRecordExplorerSelectedRecordDto,
  row: FinancialRecordExplorerRowDto | null,
  explorer: FinancialRecordExplorerDto | null
): NumberPassportItem[] {
  const candidates = collectPassportCandidates(record, row, explorer);
  const freshness = findPassportCandidateByPrimaryText(candidates, ["freshness", "as of", "updated", "current", "readiness", "status"]) ??
    findPassportCandidate(candidates, ["freshness", "as of", "updated", "current"]);
  const reconciliation = findPassportCandidate(candidates, ["reconciliation", "reconcile", "matched", "break"]);
  const approvals = findPassportCandidate(candidates, ["approval", "approved", "approver", "sign-off", "certification", "certified"]);
  const reportUsage = findPassportCandidate(candidates, ["report usage", "report-line", "report line", "reporting", "board pack", "provenance"]);
  const disabledAction = [...record.proofActions, ...(explorer?.proofActions ?? [])].find((action) => !action.isEnabled && (action.disabledReason || action.description));
  const warningCandidate = findPassportCandidate(candidates, ["blocked", "blocker", "warning", "danger", "missing"]);
  const evidencePacket = findEvidencePacketCandidate(candidates);
  const auditTrail = findPassportCandidate(candidates, ["audit", "history", "trail", "lineage"]) ??
    buildGraphPassportCandidate(explorer);

  return [
    {
      id: "source",
      label: "Source",
      value: row?.source || record.subtitle || record.recordType,
      detail: explorer?.sourceState || record.description,
      tone: row?.tone ?? record.tone,
      href: firstLinkedCellHref(row) || record.fullRecordHref
    },
    passportItemFromCandidate("freshness", "Freshness", freshness, {
      value: row?.status || "Not linked",
      detail: explorer?.sourceState || "No freshness detail in this record detail.",
      tone: row?.tone ?? record.tone,
      href: ""
    }),
    passportItemFromCandidate("reconciliation", "Reconciliation", reconciliation, {
      value: "Not linked",
      detail: "No reconciliation link in this record detail.",
      tone: "Default",
      href: ""
    }),
    passportItemFromCandidate("approvals", "Approvals", approvals, {
      value: "Not linked",
      detail: "No approval link in this record detail.",
      tone: "Default",
      href: ""
    }),
    passportItemFromCandidate("report-usage", "Report Usage", reportUsage, {
      value: "Not linked",
      detail: "No report usage link in this record detail.",
      tone: "Default",
      href: ""
    }),
    {
      id: "blockers",
      label: "Blockers",
      value: explorer?.isBlocked ? "Blocked" : disabledAction ? disabledAction.label : warningCandidate ? warningCandidate.value : "None disclosed",
      detail: explorer?.isBlocked ? explorer.blockedReason : disabledAction ? disabledAction.disabledReason || disabledAction.description : warningCandidate?.detail ?? "No blocker disclosed by this record detail.",
      tone: explorer?.isBlocked || disabledAction ? "Danger" : warningCandidate?.tone ?? "Success",
      href: warningCandidate?.href ?? ""
    },
    passportItemFromCandidate("evidence-packet", "Evidence Packet", evidencePacket, {
      value: record.fullRecordHref ? "Full record" : "Not linked",
      detail: record.fullRecordHref ? record.description : "No evidence packet link in this record detail.",
      tone: record.fullRecordHref ? record.tone : "Default",
      href: record.fullRecordHref
    }),
    passportItemFromCandidate("audit-trail", "Audit Trail", auditTrail, {
      value: "Not linked",
      detail: "No audit trail link in this record detail.",
      tone: "Default",
      href: ""
    })
  ];
}

function collectPassportCandidates(
  record: FinancialRecordExplorerSelectedRecordDto,
  row: FinancialRecordExplorerRowDto | null,
  explorer: FinancialRecordExplorerDto | null
): PassportCandidate[] {
  const candidates: PassportCandidate[] = [
    ...record.fields.map((field) => ({
      id: `field:${field.label}`,
      label: field.label,
      value: field.value,
      detail: field.detail,
      href: hrefFromDetail(field.detail),
      tone: field.tone
    })),
    ...record.usedIn.map((item) => ({
      id: item.relationshipId,
      label: item.label,
      value: item.label,
      detail: item.description,
      href: item.href,
      tone: item.tone
    })),
    ...record.impacts.map((item) => ({
      id: item.relationshipId,
      label: item.label,
      value: item.label,
      detail: item.description,
      href: item.href,
      tone: item.tone
    })),
    ...record.proofActions.map((action) => ({
      id: action.actionId,
      label: action.label,
      value: action.label,
      detail: action.disabledReason || action.description,
      href: action.isEnabled ? action.href : "",
      tone: action.tone
    })),
    ...(explorer?.proofActions ?? []).map((action) => ({
      id: action.actionId,
      label: action.label,
      value: action.label,
      detail: action.disabledReason || action.description,
      href: action.isEnabled ? action.href : "",
      tone: action.tone
    }))
  ];

  if (row) {
    candidates.push(
      {
        id: `${row.recordId}:status`,
        label: "Status",
        value: row.status,
        detail: explorer?.sourceState ?? row.recordType,
        href: "",
        tone: row.tone
      },
      ...row.cells.map((cell) => ({
        id: `${row.recordId}:cell:${cell.columnId}`,
        label: cell.columnId,
        value: cell.displayValue,
        detail: cell.rawValue,
        href: cell.linkHref,
        tone: cell.tone
      }))
    );
  }

  return candidates;
}

function passportItemFromCandidate(
  id: string,
  label: string,
  candidate: PassportCandidate | null,
  fallback: Omit<NumberPassportItem, "id" | "label">
): NumberPassportItem {
  return {
    id,
    label,
    value: candidate?.value || fallback.value,
    detail: candidate?.detail || fallback.detail,
    tone: candidate?.tone ?? fallback.tone,
    href: candidate?.href || fallback.href
  };
}

function findPassportCandidate(candidates: PassportCandidate[], keywords: string[]): PassportCandidate | null {
  return candidates.find((candidate) => {
    const text = [candidate.id, candidate.label, candidate.value, candidate.detail, candidate.href, candidate.tone].join(" ").toLowerCase();
    return keywords.some((keyword) => text.includes(keyword));
  }) ?? null;
}

function findEvidencePacketCandidate(candidates: PassportCandidate[]): PassportCandidate | null {
  const linkedCandidates = candidates.filter((candidate) => candidate.href);
  const primaryKeywords = ["evidence packet", "provider evidence", "source record", "passport", "manifest", "full record"];
  return findPassportCandidateByPrimaryText(linkedCandidates, primaryKeywords) ??
    findPassportCandidateByPrimaryText(candidates, primaryKeywords) ??
    findPassportCandidate(linkedCandidates, ["evidence", ...primaryKeywords]) ??
    findPassportCandidate(candidates, ["evidence", ...primaryKeywords]);
}

function findPassportCandidateByPrimaryText(candidates: PassportCandidate[], keywords: string[]): PassportCandidate | null {
  return candidates.find((candidate) => {
    const text = [candidate.id, candidate.label, candidate.value, candidate.href].join(" ").toLowerCase();
    return keywords.some((keyword) => text.includes(keyword));
  }) ?? null;
}

function buildGraphPassportCandidate(explorer: FinancialRecordExplorerDto | null): PassportCandidate | null {
  if (!explorer || explorer.recordGraph.nodes.length === 0) {
    return null;
  }

  const firstLinkedNode = explorer.recordGraph.nodes.find((node) => node.href);
  return {
    id: "record-graph",
    label: "Record graph",
    value: `${explorer.recordGraph.nodes.length} graph nodes`,
    detail: `${explorer.recordGraph.edges.length} retained relationships.`,
    href: firstLinkedNode?.href ?? "",
    tone: firstLinkedNode?.tone ?? "Info"
  };
}

function hrefFromDetail(detail: string): string {
  const value = detail.trim();
  return value.startsWith("/") || value.startsWith("http://") || value.startsWith("https://") ? value : "";
}

function firstLinkedCellHref(row: FinancialRecordExplorerRowDto | null): string {
  return row?.cells.find((cell) => cell.linkHref)?.linkHref ?? "";
}
