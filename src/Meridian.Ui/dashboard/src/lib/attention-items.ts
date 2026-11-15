/**
 * The workstation's one attention list.
 *
 * Every surface that says "this many things need an operator" — the sidebar badges, the masthead
 * pill, and Today's queue and its Source filter — reads the list built here and counts it with
 * {@link needsAttention}. A surface may filter the list (one workspace, one source); none of them
 * counts with a rule of its own, so the numbers add up by construction.
 */
import type { AppShellOperatorFocusItem } from "@/app-shell.workflow-continuity-types";
import { isActiveTriageSnooze, routeForTriageItem, type OperatorTriageItem } from "@/lib/operator-inbox-triage";
import { normalizeWorkspacePath, WORKSPACES } from "@/lib/workspace";
import type { WorkspaceKey } from "@/types";

/** Where an attention item comes from. */
export type AttentionItemSource = "workspace" | "inbox";

/**
 * `blocked` stops work, `action` needs an operator, `info` is visible but not yet a call to act
 * (still loading, or informational), `clear` is done.
 */
export type AttentionSeverity = "blocked" | "action" | "info" | "clear";

export interface AttentionItem {
  /** Unique across both sources; also the Today queue's row id. */
  key: string;
  /** The source's own id: the workspace signal id or the inbox work item id. */
  id: string;
  source: AttentionItemSource;
  sourceLabel: string;
  label: string;
  detail: string;
  severity: AttentionSeverity;
  /** The assigned operator. Workspace signals are not assignable, so theirs is always null. */
  owner: string | null;
  /** ISO due time, when the source tracks one. */
  dueAt: string | null;
  /** ISO time the item was observed, when known. */
  observedAt: string | null;
  /** True while an operator's snooze on the item is still running. */
  snoozed: boolean;
  workspaceKey: WorkspaceKey | null;
  workspaceLabel: string;
  /** Where the operator acts on the item. */
  route: string | null;
}

export const ATTENTION_SOURCE_LABELS: Record<AttentionItemSource, string> = {
  workspace: "Workspace signal",
  inbox: "Shared inbox"
};

/** The count rule, in the words every surface that explains its count uses. */
export const ATTENTION_COUNT_RULE = "blocked + action, excluding snoozed";

/** The one count rule: blocked and action items, excluding snoozed ones. */
export function needsAttention(item: Pick<AttentionItem, "severity" | "snoozed">): boolean {
  return (item.severity === "blocked" || item.severity === "action") && !item.snoozed;
}

const SEVERITY_RANK: Record<AttentionSeverity, number> = { blocked: 0, action: 1, info: 2, clear: 3 };

/** 0 blocked, 1 action, 2 info, 3 clear. */
export function attentionSeverityRank(severity: AttentionSeverity): number {
  return SEVERITY_RANK[severity];
}

const WORKSPACE_SIGNAL_SEVERITY: Record<AppShellOperatorFocusItem["tone"], AttentionSeverity> = {
  blocked: "blocked",
  review: "action",
  pending: "info",
  ready: "clear"
};

const INBOX_SEVERITY: Record<OperatorTriageItem["tone"], AttentionSeverity> = {
  Critical: "blocked",
  Warning: "action",
  Info: "info",
  Success: "clear"
};

export function attentionItemKey(source: AttentionItemSource, id: string): string {
  return `${source}:${id}`;
}

export function attentionItemFromWorkspaceSignal(item: AppShellOperatorFocusItem): AttentionItem {
  return {
    key: attentionItemKey("workspace", item.id),
    id: item.id,
    source: "workspace",
    sourceLabel: ATTENTION_SOURCE_LABELS.workspace,
    label: item.label,
    detail: item.detail,
    severity: WORKSPACE_SIGNAL_SEVERITY[item.tone],
    owner: null,
    dueAt: null,
    observedAt: null,
    snoozed: false,
    workspaceKey: resolveWorkspaceKey(item.route, item.workspaceLabel),
    workspaceLabel: item.workspaceLabel,
    route: item.route
  };
}

export function attentionItemFromInbox(item: OperatorTriageItem, now = Date.now()): AttentionItem {
  const route = routeForTriageItem(item);
  return {
    key: attentionItemKey("inbox", item.workItemId),
    id: item.workItemId,
    source: "inbox",
    sourceLabel: ATTENTION_SOURCE_LABELS.inbox,
    label: item.label,
    detail: item.detail,
    severity: INBOX_SEVERITY[item.tone],
    owner: item.ownerId,
    dueAt: item.dueAt,
    observedAt: item.createdAt,
    snoozed: isActiveTriageSnooze(item, now),
    workspaceKey: resolveWorkspaceKey(route, item.workspace),
    workspaceLabel: item.workspace ?? "Unspecified",
    route
  };
}

const WORK_ITEM_SIGNAL_PREFIX = "work-item:";

/**
 * True when a workspace signal and a loaded shared-inbox item are the same operator work item.
 * The inbox item then stands for both: it carries the owner, due time and snooze, and the work
 * item is listed and counted once.
 */
export function isSignalInInbox(signalId: string, inboxItems: readonly OperatorTriageItem[] | null | undefined): boolean {
  if (!inboxItems?.length || !signalId.startsWith(WORK_ITEM_SIGNAL_PREFIX)) {
    return false;
  }
  const workItemId = signalId.slice(WORK_ITEM_SIGNAL_PREFIX.length);
  return inboxItems.some((item) => item.workItemId === workItemId);
}

/**
 * The attention list: every workspace signal that is not also a shared-inbox item, then every
 * shared-inbox item. `inboxItems` is null while the shared inbox is loading or unavailable; the
 * list is then the workspace signals alone, on every surface at once.
 */
export function buildAttentionItems(
  workspaceSignals: readonly AppShellOperatorFocusItem[],
  inboxItems: readonly OperatorTriageItem[] | null | undefined,
  now = Date.now()
): AttentionItem[] {
  return [
    ...workspaceSignals.filter((signal) => !isSignalInInbox(signal.id, inboxItems)).map(attentionItemFromWorkspaceSignal),
    ...(inboxItems ?? []).map((item) => attentionItemFromInbox(item, now))
  ];
}

export interface AttentionCounts {
  /** Every item that needs attention: the count beside Today and in the queue's "All" filter. */
  total: number;
  bySource: Record<AttentionItemSource, number>;
  /** The same items split by owning workspace. An item with no known workspace counts in `total` only. */
  byWorkspace: Partial<Record<WorkspaceKey, number>>;
  /** The worst severity among each workspace's counted items. */
  severityByWorkspace: Partial<Record<WorkspaceKey, "blocked" | "action">>;
}

/** Counts a list with the one rule. Pass a filtered list to count a filtered view. */
export function countAttention(items: readonly AttentionItem[]): AttentionCounts {
  const counts: AttentionCounts = {
    total: 0,
    bySource: { workspace: 0, inbox: 0 },
    byWorkspace: {},
    severityByWorkspace: {}
  };
  for (const item of items) {
    if (!needsAttention(item)) {
      continue;
    }
    counts.total += 1;
    counts.bySource[item.source] += 1;
    if (item.workspaceKey) {
      counts.byWorkspace[item.workspaceKey] = (counts.byWorkspace[item.workspaceKey] ?? 0) + 1;
      if (item.severity === "blocked" || !counts.severityByWorkspace[item.workspaceKey]) {
        counts.severityByWorkspace[item.workspaceKey] = item.severity === "blocked" ? "blocked" : "action";
      }
    }
  }
  return counts;
}

function resolveWorkspaceKey(route: string | null, workspaceLabel: string | null | undefined): WorkspaceKey | null {
  const pathname = (route ?? "").split(/[?#]/, 1)[0].replace(/^\/workstation(?=\/)/, "");
  const segment = pathname.split("/").filter(Boolean)[0];
  if (segment) {
    const key = normalizeWorkspacePath(pathname);
    // normalizeWorkspacePath falls back to Trading for an unknown root; only trust a real match.
    if (key !== "trading" || segment === "trading") {
      return key;
    }
  }
  const label = (workspaceLabel ?? "").trim().toLowerCase();
  return WORKSPACES.find((workspace) => workspace.label.toLowerCase() === label)?.key ?? null;
}
