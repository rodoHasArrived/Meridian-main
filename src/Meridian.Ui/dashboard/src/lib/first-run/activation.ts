// Activation outcomes are the "getting started" checklist the host keeps for each user.
// The host only auto-completes `workspace-opened` (and `data-imported` for sample
// workspaces); every other outcome has to be reported by the surface that actually
// performs the work, which is what `recordActivationOutcome` does. Without that the
// masthead counter can never move past its seeded value.
//
// The cached status keeps this honest in two directions: an outcome the host already
// recorded is never re-posted (which would overwrite its original completion time), and
// a successful post is published so the masthead re-renders without a page reload.
import { apiPostJson } from "@/lib/api";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type { FirstRunStatus } from "@/features/first-run/types";

export const ACTIVATION_OUTCOME_KEYS = {
  workspaceOpened: "workspace-opened",
  dataImported: "data-imported",
  validationResolved: "validation-resolved",
  reportRun: "report-run",
  resultSaved: "result-saved"
} as const;

export type ActivationOutcomeKey =
  (typeof ACTIVATION_OUTCOME_KEYS)[keyof typeof ACTIVATION_OUTCOME_KEYS];

type ActivationListener = (status: FirstRunStatus) => void;

const listeners = new Set<ActivationListener>();
let cachedStatus: FirstRunStatus | null = null;
let inFlight = new Map<ActivationOutcomeKey, Promise<void>>();

/** Seed the cache from the shell's own first-run fetch so no redundant post is sent. */
export function primeActivationProgress(status: FirstRunStatus | null): void {
  cachedStatus = status;
}

export function subscribeToActivationProgress(listener: ActivationListener): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function isAlreadyComplete(key: ActivationOutcomeKey): boolean {
  return cachedStatus?.outcomes.some((outcome) => outcome.key === key && outcome.isComplete) ?? false;
}

/**
 * Report that the user finished an activation outcome. Fire-and-forget by contract: a
 * failure here must never surface as an error on the workflow the user just completed,
 * so the promise always resolves.
 */
export function recordActivationOutcome(key: ActivationOutcomeKey): Promise<void> {
  if (isAlreadyComplete(key)) {
    return Promise.resolve();
  }

  const pending = inFlight.get(key);
  if (pending) {
    return pending;
  }

  const request = apiPostJson<FirstRunStatus>(WORKSTATION_API_ENDPOINTS.firstRunOutcomeComplete, { key })
    .then((status) => {
      cachedStatus = status;
      for (const listener of listeners) {
        listener(status);
      }
    })
    .catch(() => {
      // Activation evidence is advisory; the completed workflow stands either way.
    })
    .finally(() => {
      inFlight.delete(key);
    });

  inFlight.set(key, request);
  return request;
}

/** Test seam: drop cached status, subscribers, and in-flight posts. */
export function resetActivationProgressForTests(): void {
  cachedStatus = null;
  listeners.clear();
  inFlight = new Map();
}
