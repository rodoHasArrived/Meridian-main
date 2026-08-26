import type { ActivationOutcome, FirstRunStatus } from "./types";

export interface GettingStartedStep {
  key: string;
  label: string;
  actionLabel: string;
  route: string;
  isComplete: boolean;
  completedAtUtc: string | null;
  /** The first outstanding step: the one the checklist offers as the next thing to do. */
  isNext: boolean;
}

export interface GettingStartedViewModel {
  /** False before setup is finished, or when the host reports no outcomes at all. */
  visible: boolean;
  completedCount: number;
  totalCount: number;
  triggerLabel: string;
  triggerAriaLabel: string;
  triggerTitle: string;
  finished: boolean;
  headline: string;
  summary: string;
  nextStep: GettingStartedStep | null;
  steps: GettingStartedStep[];
}

const EMPTY: GettingStartedViewModel = {
  visible: false,
  completedCount: 0,
  totalCount: 0,
  triggerLabel: "",
  triggerAriaLabel: "",
  triggerTitle: "",
  finished: false,
  headline: "",
  summary: "",
  nextStep: null,
  steps: []
};

/**
 * Builds the masthead getting-started checklist from host-owned activation evidence.
 *
 * The host decides what counts as done; this only decides what to show and which step to
 * offer next, so the checklist can never claim progress the host has not recorded.
 */
export function buildGettingStartedViewModel(status?: FirstRunStatus | null): GettingStartedViewModel {
  // Guard the shape as well as the flag: the shell renders this on every screen, so a malformed
  // status must degrade to a hidden chip rather than crash the masthead.
  if (!status?.isComplete || !Array.isArray(status.outcomes) || status.outcomes.length === 0) {
    return EMPTY;
  }

  const nextKey = status.outcomes.find((outcome) => !outcome.isComplete)?.key ?? null;
  const steps = status.outcomes.map((outcome) => toStep(outcome, nextKey));
  const completedCount = steps.filter((step) => step.isComplete).length;
  const totalCount = steps.length;
  const finished = completedCount === totalCount;
  const nextStep = steps.find((step) => step.isNext) ?? null;
  const triggerLabel = `Getting started ${completedCount}/${totalCount}`;

  return {
    visible: true,
    completedCount,
    totalCount,
    triggerLabel,
    triggerAriaLabel: finished
      ? `${triggerLabel} — every getting-started step is done`
      : `${triggerLabel} — next: ${nextStep?.label ?? ""}`,
    triggerTitle: "Activation is based on completed outcomes, not page visits",
    finished,
    headline: finished ? "You are set up" : "Getting started",
    summary: finished
      ? "Every getting-started step is done. This checklist stays here as a record of what Meridian saw you complete."
      : `${completedCount} of ${totalCount} done. Meridian marks a step complete when you finish it, not when you open the page.`,
    nextStep,
    steps
  };
}

function toStep(outcome: ActivationOutcome, nextKey: string | null): GettingStartedStep {
  return {
    key: outcome.key,
    label: outcome.label,
    actionLabel: outcome.actionLabel,
    route: outcome.route,
    isComplete: outcome.isComplete,
    completedAtUtc: outcome.completedAtUtc,
    isNext: outcome.key === nextKey
  };
}
