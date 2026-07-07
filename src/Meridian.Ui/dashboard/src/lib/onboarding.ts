import { getLocalStorage } from "@/lib/theme";

// Persisted "first 10 minutes" onboarding progress, following the
// meridian.workstation.<feature>.vN convention. Steps are marked complete when
// the operator actually visits the corresponding route, so progress reflects
// real actions on the real UI rather than a checklist they clicked through.
export const ONBOARDING_STORAGE_KEY = "meridian.workstation.onboarding.v1";

export interface OnboardingState {
  version: 1;
  /** Ids of tour steps the operator has completed (by visiting their route). */
  completedStepIds: string[];
  /** True once the operator dismisses the tour; hides the coach-mark and ring. */
  dismissed: boolean;
}

export function emptyOnboardingState(): OnboardingState {
  return { version: 1, completedStepIds: [], dismissed: false };
}

function normalizeState(value: unknown): OnboardingState {
  if (typeof value !== "object" || value === null) {
    return emptyOnboardingState();
  }
  const source = value as Record<string, unknown>;
  const completedStepIds = Array.isArray(source.completedStepIds)
    ? [...new Set(source.completedStepIds.filter((id): id is string => typeof id === "string" && id.length > 0))]
    : [];
  return {
    version: 1,
    completedStepIds,
    dismissed: source.dismissed === true
  };
}

export function readOnboardingState(storage: Storage | null = getLocalStorage()): OnboardingState {
  try {
    const raw = storage?.getItem(ONBOARDING_STORAGE_KEY);
    return raw ? normalizeState(JSON.parse(raw) as unknown) : emptyOnboardingState();
  } catch {
    return emptyOnboardingState();
  }
}

export function writeOnboardingState(state: OnboardingState, storage: Storage | null = getLocalStorage()) {
  try {
    storage?.setItem(ONBOARDING_STORAGE_KEY, JSON.stringify(normalizeState(state)));
  } catch {
    // Onboarding persistence is best-effort when browser storage is blocked.
  }
}

/** Marks a step complete (idempotent), returning a new state (or the same reference). */
export function withCompletedStep(state: OnboardingState, stepId: string): OnboardingState {
  if (state.completedStepIds.includes(stepId)) {
    return state;
  }
  return { ...state, completedStepIds: [...state.completedStepIds, stepId] };
}
