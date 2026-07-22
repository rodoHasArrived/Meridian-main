import type { OperatorFocusCandidateTone } from "@/app-shell.operator-focus";

export interface EvidenceTimelineCandidate {
  id: string;
  label: string;
  detail: string;
  route: string;
  workspaceLabel: string;
  timestampLabel: string;
  timestampIso: string;
  tone: OperatorFocusCandidateTone;
  ariaLabel: string;
  occurredAtMs: number;
  sourcePriority: number;
  sourceIndex: number;
}

export interface EvidenceTimelineCandidateInput {
  id: string;
  label: string;
  detail: string;
  route: string;
  workspaceLabel: string;
  timestamp: string | null | undefined;
  tone: OperatorFocusCandidateTone;
  sourcePriority: number;
  sourceIndex: number;
}

export function pushEvidenceTimelineCandidate(
  items: EvidenceTimelineCandidate[],
  input: EvidenceTimelineCandidateInput
) {
  const item = buildEvidenceTimelineCandidate(input);
  if (item) {
    items.push(item);
  }
}

export function buildEvidenceTimelineCandidate({
  id,
  label,
  detail,
  route,
  workspaceLabel,
  timestamp,
  tone,
  sourcePriority,
  sourceIndex
}: EvidenceTimelineCandidateInput): EvidenceTimelineCandidate | null {
  const timestampState = parseEvidenceTimestamp(timestamp);
  if (!timestampState) {
    return null;
  }

  return {
    id,
    label,
    detail,
    route,
    workspaceLabel,
    timestampLabel: timestampState.label,
    timestampIso: timestampState.iso,
    tone,
    ariaLabel: `${workspaceLabel}: ${label}. ${detail} ${timestampState.label}. Open evidence.`,
    occurredAtMs: timestampState.occurredAtMs,
    sourcePriority,
    sourceIndex
  };
}

function parseEvidenceTimestamp(value: string | null | undefined): { occurredAtMs: number; iso: string; label: string } | null {
  const occurredAtMs = Date.parse(value ?? "");
  if (!Number.isFinite(occurredAtMs)) {
    return null;
  }

  const iso = new Date(occurredAtMs).toISOString();
  return {
    occurredAtMs,
    iso,
    label: `${iso.slice(0, 10)} ${iso.slice(11, 16)} UTC`
  };
}
