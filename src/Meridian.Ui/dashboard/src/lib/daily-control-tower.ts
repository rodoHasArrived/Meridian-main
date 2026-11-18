import type {
  AppShellDecisionBrief,
  AppShellEvidenceTimelineItem,
  AppShellLinkedContextItem,
  AppShellOperatorFocusItem,
  AppShellTrustStripItem,
  AppShellTrustStripState,
  AppShellWorkflowContinuityStatusTone,
  AppShellWorkflowContinuityViewModel
} from "@/app-shell.view-model";
import { describeDue } from "@/lib/due-urgency";
import { latestInstant, type SinceLastVisitTimes } from "@/lib/since-last-visit";
import {
  ATTENTION_SOURCE_LABELS, attentionItemFromInbox, attentionItemFromWorkspaceSignal, attentionItemKey,
  attentionSeverityRank, buildAttentionItems, countAttention, isSignalInInbox,
  type AttentionCounts, type AttentionItem, type AttentionItemSource
} from "@/lib/attention-items";
import type { OperatorTriageItem } from "@/lib/operator-inbox-triage";
import { summarizeStatusSignals } from "@/lib/status-signals";

export type DailyControlTowerBadgeVariant = "outline" | "success" | "warning" | "danger";

export interface DailyControlTowerProofPassportItem {
  id: string;
  label: string;
  value: string;
  detail: string;
}

export interface DailyControlTowerFocusRow {
  item: AppShellOperatorFocusItem;
  statusLabel: string;
  outputLabel: string;
  badgeVariant: DailyControlTowerBadgeVariant;
  proof: AppShellEvidenceTimelineItem | null;
  proofPassportItems: DailyControlTowerProofPassportItem[];
  proofPassportSummary: string;
}

export interface DailyControlTowerDriverItem {
  id: string;
  label: string;
  value: string;
  detail: string;
  badgeVariant: DailyControlTowerBadgeVariant;
}

export interface DailyControlTowerModel {
  decision: AppShellDecisionBrief;
  statusLabel: string;
  statusTone: AppShellWorkflowContinuityStatusTone;
  statusBadgeVariant: DailyControlTowerBadgeVariant;
  ownerLabel: string;
  outputLabel: string;
  nextActionLabel: string;
  nextActionHref: string;
  nextActionAriaLabel: string;
  evidenceLabel: string;
  driverItems: DailyControlTowerDriverItem[];
  queueRows: DailyControlTowerFocusRow[];
  /** The row for the decision the headline names; its evidence is shown until a row is chosen. */
  leadingRow: DailyControlTowerFocusRow | null;
  trustItems: AppShellTrustStripItem[];
  linkedContextItems: AppShellLinkedContextItem[];
  evidenceTimelineItems: AppShellEvidenceTimelineItem[];
  emptyQueueText: string;
}

const proofPassportLabels = [
  "Source",
  "Freshness",
  "Reconciliation",
  "Approvals",
  "Report Usage",
  "Blockers",
  "Evidence Packet",
  "Audit Trail"
] as const;

/**
 * The decision queue the Daily Control Tower lists: every ranked focus candidate, or the visible
 * focus items when no ranked list exists. The one selection both the Control Tower and the
 * sidebar attention counts read, so the two can never disagree about what needs an operator.
 */
export function selectDailyControlTowerQueueItems(viewModel: AppShellWorkflowContinuityViewModel): AppShellOperatorFocusItem[] {
  return viewModel.operatorFocusCommandItems.length > 0
    ? viewModel.operatorFocusCommandItems
    : viewModel.operatorFocusItems;
}

/**
 * The queued item every status surface leads with: the one the view model ranked worst, or the
 * first of the worst tone when the view model names none. The masthead attention pill and the
 * Control Tower's headline both read this, so they name the same decision on every route.
 */
export function selectLeadingDecisionItem(viewModel: AppShellWorkflowContinuityViewModel): AppShellOperatorFocusItem | null {
  const queue = selectDailyControlTowerQueueItems(viewModel);
  const worst = summarizeStatusSignals(queue).worst ?? queue[0] ?? null;
  if (!worst) {
    return null;
  }
  const named = queue.find((item) => item.id === viewModel.leadingOperatorFocusItemId);
  return named && named.tone === worst.tone ? named : worst;
}

/** The sidebar's counts: the attention list counted by the one rule, in total and per workspace. */
export type WorkspaceAttentionCounts = Pick<AttentionCounts, "total" | "byWorkspace"> & Partial<AttentionCounts>;

/**
 * Sidebar counts of the attention list: the Control Tower queue's workspace signals plus the
 * shared inbox, counted by the one rule. Pass null while the inbox is loading or unavailable.
 */
export function buildWorkspaceAttentionCounts(
  viewModel: AppShellWorkflowContinuityViewModel,
  inboxItems: readonly OperatorTriageItem[] | null = null
): AttentionCounts {
  return countAttention(buildAttentionItems(selectDailyControlTowerQueueItems(viewModel), inboxItems));
}

export function buildDailyControlTowerModel(
  viewModel: AppShellWorkflowContinuityViewModel,
  trustStrip: AppShellTrustStripState
): DailyControlTowerModel {
  const focusItems = selectDailyControlTowerQueueItems(viewModel);
  const queueRows = focusItems.map((item) => buildFocusRow(item, viewModel));
  const leadingItemId = selectLeadingDecisionItem(viewModel)?.id ?? null;
  const leadingRow = queueRows.find((row) => row.item.id === leadingItemId) ?? queueRows[0] ?? null;
  const decision = viewModel.decisionBrief;

  return {
    decision,
    statusLabel: decision.statusLabel,
    statusTone: decision.statusTone,
    statusBadgeVariant: badgeVariantForTone(decision.statusTone),
    ownerLabel: leadingRow?.item.workspaceLabel ?? "Current workstation",
    outputLabel: leadingRow?.outputLabel ?? outputLabelForRoute(decision.actionHref, "Workstation"),
    nextActionLabel: decision.actionLabel,
    nextActionHref: decision.actionHref,
    nextActionAriaLabel: decision.actionAriaLabel,
    evidenceLabel: buildDecisionEvidenceLabel(decision.evidenceLabel, leadingRow, viewModel.evidenceTimelineItems[0] ?? null),
    driverItems: buildDriverItems({
      viewModel,
      queueRows,
      trustItems: trustStrip.items,
      linkedContextItems: viewModel.linkedContextItems,
      evidenceTimelineItems: viewModel.evidenceTimelineItems
    }),
    queueRows,
    leadingRow,
    trustItems: trustStrip.items,
    linkedContextItems: viewModel.linkedContextItems,
    evidenceTimelineItems: viewModel.evidenceTimelineItems,
    emptyQueueText: viewModel.operatorFocusEmptyText
  };
}

function buildDriverItems({
  viewModel,
  queueRows,
  trustItems,
  linkedContextItems,
  evidenceTimelineItems
}: {
  viewModel: AppShellWorkflowContinuityViewModel;
  queueRows: DailyControlTowerFocusRow[];
  trustItems: AppShellTrustStripItem[];
  linkedContextItems: AppShellLinkedContextItem[];
  evidenceTimelineItems: AppShellEvidenceTimelineItem[];
}): DailyControlTowerDriverItem[] {
  const blockedRows = queueRows.filter((row) => row.item.tone === "blocked").length;
  const reviewRows = queueRows.filter((row) => row.item.tone === "review").length;
  const trustAttention = trustItems.filter((item) => item.tone === "blocked" || item.tone === "review").length;
  const linkedAttention = linkedContextItems.filter((item) => item.tone === "blocked" || item.tone === "review" || item.tone === "pending").length;

  return [
    {
      id: "continuity",
      label: "Continuity source",
      value: viewModel.title,
      detail: `${viewModel.summary} Source route: ${viewModel.routeLabel}.`,
      badgeVariant: badgeVariantForTone(viewModel.decisionBrief.statusTone)
    },
    {
      id: "queue",
      label: "Finance queue",
      value: queueRows.length > 0 ? `${blockedRows} blocked / ${reviewRows} review` : "0 queued",
      detail: queueRows.length > 0
        ? `${queueRows.length} ranked finance focus item(s) drive the daily decision queue.`
        : "No finance focus items are currently queued.",
      badgeVariant: blockedRows > 0 ? "danger" : reviewRows > 0 ? "warning" : "success"
    },
    {
      id: "trust",
      label: "Trust status",
      value: trustAttention > 0 ? `${trustAttention} need attention` : "Trusted",
      detail: `${trustItems.length} shell trust signal(s) are folded into the landing decision.`,
      badgeVariant: trustItems.some((item) => item.tone === "blocked") ? "danger" : trustAttention > 0 ? "warning" : "success"
    },
    {
      id: "context",
      label: "Linked context",
      value: linkedContextItems.length > 0 ? `${linkedAttention}/${linkedContextItems.length} checks` : "No subject",
      detail: linkedContextItems.length > 0
        ? "Cross-workspace context is linked before the operator leaves the landing queue."
        : "No symbol or operating subject is linked yet.",
      badgeVariant: linkedContextItems.some((item) => item.tone === "blocked") ? "danger" : linkedAttention > 0 ? "warning" : "outline"
    },
    {
      id: "proof",
      label: "Evidence events",
      value: evidenceTimelineItems.length > 0 ? `${evidenceTimelineItems.length} retained` : "No events",
      detail: evidenceTimelineItems.length > 0
        ? "Timestamped evidence events back the visible queue and evidence summaries."
        : "No timestamped evidence event is linked to this decision.",
      badgeVariant: evidenceTimelineItems.length > 0 ? "success" : "warning"
    }
  ];
}

/** Where a Today queue row comes from (UX consolidation P1). */
export type TodayQueueRowSource = AttentionItemSource;

export const TODAY_QUEUE_SOURCE_LABELS = ATTENTION_SOURCE_LABELS;

/**
 * One row of Today's single queue grid: an attention item plus its source's detail. Workspace
 * signals keep their evidence passport and next action; shared-inbox rows keep the full triage
 * item so assignment and snooze stay available.
 */
export type TodayQueueRow =
  | (AttentionItem & { source: "workspace"; focus: DailyControlTowerFocusRow })
  | (AttentionItem & { source: "inbox"; item: OperatorTriageItem });

export const todayQueueRowKey = attentionItemKey;

/**
 * Interleaves workspace signals and shared-inbox items into one queue. Order: severity
 * (blocked first), then age (oldest first; rows without an observed time after those with one),
 * then each source's own ranking, workspace signals before inbox items on a tie.
 *
 * A workspace signal that is also a shared-inbox item is one work item and gets one row, the
 * inbox one. `loadedInboxItems` is the whole loaded inbox, which decides that even when
 * `inboxItems` has been narrowed by the inbox filters.
 */
export function buildTodayQueueRows(
  focusRows: readonly DailyControlTowerFocusRow[],
  inboxItems: readonly OperatorTriageItem[],
  loadedInboxItems: readonly OperatorTriageItem[] | null = inboxItems,
  nowMs: number = Date.now()
): TodayQueueRow[] {
  const rows: TodayQueueRow[] = [
    ...focusRows.filter((focus) => !isSignalInInbox(focus.item.id, loadedInboxItems)).map((focus): TodayQueueRow => ({
      ...attentionItemFromWorkspaceSignal(focus.item),
      source: "workspace",
      // A workspace signal is observed when its correlated evidence was.
      observedAt: focus.proof?.timestampIso ?? null,
      focus
    })),
    ...inboxItems.map((item): TodayQueueRow => ({ ...attentionItemFromInbox(item), source: "inbox", item }))
  ];
  const observedMs = (row: TodayQueueRow) => {
    const value = Date.parse(row.observedAt ?? "");
    return Number.isFinite(value) ? value : Number.POSITIVE_INFINITY;
  };
  // Overdue work ranks first (UX Phase E); within each group, severity then oldest first.
  return rows
    .map((row, index) => ({ row, index, observed: observedMs(row), overdue: isTodayRowOverdue(row, nowMs) }))
    .sort((left, right) => Number(right.overdue) - Number(left.overdue)
      || attentionSeverityRank(left.row.severity) - attentionSeverityRank(right.row.severity)
      || (left.observed === right.observed ? 0 : left.observed < right.observed ? -1 : 1)
      || left.index - right.index)
    .map(({ row }) => row);
}

/**
 * The times a Today row is compared on for "new since you last looked": an inbox item appeared
 * when it was created and changed with its newest activity; a workspace signal appeared when its
 * evidence was observed.
 */
export function todayRowVisitTimes(row: TodayQueueRow): SinceLastVisitTimes {
  return row.source === "inbox"
    ? { detectedAt: row.item.createdAt, lastUpdatedAt: latestInstant(row.item.activity.map((entry) => entry.occurredAt)) }
    : { detectedAt: row.observedAt, lastUpdatedAt: null };
}

/** A queue row is overdue when its due date is before today (calendar days, UTC). */
export function isTodayRowOverdue(row: Pick<TodayQueueRow, "dueAt">, nowMs: number = Date.now()): boolean {
  return describeDue(row.dueAt, nowMs).state === "overdue";
}

export function dailyControlTowerProofPassportLabels(): string[] {
  return [...proofPassportLabels];
}

function buildFocusRow(
  item: AppShellOperatorFocusItem,
  viewModel: AppShellWorkflowContinuityViewModel
): DailyControlTowerFocusRow {
  const proof = findProofForItem(item, viewModel.evidenceTimelineItems);
  const linkedContext = findLinkedContextForItem(item, viewModel.linkedContextItems);
  const outputLabel = outputLabelForRoute(item.route, item.workspaceLabel);
  const statusLabel = statusLabelForTone(item.tone);
  const proofPassportItems = buildProofPassportItems({
    item,
    proof,
    linkedContext,
    viewModel,
    outputLabel
  });

  return {
    item,
    statusLabel,
    outputLabel,
    badgeVariant: badgeVariantForTone(item.tone),
    proof,
    proofPassportItems,
    proofPassportSummary: describeFocus(item.label, item.tone, outputLabel, proof?.timestampLabel ?? null)
  };
}

/**
 * One readable sentence for the selected item, e.g. "Cash variance needs review. It is blocked and
 * affects the accounting record or close evidence. No timestamped evidence is linked yet."
 * The parts are written into a sentence, never comma-joined labels.
 */
export function describeFocus(label: string, tone: AppShellWorkflowContinuityStatusTone, outputLabel: string, evidenceTimestamp: string | null): string {
  const lowerFirst = (text: string) => /^[A-Z][a-z]/.test(text) ? text.charAt(0).toLowerCase() + text.slice(1) : text;
  const headline = label.trim().replace(/[.:]+$/, "");
  const evidence = evidenceTimestamp ? `Latest evidence: ${evidenceTimestamp}.` : "No timestamped evidence is linked yet.";
  const status = { blocked: "is blocked", review: "needs review", pending: "is pending", ready: "is ready" }[tone];
  return `${headline}. It ${status} and affects the ${lowerFirst(outputLabel.trim())}. ${evidence}`;
}

function buildProofPassportItems({
  item,
  proof,
  linkedContext,
  viewModel,
  outputLabel
}: {
  item: AppShellOperatorFocusItem;
  proof: AppShellEvidenceTimelineItem | null;
  linkedContext: AppShellLinkedContextItem | null;
  viewModel: AppShellWorkflowContinuityViewModel;
  outputLabel: string;
}): DailyControlTowerProofPassportItem[] {
  const source = proof?.workspaceLabel ?? item.workspaceLabel;
  const freshness = proof?.timestampLabel ?? "No timestamped evidence";
  const reconciliation = linkedContext?.statusLabel ?? viewModel.linkedContextPostureLabel;
  const approvals = approvalsLabelForTone(item.tone);
  const evidencePacket = proof?.label ?? "No evidence packet linked";
  const auditTrail = proof?.timestampIso ?? "Audit timestamp unavailable";

  return [
    {
      id: "source",
      label: "Source",
      value: source,
      detail: `Primary source workspace for ${item.label}.`
    },
    {
      id: "freshness",
      label: "Freshness",
      value: freshness,
      detail: proof ? proof.detail : "No timestamped evidence event is linked to this item."
    },
    {
      id: "reconciliation",
      label: "Reconciliation",
      value: reconciliation,
      detail: linkedContext?.detail ?? viewModel.linkedContextSummary
    },
    {
      id: "approvals",
      label: "Approvals",
      value: approvals,
      detail: item.actionLabel
    },
    {
      id: "report-usage",
      label: "Report Usage",
      value: outputLabel,
      detail: `Downstream output affected by ${item.label}.`
    },
    {
      id: "blockers",
      label: "Blockers",
      value: item.detail,
      detail: item.ariaLabel
    },
    {
      id: "evidence-packet",
      label: "Evidence Packet",
      value: evidencePacket,
      detail: proof?.ariaLabel ?? "No correlated evidence packet is linked to this queue item."
    },
    {
      id: "audit-trail",
      label: "Audit Trail",
      value: auditTrail,
      detail: proof?.route ?? item.route
    }
  ];
}

/**
 * The hero's evidence line for the leading decision. Evidence association is fail-closed (see
 * findProofForItem): the workstation's latest evidence event is this decision's evidence only
 * when it is the event correlated with the leading row. Without a correlated event the line says
 * so and names the latest event as workstation context, which is the same answer the Freshness
 * card gives for the row instead of a timestamp that reads as proof for it.
 */
function buildDecisionEvidenceLabel(
  decisionEvidenceLabel: string | null,
  leadingRow: DailyControlTowerFocusRow | null,
  latestEvidence: AppShellEvidenceTimelineItem | null
): string {
  if (!leadingRow) {
    return decisionEvidenceLabel ?? "No evidence linked";
  }

  if (leadingRow.proof) {
    return `Linked evidence: ${leadingRow.proof.workspaceLabel} ${leadingRow.proof.timestampLabel}`;
  }

  return latestEvidence
    ? `No evidence linked to this item. Latest workstation evidence: ${latestEvidence.workspaceLabel} ${latestEvidence.timestampLabel}`
    : "No evidence linked to this item";
}

function findProofForItem(
  item: AppShellOperatorFocusItem,
  evidenceItems: AppShellEvidenceTimelineItem[]
): AppShellEvidenceTimelineItem | null {
  // Evidence association is deliberately fail-closed. Route, workspace, tone,
  // and list position are presentation attributes, not durable correlation
  // keys, so they must never be used to attach a different event to this row.
  return evidenceItems.find((proof) => proof.id === item.id) ?? null;
}

function findLinkedContextForItem(
  item: AppShellOperatorFocusItem,
  linkedContextItems: AppShellLinkedContextItem[]
): AppShellLinkedContextItem | null {
  return linkedContextItems.find((context) => routeFingerprint(context.route) === routeFingerprint(item.route))
    ?? linkedContextItems.find((context) => context.workspaceLabel === item.workspaceLabel)
    ?? null;
}

function routeFingerprint(route: string): string {
  return route.split(/[?#]/, 1)[0] || "/";
}

function statusLabelForTone(tone: AppShellWorkflowContinuityStatusTone): string {
  switch (tone) {
    case "blocked":
      return "Blocked";
    case "review":
      return "Review";
    case "pending":
      return "Pending";
    case "ready":
      return "Ready";
  }
}

function badgeVariantForTone(tone: AppShellWorkflowContinuityStatusTone): DailyControlTowerBadgeVariant {
  switch (tone) {
    case "blocked":
      return "danger";
    case "review":
      return "warning";
    case "ready":
      return "success";
    case "pending":
      return "outline";
  }
}

function approvalsLabelForTone(tone: AppShellWorkflowContinuityStatusTone): string {
  switch (tone) {
    case "blocked":
      return "Blocked";
    case "review":
      return "Review required";
    case "pending":
      return "Pending";
    case "ready":
      return "Ready";
  }
}

function outputLabelForRoute(route: string, workspaceLabel: string): string {
  const pathname = routeFingerprint(route);
  if (pathname.startsWith("/reporting")) {
    return "Reporting package or evidence";
  }

  if (pathname.startsWith("/accounting")) {
    return "Accounting record or close evidence";
  }

  if (pathname.startsWith("/portfolio")) {
    return "Portfolio exposure or brokerage sync";
  }

  if (pathname.startsWith("/trading")) {
    return "Trading readiness or paper session";
  }

  if (pathname.startsWith("/data")) {
    return "Data health or provider review";
  }

  if (pathname.startsWith("/settings")) {
    return "Provider connections or diagnostics";
  }

  if (pathname.startsWith("/strategy")) {
    return "Strategy run or research evidence";
  }

  return `${workspaceLabel} output`;
}

const SEVERITY_WORDS: Record<TodayQueueRow["severity"], string> = { blocked: "blocked", action: "needs action", info: "informational", clear: "clear" };

/**
 * Why a row sits where it does in the queue, in the order `buildTodayQueueRows` sorts by:
 * severity first, then the oldest observed time, then the source's own order. `rows` is the
 * queue as shown, so the position matches what the operator sees.
 */
export function describeTodayQueueRank(rows: readonly TodayQueueRow[], key: string, nowMs: number = Date.now()): string | null {
  const index = rows.findIndex((row) => row.key === key);
  if (index < 0) return null;
  const row = rows[index]!;
  const overdue = isTodayRowOverdue(row, nowMs);
  const overdueCount = rows.filter((candidate) => isTodayRowOverdue(candidate, nowMs)).length;
  const peers = rows.filter((candidate) => candidate.severity === row.severity && isTodayRowOverdue(candidate, nowMs) === overdue);
  const position = peers.indexOf(row) + 1;
  const observed = Number.isFinite(Date.parse(row.observedAt ?? ""));
  const word = SEVERITY_WORDS[row.severity];
  const order = !observed
    ? "no observed time, so it follows timed items in its source's order"
    : peers.length === 1
      ? `the only ${word} item`
      : position === 1
        ? `the oldest of ${peers.length} ${word} items`
        : `${ordinal(position)} oldest of ${peers.length} ${word} items`;
  const group = overdue ? `${describeDue(row.dueAt, nowMs).label}; overdue items rank first, then ` : overdueCount > 0 ? "After the overdue items, sorted " : "Sorted ";
  return `Ranked ${index + 1} of ${rows.length}. ${group}by severity (${word}), then oldest first: ${order}.`;
}

function ordinal(value: number): string {
  const tens = value % 100;
  const suffix = tens >= 11 && tens <= 13 ? "th" : ({ 1: "st", 2: "nd", 3: "rd" } as Record<number, string>)[value % 10] ?? "th";
  return `${value}${suffix}`;
}
