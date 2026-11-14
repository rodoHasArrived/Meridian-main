// The scope a figures strip was counted over. Kept beside the other operating-scope rules so the
// strip, the scope bar and Today word an unnarrowed scope the same way.
import { OPERATING_SCOPE_EMPTY_SUMMARY } from "@/app-shell.operating-scope";
import { resolveFigureScope, type FigureSourceKey, type WorkspaceFigureContextValue } from "@/lib/workspace-figure-context";

/** The scope label for a payload loaded for every authorized fund account. */
export const ALL_AUTHORIZED_ACCOUNTS_SCOPE = "All authorized accounts";

/**
 * The scope a strip's figures were counted over, most specific evidence first:
 *
 * 1. the screen's own `scope`, when it passes one;
 * 2. the fund account the supplying payload was actually loaded for (the selection can change
 *    before that payload reloads), or "All authorized accounts" when it was loaded for every one;
 * 3. for a payload the shell loads without any scope filter, "All authorized scopes" — not the
 *    operator's selection, which those figures are not narrowed by;
 * 4. otherwise the scope the shell applies on this route;
 *
 * and null outside the shell, where nothing is known.
 *
 * Entity and fund follow the same rule as every other dimension: the route label (4) names only
 * what the route applies. Entity is applied on Accounting, which reads `entityId` from its URL, so
 * a figure there can read "Entity … · Account …"; Fund is applied nowhere yet, so no figure label
 * claims a fund narrowing that its payload was not loaded with. Rules 2 and 3 are keyed to the
 * fund account a payload was loaded for and never borrow a selected entity or fund.
 */
export function resolveStatStripScope({
  scope,
  figureSource,
  workspace,
  shellScopeLabel
}: {
  scope?: string | null;
  figureSource?: FigureSourceKey;
  workspace: WorkspaceFigureContextValue | null;
  shellScopeLabel: string | null;
}): string | null {
  const explicit = scope?.trim();
  if (explicit) {
    return explicit;
  }

  if (figureSource && workspace) {
    const loadedFor = resolveFigureScope(workspace, figureSource);
    if (loadedFor !== undefined) {
      return loadedFor ? `Account ${loadedFor}` : ALL_AUTHORIZED_ACCOUNTS_SCOPE;
    }
    return OPERATING_SCOPE_EMPTY_SUMMARY;
  }

  return shellScopeLabel;
}
