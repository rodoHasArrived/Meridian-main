/**
 * The signed-in operator, read from the caller's own auth profile.
 *
 * Use this, not the workstation session's `displayName`, wherever the browser needs to know *who* is
 * acting (recovery keys, assignee defaults, request bodies). The server fills `displayName` with a desk
 * label ("Meridian Operator", "<strategy> Desk") that changes with the latest strategy run.
 *
 * Governed-write routes still derive the recorded actor from the session server-side; a username sent
 * in a body is never trusted as identity.
 */

import { apiGetJson } from "@/lib/api";
import { isApiError } from "@/lib/api-errors";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

/**
 * The signed-in operator's username from `/api/auth/me`. Null without a named session (401, 403 or
 * 404, e.g. an anonymous development host), so callers simply treat the operator as unknown.
 */
export async function getSignedInUsername(signal?: AbortSignal): Promise<string | null> {
  try {
    const caller = await apiGetJson<{ username?: unknown }>(UI_API_ROUTES.AuthApiMe, { signal });
    return typeof caller.username === "string" && caller.username.trim() ? caller.username.trim() : null;
  } catch (error: unknown) {
    if (isApiError(error) && (error.status === 401 || error.status === 403 || error.status === 404)) return null;
    throw error;
  }
}
