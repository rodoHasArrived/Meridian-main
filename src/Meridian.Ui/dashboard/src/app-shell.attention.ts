import type { ShellStatusItem } from "@/app-shell.status-panel";
import type { AppShellTrustStripItem, AppShellTrustStripState, AppShellTrustStripTone } from "@/app-shell.trust-strip";
import type { AppShellWorkflowContinuityViewModel } from "@/app-shell.workflow-continuity-types";
import { attentionSeverityRank, buildAttentionItems, needsAttention, type AttentionItem } from "@/lib/attention-items";
import { selectDailyControlTowerQueueItems, selectLeadingDecisionItem } from "@/lib/daily-control-tower";
import { buildDegradedModeNotices } from "@/lib/degraded-mode";
import { pluralizeCount } from "@/lib/format";
import type { OperatorTriageItem } from "@/lib/operator-inbox-triage";
import { summarizeStatusSignals, type StatusSignalCounts } from "@/lib/status-signals";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { DegradedModeStatus } from "@/types";

export type AppShellAttentionSignalId = "decisions" | "providers" | "source" | "workspace-data" | "host";

/**
 * One workstation-wide status fact. `source` facts describe whether the data on screen can be
 * relied on, so every page status row repeats the ones that are issues; `workflow` facts are
 * operator work that belongs to the queue on Today.
 */
export interface AppShellAttentionSignal {
  id: AppShellAttentionSignalId;
  kind: "source" | "workflow";
  label: string;
  value: string;
  detail: string;
  tone: AppShellTrustStripTone;
  /** How the masthead and footer name this signal when it is the worst issue. */
  headline: string;
  href: string | null;
  actionLabel: string | null;
}

/**
 * The workstation's single attention state. The masthead pill, the footer, every page status row
 * and Today read this one object, so they agree by construction.
 */
export interface AppShellAttentionState {
  tone: AppShellTrustStripTone;
  clear: boolean;
  /** Short text status for the worst tone: Blocked, Review, Checking or All clear. */
  statusLabel: string;
  /** The worst issue's headline, or "No open issues". */
  title: string;
  /** Issues beyond the one named in `title`, counting each attention item. */
  moreCount: number;
  /**
   * The attention list every count reads: the queue's workspace signals plus the shared inbox.
   * Count it with `needsAttention`; the sidebar badges and Today's queue are filtered views of it.
   */
  items: AttentionItem[];
  counts: StatusSignalCounts;
  /** The pill always opens Today, where every signal is listed. */
  href: string;
  ariaLabel: string;
  /** Every signal, ready ones included, in display order. */
  signals: AppShellAttentionSignal[];
  /** Signals that are not ready, worst first. */
  issues: AppShellAttentionSignal[];
  /** The source-kind signals, for page status rows. */
  sourceSignals: AppShellAttentionSignal[];
}

export const APP_SHELL_TODAY_ROUTE = "/";

const WORKFLOW_CATALOG_FAILURE_KEY = "workflow-catalog";

/** What the bootstrap requests carry, as the operator would name it. */
const BOOTSTRAP_REQUEST_LABEL = "Session, host status or shared counts";

/** Source signals whose issues the decision queue also lists, by the queue item id prefix. */
const QUEUED_SOURCE_SIGNALS: ReadonlyArray<{ signalId: AppShellAttentionSignalId; queueIdPrefix: string }> = [
  { signalId: "providers", queueIdPrefix: "provider:" },
  { signalId: "workspace-data", queueIdPrefix: "workspace-error:" }
];

const STATUS_LABEL: Record<AppShellTrustStripTone, string> = {
  blocked: "Blocked",
  review: "Review",
  pending: "Checking",
  ready: "All clear"
};

export function buildAppShellAttentionState({
  loading,
  bootstrapFailed,
  failedItems,
  error = null,
  trustStrip,
  workflowContinuity,
  degradedMode = null,
  inboxItems = null
}: {
  loading: boolean;
  bootstrapFailed: boolean;
  failedItems: ShellStatusItem[];
  /**
   * The data hook's global request error: set when a workspace area or a bootstrap request
   * (session, host status, shared counts) failed. The footer's Sync item reads the same value.
   */
  error?: string | null;
  trustStrip: AppShellTrustStripState;
  workflowContinuity: AppShellWorkflowContinuityViewModel;
  degradedMode?: DegradedModeStatus | null;
  /** The shared operator inbox, or null while it is loading or unavailable. */
  inboxItems?: readonly OperatorTriageItem[] | null;
}): AppShellAttentionState {
  const decisions = buildDecisionSignal(workflowContinuity, inboxItems);
  const providers = trustStrip.items.find((item) => item.id === "providers") ?? null;
  const source = trustStrip.items.find((item) => item.id === "source") ?? null;
  const host = buildHostSignal(degradedMode);

  // Display order doubles as the tie-break between equal tones: operator-owned decisions lead,
  // then the facts about whether the data can be relied on.
  const signals: AppShellAttentionSignal[] = [
    decisions.signal,
    ...(providers ? [fromTrustItem("providers", providers, `Providers: ${providers.value}`)] : []),
    ...(source ? [fromTrustItem("source", source, dataSourceHeadline(source))] : []),
    buildWorkspaceDataSignal({ loading, bootstrapFailed, failedItems, error }),
    ...(host ? [host] : [])
  ];

  const summary = summarizeStatusSignals(signals);
  // Each attention item is its own issue; the decisions signal stands for all of them. A
  // provider warning or a failed workspace area is already queued as a decision, so its source
  // signal is not counted a second time and the total matches the count beside Today.
  const sourceIssuesOutsideQueue = summary.issues.filter(
    (signal) => signal.id !== "decisions" && !decisions.coveredSignalIds.has(signal.id)
  ).length;
  const issueCount = decisions.issueCount + sourceIssuesOutsideQueue;
  const moreCount = Math.max(issueCount - 1, 0);
  const statusLabel = STATUS_LABEL[summary.tone];
  const title = summary.worst?.headline ?? "No open issues";

  return {
    tone: summary.tone,
    clear: summary.clear,
    statusLabel,
    title,
    moreCount,
    counts: summary.counts,
    href: APP_SHELL_TODAY_ROUTE,
    ariaLabel: summary.clear
      ? "Workstation attention: All clear. No open issues. Open Today."
      : `Workstation attention: ${statusLabel}. ${title}.${moreCount > 0 ? ` ${pluralizeCount(moreCount, "more issue")}.` : ""} Open Today.`,
    items: decisions.items,
    signals,
    issues: summary.issues,
    sourceSignals: signals.filter((signal) => signal.kind === "source")
  };
}

function buildDecisionSignal(
  workflowContinuity: AppShellWorkflowContinuityViewModel,
  inboxItems: readonly OperatorTriageItem[] | null
): {
  signal: AppShellAttentionSignal;
  items: AttentionItem[];
  issueCount: number;
  coveredSignalIds: ReadonlySet<AppShellAttentionSignalId>;
} {
  const queue = selectDailyControlTowerQueueItems(workflowContinuity);
  const items = buildAttentionItems(queue, inboxItems);
  // The one count rule: blocked and action items, excluding snoozed.
  const issueCount = items.filter(needsAttention).length;
  const summary = summarizeStatusSignals(queue);
  const worstSignal = summary.clear ? null : selectLeadingDecisionItem(workflowContinuity);
  // A shared-inbox item leads only when it is strictly worse than every workspace signal, so the
  // pill and Today's headline keep naming the same decision otherwise.
  const worstInbox = items
    .filter((item) => item.source === "inbox" && needsAttention(item))
    .sort((left, right) => attentionSeverityRank(left.severity) - attentionSeverityRank(right.severity))[0] ?? null;
  const worstSignalRank = worstSignal ? SIGNAL_TONE_RANK[worstSignal.tone] : Number.POSITIVE_INFINITY;
  const inboxLeads = worstInbox !== null && attentionSeverityRank(worstInbox.severity) < worstSignalRank;

  if (!worstSignal && !inboxLeads) {
    return {
      items,
      issueCount,
      coveredSignalIds: new Set(),
      signal: {
        id: "decisions",
        kind: "workflow",
        label: "Decisions",
        value: "No open decisions",
        detail: "No operator decision is queued for the loaded workspaces.",
        tone: "ready",
        headline: "No open decisions",
        href: null,
        actionLabel: null
      }
    };
  }

  const coveredSignalIds = new Set(
    QUEUED_SOURCE_SIGNALS
      .filter(({ queueIdPrefix }) => summary.issues.some((item) => item.id.startsWith(queueIdPrefix)))
      .map(({ signalId }) => signalId)
  );
  const leading = inboxLeads && worstInbox
    ? {
        label: worstInbox.label,
        detail: worstInbox.detail,
        tone: worstInbox.severity === "blocked" ? "blocked" as const : "review" as const,
        href: worstInbox.route ?? APP_SHELL_TODAY_ROUTE,
        actionLabel: worstInbox.route ? "Open in workspace" : "Open Today",
        counted: true
      }
    : {
        label: worstSignal!.label,
        detail: worstSignal!.detail,
        tone: worstSignal!.tone,
        href: worstSignal!.route,
        actionLabel: worstSignal!.actionLabel,
        // Counted unless it is informational, or its shared-inbox twin is snoozed.
        counted: items.some((item) => needsAttention(item)
          && (item.id === worstSignal!.id || `work-item:${item.id}` === worstSignal!.id))
      };
  const others = issueCount - (leading.counted ? 1 : 0);
  return {
    items,
    issueCount,
    coveredSignalIds,
    signal: {
      id: "decisions",
      kind: "workflow",
      label: "Decisions",
      value: others > 0 ? `${leading.label} · ${others} more` : leading.label,
      detail: leading.detail,
      tone: leading.tone,
      headline: leading.label,
      href: leading.href,
      actionLabel: leading.actionLabel
    }
  };
}

/** A workspace signal's tone on the attention-severity scale (0 blocked … 3 clear). */
const SIGNAL_TONE_RANK: Record<AppShellTrustStripTone, number> = { blocked: 0, review: 1, pending: 2, ready: 3 };

function fromTrustItem(
  id: "providers" | "source",
  item: AppShellTrustStripItem,
  headline: string
): AppShellAttentionSignal {
  return {
    id,
    kind: "source",
    label: item.label,
    value: item.value,
    detail: item.detail,
    tone: item.tone,
    headline,
    href: item.href,
    actionLabel: item.actionLabel
  };
}

function dataSourceHeadline(source: AppShellTrustStripItem): string {
  if (source.tone === "blocked") {
    return `${source.value} data in a LIVE session`;
  }
  return source.tone === "review" ? "Data source unverified" : `${source.label}: ${source.value}`;
}

function buildWorkspaceDataSignal({
  loading,
  bootstrapFailed,
  failedItems,
  error
}: {
  loading: boolean;
  bootstrapFailed: boolean;
  failedItems: ShellStatusItem[];
  error: string | null;
}): AppShellAttentionSignal {
  const base = { id: "workspace-data", kind: "source", label: "Workspace data" } as const;

  if (loading) {
    return {
      ...base,
      value: "Loading",
      detail: "Session state and workspace data are still loading.",
      tone: "pending",
      headline: "Workspace data loading",
      href: null,
      actionLabel: null
    };
  }

  if (bootstrapFailed) {
    return {
      ...base,
      value: "Unavailable",
      detail: "Meridian could not load workspace data. Retry, or open diagnostics.",
      tone: "blocked",
      headline: "Workspace data unavailable",
      href: WORKSTATION_ROUTE_CATALOG.settingsBackendCapabilityCoverage,
      actionLabel: "Open diagnostics"
    };
  }

  const workspaceAreaFailed = failedItems.some((item) => item.key !== WORKFLOW_CATALOG_FAILURE_KEY);
  // The global error is set by a failed workspace area or a failed bootstrap request. With no
  // failed workspace area to explain it, a bootstrap request failed while the workspaces loaded:
  // session, host status or shared counts are missing or retained from an earlier refresh. That
  // is not "loaded", and the footer's Sync item already reports it as an error.
  const bootstrapRequestFailed = Boolean(error) && !workspaceAreaFailed;

  if (failedItems.length > 0 || bootstrapRequestFailed) {
    const notLoaded = [
      ...failedItems.map((item) => item.label),
      ...(bootstrapRequestFailed ? [BOOTSTRAP_REQUEST_LABEL] : [])
    ];
    const value = `${pluralizeCount(notLoaded.length, "area")} did not load`;
    return {
      ...base,
      value,
      detail: `Not loaded: ${notLoaded.join(", ")}. ${
        bootstrapRequestFailed ? "Those figures may be missing or retained from an earlier refresh. " : ""
      }Available routes remain open.`,
      // Same classification the decision queue gives these failures: a workspace area that did
      // not load blocks the work that depends on it, and so does a failed bootstrap request; a
      // missing workflow catalog alone needs review.
      tone: workspaceAreaFailed || bootstrapRequestFailed ? "blocked" : "review",
      headline: `Workspace data: ${value}`,
      href: WORKSTATION_ROUTE_CATALOG.settingsBackendCapabilityCoverage,
      actionLabel: "Review diagnostics"
    };
  }

  return {
    ...base,
    value: "Loaded",
    detail: "Every requested workspace area loaded.",
    tone: "ready",
    headline: "Workspace data loaded",
    href: null,
    actionLabel: null
  };
}

function buildHostSignal(degradedMode: DegradedModeStatus | null): AppShellAttentionSignal | null {
  const notices = buildDegradedModeNotices(degradedMode);
  if (notices.length === 0) {
    return null;
  }

  const value = notices.map((notice) => sentenceCase(notice.title)).join(" · ");
  return {
    id: "host",
    kind: "source",
    label: "Host",
    value,
    detail: notices.map((notice) => notice.detail).join(" "),
    // Matches the red degraded-mode banner, which reads the same notices.
    tone: "blocked",
    headline: `Host: ${value}`,
    href: null,
    actionLabel: null
  };
}

function sentenceCase(value: string): string {
  const lower = value.toLowerCase();
  return lower.charAt(0).toUpperCase() + lower.slice(1);
}
