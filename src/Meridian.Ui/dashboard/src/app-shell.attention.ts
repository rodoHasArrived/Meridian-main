import type { ShellStatusItem } from "@/app-shell.status-panel";
import type { AppShellTrustStripItem, AppShellTrustStripState, AppShellTrustStripTone } from "@/app-shell.trust-strip";
import type { AppShellWorkflowContinuityViewModel } from "@/app-shell.workflow-continuity-types";
import { selectDailyControlTowerQueueItems, selectLeadingDecisionItem } from "@/lib/daily-control-tower";
import { buildDegradedModeNotices } from "@/lib/degraded-mode";
import { pluralizeCount } from "@/lib/format";
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
  /** Issues beyond the one named in `title`, counting each queued decision. */
  moreCount: number;
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
  trustStrip,
  workflowContinuity,
  degradedMode = null
}: {
  loading: boolean;
  bootstrapFailed: boolean;
  failedItems: ShellStatusItem[];
  trustStrip: AppShellTrustStripState;
  workflowContinuity: AppShellWorkflowContinuityViewModel;
  degradedMode?: DegradedModeStatus | null;
}): AppShellAttentionState {
  const decisions = buildDecisionSignal(workflowContinuity);
  const providers = trustStrip.items.find((item) => item.id === "providers") ?? null;
  const source = trustStrip.items.find((item) => item.id === "source") ?? null;
  const host = buildHostSignal(degradedMode);

  // Display order doubles as the tie-break between equal tones: operator-owned decisions lead,
  // then the facts about whether the data can be relied on.
  const signals: AppShellAttentionSignal[] = [
    decisions.signal,
    ...(providers ? [fromTrustItem("providers", providers, `Providers: ${providers.value}`)] : []),
    ...(source ? [fromTrustItem("source", source, dataSourceHeadline(source))] : []),
    buildWorkspaceDataSignal({ loading, bootstrapFailed, failedItems }),
    ...(host ? [host] : [])
  ];

  const summary = summarizeStatusSignals(signals);
  // Each queued decision is its own issue; the decisions signal stands for all of them. A
  // provider warning or a failed workspace area is already queued as a decision, so its source
  // signal is not counted a second time and the total matches the queue count beside Today.
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
    signals,
    issues: summary.issues,
    sourceSignals: signals.filter((signal) => signal.kind === "source")
  };
}

function buildDecisionSignal(workflowContinuity: AppShellWorkflowContinuityViewModel): {
  signal: AppShellAttentionSignal;
  issueCount: number;
  coveredSignalIds: ReadonlySet<AppShellAttentionSignalId>;
} {
  const queue = selectDailyControlTowerQueueItems(workflowContinuity);
  const summary = summarizeStatusSignals(queue);
  const worst = summary.clear ? null : selectLeadingDecisionItem(workflowContinuity);

  if (!worst) {
    return {
      issueCount: 0,
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

  const others = summary.issues.length - 1;
  return {
    issueCount: summary.issues.length,
    coveredSignalIds: new Set(
      QUEUED_SOURCE_SIGNALS
        .filter(({ queueIdPrefix }) => summary.issues.some((item) => item.id.startsWith(queueIdPrefix)))
        .map(({ signalId }) => signalId)
    ),
    signal: {
      id: "decisions",
      kind: "workflow",
      label: "Decisions",
      value: others > 0 ? `${worst.label} · ${others} more` : worst.label,
      detail: worst.detail,
      tone: worst.tone,
      headline: worst.label,
      href: worst.route,
      actionLabel: worst.actionLabel
    }
  };
}

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
  failedItems
}: {
  loading: boolean;
  bootstrapFailed: boolean;
  failedItems: ShellStatusItem[];
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

  if (failedItems.length > 0) {
    const value = `${pluralizeCount(failedItems.length, "area")} did not load`;
    // Same classification the decision queue gives these failures: a workspace area that did not
    // load blocks the work that depends on it; a missing workflow catalog alone needs review.
    const workspaceAreaFailed = failedItems.some((item) => item.key !== WORKFLOW_CATALOG_FAILURE_KEY);
    return {
      ...base,
      value,
      detail: `Not loaded: ${failedItems.map((item) => item.label).join(", ")}. Available routes remain open.`,
      tone: workspaceAreaFailed ? "blocked" : "review",
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
