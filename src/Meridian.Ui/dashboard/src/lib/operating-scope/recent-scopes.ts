// Recently applied operating scopes (C1), newest first, so the scope picker can offer the last few
// back in one click. Per-browser convenience only: the operator's default scope lives on the server
// (operator scope preference) so it follows them to other machines and to WPF.
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";
import { getLocalStorage } from "@/lib/theme";
import { compactOperatingScope, hasOperatingScopeValues, parseStoredOperatingScope } from "@/lib/operating-scope/persistence";

export const RECENT_SCOPES_STORAGE_KEY = "meridian.workstation.operatingScope.recent.v1";
export const RECENT_SCOPES_LIMIT = 5;

const SCOPE_FIELDS = ["symbol", "fundAccountId", "provider", "runId", "from", "to", "date", "asOf"] as const;

/** A stable identity for a scope, so the same scope applied twice is remembered once. */
export function operatingScopeKey(scope: AppShellOperatingScopeInput): string {
  const compacted = compactOperatingScope(scope);
  return SCOPE_FIELDS.map((field) => `${field}=${compacted[field] ?? ""}`).join("&");
}

export function readRecentScopes(storage: Storage | null = getLocalStorage()): AppShellOperatingScopeInput[] {
  try {
    const raw = storage?.getItem(RECENT_SCOPES_STORAGE_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    if (!Array.isArray(parsed)) {
      return [];
    }
    return parsed
      .map((entry) => parseStoredOperatingScope(JSON.stringify(entry)))
      .filter(hasOperatingScopeValues)
      .slice(0, RECENT_SCOPES_LIMIT);
  } catch {
    return [];
  }
}

/** Puts a just-applied scope first, drops its older copy, and keeps the last few. */
export function rememberRecentScope(
  scope: AppShellOperatingScopeInput,
  current: readonly AppShellOperatingScopeInput[],
  storage: Storage | null = getLocalStorage()
): AppShellOperatingScopeInput[] {
  const compacted = compactOperatingScope(scope);
  if (!hasOperatingScopeValues(compacted)) {
    return [...current];
  }
  const key = operatingScopeKey(compacted);
  const next = [compacted, ...current.filter((entry) => operatingScopeKey(entry) !== key)].slice(0, RECENT_SCOPES_LIMIT);
  try {
    storage?.setItem(RECENT_SCOPES_STORAGE_KEY, JSON.stringify(next));
  } catch {
    // Recent scopes are a per-browser convenience; blocked storage keeps them for this session.
  }
  return next;
}

/** "AAPL · fund 1111…1111 · alpaca · 2026-06-01 to 2026-06-30" for a picker button. */
export function describeOperatingScope(scope: AppShellOperatingScopeInput): string {
  const parts: string[] = [];
  if (scope.symbol) parts.push(scope.symbol);
  if (scope.fundAccountId) {
    const id = scope.fundAccountId;
    parts.push(`fund ${id.length > 12 ? `${id.slice(0, 4)}…${id.slice(-4)}` : id}`);
  }
  if (scope.provider) parts.push(scope.provider);
  if (scope.runId) parts.push(`run ${scope.runId}`);
  if (scope.from || scope.to) parts.push(`${scope.from ?? "…"} to ${scope.to ?? "…"}`);
  if (scope.date) parts.push(scope.date);
  if (scope.asOf) parts.push(`as of ${scope.asOf}`);
  return parts.length > 0 ? parts.join(" · ") : "No scope";
}
