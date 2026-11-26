import { apiGetJson, type ApiRequestOptions } from "@/lib/api/http-client";
import { isApiError } from "@/lib/api-errors";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

/**
 * The signed-in operator's username from `GET /api/auth/me`, or `null` when no one is signed in.
 *
 * Use this, not the workstation session's `displayName`, wherever the browser needs to know *who*
 * is operating (client-side recovery keys, per-operator component state). The server fills
 * `displayName` with a desk label ("Meridian Operator", "<strategy> Desk"), so every operator on the
 * same desk shares it. Request bodies should not carry an actor at all: governed write routes take
 * the actor from the authenticated session and overwrite any client-supplied value.
 */
export async function getSignedInUsername(options: ApiRequestOptions = {}): Promise<string | null> {
  try {
    const caller = await apiGetJson<{ username?: unknown }>(UI_API_ROUTES.AuthApiMe, { ...options, allowDevelopmentFallback: false });
    return typeof caller.username === "string" && caller.username.trim() ? caller.username.trim() : null;
  } catch (error: unknown) {
    if (isApiError(error) && (error.status === 401 || error.status === 403 || error.status === 404)) return null;
    throw error;
  }
}
