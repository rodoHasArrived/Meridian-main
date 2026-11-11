import type { CreateOnboardingWorkspaceRequest } from "@/types/accounting-onboarding";
/** Display-only intent metadata: not a replayable request and never approval authority. */
export interface OnboardingCreationIntent { intentId: string; name: string; scope: CreateOnboardingWorkspaceRequest["scope"]; requestedAt: string }
const key = (actorKey: string) => `meridian.onboarding.creation-review.v1:${encodeURIComponent(actorKey)}`;
export function readOnboardingCreationIntent(actorKey: string): OnboardingCreationIntent | null {
  try {
    const value = JSON.parse(sessionStorage.getItem(key(actorKey)) ?? "null") as OnboardingCreationIntent | null;
    return value && typeof value.name === "string" && typeof value.scope?.ledgerBookId === "string"
      && typeof value.scope.entityId === "string" && Array.isArray(value.scope.accountIds) ? value : null;
  } catch { return null; }
}
export function retainOnboardingCreationIntent(actorKey: string, intent: OnboardingCreationIntent | null, expectedIntentId?: string): boolean {
  try {
    if (intent) sessionStorage.setItem(key(actorKey), JSON.stringify(intent));
    else if (!expectedIntentId || readOnboardingCreationIntent(actorKey)?.intentId === expectedIntentId) sessionStorage.removeItem(key(actorKey));
    return true;
  } catch { return false; }
}
