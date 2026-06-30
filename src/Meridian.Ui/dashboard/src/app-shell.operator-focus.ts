export type OperatorFocusCandidateTone = "ready" | "review" | "blocked" | "pending";

export interface OperatorFocusCandidate {
  id: string;
  label: string;
  detail: string;
  route: string;
  workspaceLabel: string;
  actionLabel: string;
  tone: OperatorFocusCandidateTone;
  ariaLabel: string;
  sourcePriority: number;
  sourceIndex: number;
}

export function buildOperatorFocusCandidate({
  id,
  label,
  detail,
  route,
  workspaceLabel,
  actionLabel,
  tone,
  sourcePriority,
  sourceIndex
}: Omit<OperatorFocusCandidate, "ariaLabel">): OperatorFocusCandidate {
  return {
    id,
    label,
    detail,
    route,
    workspaceLabel,
    actionLabel,
    tone,
    sourcePriority,
    sourceIndex,
    ariaLabel: `${workspaceLabel}: ${label}. ${detail} ${actionLabel}.`
  };
}
