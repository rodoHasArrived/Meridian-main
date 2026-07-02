// Resolves which workflow action owns a workstation route, so saved views can
// attach to the existing workflow preset system. Longest route-prefix match wins.
import type { WorkflowAction, WorkflowDefinition, WorkflowLibrary } from "@/types";

export interface WorkflowRouteMatch {
  workflow: WorkflowDefinition;
  action: WorkflowAction;
}

export function findWorkflowActionForRoute(
  workflowLibrary: WorkflowLibrary | null | undefined,
  pathname: string
): WorkflowRouteMatch | null {
  if (!workflowLibrary || !pathname) {
    return null;
  }

  let best: { match: WorkflowRouteMatch; specificity: number } | null = null;
  for (const workflow of workflowLibrary.workflows) {
    for (const action of workflow.actions) {
      const specificity = routeMatchSpecificity(action, pathname);
      if (specificity > (best?.specificity ?? 0)) {
        best = { match: { workflow, action }, specificity };
      }
    }
  }

  return best?.match ?? null;
}

function routeMatchSpecificity(action: WorkflowAction, pathname: string): number {
  const prefixes = action.routePrefixes ?? [];
  let specificity = 0;
  for (const prefix of prefixes) {
    const normalized = prefix.trim();
    if (!normalized) {
      continue;
    }

    if (pathname === normalized || pathname.startsWith(`${normalized.replace(/\/$/, "")}/`)) {
      specificity = Math.max(specificity, normalized.length);
    } else if (pathname.startsWith(normalized)) {
      specificity = Math.max(specificity, normalized.length);
    }
  }

  for (const fragment of action.routeContains ?? []) {
    const contains = fragment.trim();
    if (contains && pathname.includes(contains)) {
      // Substring matches are weaker than any prefix match of the same length.
      specificity = Math.max(specificity, Math.max(1, contains.length - 1));
    }
  }

  return specificity;
}
